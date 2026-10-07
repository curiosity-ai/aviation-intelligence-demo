[task: Curiosity.Tasks.Name("Aviation RSS feeds")]
[task: Curiosity.Tasks.UID("AVconnRSS1111111111111")]
[task: Curiosity.Tasks.Schedule("17 * * * *")]
[task: Curiosity.Tasks.Connector]

//The in-workspace port of src/AviationIntel.Connector (FeedDirectoryIngest, FeedFetcher, ArticlesIngest):
//reads the curated feed directory, fetches every feed, reads each new article's page with Readability
//(SmartReader, which the workspace ships) and writes feeds, publishers, categories, tags and articles.
//
//What differs from the stand-alone connector, and why:
//- No browser. Pages are read with a plain HTTP request, so a page that only renders its body with
//  JavaScript, or sits behind a bot check, falls back to the feed's own text and says so in
//  ContentSource / ExtractionNote. The stand-alone connector (Chromium) reads more of those; run it for a
//  first full ingest when the best extraction matters, and let this one keep the corpus current.
//- No hash cache. An article already in the workspace is never re-read or re-written - only linked to any
//  feed that newly carries it - so whatever the stand-alone run extracted is kept. A page that failed for
//  a reason that passes (timeout, dropped connection) is marked "(retried next run)" and read again.
//- The directory is a workspace file, stored by `AviationIntel.Connector --upload-data`.
//
//Only what the sources state is written. Which companies, aircraft and airports an article is about is
//left to the workspace's entity recognition (_Mentions / _AppearsIn).

const string DIRECTORY_FILE = "aviation-intel/data/feeds/aviation-aerospace-rss-directory.csv";
const int SINCE_DAYS = 14;
const int MAX_ITEMS_PER_FEED = 50;
const int FEED_CONCURRENCY = 8;
const int PAGE_CONCURRENCY = 8;
const int BATCH_SIZE = 100;
const int MIN_READABLE_WORDS = 80;   //below this Readability has most likely read a teaser or a cookie wall
const int MIN_FEED_BODY_WORDS = 80;   //below this a feed's content:encoded is a summary, not a body
const string RETRY_MARKER = "(retried next run)";

//A current desktop Chrome for feeds - several newsrooms answer a library user agent with 403 - and an
//identifying one for article pages, as the stand-alone connector's plain-request fallback does.
const string FEED_USER_AGENT = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
const string PAGE_USER_AGENT = "AviationIntelligenceDemo/1.0 (+https://github.com/curiosity-ai/aviation-intelligence-demo)";

var outcomes = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
void Outcome(string what) => outcomes.AddOrUpdate(what, 1, (_, n) => n + 1);

// 1. The directory ---------------------------------------------------------------------------------------

var rows = await ReadDirectoryAsync();

//A dry run from the integration editor reads a handful of feeds rather than all 248.
if (IsDryRun) rows = rows.Take(5).ToList();

await Graph.LogAsync(LogLevel.Information, $"{rows.Count} feeds in the directory");

// 2. Fetch every feed before any feed node is written, so each Feed is written once with this run's status.

var since = DateTimeOffset.UtcNow.AddDays(-SINCE_DAYS);
var fetched = new System.Collections.Concurrent.ConcurrentDictionary<string, FetchResult>(StringComparer.OrdinalIgnoreCase);

using (var http = CreateHttpClient())
{
    http.Timeout = TimeSpan.FromSeconds(30);
    http.DefaultRequestHeaders.UserAgent.ParseAdd(FEED_USER_AGENT);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");

    var gate = new SemaphoreSlim(FEED_CONCURRENCY);

    await Task.WhenAll(rows.Select(async row =>
    {
        await gate.WaitAsync(CancellationToken);

        try
        {
            var result = await FetchFeedAsync(http, row);
            fetched[row.FeedUrl] = result;
            Outcome("feed " + result.Status);
        }
        finally
        {
            gate.Release();
        }
    }));
}

ProgressReporter?.Message($"{fetched.Values.Count(f => f.Status == "OK")} of {rows.Count} feeds fetched");

// 3. Feeds, and the vocabularies the directory classifies them by ------------------------------------------

var minted = new HashSet<string>(StringComparer.Ordinal);
bool Mint(string type, string key) => !string.IsNullOrWhiteSpace(key) && minted.Add(type + "\u0000" + key);

