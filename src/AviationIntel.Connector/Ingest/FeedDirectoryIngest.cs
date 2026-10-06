using AviationIntel.Connector.Extraction;
using Curiosity.Library;
using CsvHelper.Configuration.Attributes;

namespace AviationIntel.Connector.Ingest;

/// <summary>
/// <c>data/feeds/aviation-aerospace-rss-directory.csv</c> → one <see cref="Feed"/> per row, plus the
/// shared vocabularies the directory classifies them by.
/// </summary>
/// <remarks>
/// The directory is the curated part of the corpus: an analyst's judgement of what each feed is for,
/// how much to trust it and whether its articles can be read. All of that travels onto the
/// <see cref="Feed"/> node, because the reports and the agent lean on it - a "Subscription likely"
/// feed is quoted from its summary only, a "Low frequency" one is not expected to have news today.
/// </remarks>
internal static class FeedDirectoryIngest
{
    public const int Version = 1;

    /// <summary>One directory row, exactly as the CSV has it.</summary>
    internal sealed class Row
    {
        [Name("Source / feed")]                public string Name                 { get; set; } = "";
        [Name("Category")]                     public string Category             { get; set; } = "";
        [Name("Priority")]                     public string Priority             { get; set; } = "";
        [Name("Feed URL")]                     public string FeedUrl              { get; set; } = "";
        [Name("Market intelligence use")]      public string MarketUse            { get; set; } = "";
        [Name("Source type")]                  public string SourceType           { get; set; } = "";
        [Name("Coverage focus")]               public string CoverageFocus        { get; set; } = "";
        [Name("Expected language")]            public string ExpectedLanguage     { get; set; } = "";
        [Name("Article access note")]          public string AccessNote           { get; set; } = "";
        [Name("Validation status")]            public string ValidationStatus     { get; set; } = "";
        [Name("Latest item age")]              public string LatestItemAge        { get; set; } = "";
        [Name("Latest item date (UTC)")]       public string LatestItemDate       { get; set; } = "";
        [Name("Items returned")]               public string ItemsReturned        { get; set; } = "";
        [Name("Format")]                       public string Format               { get; set; } = "";
        [Name("Checked (UTC)")]                public string CheckedAt            { get; set; } = "";
        [Name("Evidence / discovery page")]    public string EvidencePage         { get; set; } = "";
        [Name("Evidence method")]              public string EvidenceMethod       { get; set; } = "";
        [Name("Resolved feed URL")]            public string ResolvedFeedUrl      { get; set; } = "";
        [Name("HTTP status")]                  public string HttpStatus           { get; set; } = "";
        [Name("Feed title as returned")]       public string FeedTitle            { get; set; } = "";
        [Name("Validation note")]              public string ValidationNote       { get; set; } = "";
        [Name("Overlap / analysis note")]      public string OverlapNote          { get; set; } = "";

        /// <summary>The URL the connector actually fetches: the resolved one when the directory has it.</summary>
        public string FetchUrl => string.IsNullOrWhiteSpace(ResolvedFeedUrl) ? FeedUrl : ResolvedFeedUrl;

        /// <summary>The publisher: from the page the feed was found on, since a feed host can be a feed service.</summary>
        public string? PublisherDomain => Urls.RegistrableDomain(string.IsNullOrWhiteSpace(EvidencePage) ? FeedUrl : EvidencePage);
    }

    [Node]
    internal sealed class Feed
    {
        [Key]       public string         FeedUrl            { get; set; } = "";
        [Property]  public string         Name               { get; set; } = "";
        [Property]  public string         MarketUse          { get; set; } = "";
        [Property]  public string         CoverageFocus      { get; set; } = "";
        [Property]  public string         ExpectedLanguage   { get; set; } = "";
        [Property]  public string         AccessNote         { get; set; } = "";
        [Property]  public string         Format             { get; set; } = "";
        [Property]  public string         ResolvedFeedUrl    { get; set; } = "";
        [Property]  public string         EvidencePage       { get; set; } = "";
        [Property]  public string         DirectoryFeedTitle { get; set; } = "";
        [Property]  public string         DirectoryStatus    { get; set; } = "";
        [Property]  public string         DirectoryAge       { get; set; } = "";
        [Property]  public string         DirectoryNote      { get; set; } = "";
        [Property]  public string         OverlapNote        { get; set; } = "";

        // What the last run found - written in the same AddOrUpdate as the directory fields, because
        // the feeds are fetched before any feed node is written.
        [Property]  public string         LastFetchStatus    { get; set; } = "";
        [Property]  public string         LastFetchError     { get; set; } = "";
        [Property]  public int            LastItemCount      { get; set; }
        [Property]  public DateTimeOffset LastItemAt         { get; set; }
        [Timestamp] public DateTimeOffset LastFetchedAt      { get; set; }
    }

    [Node] internal sealed class FeedCategory { [Key] public string Name   { get; set; } = ""; }
    [Node] internal sealed class SourceType   { [Key] public string Name   { get; set; } = ""; }
    [Node] internal sealed class Priority     { [Key] public string Name   { get; set; } = ""; [Property] public int Rank { get; set; } }
    [Node] internal sealed class Publisher    { [Key] public string Domain { get; set; } = ""; [Property] public string Name { get; set; } = ""; }

