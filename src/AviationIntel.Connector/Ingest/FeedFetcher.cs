using System.Net;
using AviationIntel.Connector.Extraction;
using CodeHollow.FeedReader;

namespace AviationIntel.Connector.Ingest;

/// <summary>
/// Downloads and parses every feed in the directory. Writes nothing: the feed nodes are written
/// afterwards, once, carrying what this pass found.
/// </summary>
internal static class FeedFetcher
{
    /// <summary>One item, as the feed published it. Also the hash-cache DTO of an article - see <see cref="ArticlesIngest"/>.</summary>
    internal sealed record Item(
        string          Url,
        string          Title,
        string          SummaryText,
        string          ContentText,
        string          Author,
        DateTimeOffset  PublishedAt,
        string[]        Tags);

    internal sealed record FetchResult(string Status, string? Error, DateTimeOffset FetchedAt, IReadOnlyList<Item> Items, int ItemsInFeed);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect      = true,
            MaxAutomaticRedirections = 10,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ConnectorOptions.DefaultUserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");

        return client;
    }

    public static async Task<IReadOnlyDictionary<string, FetchResult>> FetchAllAsync(
        ConnectorContext                   ctx,
        IReadOnlyList<FeedDirectoryIngest.Row> rows,
        ArticleExtractor?                  browser)
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, FetchResult>(StringComparer.OrdinalIgnoreCase);
        var since   = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, ctx.Options.SinceDays));

        await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (row, ct) =>
        {
            var result = await FetchOneAsync(ctx, row, browser, since, ct);
            results[row.FeedUrl] = result;
            ctx.CountOutcome("feed " + result.Status);

            var line = $"    {Truncate(row.Name, 48),-48} {result.Status,-14} {result.Items.Count,3}/{result.ItemsInFeed,-3}";
            Console.WriteLine(result.Error is null ? line : line + "  " + Truncate(result.Error, 70));
        });

        return results;
    }

    private static async Task<FetchResult> FetchOneAsync(
        ConnectorContext           ctx,
        FeedDirectoryIngest.Row    row,
        ArticleExtractor?          browser,
        DateTimeOffset             since,
        CancellationToken          cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var (body, error) = await GetWithHttpAsync(row.FetchUrl, cancellationToken);
        var (feed, parseError) = Parse(body);

        //Bot-protected newsrooms refuse an HTTP client - with a 403, or with a 200 carrying a
        //challenge page instead of the feed - and serve a browser.
        if (feed is null && browser is not null)
        {
            var (browserBody, _, browserError) = await browser.FetchTextAsync(row.FetchUrl);
            var (browserFeed, browserParseError) = Parse(browserBody);

            if (browserFeed is not null)
            {
                feed = browserFeed;
            }
            else
            {
                error      = $"{error ?? parseError}; browser: {browserError ?? browserParseError}";
                parseError = null;
            }
        }

        if (feed is null)
        {
            return parseError is not null
                ? new FetchResult("Invalid feed", parseError, now, Array.Empty<Item>(), 0)
                : new FetchResult("Unreachable",  error,      now, Array.Empty<Item>(), 0);
        }

        var items = feed.Items
                        .Select(i => ToItem(i, row.FetchUrl, now))
                        .Where(i => i is not null)
                        .Select(i => i!)
                        //A feed can carry items dated in the future (scheduled embargoes, time-zone bugs);
                        //they are kept, but never sort ahead of today.
                        .Where(i => i.PublishedAt >= since)
                        .OrderByDescending(i => i.PublishedAt)
                        .Take(Math.Max(1, ctx.Options.MaxItemsPerFeed))
                        .ToList();

        return new FetchResult(feed.Items.Count == 0 ? "Empty" : "OK", null, now, items, feed.Items.Count);
    }

    private static (Feed? Feed, string? Error) Parse(string? body)
    {
        if (body is null) return (null, null);

        var text = body.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');

        if (text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "an HTML page instead of a feed (bot check or moved feed)");
        }

        try
        {
            return (FeedReader.ReadFromString(text), null);
        }
        catch (Exception e) when (e is System.Xml.XmlException or CodeHollow.FeedReader.Parser.FeedTypeNotSupportedException or ArgumentException or InvalidOperationException)
        {
            return (null, e.Message.Split('\n')[0]);
        }
    }

    private static async Task<(string? Body, string? Error)> GetWithHttpAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode) return (null, $"HTTP {(int)response.StatusCode}");

            return (await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
        catch (HttpRequestException e)
        {
            return (null, e.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "timed out");
        }
    }

    private static Item? ToItem(FeedItem item, string feedUrl, DateTimeOffset fetchedAt)
    {
        var url = Urls.Canonicalize(item.Link, feedUrl) ?? Urls.Canonicalize(item.Id, feedUrl);

        if (url is null || string.IsNullOrWhiteSpace(item.Title)) return null;

        //No date at all: the item is as new as the fetch that found it. Rare, and only for feeds the
        //directory already flags.
        //Atom entries often carry only <updated>; FeedReader's PublishingDate reads <published>.
        var date = item.PublishingDate
                ?? (item.SpecificItem as CodeHollow.FeedReader.Feeds.AtomFeedItem)?.UpdatedDate;

        var published = date is { } d
            ? new DateTimeOffset(DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind)).ToUniversalTime()
            : fetchedAt;

        if (published > fetchedAt.AddHours(1)) published = fetchedAt;

        return new Item(
            Url:         url,
            Title:       HtmlText.ToText(item.Title),
            SummaryText: HtmlText.ToText(item.Description),
            ContentText: HtmlText.ToText(item.Content),
            Author:      HtmlText.ToText(item.Author),
            PublishedAt: published,
            Tags:        (item.Categories ?? Array.Empty<string>())
                            .Select(c => HtmlText.ToText(c).Trim().ToLowerInvariant())
                            .Where(c => c.Length is > 1 and < 60)
                            .Distinct()
                            .OrderBy(c => c, StringComparer.Ordinal)
                            .ToArray());
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