foreach (var row in rows)
{
    fetched.TryGetValue(row.FeedUrl, out var result);

    var lastItemAt = result?.Items.Select(i => i.PublishedAt).DefaultIfEmpty().Max() ?? default;

    var feed = Graph.AddOrUpdate(Key(N.Feed.Type, row.FeedUrl), new
    {
        FeedUrl = row.FeedUrl,
        Name = row.Name,
        MarketUse = row.MarketUse,
        CoverageFocus = row.CoverageFocus,
        ExpectedLanguage = row.ExpectedLanguage,
        AccessNote = row.AccessNote,
        Format = row.Format,
        ResolvedFeedUrl = row.ResolvedFeedUrl,
        EvidencePage = row.EvidencePage,
        DirectoryFeedTitle = row.FeedTitle,
        DirectoryStatus = row.ValidationStatus,
        DirectoryAge = row.LatestItemAge,
        DirectoryNote = row.ValidationNote,
        OverlapNote = row.OverlapNote,
        LastFetchStatus = result?.Status ?? "Not fetched",
        LastFetchError = result?.Error ?? "",
        LastItemCount = result?.Items.Count ?? 0,
        LastItemAt = lastItemAt,
        Timestamp = result?.FetchedAt ?? DateTimeOffset.UtcNow,
    });

    if (Mint(N.FeedCategory.Type, row.Category)) Graph.TryAdd(Key(N.FeedCategory.Type, row.Category), new { Name = row.Category });
    if (Mint(N.SourceType.Type, row.SourceType)) Graph.TryAdd(Key(N.SourceType.Type, row.SourceType), new { Name = row.SourceType });
    if (Mint(N.Priority.Type, row.Priority)) Graph.AddOrUpdate(Key(N.Priority.Type, row.Priority), new { Name = row.Priority, Rank = FeedDirectory.RankOf(row.Priority) });

    LinkKey(feed, N.FeedCategory.Type, row.Category, E.FeedInCategory, E.CategoryHasFeed);
    LinkKey(feed, N.SourceType.Type, row.SourceType, E.FeedOfSourceType, E.SourceTypeHasFeed);
    LinkKey(feed, N.Priority.Type, row.Priority, E.FeedHasPriority, E.PriorityOfFeed);

    var domain = row.PublisherDomain;

    if (domain is object)
    {
        if (Mint(N.Publisher.Type, domain)) Graph.TryAdd(Key(N.Publisher.Type, domain), new { Domain = domain, Name = FeedDirectory.PublisherName(row, domain) });
        LinkKey(feed, N.Publisher.Type, domain, E.PublishedBy, E.PublisherOfFeed);
    }
}

await Graph.CommitPendingAsync();

// 4. Articles ----------------------------------------------------------------------------------------------

//An item carried by several feeds is one article, grouped by canonical URL; the richest copy wins.
var byUrl = new Dictionary<string, (FeedItem Item, List<DirectoryRow> Feeds)>(StringComparer.Ordinal);

foreach (var row in rows)
{
    if (!fetched.TryGetValue(row.FeedUrl, out var result)) continue;

    foreach (var item in result.Items)
    {
        if (byUrl.TryGetValue(item.Url, out var existing))
        {
            existing.Feeds.Add(row);
            if (item.ContentText.Length > existing.Item.ContentText.Length) byUrl[item.Url] = (item, existing.Feeds);
        }
        else
        {
            byUrl[item.Url] = (item, new List<DirectoryRow> { row });
        }
    }
}

//An article already in the workspace keeps the content it was written with; it only gains the edges of a
//feed that newly carries it. One whose page failed transiently last time is read again.
var toRead = new List<(FeedItem Item, List<DirectoryRow> Feeds)>();

foreach (var (url, entry) in byUrl.OrderBy(kv => kv.Key, StringComparer.Ordinal))
{
    var uid = Mosaik.GraphDB.Node.GetUID(N.Article.Type, url);

    if (Workspace.TryGetReadOnlyContent(uid, out var existing) && !(existing.GetString(N.Article.ExtractionNote) ?? "").EndsWith(RETRY_MARKER, StringComparison.Ordinal))
    {
        LinkArticle(Key(N.Article.Type, url), entry.Item, entry.Feeds);
        Outcome("article already ingested");
        continue;
    }

    toRead.Add(entry);
}

await Graph.LogAsync(LogLevel.Information, $"{byUrl.Count:n0} distinct items in the last {SINCE_DAYS} days, {toRead.Count:n0} to read");