    public static IReadOnlyList<Row> Load(ConnectorContext ctx)
    {
        var rows = ctx.ReadCsv<Row>(ctx.Options.DirectoryCsv)
                      .Where(r => !string.IsNullOrWhiteSpace(r.FeedUrl))
                      .GroupBy(r => r.FeedUrl.Trim(), StringComparer.OrdinalIgnoreCase)
                      .Select(g => g.First())
                      .ToList();

        IEnumerable<Row> selected = rows;

        if (!string.IsNullOrWhiteSpace(ctx.Options.FeedFilter))
        {
            var filter = ctx.Options.FeedFilter!;
            selected = selected.Where(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                        || r.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                        || r.FeedUrl.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ctx.Options.PriorityFilter))
        {
            selected = selected.Where(r => r.Priority.Equals(ctx.Options.PriorityFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (ctx.Options.FeedLimit > 0) selected = selected.Take(ctx.Options.FeedLimit);

        return selected.ToList();
    }

    /// <summary>Writes every feed with what this run's fetch found, and the vocabularies it is classified by.</summary>
    public static async Task WriteAsync(ConnectorContext ctx, IReadOnlyList<Row> rows, IReadOnlyDictionary<string, FeedFetcher.FetchResult> fetched)
    {
        foreach (var row in rows)
        {
            fetched.TryGetValue(row.FeedUrl, out var result);

            var node = ctx.Graph.AddOrUpdate(new Feed
            {
                FeedUrl            = row.FeedUrl,
                Name               = row.Name,
                MarketUse          = row.MarketUse,
                CoverageFocus      = row.CoverageFocus,
                ExpectedLanguage   = row.ExpectedLanguage,
                AccessNote         = row.AccessNote,
                Format             = row.Format,
                ResolvedFeedUrl    = row.ResolvedFeedUrl,
                EvidencePage       = row.EvidencePage,
                DirectoryFeedTitle = row.FeedTitle,
                DirectoryStatus    = row.ValidationStatus,
                DirectoryAge       = row.LatestItemAge,
                DirectoryNote      = row.ValidationNote,
                OverlapNote        = row.OverlapNote,
                LastFetchStatus    = result?.Status ?? "Not fetched",
                LastFetchError     = result?.Error ?? "",
                LastItemCount      = result?.Items.Count ?? 0,
                LastItemAt         = result?.Items.Select(i => i.PublishedAt).DefaultIfEmpty().Max() ?? default,
                LastFetchedAt      = result?.FetchedAt ?? default,
            });
            ctx.CountNode(nameof(Feed));

            if (ctx.Mint(nameof(FeedCategory), row.Category)) { ctx.Graph.TryAdd(new FeedCategory { Name = row.Category }); ctx.CountNode(nameof(FeedCategory)); }
            if (ctx.Mint(nameof(SourceType), row.SourceType)) { ctx.Graph.TryAdd(new SourceType { Name = row.SourceType }); ctx.CountNode(nameof(SourceType)); }
            if (ctx.Mint(nameof(Priority), row.Priority))     { ctx.Graph.AddOrUpdate(new Priority { Name = row.Priority, Rank = RankOf(row.Priority) }); ctx.CountNode(nameof(Priority)); }

            ctx.LinkKey(node, nameof(FeedCategory), row.Category,   Edges.FeedInCategory,   Edges.CategoryHasFeed);
            ctx.LinkKey(node, nameof(SourceType),   row.SourceType, Edges.FeedOfSourceType, Edges.SourceTypeHasFeed);
            ctx.LinkKey(node, nameof(Priority),     row.Priority,   Edges.FeedHasPriority,  Edges.PriorityOfFeed);

            var domain = row.PublisherDomain;

            if (domain is not null)
            {
                if (ctx.Mint(nameof(Publisher), domain)) { ctx.Graph.TryAdd(new Publisher { Domain = domain, Name = PublisherName(row, domain) }); ctx.CountNode(nameof(Publisher)); }
                ctx.LinkKey(node, nameof(Publisher), domain, Edges.PublishedBy, Edges.PublisherOfFeed);
            }
        }

        await ctx.Graph.CommitPendingAsync();
    }

    //Core feeds lead a report section; review-only ones never do.
    private static int RankOf(string priority) => priority switch
    {
        "Core"      => 1,
        "Secondary" => 2,
        _ when priority.StartsWith("Low", StringComparison.OrdinalIgnoreCase) => 3,
        _           => 4,
    };

    //"Lufthansa Group — German news" and "Lufthansa Group — News" are one publisher, "Lufthansa Group".
    private static string PublisherName(Row row, string domain)
    {
        var name = row.Name.Split(new[] { " — ", " - ", " – " }, StringSplitOptions.None)[0].Trim();

        return string.IsNullOrWhiteSpace(name) ? domain : name;
    }
}
