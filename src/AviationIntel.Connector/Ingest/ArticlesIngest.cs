using AviationIntel.Connector.Extraction;
using Curiosity.Library;

namespace AviationIntel.Connector.Ingest;

/// <summary>
/// Feed items → <see cref="Article"/> nodes, with the article text read from the page itself.
/// </summary>
/// <remarks>
/// <para>
/// The four beats, with two changes: the expensive part of an article is loading its page, so the
/// cache is consulted <i>before</i> extraction and only the misses are loaded - in parallel, through
/// the browser's per-host gates - and they are loaded, written and committed in batches, so a run
/// that fails part-way keeps what it already read. Writing stays serial, one
/// <c>AddOrUpdate</c> per article carrying everything, because the pending buffer is not safe for
/// concurrent writers.
/// </para>
/// <para>
/// An item that arrives through two feeds is one article: it is grouped by canonical URL first, and
/// its cache fingerprint includes every feed it was seen in, so being picked up by a new feed
/// rewrites it with the extra edge.
/// </para>
/// </remarks>
internal static class ArticlesIngest
{
    public const int Version = 2;

    //Below this many words Readability has most likely read a teaser, a cookie wall or a
    //navigation page rather than the article. Short enough to keep a terse press release.
    private const int MIN_READABLE_WORDS = 80;

    //Below this many words a feed's own content:encoded is a summary, not a body.
    private const int MIN_FEED_BODY_WORDS = 80;

    //Articles loaded, written and committed together. Small enough that a failed run loses little,
    //large enough that the slowest page in a batch does not hold the others up for long.
    private const int BATCH_SIZE = 100;

    [Node]
    internal sealed class Article
    {
        [Key]       public string         Url            { get; set; } = "";
        [Property]  public string         Title          { get; set; } = "";
        [Property]  public string         Excerpt        { get; set; } = "";
        [Property]  public string         Body           { get; set; } = "";
        [Property]  public string         Author         { get; set; } = "";
        [Property]  public string         SiteName       { get; set; } = "";
        [Property]  public string         Language       { get; set; } = "";
        [Property]  public string         ImageUrl       { get; set; } = "";
        [Property]  public string         FinalUrl       { get; set; } = "";
        [Property]  public string         ContentSource  { get; set; } = "";
        [Property]  public string         ExtractionNote { get; set; } = "";
        [Property]  public int            WordCount      { get; set; }
        [Property]  public string         PublishedDay   { get; set; } = "";
        [Timestamp] public DateTimeOffset PublishedAt    { get; set; }
    }

    [Node] internal sealed class Tag { [Key] public string Name { get; set; } = ""; }

    /// <summary>What the cache hashes: the item as published, and every feed it was seen in.</summary>
    private sealed record Fingerprint(string Url, string Title, DateTimeOffset PublishedAt, string Summary, string Content, string[] Feeds, bool Browser);

    private sealed record Candidate(FeedFetcher.Item Item, List<FeedDirectoryIngest.Row> Feeds, Fingerprint Fingerprint, UID.UID128 Hash);

    public static async Task RunAsync(
        ConnectorContext                                       ctx,
        IReadOnlyList<FeedDirectoryIngest.Row>                 rows,
        IReadOnlyDictionary<string, FeedFetcher.FetchResult>   fetched,
        ArticleExtractor?                                      extractor)
    {
        // 1. LOAD - group every item by its canonical URL across all feeds.
        var byUrl = new Dictionary<string, (FeedFetcher.Item Item, List<FeedDirectoryIngest.Row> Feeds)>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!fetched.TryGetValue(row.FeedUrl, out var result)) continue;