//Round-robin across hosts: in URL order one publisher's fifty items sit together and every reader waits
//on that host's politeness gate.
var interleaved = toRead.GroupBy(e => Urls.Host(e.Item.Url), StringComparer.OrdinalIgnoreCase)
                        .SelectMany(g => g.Select((e, i) => (Entry: e, Turn: i)))
                        .OrderBy(x => x.Turn)
                        .ThenBy(x => x.Entry.Item.Url, StringComparer.Ordinal)
                        .Select(x => x.Entry)
                        .ToList();

if (IsDryRun) interleaved = interleaved.Take(10).ToList();

var done = 0;

using (var http = CreateHttpClient())
{
    http.Timeout = TimeSpan.FromSeconds(45);
    http.DefaultRequestHeaders.UserAgent.ParseAdd(PAGE_USER_AGENT);

    var gate = new SemaphoreSlim(PAGE_CONCURRENCY);
    var hosts = new HostGates(TimeSpan.FromSeconds(1));

    foreach (var batch in interleaved.Chunk(BATCH_SIZE))
    {
        ThrowIfCancellationRequested();

        var pages = new System.Collections.Concurrent.ConcurrentDictionary<string, (ReadPage Page, string Error)>(StringComparer.Ordinal);

        await Task.WhenAll(batch.Select(async entry =>
        {
            using (await hosts.EnterAsync(Urls.Host(entry.Item.Url), CancellationToken))
            {
                await gate.WaitAsync(CancellationToken);

                try
                {
                    pages[entry.Item.Url] = await ReadPageAsync(http, entry.Item.Url, unwrapRedirect: true);
                }
                finally
                {
                    gate.Release();
                }
            }
        }));

        //Writing stays serial: one AddOrUpdate per article, carrying everything.
        foreach (var entry in batch)
        {
            pages.TryGetValue(entry.Item.Url, out var page);
            WriteArticle(entry.Item, entry.Feeds, page.Page, page.Error);
        }

        await Graph.CommitPendingAsync();

        done += batch.Length;
        ProgressReporter?.Message($"{done:n0} of {interleaved.Count:n0} articles read");
    }
}

var summary = string.Join(", ", outcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value:n0}"));

await Graph.LogAsync(LogLevel.Information, summary);

return summary;

// ---------------------------------------------------------------------------------------------------------

void LinkKey(Curiosity.Library.Node from, string toType, string toKey, string forward, string reverse)
{
    if (string.IsNullOrWhiteSpace(toKey)) return;
    Graph.Link(from, Key(toType, toKey), forward, reverse);
}

void LinkArticle(Curiosity.Library.Node article, FeedItem item, List<DirectoryRow> feeds)
{
    foreach (var feed in feeds)
    {
        LinkKey(article, N.Feed.Type, feed.FeedUrl, E.AppearedIn, E.FeedHasArticle);
        LinkKey(article, N.FeedCategory.Type, feed.Category, E.ArticleInCategory, E.CategoryHasArticle);
        LinkKey(article, N.Publisher.Type, feed.PublisherDomain, E.ArticleFromPublisher, E.PublisherHasArticle);
    }

    foreach (var tag in item.Tags)
    {
        if (Mint(N.Tag.Type, tag)) Graph.TryAdd(Key(N.Tag.Type, tag), new { Name = tag });
        LinkKey(article, N.Tag.Type, tag, E.TaggedWith, E.TagOf);
    }
}

void WriteArticle(FeedItem item, List<DirectoryRow> feeds, ReadPage page, string error)
{
    var primary = feeds.OrderBy(f => FeedDirectory.RankOf(f.Priority)).ThenBy(f => f.FeedUrl, StringComparer.Ordinal).First();

    var (body, source, note) = ChooseBody(item, page, error, primary, feeds);

    Outcome("content: " + source);

    var article = Graph.AddOrUpdate(Key(N.Article.Type, item.Url), new
    {
        Url = item.Url,
        Title = item.Title,
        Excerpt = ExcerptOf(item, page),
        Body = body,
        Author = FirstNonEmpty(item.Author, page?.Author, page?.Byline),
        SiteName = FirstNonEmpty(page?.SiteName, primary.Name),
        Language = LanguageOf(page, primary),
        ImageUrl = page?.FeaturedImage ?? "",
        FinalUrl = page?.FinalUrl ?? item.Url,
        ContentSource = source,
        ExtractionNote = note,
        WordCount = Text.WordCount(body),
        PublishedDay = item.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd"),
        Timestamp = item.PublishedAt,
    });

    LinkArticle(article, item, feeds);
}

//Which text becomes the body, and how a reader should treat it - recorded so a report can say "summary
//only" rather than present a teaser as the whole story.
(string Body, string Source, string Note) ChooseBody(FeedItem item, ReadPage page, string error, DirectoryRow feed, List<DirectoryRow> feeds)
{
    var readableWords = Text.WordCount(page?.TextContent);
    var feedWords = Text.WordCount(item.ContentText);
    var restricted = feeds.All(f => f.AccessNote.Contains("Subscription", StringComparison.OrdinalIgnoreCase)
                                    || f.AccessNote.Contains("restricted", StringComparison.OrdinalIgnoreCase));

    if (page is object && readableWords >= MIN_READABLE_WORDS && readableWords >= feedWords / 2)
    {
        return restricted && readableWords < 300 && feedWords < MIN_FEED_BODY_WORDS
            ? (page.TextContent, "Page (possibly paywalled)", $"{feed.AccessNote}; {readableWords} words extracted")
            : (page.TextContent, "Page", "");
    }

    var retry = page is null && IsTransient(error) ? " " + RETRY_MARKER : "";

    if (feedWords >= MIN_FEED_BODY_WORDS)
    {
        return (item.ContentText, "Feed full text", (error ?? (page is null ? "" : $"page gave {readableWords} words")) + retry);
    }

    var summary = FirstNonEmpty(item.ContentText, item.SummaryText, page?.TextContent, page?.Excerpt);

    var why = error is object ? error
            : restricted ? feed.AccessNote
            : $"page gave {readableWords} words (no browser in the workspace connector)";

    return (summary, "Feed summary only", why + retry);
}

static bool IsTransient(string error)
    => error is object
    && (error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
     || error.Contains("connection", StringComparison.OrdinalIgnoreCase)
     || error.StartsWith("HTTP 5", StringComparison.Ordinal));

static string ExcerptOf(FeedItem item, ReadPage page)
{
    var summary = FirstNonEmpty(item.SummaryText, page?.Excerpt, item.ContentText);
    return summary.Length <= 1200 ? summary : summary.Substring(0, 1200).TrimEnd() + "…";
}