            foreach (var item in result.Items)
            {
                if (byUrl.TryGetValue(item.Url, out var existing))
                {
                    existing.Feeds.Add(row);

                    //Keep the richest copy of a syndicated item.
                    if (item.ContentText.Length > existing.Item.ContentText.Length) byUrl[item.Url] = (item, existing.Feeds);
                }
                else
                {
                    byUrl[item.Url] = (item, new List<FeedDirectoryIngest.Row> { row });
                }
            }
        }

        // 2. LOOP - the cache decides which items need their page loaded at all.
        var candidates = new List<Candidate>();

        foreach (var (_, (item, feeds)) in byUrl.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var fingerprint = new Fingerprint(item.Url, item.Title, item.PublishedAt, item.SummaryText, item.ContentText,
                                              feeds.Select(f => f.FeedUrl).OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                                              Browser: extractor is not null);

            var hash = ctx.Cache.Hash(fingerprint);

            if (ctx.Cache.ContainsHash(Version, hash))
            {
                ctx.CountOutcome("article unchanged (cached)");
                continue;
            }

            candidates.Add(new Candidate(item, feeds, fingerprint, hash));
        }

        Console.WriteLine($"    {byUrl.Count:N0} distinct items, {candidates.Count:N0} new or changed");

        //Round-robin across hosts. In URL order one publisher's fifty items sit together, every
        //worker queues on that host's politeness gate, and the run goes one page at a time.
        var interleaved = candidates.GroupBy(c => Urls.Host(c.Item.Url), StringComparer.OrdinalIgnoreCase)
                                    .SelectMany(g => g.Select((c, i) => (Candidate: c, Turn: i)))
                                    .OrderBy(x => x.Turn)
                                    .ThenBy(x => x.Candidate.Item.Url, StringComparer.Ordinal)
                                    .Select(x => x.Candidate)
                                    .ToList();

        // 3. + 4. per batch: a first ingest loads a thousand pages, and a run that dies at page 900
        // must not have to load them again. Each batch is written, committed to the graph and only
        // then recorded in the cache.
        var done = 0;

        foreach (var batch in interleaved.Chunk(BATCH_SIZE))
        {
            var extracted = new System.Collections.Concurrent.ConcurrentDictionary<string, (ExtractedArticle? Article, string? Error)>(StringComparer.Ordinal);

            if (extractor is not null)
            {
                await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, ctx.Options.BrowserConcurrency * 4) }, async (c, ct) =>
                {
                    extracted[c.Item.Url] = await extractor.ExtractAsync(c.Item.Url, ct);
                });
            }

            //Writing stays serial: the pending buffer is not safe for concurrent writers.
            foreach (var candidate in batch)
            {
                extracted.TryGetValue(candidate.Item.Url, out var page);
                Write(ctx, candidate, page.Article, page.Error, extractor is not null);
            }

            await ctx.Graph.CommitPendingAsync();

            if (!ctx.DryRun)
            {
                foreach (var candidate in batch) ctx.Cache.EnqueueHash(Version, candidate.Hash);
            }

            await ctx.CommitCacheAsync();

            done += batch.Length;
            if (extractor is not null) Console.WriteLine($"    extracted and written {done:N0}/{candidates.Count:N0}");
        }
    }

    private static void Write(ConnectorContext ctx, Candidate candidate, ExtractedArticle? page, string? error, bool browserUsed)
    {
        var item    = candidate.Item;
        var primary = candidate.Feeds.OrderBy(f => RankOf(f.Priority)).ThenBy(f => f.FeedUrl, StringComparer.Ordinal).First();

        var (body, source, note) = ChooseBody(item, page, error, primary, candidate.Feeds, browserUsed);

        ctx.CountOutcome("content: " + source);

        var node = ctx.Graph.AddOrUpdate(new Article
        {
            Url            = item.Url,
            Title          = item.Title,
            Excerpt        = ExcerptOf(item, page),
            Body           = body,
            Author         = FirstNonEmpty(item.Author, page?.Author, page?.Byline),
            SiteName       = FirstNonEmpty(page?.SiteName, primary.Name),
            Language       = LanguageOf(page, primary),
            ImageUrl       = page?.FeaturedImage ?? "",
            FinalUrl       = page?.FinalUrl ?? item.Url,
            ContentSource  = source,
            ExtractionNote = note,
            WordCount      = HtmlText.WordCount(body),
            PublishedDay   = item.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd"),
            PublishedAt    = item.PublishedAt,
        });
        ctx.CountNode(nameof(Article));

        foreach (var feed in candidate.Feeds)
        {
            ctx.LinkKey(node, nameof(FeedDirectoryIngest.Feed), feed.FeedUrl, Edges.AppearedIn, Edges.FeedHasArticle);
            ctx.LinkKey(node, nameof(FeedDirectoryIngest.FeedCategory), feed.Category, Edges.ArticleInCategory, Edges.CategoryHasArticle);
            ctx.LinkKey(node, nameof(FeedDirectoryIngest.Publisher), feed.PublisherDomain, Edges.ArticleFromPublisher, Edges.PublisherHasArticle);
        }

        foreach (var tag in item.Tags)
        {
            if (ctx.Mint(nameof(Tag), tag)) { ctx.Graph.TryAdd(new Tag { Name = tag }); ctx.CountNode(nameof(Tag)); }
            ctx.LinkKey(node, nameof(Tag), tag, Edges.TaggedWith, Edges.TagOf);
        }
    }

    /// <summary>
    /// Which text becomes the article's body, and how the reader should treat it. Recorded on the node
    /// so a report can say "summary only" rather than present a teaser as the whole story.
    /// </summary>
    private static (string Body, string Source, string Note) ChooseBody(
        FeedFetcher.Item                       item,
        ExtractedArticle?                      page,
        string?                                error,
        FeedDirectoryIngest.Row                feed,
        IReadOnlyList<FeedDirectoryIngest.Row> feeds,
        bool                                   browserUsed)
    {
        var readableWords = HtmlText.WordCount(page?.TextContent);
        var feedWords     = HtmlText.WordCount(item.ContentText);
        var restricted    = feeds.All(f => f.AccessNote.Contains("Subscription", StringComparison.OrdinalIgnoreCase)
                                    || f.AccessNote.Contains("restricted",   StringComparison.OrdinalIgnoreCase));

        if (page is not null && readableWords >= MIN_READABLE_WORDS && readableWords >= feedWords / 2)
        {
            //A short page from a subscription publisher whose feed carries no body of its own is
            //most likely the free teaser; say so rather than present it as the whole story.
            return restricted && readableWords < 300 && feedWords < MIN_FEED_BODY_WORDS
                ? (page.TextContent, "Page (possibly paywalled)", $"{feed.AccessNote}; {readableWords} words extracted")
                : (page.TextContent, "Page", "");
        }

        if (feedWords >= MIN_FEED_BODY_WORDS)
        {
            return (item.ContentText, "Feed full text", error ?? (page is null ? "" : $"page gave {readableWords} words"));
        }

        var summary = FirstNonEmpty(item.ContentText, item.SummaryText, page?.TextContent, page?.Excerpt);

        var why = !browserUsed     ? "page not loaded (--no-browser)"
                : error is not null ? error
                : restricted        ? feed.AccessNote
                :                     $"page gave {readableWords} words";

        return (summary, "Feed summary only", why);
    }

    private static string ExcerptOf(FeedFetcher.Item item, ExtractedArticle? page)
    {
        var summary = FirstNonEmpty(item.SummaryText, page?.Excerpt, item.ContentText);

        return summary.Length <= 1200 ? summary : summary[..1200].TrimEnd() + "…";
    }

    //The page's own lang attribute when Readability found one; otherwise what the directory expects.
    private static string LanguageOf(ExtractedArticle? page, FeedDirectoryIngest.Row feed)
    {
        if (!string.IsNullOrWhiteSpace(page?.Language)) return page!.Language.Split('-')[0].ToLowerInvariant();

        var expected = feed.ExpectedLanguage;

        foreach (var (name, code) in Languages)
        {
            if (expected.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return code;
        }

        return expected.StartsWith("English", StringComparison.OrdinalIgnoreCase) ? "en" : "";
    }

    private static readonly (string Name, string Code)[] Languages =
    {
        ("German", "de"), ("French", "fr"), ("Dutch", "nl"), ("Italian", "it"), ("Spanish", "es"), ("Danish", "da"),
        ("Greek", "el"), ("Hungarian", "hu"), ("Norwegian", "no"), ("Portuguese", "pt"), ("Romanian", "ro"),
        ("Finnish", "fi"), ("Swedish", "sv"), ("Czech", "cs"),
    };

    private static int RankOf(string priority) => priority == "Core" ? 1 : priority == "Secondary" ? 2 : 3;

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