//The page's own lang attribute when Readability found one; otherwise what the directory expects.
static string LanguageOf(ReadPage page, DirectoryRow feed)
{
    if (!string.IsNullOrWhiteSpace(page?.Language)) return page.Language.Split('-')[0].ToLowerInvariant();

    var expected = feed.ExpectedLanguage ?? "";
    var languages = new[]
    {
        ("German", "de"), ("French", "fr"), ("Dutch", "nl"), ("Italian", "it"), ("Spanish", "es"), ("Danish", "da"),
        ("Greek", "el"), ("Hungarian", "hu"), ("Norwegian", "no"), ("Portuguese", "pt"), ("Romanian", "ro"),
        ("Finnish", "fi"), ("Swedish", "sv"), ("Czech", "cs"),
    };

    foreach (var (name, code) in languages)
    {
        if (expected.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return code;
    }

    return expected.StartsWith("English", StringComparison.OrdinalIgnoreCase) ? "en" : "";
}

static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

async Task<List<DirectoryRow>> ReadDirectoryAsync()
{
    var file = Graph.GetFile(UID(GraphDB.Schema._FileEntry.For(DIRECTORY_FILE)));
    var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
    {
        TrimOptions = CsvHelper.Configuration.TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
    };

    using (var stream = await Graph.OpenFileAsync(file.Node))
    using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
    using (var csv = new CsvHelper.CsvReader(reader, config))
    {
        return csv.GetRecords<DirectoryRow>()
                  .Where(r => !string.IsNullOrWhiteSpace(r.FeedUrl))
                  .GroupBy(r => r.FeedUrl.Trim(), StringComparer.OrdinalIgnoreCase)
                  .Select(g => g.First())
                  .ToList();
    }
}

async Task<FetchResult> FetchFeedAsync(HttpClient http, DirectoryRow row)
{
    var now = DateTimeOffset.UtcNow;

    string body;

    try
    {
        using (var response = await http.GetAsync(row.FetchUrl, CancellationToken))
        {
            if (!response.IsSuccessStatusCode) return new FetchResult("Unreachable", $"HTTP {(int)response.StatusCode}", now, new List<FeedItem>(), 0);

            body = await response.Content.ReadAsStringAsync(CancellationToken);
        }
    }
    catch (HttpRequestException e)
    {
        return new FetchResult("Unreachable", e.Message.Split('\n')[0], now, new List<FeedItem>(), 0);
    }
    catch (TaskCanceledException) when (!CancellationToken.IsCancellationRequested)
    {
        return new FetchResult("Unreachable", "timed out", now, new List<FeedItem>(), 0);
    }

    var (items, inFeed, parseError) = Feeds.Parse(body, row.FetchUrl, now);

    if (parseError is object) return new FetchResult("Invalid feed", parseError, now, new List<FeedItem>(), 0);

    var kept = items.Where(i => i.PublishedAt >= since)
                    .OrderByDescending(i => i.PublishedAt)
                    .Take(MAX_ITEMS_PER_FEED)
                    .ToList();

    return new FetchResult(inFeed == 0 ? "Empty" : "OK", null, now, kept, inFeed);
}

async Task<(ReadPage Page, string Error)> ReadPageAsync(HttpClient http, string url, bool unwrapRedirect)
{
    try
    {
        using (var response = await http.GetAsync(url, CancellationToken))
        {
            if (!response.IsSuccessStatusCode) return (null, $"HTTP {(int)response.StatusCode}");

            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;

            //A feed service's click tracker ends on a "Redirect Notice" naming the article: read that.
            if (unwrapRedirect && Urls.TryUnwrapRedirectNotice(finalUrl, out var target)) return await ReadPageAsync(http, target, unwrapRedirect: false);

            var html = await response.Content.ReadAsStringAsync(CancellationToken);

            return (ReadPage.From(finalUrl, html), null);
        }
    }
    catch (HttpRequestException e)
    {
        return (null, "connection failed: " + e.Message.Split('\n')[0]);
    }
    catch (TaskCanceledException) when (!CancellationToken.IsCancellationRequested)
    {
        return (null, "timed out");
    }
    catch (Exception e) when (!(e is OperationCanceledException))
    {
        return (null, "extraction failed: " + e.Message.Split('\n')[0]);
    }
}

// ---------------------------------------------------------------------------------------------------------

/// <summary>One directory row, exactly as the CSV has it.</summary>
public sealed class DirectoryRow
{
    [CsvHelper.Configuration.Attributes.Name("Source / feed")] public string Name { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Category")] public string Category { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Priority")] public string Priority { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Feed URL")] public string FeedUrl { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Market intelligence use")] public string MarketUse { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Source type")] public string SourceType { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Coverage focus")] public string CoverageFocus { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Expected language")] public string ExpectedLanguage { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Article access note")] public string AccessNote { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Validation status")] public string ValidationStatus { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Latest item age")] public string LatestItemAge { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Format")] public string Format { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Evidence / discovery page")] public string EvidencePage { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Resolved feed URL")] public string ResolvedFeedUrl { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Feed title as returned")] public string FeedTitle { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Validation note")] public string ValidationNote { get; set; } = "";
    [CsvHelper.Configuration.Attributes.Name("Overlap / analysis note")] public string OverlapNote { get; set; } = "";

    /// <summary>The URL actually fetched: the resolved one when the directory has it.</summary>
    public string FetchUrl => string.IsNullOrWhiteSpace(ResolvedFeedUrl) ? FeedUrl : ResolvedFeedUrl;

    /// <summary>The publisher: from the page the feed was found on, since a feed host can be a feed service.</summary>
    public string PublisherDomain => Urls.RegistrableDomain(string.IsNullOrWhiteSpace(EvidencePage) ? FeedUrl : EvidencePage);
}

public static class FeedDirectory
{
    //Core feeds lead a report section; review-only ones never do.
    public static int RankOf(string priority)
    {
        if (priority == "Core") return 1;
        if (priority == "Secondary") return 2;
        return priority.StartsWith("Low", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
    }

    //"Lufthansa Group — German news" and "Lufthansa Group — News" are one publisher, "Lufthansa Group".
    public static string PublisherName(DirectoryRow row, string domain)
    {
        var name = row.Name.Split(new[] { " — ", " - ", " – " }, StringSplitOptions.None)[0].Trim();
        return string.IsNullOrWhiteSpace(name) ? domain : name;
    }
}

public sealed record FeedItem(string Url, string Title, string SummaryText, string ContentText, string Author, DateTimeOffset PublishedAt, string[] Tags);

public sealed record FetchResult(string Status, string Error, DateTimeOffset FetchedAt, List<FeedItem> Items, int ItemsInFeed);

/// <summary>What Readability made of one article page.</summary>
public sealed class ReadPage
{
    public string FinalUrl { get; set; }
    public string TextContent { get; set; }
    public string Excerpt { get; set; }
    public string Byline { get; set; }
    public string Author { get; set; }
    public string SiteName { get; set; }
    public string Language { get; set; }
    public string FeaturedImage { get; set; }

    public static ReadPage From(string finalUrl, string html)
    {
        var article = new SmartReader.Reader(finalUrl, html) { Logging = SmartReader.ReportLevel.Issue }.GetArticle();

        return new ReadPage
        {
            FinalUrl = finalUrl,
            TextContent = Text.NormaliseReadable(article.TextContent),
            Excerpt = article.Excerpt ?? "",
            Byline = article.Byline ?? "",
            Author = article.Author ?? "",
            SiteName = article.SiteName ?? "",
            Language = article.Language ?? "",
            FeaturedImage = article.FeaturedImage ?? "",
        };
    }
}

/// <summary>RSS 2.0, RSS 1.0 (RDF) and Atom, read with LINQ to XML.</summary>
public static class Feeds
{
    private static readonly System.Xml.Linq.XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly System.Xml.Linq.XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly System.Xml.Linq.XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    public static (List<FeedItem> Items, int InFeed, string Error) Parse(string body, string feedUrl, DateTimeOffset fetchedAt)
    {
        var text = (body ?? "").TrimStart('﻿', ' ', '\r', '\n', '\t');

        if (text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return (null, 0, "an HTML page instead of a feed (bot check or moved feed)");
        }

        System.Xml.Linq.XDocument doc;

        try
        {
            doc = System.Xml.Linq.XDocument.Parse(text);
        }
        catch (System.Xml.XmlException e)
        {
            return (null, 0, e.Message.Split('\n')[0]);
        }

        var root = doc.Root;

        if (root is null) return (null, 0, "empty document");

        var isAtom = root.Name == Atom + "feed";
        var entries = isAtom
            ? root.Elements(Atom + "entry").ToList()
            : root.Descendants().Where(e => e.Name.LocalName == "item").ToList();

        if (!isAtom && root.Name.LocalName != "rss" && root.Name.LocalName != "RDF") return (null, 0, $"not a feed (<{root.Name.LocalName}>)");

        var items = new List<FeedItem>();

        foreach (var e in entries)
        {
            var item = isAtom ? FromAtom(e, feedUrl, fetchedAt) : FromRss(e, feedUrl, fetchedAt);
            if (item is object) items.Add(item);
        }

        return (items, entries.Count, null);
    }

    private static FeedItem FromRss(System.Xml.Linq.XElement e, string feedUrl, DateTimeOffset fetchedAt)
    {
        string Child(string localName) => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName && c.Name.Namespace != Content)?.Value;

        var url = Urls.Canonicalize(Child("link"), feedUrl) ?? Urls.Canonicalize(Child("guid"), feedUrl);

        return Make(url, Child("title"), Child("description"), e.Element(Content + "encoded")?.Value,
                    Child("author") ?? e.Element(Dc + "creator")?.Value,
                    Child("pubDate") ?? e.Element(Dc + "date")?.Value,
                    e.Elements().Where(c => c.Name.LocalName == "category" || c.Name == Dc + "subject").Select(c => c.Value),
                    fetchedAt);
    }

    private static FeedItem FromAtom(System.Xml.Linq.XElement e, string feedUrl, DateTimeOffset fetchedAt)
    {
        var links = e.Elements(Atom + "link").ToList();
        var link = links.FirstOrDefault(l => (string)l.Attribute("rel") is null || (string)l.Attribute("rel") == "alternate") ?? links.FirstOrDefault();
        var url = Urls.Canonicalize((string)link?.Attribute("href"), feedUrl) ?? Urls.Canonicalize(e.Element(Atom + "id")?.Value, feedUrl);

        //Atom entries often carry only <updated>.
        return Make(url, e.Element(Atom + "title")?.Value, e.Element(Atom + "summary")?.Value, e.Element(Atom + "content")?.Value,
                    e.Element(Atom + "author")?.Element(Atom + "name")?.Value,
                    e.Element(Atom + "published")?.Value ?? e.Element(Atom + "updated")?.Value,
                    e.Elements(Atom + "category").Select(c => (string)c.Attribute("term") ?? c.Value),
                    fetchedAt);
    }

    private static FeedItem Make(string url, string title, string summary, string content, string author, string date, IEnumerable<string> categories, DateTimeOffset fetchedAt)
    {
        if (url is null || string.IsNullOrWhiteSpace(title)) return null;

        //No date at all: the item is as new as the fetch that found it. Never later than the fetch either.
        var published = Dates.TryParse(date, out var d) ? d : fetchedAt;
        if (published > fetchedAt.AddHours(1)) published = fetchedAt;

        return new FeedItem(
            Url: url,
            Title: Text.FromHtml(title),
            SummaryText: Text.FromHtml(summary),
            ContentText: Text.FromHtml(content),
            Author: Text.FromHtml(author),
            PublishedAt: published,
            Tags: categories.Select(c => Text.FromHtml(c).Trim().ToLowerInvariant())
                                   .Where(c => c.Length > 1 && c.Length < 60)
                                   .Distinct()
                                   .OrderBy(c => c, StringComparer.Ordinal)
                                   .ToArray());
    }
}

public static class Dates
{
    //RFC 822 dates in feeds use zone names DateTimeOffset does not read.
    private static readonly (string Zone, string Offset)[] Zones =
    {
        (" GMT", " +0000"), (" UTC", " +0000"), (" UT", " +0000"), (" Z", " +0000"),
        (" EDT", " -0400"), (" EST", " -0500"), (" CDT", " -0500"), (" CST", " -0600"),
        (" MDT", " -0600"), (" MST", " -0700"), (" PDT", " -0700"), (" PST", " -0800"),
        (" BST", " +0100"), (" CET", " +0100"), (" CEST", " +0200"),
    };

    public static bool TryParse(string value, out DateTimeOffset result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();

        foreach (var (zone, offset) in Zones)
        {
            if (text.EndsWith(zone, StringComparison.OrdinalIgnoreCase)) { text = text.Substring(0, text.Length - zone.Length) + offset; break; }
        }

        //"Tue, 07 Oct 2026 10:00:00 +0000": the weekday is redundant, and a wrong one fails the parse.
        var comma = text.IndexOf(',');
        if (comma > 0 && comma < 5) text = text.Substring(comma + 1).Trim();

        var styles = System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeUniversal;

        if (DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, styles, out result)) { result = result.ToUniversalTime(); return true; }

        //"07 Oct 2026 10:00:00 +0000" with a four-digit offset DateTimeOffset may not take.
        if (DateTimeOffset.TryParseExact(text, new[] { "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz", "d MMM yyyy HH:mm:ss", "d MMMM yyyy HH:mm:ss zzz" },
                                         System.Globalization.CultureInfo.InvariantCulture, styles, out result))
        {
            result = result.ToUniversalTime();
            return true;
        }

        var fixedOffset = System.Text.RegularExpressions.Regex.Replace(text, @"([+-]\d\d)(\d\d)$", "$1:$2");

        if (DateTimeOffset.TryParse(fixedOffset, System.Globalization.CultureInfo.InvariantCulture, styles, out result)) { result = result.ToUniversalTime(); return true; }

        return false;
    }
}

/// <summary>Plain text from the HTML fragments feeds carry, and Readability output collapsed to paragraphs.</summary>
public static class Text
{
    private static readonly System.Text.RegularExpressions.Regex NonContent = new(@"<(script|style|noscript)[^>]*>.*?</\1>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex BlockBreaks = new(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr|/blockquote)\s*/?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex Tags = new(@"<[^>]+>");
    private static readonly System.Text.RegularExpressions.Regex Spaces = new(@"[ \t ]+");
    private static readonly System.Text.RegularExpressions.Regex BlankLines = new(@"\n\s*\n\s*\n+");
    private static readonly System.Text.RegularExpressions.Regex AppearedFirstOn = new(@"The post .{1,300}? appeared first on .{1,200}?\.?\s*$", System.Text.RegularExpressions.RegexOptions.Singleline);
    private static readonly System.Text.RegularExpressions.Regex LeftoverMarkup = new(@"</?(span|div|p|a|br|em|strong|b|i)\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    //Some feeds entity-encode HTML that was already HTML; one more pass, never more.
    public static string FromHtml(string html)
    {
        var text = Once(html);
        return LeftoverMarkup.IsMatch(text) ? Once(text) : text;
    }

    private static string Once(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var text = NonContent.Replace(html, " ");
        text = BlockBreaks.Replace(text, "\n");
        text = Tags.Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Spaces.Replace(text, " ");
        text = string.Join("\n", text.Split('\n').Select(l => l.Trim()));
        text = BlankLines.Replace(text, "\n\n");
        text = AppearedFirstOn.Replace(text, "");

        return text.Trim();
    }

    //Readability keeps the page's indentation; collapse it to paragraphs, and drop the page furniture
    //("Skip to content", a dateline, an image credit) it keeps above the first sentence.
    public static string NormaliseReadable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim());
        var paragraphs = string.Join("\n", lines)
                               .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => string.Join(" ", p.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)))
                               .Where(p => p.Length > 0)
                               .ToList();

        for (var i = 0; i < 3 && paragraphs.Count > 1 && WordCount(paragraphs[0]) < 6; i++) paragraphs.RemoveAt(0);

        return string.Join("\n\n", paragraphs);
    }

    public static int WordCount(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>URL normalisation - the article key and the publisher key both come from here.</summary>
public static class Urls
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_name", "utm_id",
        "fbclid", "gclid", "mc_cid", "mc_eid", "cmpid", "ito", "ncid", "sr_share", "rss", "ref", "src",
    };

    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "gov.uk", "org.uk", "ac.uk", "com.au", "gov.au", "org.au", "co.nz", "govt.nz", "co.jp", "or.jp",
        "com.br", "gov.br", "com.cn", "gov.cn", "co.in", "gov.in", "nic.in", "com.sg", "gov.sg", "co.za", "gov.za",
        "europa.eu", "gc.ca", "gouv.fr", "go.jp", "com.tr", "gov.tr", "com.mx", "gob.mx",
    };

    /// <summary>
    /// Absolute, no fragment, no tracking parameters, no trailing slash on the path - identical to the
    /// stand-alone connector's, so both write the same article to the same node.
    /// </summary>
    public static string Canonicalize(string link, string baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
        {
            if (baseUrl is null || !Uri.TryCreate(new Uri(baseUrl), link.Trim(), out uri)) return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var query = string.Join("&", uri.Query.TrimStart('?')
                                        .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Where(p => !TrackingParameters.Contains(p.Split('=')[0])));

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query = query,
            Path = path,
            Host = uri.Host.ToLowerInvariant(),
        };

        if (builder.Uri.IsDefaultPort) builder.Port = -1;

        return builder.Uri.AbsoluteUri;
    }

    /// <summary>"www.easa.europa.eu" is "easa.europa.eu"; on a hosted newsroom platform the tenant is the publisher.</summary>
    public static string RegistrableDomain(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var labels = uri.Host.ToLowerInvariant().Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

        if (labels.Length <= 2) return string.Join(".", labels);

        var lastTwo = labels[labels.Length - 2] + "." + labels[labels.Length - 1];

        if (lastTwo == "mediaroom.com" || lastTwo == "gcs-web.com" || lastTwo == "prnewswire.com" || lastTwo == "businesswire.com" || lastTwo == "q4cdn.com" || lastTwo == "feedburner.com")
        {
            return labels[labels.Length - 3] + "." + lastTwo;
        }

        return MultiLabelSuffixes.Contains(lastTwo) ? labels[labels.Length - 3] + "." + lastTwo : lastTwo;
    }

    /// <summary>follow.it sends a reader through api.follow.it/redirect-to-url?q=&lt;article&gt;.</summary>
    public static bool TryUnwrapRedirectNotice(string url, out string target)
    {
        target = "";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("follow.it", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var pair in uri.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(new[] { '=' }, 2);

            if (parts.Length == 2 && parts[0] == "q" && Uri.TryCreate(Uri.UnescapeDataString(parts[1]), UriKind.Absolute, out var inner) && inner.Scheme.StartsWith("http"))
            {
                target = Canonicalize(inner.AbsoluteUri) ?? inner.AbsoluteUri;
                return true;
            }
        }

        return false;
    }

    public static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
}

/// <summary>One request at a time per host, with a pause between them; different hosts run in parallel.</summary>
public sealed class HostGates
{
    private readonly TimeSpan _delay;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (SemaphoreSlim Gate, DateTimeOffset[] Next)> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public HostGates(TimeSpan delay) => _delay = delay;

    public async Task<IDisposable> EnterAsync(string host, CancellationToken cancellationToken)
    {
        var entry = _hosts.GetOrAdd(host, _ => (new SemaphoreSlim(1), new DateTimeOffset[1]));

        await entry.Gate.WaitAsync(cancellationToken);

        var wait = entry.Next[0] - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

        return new Release(() =>
        {
            entry.Next[0] = DateTimeOffset.UtcNow + _delay;
            entry.Gate.Release();
        });
    }

    private sealed class Release : IDisposable
    {
        private readonly Action _onDispose;
        public Release(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

