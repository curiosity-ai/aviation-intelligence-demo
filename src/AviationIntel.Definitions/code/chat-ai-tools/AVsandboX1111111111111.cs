[tools: Curiosity.ChatAITools.UID("AVsandboX1111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Aviation News Desk")]
[tools: Curiosity.ChatAITools.Description("The aviation and aerospace news desk: every monitored RSS feed and every article the connector has read, as files you can grep, plus commands to filter the news by date, category, priority and publisher, and report templates that turn one JSON file into an HTML brief and a PowerPoint deck. Use it to answer questions about recent aviation news and to produce daily and weekly market intelligence reports.")]
[tools: Curiosity.ChatAITools.DisplayDescription("Browse the monitored aviation feeds and articles, and publish daily or weekly market reports as an HTML page or a PowerPoint deck.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-plane-alt")]
[tools: Curiosity.ChatAITools.EnableMonitoring()]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// A sandbox over the aviation news corpus written by AviationIntel.Connector.
//
//   /news       articles, one markdown file each, by day - walked lazily, so nothing is read
//               until something asks for it
//   /feeds      the feed directory with this run's fetch status, one file per feed
//   /templates  the report templates and their JSON contract
//   /work       scratch space
//   /artifacts  added by the workspace because Artifacts is declared; `publish` shows a file
//
// Every node type and field below is named through the injected N / E constants, which the
// workspace generates from the schema the connector registered - so this tool is imported
// after the first ingest, and a renamed field is a compile error here rather than an empty
// column in a report.

public class AviationNewsDesk : ISandbox
{
    //How far back /news/days reaches, and the window /news/index.tsv covers. The `news` command
    //is not bound by either.
    private const int DAYS_LISTED = 31;
    private const int INDEX_DAYS = 7;
    private const int MAX_NEWS_ROWS = 2_000;

    public string FunctionName => "news_desk";

    public string HomeDirectory => "/work";

    public int MaxDurationSeconds => 120;

    public string Description =>
        "The aviation and aerospace news desk: every monitored RSS feed and every article read from them, as files. "
      + "Start with `cat /news/README.md`. Use `news` to list articles by date, category, priority, publisher or text, "
      + "`article <uid>` to read one in full, and `stats` for counts and feed health. "
      + "For a daily or weekly market report, read the aviation-market-reports skill, write the report JSON to /work, "
      + "then `render-report report /work/<file>.json` (HTML) and/or `render-report deck /work/<file>.json` (PowerPoint), and `publish` the result.";

    public SandboxMount[] Mounts => new[]
    {
        SandboxMount.Lazy("/news", "articles by day as markdown, README.md and a 7-day index.tsv", ListNews, ReadNews),
        SandboxMount.Live("/feeds", "the feed directory: directory.tsv plus one markdown file per feed", ReadFeeds),
        SandboxMount.Files("/templates", "report templates: report.html, deck.html, the JSON contract in README.md and a sample",
            SandboxFile.Text("README.md", TemplatesReadme),
            SandboxFile.Text("report.html", ReportTemplate),
            SandboxFile.Text("deck.html", DeckTemplate),
            SandboxFile.Text("sample-daily.json", SampleDaily)),
        SandboxMount.Scratch("/work", "scratch space for report JSON, notes and drafts"),
    };

    public Mosaik.Schema.ArtifactType[] Artifacts => new[] { Mosaik.Schema.ArtifactType.Html, Mosaik.Schema.ArtifactType.Pdf };

    public SandboxQueryAccess Query => SandboxQueryAccess.For(
        N.Article.Type, N.Feed.Type, N.Publisher.Type, N.FeedCategory.Type, N.SourceType.Type, N.Priority.Type, N.Tag.Type,
        N.Organisation.Type, N.AircraftType.Type, N.EngineType.Type, N.LaunchVehicle.Type, N.Airport.Type).WithMaxNodes(200);

    //The vocabularies entity linking resolves mentions to. An article reaches them over _Mentions,
    //and they reach their articles over _AppearsIn - both written by the workspace, not the connector.
    private static readonly string[] EntityTypes = { N.Organisation.Type, N.AircraftType.Type, N.EngineType.Type, N.LaunchVehicle.Type, N.Airport.Type };

    public SandboxCommand[] Commands => new[]
    {
        SandboxCommand.Create("news",
            "news [--since 1d|7d|YYYY-MM-DD] [--until YYYY-MM-DD] [--category <text>] [--priority Core|Secondary] [--publisher <text>] "
          + "[--feed <text>] [--lang <code>] [--grep <text>] [--mentions <name>] [--content page|summary] [--limit N] [--json]: "
          + "articles newest first, one per line as TSV (published, uid, category, priority, publisher, lang, content, words, title, url) "
          + "or JSON lines with --json. Exact over the whole corpus - use it rather than reading a count off /news. "
          + "--since defaults to 1d; --grep matches title and excerpt; --content summary lists the ones only known from their feed summary; "
          + "--mentions keeps articles that entity linking found naming an organisation, aircraft type, engine type, launch vehicle or airport "
          + "(exact name first, else every name containing the text: --mentions Boeing, --mentions \"A350\", --mentions Heathrow).",
            call => Task.FromResult(News(call))),

        SandboxCommand.Create("article",
            "article <uid> [<uid> ...]: the full article as markdown - metadata, feeds, excerpt and body. Same content as the file under /news/days.",
            call =>
            {
                if (call.Arguments.Count == 0) return Task.FromResult(call.Fail("usage: article <uid> [<uid> ...]"));

                var sb = new StringBuilder();

                foreach (var arg in call.Arguments)
                {
                    if (!UID128.TryParse(arg, out var uid)) return Task.FromResult(call.Fail($"'{arg}' is not an article uid"));

                    var text = RenderArticle(call.Scope, uid);

                    if (text is null) return Task.FromResult(call.Fail($"no article {arg}"));

                    sb.AppendLine(text).AppendLine();
                }

                return Task.FromResult(sb.ToString());
            }),

        SandboxCommand.Create("stats",
            "stats [--since 1d|7d|YYYY-MM-DD] [--until YYYY-MM-DD] [--mentions <name>]: article counts by category, priority, publisher, day and content source, "
          + "the most-mentioned organisations, aircraft types, engine types, launch vehicles and airports (top_mentions), plus feed health (feeds that failed on their last fetch, Core feeds with nothing in the window). The numbers for a report's metrics and charts.",
            call => Task.FromResult(Stats(call))),

        SandboxCommand.Create("render-report",
            "render-report report|deck <data.json> [<name>]: fills /templates/report.html (an HTML brief) or /templates/deck.html "
          + "(a page that builds a PowerPoint deck) with the report JSON and writes /artifacts/<name>.html (an agent run: its own directory). Checks the JSON against "
          + "the contract in /templates/README.md first and refuses a report with missing sections or sources. Then run: publish <name>.html",
            RenderReportAsync),

        SandboxCommand.Search(new[] { N.Article.Type, N.Feed.Type, N.Publisher.Type, N.Organisation.Type, N.AircraftType.Type, N.EngineType.Type, N.LaunchVehicle.Type, N.Airport.Type }),
        SandboxCommand.Consult(new[] { N.Article.Type, N.Feed.Type, N.Publisher.Type }),
    };

    // ── /news ────────────────────────────────────────────────────────────────

    private static Task<SandboxDirectory> ListNews(ToolScope scope, string directory)
    {
        if (directory.Length == 0)
        {
            return Task.FromResult(SandboxDirectory.Of(
                SandboxEntry.File("README.md"),
                SandboxEntry.File("index.tsv"),
                SandboxEntry.Directory("days")));
        }

        if (directory == "days")
        {
            var days = Enumerable.Range(0, DAYS_LISTED)
                                 .Select(i => DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                                 .Select(SandboxEntry.Directory);

            return Task.FromResult(SandboxDirectory.Of(days));
        }

        if (directory.StartsWith("days/") && TryDay(directory.Substring(5), out var day))
        {
            var entries = ArticlesOn(scope, day).Select(n => SandboxEntry.File(n.UID + ".md", n.GetInt(N.Article.WordCount) * 6, n.Timestamp.ToDateTimeOffset()))
                                                .Prepend(SandboxEntry.File("index.tsv"));

            return Task.FromResult(SandboxDirectory.Of(entries));
        }

        return Task.FromResult(SandboxDirectory.None);
    }

    private static Task<SandboxFile> ReadNews(ToolScope scope, string path)
    {
        if (path == "README.md") return Task.FromResult(SandboxFile.Text(path, NewsReadme(scope)));

        if (path == "index.tsv")
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.UtcDateTime.Date.AddDays(-(INDEX_DAYS - 1));

            return Task.FromResult(SandboxFile.Text(path, Tsv(scope, Articles(scope, new DateTimeOffset(from, TimeSpan.Zero), to.AddHours(1)))));
        }

        var parts = path.Split('/');

        if (parts.Length == 3 && parts[0] == "days" && TryDay(parts[1], out var day))
        {
            if (parts[2] == "index.tsv") return Task.FromResult(SandboxFile.Text(path, Tsv(scope, ArticlesOn(scope, day))));

            if (parts[2].EndsWith(".md") && UID128.TryParse(parts[2].Substring(0, parts[2].Length - 3), out var uid))
            {
                var text = RenderArticle(scope, uid);

                return Task.FromResult(text is null ? null : SandboxFile.Text(path, text));
            }
        }

        return Task.FromResult<SandboxFile>(null);
    }

    private static string NewsReadme(ToolScope scope)
    {
        var total = scope.Q().StartAt(N.Article.Type).Count();
        var feeds = scope.Q().StartAt(N.Feed.Type).Count();

        return $"""
            # /news - the aviation news corpus

            {total:N0} articles from {feeds:N0} monitored feeds, ingested by the RSS connector. Every article
            was loaded in a browser and read with Readability where the page allowed it; otherwise its
            body is the feed's own text, and the `content` column says which.

            ## Layout

            - `index.tsv` - every article of the last {INDEX_DAYS} days (UTC), newest first.
            - `days/<YYYY-MM-DD>/index.tsv` - one day's articles. The last {DAYS_LISTED} days are listed.
            - `days/<YYYY-MM-DD>/<uid>.md` - one article: metadata, feeds, excerpt and body.

            Days are UTC calendar days of the article's publication time as the feed stated it.

            ## Columns (tab-separated, header line starts with #)

            published (UTC) · uid · category · priority · publisher · lang · content · words · title · url

            - `category` is the feed's category from the curated directory; an article seen in several feeds
              takes the category of its highest-priority feed.
            - `priority` is the directory's judgement of the feed: Core > Secondary > Low frequency.
            - `content`: `Page` (read from the article page), `Page (possibly paywalled)`, `Feed full text`,
              or `Feed summary only` - never present a summary-only article as if you had read it.
            - `lang` is the article's language code; the directory includes German, French and other
              national-language feeds.

            ## Beyond the files

            - `news --since 7d --category Airlines` - exact filters over the whole corpus, not just the index.
            - `article <uid>` - same as reading the file, by uid.
            - `stats --since 1d` - counts for a report's metrics and charts, the most-mentioned entities
              (`top_mentions`), and feed health.
            - `news --since 7d --mentions "Boeing 737 MAX"` - the articles entity linking found naming it.
            - `search <words>` / `consult <uid>` - the workspace's own search and reader.
            - `query '...'` - the graph query engine over Article, Feed, Publisher, FeedCategory, SourceType,
              Priority, Tag, Organisation, AircraftType, EngineType, LaunchVehicle and Airport.

            ## Mentions

            The workspace spots organisations (airlines, manufacturers, authorities, agencies), aircraft
            types, engine types, launch vehicles and airports in every article's title and body, from master lists built
            from Wikidata and OurAirports. `article <uid>` lists them; `--mentions` filters by them.

            They are found by name, so treat them as a lead, not a fact: an article that mentions Boeing
            is not necessarily about Boeing, and an entity missing from the list may still be in the text.
            Read the article before you say what it is about.
            """;
    }

    // ── /feeds ───────────────────────────────────────────────────────────────

    private static Task<IEnumerable<SandboxFile>> ReadFeeds(ToolScope scope)
    {
        var lookup = FeedLookup.Build(scope);
        var files = new List<SandboxFile>();
        var tsv = new StringBuilder("#name\tcategory\tpriority\tsource_type\tpublisher\tlanguage\taccess\tlast_status\tlast_items\tlast_item_utc\turl\tslug\n");

        foreach (var feed in lookup.Feeds.Values.OrderBy(f => f.Rank).ThenBy(f => f.Category).ThenBy(f => f.Name))
        {
            tsv.Append(Clean(feed.Name)).Append('\t').Append(Clean(feed.Category)).Append('\t').Append(feed.Priority).Append('\t')
               .Append(Clean(feed.SourceType)).Append('\t').Append(Clean(feed.Publisher)).Append('\t').Append(Clean(feed.Language)).Append('\t')
               .Append(Clean(feed.Access)).Append('\t').Append(Clean(feed.LastStatus)).Append('\t').Append(feed.LastItems).Append('\t')
               .Append(feed.LastItemAt).Append('\t').Append(feed.Url).Append('\t').Append(feed.Slug).Append('\n');

            var uid = feed.UID;
            files.Add(SandboxFile.Lazy("feeds/" + feed.Slug + ".md", () => RenderFeed(scope, uid, lookup)));
        }

        files.Add(SandboxFile.Text("directory.tsv", tsv.ToString()));

        return Task.FromResult<IEnumerable<SandboxFile>>(files);
    }

    private static string RenderFeed(ToolScope scope, UID128 uid, FeedLookup lookup)
    {
        var node = scope.Q().StartAt(uid).AsEnumerable().FirstOrDefault();

        if (node is null) return "This feed no longer exists.";

        var feed = lookup.Feeds[uid];
        var sb = new StringBuilder();

        sb.AppendLine("# " + feed.Name).AppendLine();
        sb.AppendLine($"- uid: {uid}");
        sb.AppendLine($"- url: {feed.Url}");
        sb.AppendLine($"- category: {feed.Category}");
        sb.AppendLine($"- priority: {feed.Priority}");
        sb.AppendLine($"- source type: {feed.SourceType}");
        sb.AppendLine($"- publisher: {feed.Publisher}");
        sb.AppendLine($"- expected language: {feed.Language}");
        sb.AppendLine($"- article access: {feed.Access}");
        sb.AppendLine($"- market intelligence use: {node.GetString(N.Feed.MarketUse)}");
        sb.AppendLine($"- coverage focus: {node.GetString(N.Feed.CoverageFocus)}");
        sb.AppendLine($"- last fetch: {feed.LastStatus} at {node.Timestamp.ToDateTimeOffset():yyyy-MM-dd HH:mm} UTC, {feed.LastItems} items in window"
                    + (string.IsNullOrWhiteSpace(node.GetString(N.Feed.LastFetchError)) ? "" : $" ({node.GetString(N.Feed.LastFetchError)})"));
        sb.AppendLine($"- directory note: {node.GetString(N.Feed.OverlapNote)}");
        sb.AppendLine().AppendLine("## Latest articles").AppendLine();

        foreach (var article in scope.Q().StartAt(uid).Out(N.Article.Type, E.FeedHasArticle).SortByTimestamp(oldestFirst: false).Take(25).AsEnumerable())
        {
            sb.AppendLine($"- {article.Timestamp.ToDateTimeOffset():yyyy-MM-dd} `{article.UID}` {article.GetString(N.Article.Title)}");
        }

        return sb.ToString();
    }

    // ── articles ─────────────────────────────────────────────────────────────

    private static IEnumerable<ReadOnlyNode> Articles(ToolScope scope, DateTimeOffset from, DateTimeOffset to) =>
        scope.Q().StartAt(N.Article.Type)
             .WhereTimestamp(new Time(from), new Time(to), true)
             .SortByTimestamp(oldestFirst: false)
             .AsEnumerable();

    private static IEnumerable<ReadOnlyNode> ArticlesOn(ToolScope scope, DateTime day) =>
        Articles(scope, new DateTimeOffset(day, TimeSpan.Zero), new DateTimeOffset(day.AddDays(1).AddTicks(-1), TimeSpan.Zero));

    private static string Tsv(ToolScope scope, IEnumerable<ReadOnlyNode> articles, int limit = int.MaxValue)
    {
        var lookup = FeedLookup.Build(scope);
        var sb = new StringBuilder("#published\tuid\tcategory\tpriority\tpublisher\tlang\tcontent\twords\ttitle\turl\n");
        var count = 0;

        foreach (var node in articles)
        {
            if (count++ >= limit) break;
            AppendRow(sb, node, lookup.PrimaryFeedOf(scope, node.UID));
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, ReadOnlyNode node, FeedInfo feed)
    {
        sb.Append(node.Timestamp.ToDateTimeOffset().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\t')
          .Append(node.UID).Append('\t')
          .Append(Clean(feed?.Category)).Append('\t')
          .Append(feed?.Priority ?? "").Append('\t')
          .Append(Clean(feed?.Publisher)).Append('\t')
          .Append(node.GetString(N.Article.Language)).Append('\t')
          .Append(node.GetString(N.Article.ContentSource)).Append('\t')
          .Append(node.GetInt(N.Article.WordCount)).Append('\t')
          .Append(Clean(node.GetString(N.Article.Title))).Append('\t')
          .Append(node.GetString(N.Article.Url)).Append('\n');
    }

    private static string RenderArticle(ToolScope scope, UID128 uid)
    {
        var node = scope.Q().StartAt(uid).AsEnumerable().FirstOrDefault();

        if (node is null || node.Type != N.Article.Type) return null;

        var lookup = FeedLookup.Build(scope);
        var primary = lookup.PrimaryFeedOf(scope, uid);
        var feeds = scope.Q().StartAt(uid).Out(N.Feed.Type, E.AppearedIn).AsUIDEnumerable().Where(lookup.Feeds.ContainsKey).Select(f => lookup.Feeds[f]).OrderBy(f => f == primary ? 0 : 1).ThenBy(f => f.Rank).ToList();
        var tags = scope.Q().StartAt(uid).Out(N.Tag.Type, E.TaggedWith).AsEnumerable().Select(t => t.GetString(N.Tag.Name)).ToList();
        var note = node.GetString(N.Article.ExtractionNote);
        var sb = new StringBuilder();

        sb.AppendLine("# " + node.GetString(N.Article.Title)).AppendLine();
        sb.AppendLine($"- uid: {uid}");
        sb.AppendLine($"- published: {node.Timestamp.ToDateTimeOffset():yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- url: {node.GetString(N.Article.Url)}");
        sb.AppendLine($"- publisher: {feeds.FirstOrDefault()?.Publisher ?? node.GetString(N.Article.SiteName)}");
        sb.AppendLine($"- author: {node.GetString(N.Article.Author)}");
        sb.AppendLine($"- language: {node.GetString(N.Article.Language)}");
        sb.AppendLine($"- content: {node.GetString(N.Article.ContentSource)}, {node.GetInt(N.Article.WordCount)} words" + (string.IsNullOrWhiteSpace(note) ? "" : $" ({note})"));
        sb.AppendLine($"- feeds: {string.Join("; ", feeds.Select(f => $"{f.Name} [{f.Priority}, {f.Category}]"))}");

        if (tags.Count > 0) sb.AppendLine($"- publisher tags: {string.Join(", ", tags)}");

        var mentions = MentionsOf(scope, uid).Select(e => $"{EntityName(e)} ({EntityKind(e)})").ToList();
        if (mentions.Count > 0) sb.AppendLine($"- mentions: {string.Join("; ", mentions)}");

        sb.AppendLine().AppendLine("## Excerpt").AppendLine().AppendLine(node.GetString(N.Article.Excerpt));
        sb.AppendLine().AppendLine("## Body").AppendLine().AppendLine(node.GetString(N.Article.Body));

        return sb.ToString();
    }

    // ── news / stats ─────────────────────────────────────────────────────────

    private static string News(SandboxCommandCall call)
    {
        var args = Options.Parse(call.Arguments);

        if (args.Error is not null) return call.Fail(args.Error);

        if (!args.ResolveMentions(call.Scope, out var mentionError)) return call.Fail(mentionError);

        var lookup = FeedLookup.Build(call.Scope);
        var sb = new StringBuilder(args.Json ? "" : "#published\tuid\tcategory\tpriority\tpublisher\tlang\tcontent\twords\ttitle\turl\n");
        var rows = 0;
        var more = 0;

        foreach (var node in Articles(call.Scope, args.From, args.To))
        {
            var feed = lookup.PrimaryFeedOf(call.Scope, node.UID);

            if (!args.Matches(node, feed)) continue;

            if (rows >= args.Limit) { more++; continue; }

            rows++;

            if (args.Json)
            {
                sb.AppendLine(JsonSerializer.Serialize(new
                {
                    published = node.Timestamp.ToDateTimeOffset().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    uid = node.UID.ToString(),
                    category = feed?.Category,
                    priority = feed?.Priority,
                    publisher = feed?.Publisher,
                    feed = feed?.Name,
                    lang = node.GetString(N.Article.Language),
                    content = node.GetString(N.Article.ContentSource),
                    words = node.GetInt(N.Article.WordCount),
                    title = node.GetString(N.Article.Title),
                    url = node.GetString(N.Article.Url),
                    excerpt = node.GetString(N.Article.Excerpt),
                    mentions = MentionsOf(call.Scope, node.UID).Select(EntityName).ToArray(),
                }));
            }
            else
            {
                AppendRow(sb, node, feed);
            }
        }

        if (more > 0) sb.AppendLine($"# {more} more not shown - narrow the filters or raise --limit (max {MAX_NEWS_ROWS})");
        if (rows == 0) sb.AppendLine($"# no articles between {args.From:yyyy-MM-dd HH:mm} and {args.To:yyyy-MM-dd HH:mm} UTC match");

        return sb.ToString();
    }

    private static string Stats(SandboxCommandCall call)
    {
        var args = Options.Parse(call.Arguments);

        if (args.Error is not null) return call.Fail(args.Error);

        if (!args.ResolveMentions(call.Scope, out var mentionError)) return call.Fail(mentionError);

        var lookup = FeedLookup.Build(call.Scope);
        var byCategory = new Dictionary<string, int>();
        var byMention = new Dictionary<string, int>();
        var byPriority = new Dictionary<string, int>();
        var byPublisher = new Dictionary<string, int>();
        var byDay = new SortedDictionary<string, int>();
        var byContent = new Dictionary<string, int>();
        var feedsSeen = new HashSet<UID128>();
        var total = 0;

        foreach (var node in Articles(call.Scope, args.From, args.To))
        {
            var feed = lookup.PrimaryFeedOf(call.Scope, node.UID);

            if (!args.Matches(node, feed)) continue;

            total++;

            foreach (var entity in MentionsOf(call.Scope, node.UID)) Bump(byMention, EntityKind(entity) + "\t" + EntityName(entity));

            Bump(byCategory, feed?.Category ?? "(none)");
            Bump(byPriority, feed?.Priority ?? "(none)");
            Bump(byPublisher, feed?.Publisher ?? "(none)");
            Bump(byContent, node.GetString(N.Article.ContentSource));
            byDay[node.Timestamp.ToDateTimeOffset().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = byDay.GetValueOrDefault(node.Timestamp.ToDateTimeOffset().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + 1;

            foreach (var f in call.Scope.Q().StartAt(node.UID).Out(N.Feed.Type, E.AppearedIn).AsUIDEnumerable()) feedsSeen.Add(f);
        }

        var sb = new StringBuilder();

        sb.AppendLine($"# window {args.From:yyyy-MM-dd HH:mm} .. {args.To:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"articles\t{total}");
        sb.AppendLine($"feeds_with_articles\t{feedsSeen.Count}\tof {lookup.Feeds.Count} monitored");
        sb.AppendLine($"publishers_with_articles\t{feedsSeen.Where(lookup.Feeds.ContainsKey).Select(f => lookup.Feeds[f].Publisher).Distinct().Count()}");

        Section(sb, "by_category", byCategory);
        Section(sb, "by_priority", byPriority);
        Section(sb, "by_publisher", byPublisher, 15);
        Section(sb, "by_content", byContent);

        //Tab-separated kind and name, so the section reads as three columns: kind, name, articles.
        sb.AppendLine().AppendLine("## top_mentions");
        foreach (var (key, n) in byMention.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(30)) sb.AppendLine($"{key}\t{n}");

        sb.AppendLine().AppendLine("## by_day");
        foreach (var (day, n) in byDay) sb.AppendLine($"{day}\t{n}");

        var failing = lookup.Feeds.Values.Where(f => f.LastStatus != "OK" && f.LastStatus != "Empty").OrderBy(f => f.Rank).ToList();
        var silent = lookup.Feeds.Values.Where(f => f.Priority == "Core" && !feedsSeen.Contains(f.UID)).OrderBy(f => f.Name).ToList();

        sb.AppendLine().AppendLine($"## feed_health\nfailing_on_last_fetch\t{failing.Count}\ncore_feeds_without_articles\t{silent.Count}");
        foreach (var f in failing.Take(20)) sb.AppendLine($"failing\t{f.Name}\t{f.LastStatus}");
        foreach (var f in silent.Take(20)) sb.AppendLine($"silent\t{f.Name}\t{f.Category}");

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string name, Dictionary<string, int> counts, int max = 50)
    {
        sb.AppendLine().AppendLine("## " + name);
        foreach (var (key, n) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(max)) sb.AppendLine($"{Clean(key)}\t{n}");
    }

    private static void Bump(Dictionary<string, int> counts, string key) => counts[key ?? ""] = counts.GetValueOrDefault(key ?? "") + 1;

    // ── mentions ─────────────────────────────────────────────────────────────

    private static IEnumerable<ReadOnlyNode> MentionsOf(ToolScope scope, UID128 article) =>
        scope.Q().StartAt(article).Out(edgeType: E._Mentions).AsEnumerable()
             .Where(n => EntityTypes.Contains(n.Type))
             .OrderBy(n => Array.IndexOf(EntityTypes, n.Type)).ThenBy(EntityName);

    private static string EntityName(ReadOnlyNode n) => n.Type switch
    {
        _ when n.Type == N.Organisation.Type => n.GetString(N.Organisation.Name),
        _ when n.Type == N.AircraftType.Type => n.GetString(N.AircraftType.Name),
        _ when n.Type == N.EngineType.Type => n.GetString(N.EngineType.Name),
        _ when n.Type == N.LaunchVehicle.Type => n.GetString(N.LaunchVehicle.Name),
        _ => n.GetString(N.Airport.Name),
    };

    private static string EntityKind(ReadOnlyNode n) => n.Type switch
    {
        _ when n.Type == N.Organisation.Type => n.GetString(N.Organisation.Kind),
        _ when n.Type == N.AircraftType.Type => "Aircraft type",
        _ when n.Type == N.EngineType.Type => "Engine type",
        _ when n.Type == N.LaunchVehicle.Type => "Launch vehicle",
        _ => "Airport",
    };

    // ── render-report ────────────────────────────────────────────────────────

    private static async Task<string> RenderReportAsync(SandboxCommandCall call)
    {
        if (call.Arguments.Count < 2 || (call.Arguments[0] != "report" && call.Arguments[0] != "deck"))
        {
            return call.Fail("usage: render-report report|deck <data.json> [<name>]");
        }

        var template = call.Arguments[0] == "deck" ? DeckTemplate : ReportTemplate;
        var dataPath = call.Files.ResolvePath(call.Arguments[1]);

        if (!await call.Files.ExistsAsync(dataPath, call.CancellationToken)) return call.Fail($"{dataPath} does not exist - write the report JSON first");

        var json = await call.Files.ReadTextAsync(dataPath, call.CancellationToken);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            return call.Fail($"{dataPath} is not valid JSON: {e.Message}");
        }

        var problems = Validate(document.RootElement);

        if (problems.Count > 0) return call.Fail("the report does not match /templates/README.md:\n  - " + string.Join("\n  - ", problems));

        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString();
        var name = call.Arguments.Count > 2
            ? call.Arguments[2]
            : Slug((root.TryGetProperty("fileName", out var fn) ? fn.GetString() : null) ?? (kind + "-" + (root.GetProperty("period").TryGetProperty("to", out var to) ? to.GetString() : "report"))) + (call.Arguments[0] == "deck" ? "-deck" : "");

        if (name.EndsWith(".html")) name = name.Substring(0, name.Length - 5);

        //Re-serialised compactly, with "</" escaped so no string in the data can close the script element.
        var data = JsonSerializer.Serialize(root).Replace("</", "<\\/");
        //The artifact is named from the page's <title> when it is published, so the report's own title
        //is written there rather than left to the script that sets it when the page runs.
        var title = System.Net.WebUtility.HtmlEncode(root.GetProperty("title").GetString() + (call.Arguments[0] == "deck" ? " (deck)" : ""));
        var html = Regex.Replace(template, "<title>[^<]*</title>", "<title>" + title.Replace("$", "$$") + "</title>")
                         .Replace("/*REPORT_DATA*/", data);
        //An agent the conversation delegated to may write only in its own directory under /agents and
        //cannot publish, so its render lands there and the conversation copies it into /artifacts.
        var delegated = call.WorkingDirectory.StartsWith("/agents/", StringComparison.Ordinal);
        var path = (delegated ? call.WorkingDirectory.TrimEnd('/') : "/artifacts") + "/" + name + ".html";

        await call.Files.WriteTextAsync(path, html, call.CancellationToken);

        var sections = root.GetProperty("sections").GetArrayLength();
        var items = root.GetProperty("sections").EnumerateArray().Sum(s => s.TryGetProperty("items", out var i) ? i.GetArrayLength() : 0);
        var next = delegated
            ? $"this run cannot publish: return {path} - the conversation copies it to /artifacts and publishes it"
            : $"publish {name}.html";

        return $"wrote {path} ({(call.Arguments[0] == "deck" ? "PowerPoint deck page" : "HTML report")}, {sections} sections, {items} items, {html.Length / 1024} KB)\nnext: {next}";
    }

    private static List<string> Validate(JsonElement root)
    {
        var problems = new List<string>();

        string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        if (root.ValueKind != JsonValueKind.Object) { problems.Add("the top level must be an object"); return problems; }

        if (Str(root, "kind") is not ("daily" or "weekly")) problems.Add("\"kind\" must be \"daily\" or \"weekly\"");
        if (string.IsNullOrWhiteSpace(Str(root, "title"))) problems.Add("\"title\" is required");
        if (string.IsNullOrWhiteSpace(Str(root, "headline"))) problems.Add("\"headline\" is required - one sentence, the top line of the period");

        if (!root.TryGetProperty("period", out var period) || string.IsNullOrWhiteSpace(Str(period, "from")) || string.IsNullOrWhiteSpace(Str(period, "label")))
        {
            problems.Add("\"period\" needs \"from\", \"to\" and \"label\"");
        }

        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Array || summary.GetArrayLength() == 0)
        {
            problems.Add("\"summary\" must hold 3-5 bullet strings");
        }

        if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array || sections.GetArrayLength() == 0)
        {
            problems.Add("\"sections\" must hold at least one section");
            return problems;
        }

        var s = 0;

        foreach (var section in sections.EnumerateArray())
        {
            s++;
            if (string.IsNullOrWhiteSpace(Str(section, "title"))) problems.Add($"section {s} has no \"title\"");

            if (!section.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) { problems.Add($"section {s} has no \"items\" array"); continue; }

            var i = 0;

            foreach (var item in items.EnumerateArray())
            {
                i++;
                var where = $"section {s} item {i}";

                if (string.IsNullOrWhiteSpace(Str(item, "headline"))) problems.Add($"{where} has no \"headline\"");
                if (string.IsNullOrWhiteSpace(Str(item, "so_what"))) problems.Add($"{where} has no \"so_what\" - say why it matters, not what happened");
                if (Str(item, "signal") is { } sig && sig is not ("positive" or "negative" or "watch" or "neutral")) problems.Add($"{where}: \"signal\" must be positive, negative, watch or neutral");

                if (!item.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
                {
                    problems.Add($"{where} cites no \"sources\" - every item links to at least one article");
                    continue;
                }

                foreach (var source in sources.EnumerateArray())
                {
                    var url = Str(source, "url");
                    if (url is null || !(url.StartsWith("http://") || url.StartsWith("https://"))) problems.Add($"{where} has a source without an http(s) \"url\"");
                }
            }
        }

        return problems;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool TryDay(string text, out DateTime day) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out day);

    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static string Slug(string s) =>
        new string((s ?? "report").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-').Replace("--", "-");

    /// <summary>One feed as the files describe it, resolved once per script rather than once per article.</summary>
    private sealed class FeedInfo
    {
        public UID128 UID { get; init; }
        public string Name { get; init; }
        public string Url { get; init; }
        public string Slug { get; init; }
        public string Category { get; init; }
        public string Priority { get; init; }
        public int Rank { get; init; }
        public string SourceType { get; init; }
        public string Publisher { get; init; }
        public string Language { get; init; }
        public string Access { get; init; }
        public string LastStatus { get; init; }
        public int LastItems { get; init; }
        public string LastItemAt { get; init; }
    }

    private sealed class FeedLookup
    {
        public Dictionary<UID128, FeedInfo> Feeds { get; } = new();

        public static FeedLookup Build(ToolScope scope)
        {
            var lookup = new FeedLookup();
            var slugs = new HashSet<string>();

            foreach (var feed in scope.Q().StartAt(N.Feed.Type).AsEnumerable())
            {
                string One(string type, string edge, string field) =>
                    scope.Q().StartAt(feed.UID).Out(type, edge).AsEnumerable().Select(n => n.GetString(field)).FirstOrDefault() ?? "";

                var priority = scope.Q().StartAt(feed.UID).Out(N.Priority.Type, E.FeedHasPriority).AsEnumerable().FirstOrDefault();
                var name = feed.GetString(N.Feed.Name);
                var slug = Slug(name);

                while (!slugs.Add(slug)) slug += "-x";

                var lastItem = feed.GetTime(N.Feed.LastItemAt).ToDateTimeOffset();

                lookup.Feeds[feed.UID] = new FeedInfo
                {
                    UID = feed.UID,
                    Name = name,
                    Url = feed.GetString(N.Feed.FeedUrl),
                    Slug = slug,
                    Category = One(N.FeedCategory.Type, E.FeedInCategory, N.FeedCategory.Name),
                    Priority = priority?.GetString(N.Priority.Name) ?? "",
                    Rank = priority?.GetInt(N.Priority.Rank) ?? 9,
                    SourceType = One(N.SourceType.Type, E.FeedOfSourceType, N.SourceType.Name),
                    Publisher = One(N.Publisher.Type, E.PublishedBy, N.Publisher.Name),
                    Language = feed.GetString(N.Feed.ExpectedLanguage),
                    Access = feed.GetString(N.Feed.AccessNote),
                    LastStatus = feed.GetString(N.Feed.LastFetchStatus),
                    LastItems = feed.GetInt(N.Feed.LastItemCount),
                    LastItemAt = lastItem.Year > 2000 ? lastItem.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
                };
            }

            return lookup;
        }

        /// <summary>
        /// The feed an article is filed under: its highest-priority feed, and among equals the
        /// publisher's own newsroom over a trade title that re-posted it.
        /// </summary>
        public FeedInfo PrimaryFeedOf(ToolScope scope, UID128 article) =>
            scope.Q().StartAt(article).Out(N.Feed.Type, E.AppearedIn).AsUIDEnumerable()
                 .Where(Feeds.ContainsKey).Select(f => Feeds[f])
                 .OrderBy(f => f.Rank)
                 .ThenBy(f => f.SourceType.StartsWith("Official", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                 .ThenBy(f => f.Name)
                 .FirstOrDefault();
    }

    /// <summary>The filters `news` and `stats` share.</summary>
    private sealed class Options
    {
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; } = DateTimeOffset.UtcNow.AddHours(1);
        public string Category { get; private set; }
        public string Priority { get; private set; }
        public string Publisher { get; private set; }
        public string Feed { get; private set; }
        public string Lang { get; private set; }
        public string Grep { get; private set; }
        public string Content { get; private set; }
        public string Mentions { get; private set; }
        public HashSet<UID128> MentionedArticles { get; private set; }
        public int Limit { get; private set; } = 200;
        public bool Json { get; private set; }
        public string Error { get; private set; }

        public static Options Parse(IReadOnlyList<string> argv)
        {
            var o = new Options { From = DateTimeOffset.UtcNow.AddDays(-1) };

            for (var i = 0; i < argv.Count; i++)
            {
                string Next() => i + 1 < argv.Count ? argv[++i] : null;

                switch (argv[i])
                {
                    case "--since": o.From = ParseWhen(Next(), o, isEnd: false); break;
                    case "--until": o.To = ParseWhen(Next(), o, isEnd: true); break;
                    case "--category": o.Category = Next(); break;
                    case "--priority": o.Priority = Next(); break;
                    case "--publisher": o.Publisher = Next(); break;
                    case "--feed": o.Feed = Next(); break;
                    case "--lang": o.Lang = Next(); break;
                    case "--grep": o.Grep = Next(); break;
                    case "--content": o.Content = Next(); break;
                    case "--mentions": o.Mentions = Next(); break;
                    case "--json": o.Json = true; break;
                    case "--limit": o.Limit = int.TryParse(Next(), out var n) ? Math.Clamp(n, 1, MAX_NEWS_ROWS) : 200; break;
                    default: o.Error = $"unknown option '{argv[i]}' - see `help news`"; break;
                }
            }

            return o;
        }

        private static DateTimeOffset ParseWhen(string text, Options o, bool isEnd)
        {
            if (string.IsNullOrWhiteSpace(text)) { o.Error = "--since / --until need a value: 1d, 7d, 24h or YYYY-MM-DD"; return DateTimeOffset.UtcNow; }

            var now = DateTimeOffset.UtcNow;

            if (text.EndsWith("d") && int.TryParse(text[..^1], out var days)) return now.AddDays(-days);
            if (text.EndsWith("h") && int.TryParse(text[..^1], out var hours)) return now.AddHours(-hours);

            if (TryDay(text, out var day)) return new DateTimeOffset(isEnd ? day.AddDays(1).AddTicks(-1) : day, TimeSpan.Zero);

            o.Error = $"'{text}' is not 1d, 7d, 24h or YYYY-MM-DD";
            return now;
        }

        /// <summary>
        /// --mentions as the set of articles its entities appear in: the entities named exactly
        /// that when there are any, otherwise every one whose name contains the text.
        /// </summary>
        public bool ResolveMentions(ToolScope scope, out string error)
        {
            error = null;

            if (Mentions is null) return true;

            var all = EntityTypes.SelectMany(t => scope.Q().StartAt(t).AsEnumerable()).ToList();
            var exact = all.Where(e => string.Equals(EntityName(e), Mentions, StringComparison.OrdinalIgnoreCase)).ToList();
            var entities = exact.Count > 0 ? exact : all.Where(e => EntityName(e).Contains(Mentions, StringComparison.OrdinalIgnoreCase)).ToList();

            if (entities.Count == 0)
            {
                error = $"no organisation, aircraft type, engine type, launch vehicle or airport is named like '{Mentions}' - try a shorter name";
                return false;
            }

            MentionedArticles = new HashSet<UID128>(entities.SelectMany(e => scope.Q().StartAt(e.UID).Out(N.Article.Type, E._AppearsIn).AsUIDEnumerable()));
            return true;
        }

        public bool Matches(ReadOnlyNode node, FeedInfo feed)
        {
            if (MentionedArticles is not null && !MentionedArticles.Contains(node.UID)) return false;

            static bool Has(string value, string filter) => filter is null || (value ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);

            if (!Has(feed?.Category, Category) || !Has(feed?.Publisher, Publisher) || !Has(feed?.Name, Feed)) return false;
            if (Priority is not null && !string.Equals(feed?.Priority, Priority, StringComparison.OrdinalIgnoreCase)) return false;
            if (Lang is not null && !string.Equals(node.GetString(N.Article.Language), Lang, StringComparison.OrdinalIgnoreCase)) return false;

            if (Content is not null)
            {
                var source = node.GetString(N.Article.ContentSource);
                var isPage = source.StartsWith("Page") || source == "Feed full text";

                if (Content.Equals("page", StringComparison.OrdinalIgnoreCase) != isPage) return false;
            }

            return Grep is null || Has(node.GetString(N.Article.Title), Grep) || Has(node.GetString(N.Article.Excerpt), Grep);
        }
    }

    // ── templates (generated by tools/embed_templates.py from src/AviationIntel.Definitions/templates) ──
    // <templates>
    private const string TemplatesReadme = """
# /templates - the report templates

Two templates, one data file. You write the report **once**, as JSON, and render it as:

- `render-report report /work/<file>.json` - `report.html`, a self-contained HTML brief (KPI tiles,
  executive summary, charts, sections of item cards with signal chips, watchlist, coverage note).
  Light and dark theme, readable in the side pane, links on every source.
- `render-report deck /work/<file>.json` - `deck.html`, a page that builds a native PowerPoint deck
  with PptxGenJS and hands it over from a download button: title, summary + KPIs, native charts,
  one slide per section (first four items), watchlist table, sources appendix.

Both write to `/artifacts/`; then `publish <name>.html`. `sample-daily.json` is a complete example.
Never edit the HTML templates for one report - everything that varies is in the JSON.

## The JSON contract

```jsonc
{
  "kind": "daily",                       // "daily" | "weekly"                          (required)
  "title": "Aviation Daily Brief",       //                                             (required)
  "fileName": "aviation-daily-2026-10-06", // optional; names the artifact and the .pptx
  "period": { "from": "2026-10-05", "to": "2026-10-06", "label": "Tuesday 6 October 2026" }, // (required)
  "generatedAt": "2026-10-06 18:00 UTC",
  "audience": "Market intelligence desk",
  "headline": "One sentence: the single most important thing in the period.",            // (required)
  "summary": ["3-5 bullets, each a complete, specific sentence with its number"],         // (required)
  "metrics": [ { "label": "Articles reviewed", "value": "128", "note": "last 24 h" } ],   // 3-4 tiles
  "charts": {
    "byCategory":  [ { "label": "Airlines and commercial aviation", "value": 12 } ],      // from `stats`
    "byPublisher": [ { "label": "AeroTime", "value": 9 } ],                               // daily: right-hand chart
    "byDay":       [ { "label": "2026-10-01", "value": 31 } ]                             // weekly: right-hand chart
  },
  "sections": [                                                                           // (required, >= 1)
    {
      "title": "Airlines",
      "takeaway": "One line: what this section adds up to.",
      "items": [
        {
          "headline": "Short noun-phrase headline, your words, not the source's",          // (required)
          "so_what": "Why it matters to the market - the implication, with the number.",   // (required)
          "detail": "Optional supporting context (HTML report only).",
          "signal": "positive",          // positive | negative | watch | neutral
          "sources": [                   // (required, >= 1, http(s) url each)
            { "title": "Article title", "publisher": "Publisher", "url": "https://...", "published": "2026-10-06",
              "uid": "article uid", "note": "summary only" }
          ]
        }
      ]
    }
  ],
  "watchlist": [ { "item": "What to follow", "why": "Why it matters", "next": "Date or event" } ],
  "coverage": { "articles": 128, "feeds": 64, "publishers": 51, "summaryOnly": 9,
                "notes": "What you could not read, and anything that limits the report." }
}
```

`render-report` refuses a file that misses a required field, an item without `so_what`, or an item
without a source URL, and says which.

## Where the numbers come from

- `stats --since 1d` (daily) or `stats --since 7d` (weekly): `articles`, `feeds_with_articles`,
  `publishers_with_articles`, `by_category`, `by_publisher`, `by_day`, `by_content`
  (`Feed summary only` is `coverage.summaryOnly`).
- Figures inside items come from the article text - quote them as the source states them.
""";

    private const string ReportTemplate = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Aviation Market Brief</title>
<style>
  :root {
    --bg: #f6f5f1; --surface: #ffffff; --fg: #17202b; --muted: #5d6673; --line: #dfdcd3;
    --accent: #0e4c7a; --accent-soft: #e3edf5; --on-accent: #ffffff;
    --pos: #1d7a46; --neg: #b3361f; --watch: #a36a00; --neutral: #5d6673;
    --bar: #0e4c7a; --bar-2: #7fa7c4;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #11161d; --surface: #171e27; --fg: #e8ecf1; --muted: #9aa5b3; --line: #29323e;
      --accent: #6cb4e8; --accent-soft: #1b2a3a; --on-accent: #0b1622;
      --pos: #5ccf8d; --neg: #ff8a73; --watch: #f2b84b; --neutral: #9aa5b3;
      --bar: #6cb4e8; --bar-2: #35607f;
    }
  }
  * { box-sizing: border-box; }
  body { margin: 0; padding: 28px 16px 48px; background: var(--bg); color: var(--fg);
         font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; line-height: 1.5; }
  main { max-width: 920px; margin: 0 auto; display: grid; gap: 28px; }
  .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
  header { display: grid; gap: 8px; border-bottom: 3px solid var(--accent); padding-bottom: 18px; }
  .kicker { margin: 0; font: 600 12px/1.4 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            letter-spacing: .08em; text-transform: uppercase; color: var(--accent); }
  h1 { margin: 0; font-size: clamp(26px, 4vw, 36px); line-height: 1.15; text-wrap: balance; }
  .headline { margin: 0; font-size: 18px; color: var(--fg); max-width: 70ch; }
  .meta { margin: 0; font-size: 13px; color: var(--muted); }
  h2 { margin: 0 0 12px; font-size: 20px; }
  h3 { margin: 0; font-size: 16px; line-height: 1.35; }
  p { margin: 0; }
  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
  .tile { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 14px 16px; }
  .tile .value { font-size: 28px; font-weight: 700; line-height: 1.1; color: var(--accent); }
  .tile .label { font-size: 13px; font-weight: 600; margin-top: 4px; }
  .tile .note { font-size: 12px; color: var(--muted); }
  .summary { background: var(--accent-soft); border-radius: 10px; padding: 18px 20px; }
  .summary ul { margin: 0; padding-left: 20px; display: grid; gap: 8px; }
  .charts { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; }
  .chart { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 16px; }
  .chart h3 { font-size: 14px; margin-bottom: 10px; }
  .chart svg { width: 100%; height: auto; display: block; }
  .chart text { fill: var(--fg); font-size: 11px; font-family: system-ui, sans-serif; }
  .chart .axis { fill: var(--muted); }
  section.topic { display: grid; gap: 12px; }
  .topic-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 6px 16px;
                border-bottom: 1px solid var(--line); padding-bottom: 6px; }
  .topic-head h2 { margin: 0; }
  .takeaway { color: var(--muted); font-size: 14px; }
  .items { display: grid; gap: 12px; }
  .item { background: var(--surface); border: 1px solid var(--line); border-left: 4px solid var(--neutral);
          border-radius: 8px; padding: 14px 16px; display: grid; gap: 8px; }
  .item.positive { border-left-color: var(--pos); } .item.negative { border-left-color: var(--neg); }
  .item.watch { border-left-color: var(--watch); }
  .item-top { display: flex; gap: 10px; align-items: flex-start; justify-content: space-between; }
  .chip { flex: none; font: 600 11px/1 ui-monospace, Menlo, monospace; text-transform: uppercase; letter-spacing: .06em;
          padding: 5px 8px; border-radius: 999px; border: 1px solid currentColor; color: var(--neutral); }
  .chip.positive { color: var(--pos); } .chip.negative { color: var(--neg); } .chip.watch { color: var(--watch); }
  .so-what { font-weight: 600; }
  .detail { font-size: 14px; color: var(--muted); }
  .sources { margin: 0; padding: 0; list-style: none; display: flex; flex-wrap: wrap; gap: 6px 14px; font-size: 12px; color: var(--muted); }
  .sources a { color: var(--accent); }
  table { width: 100%; border-collapse: collapse; font-size: 14px; }
  .table-wrap { overflow-x: auto; background: var(--surface); border: 1px solid var(--line); border-radius: 10px; }
  th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--line); vertical-align: top; }
  th { font-size: 12px; text-transform: uppercase; letter-spacing: .05em; color: var(--muted); }
  tr:last-child td { border-bottom: 0; }
  footer { font-size: 12px; color: var(--muted); border-top: 1px solid var(--line); padding-top: 14px; display: grid; gap: 6px; }
  .empty { color: var(--muted); font-style: italic; }
  a:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  @media (prefers-reduced-motion: reduce) { * { transition: none !important; animation: none !important; } }
</style>
</head>
<body>
<main id="report" aria-live="polite"></main>

<!-- The report's content. render-report replaces the marker below with the report JSON; nothing
     else in this file needs to change for a new report. -->
<script id="report-data" type="application/json">/*REPORT_DATA*/</script>

<script>
(function () {
  const root = document.getElementById('report');
  let data;

  try {
    data = JSON.parse(document.getElementById('report-data').textContent);
  } catch (err) {
    root.textContent = 'This report could not be read: ' + err.message;
    return;
  }

  const SIGNALS = { positive: 'Positive', negative: 'Negative', watch: 'Watch', neutral: 'Context' };

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined && text !== null) e.textContent = String(text);
    return e;
  }

  function add(parent, ...children) { children.filter(Boolean).forEach(c => parent.appendChild(c)); return parent; }

  function safeUrl(u) { return /^https?:\/\//i.test(u || '') ? u : null; }

  //A horizontal bar chart drawn to one scale: bars, ticks and labels all come from max.
  function barChart(title, rows, color) {
    if (!rows || !rows.length) return null;
    const box = el('div', 'chart');
    add(box, el('h3', null, title));

    const labelW = 150, barW = 180, rowH = 22, pad = 6, valueW = 30;
    const w = labelW + barW + valueW, h = rows.length * rowH + pad * 2;
    const max = Math.max(1, ...rows.map(r => Number(r.value) || 0));
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
    svg.setAttribute('role', 'img');
    svg.setAttribute('aria-label', title);

    rows.forEach((r, i) => {
      const y = pad + i * rowH;
      const v = Number(r.value) || 0;
      const label = document.createElementNS(ns, 'text');
      label.setAttribute('x', labelW - 8); label.setAttribute('y', y + rowH / 2 + 4); label.setAttribute('text-anchor', 'end');
      label.textContent = String(r.label).length > 26 ? String(r.label).slice(0, 25) + '…' : String(r.label);
      const rect = document.createElementNS(ns, 'rect');
      rect.setAttribute('x', labelW); rect.setAttribute('y', y + 4); rect.setAttribute('height', rowH - 8);
      rect.setAttribute('width', Math.max(2, barW * v / max)); rect.setAttribute('rx', 3);
      rect.setAttribute('fill', color);
      const value = document.createElementNS(ns, 'text');
      value.setAttribute('x', labelW + Math.max(2, barW * v / max) + 6); value.setAttribute('y', y + rowH / 2 + 4);
      value.setAttribute('class', 'axis'); value.textContent = v;
      svg.appendChild(label); svg.appendChild(rect); svg.appendChild(value);
    });

    box.appendChild(svg);
    return box;
  }

  function sourcesList(sources) {
    if (!sources || !sources.length) return null;
    const ul = el('ul', 'sources');
    sources.forEach(s => {
      const li = el('li');
      const url = safeUrl(s.url);
      const name = (s.publisher ? s.publisher + ': ' : '') + (s.title || s.url || 'source');
      if (url) { const a = el('a', null, name); a.href = url; a.rel = 'noopener'; li.appendChild(a); }
      else li.textContent = name;
      if (s.published) li.appendChild(document.createTextNode(' · ' + s.published));
      if (s.note) li.appendChild(document.createTextNode(' · ' + s.note));
      ul.appendChild(li);
    });
    return ul;
  }

  // ── header ──
  const kind = data.kind === 'weekly' ? 'Weekly market report' : 'Daily market brief';
  document.title = data.title || kind;
  const header = el('header');
  add(header,
      el('p', 'kicker', kind + (data.period && data.period.label ? ' · ' + data.period.label : '')),
      el('h1', null, data.title || kind),
      data.headline ? el('p', 'headline', data.headline) : null,
      el('p', 'meta', [data.audience, data.generatedAt ? 'Generated ' + data.generatedAt : null,
                       data.period && data.period.from ? 'Coverage ' + data.period.from + (data.period.to && data.period.to !== data.period.from ? ' to ' + data.period.to : '') : null]
                      .filter(Boolean).join(' · ')));
  root.appendChild(header);

  // ── metrics ──
  if (data.metrics && data.metrics.length) {
    const tiles = el('div', 'tiles');
    data.metrics.forEach(m => {
      const t = el('div', 'tile');
      add(t, el('div', 'value mono', m.value), el('div', 'label', m.label), m.note ? el('div', 'note', m.note) : null);
      tiles.appendChild(t);
    });
    root.appendChild(tiles);
  }

  // ── executive summary ──
  if (data.summary && data.summary.length) {
    const s = el('section', 'summary');
    add(s, el('h2', null, data.kind === 'weekly' ? 'The week in brief' : 'What matters today'));
    const ul = el('ul');
    data.summary.forEach(b => ul.appendChild(el('li', null, b)));
    s.appendChild(ul);
    root.appendChild(s);
  }

  // ── charts ──
  const charts = data.charts || {};
  const chartBoxes = [barChart('Articles by category', charts.byCategory, 'var(--bar)'),
                      barChart(data.kind === 'weekly' ? 'Articles per day' : 'Most active publishers', data.kind === 'weekly' ? charts.byDay : charts.byPublisher, 'var(--bar-2)')]
                     .filter(Boolean);
  if (chartBoxes.length) add(root, add(el('div', 'charts'), ...chartBoxes));

  // ── topic sections ──
  (data.sections || []).forEach(sec => {
    const s = el('section', 'topic');
    const head = el('div', 'topic-head');
    add(head, el('h2', null, sec.title), sec.takeaway ? el('p', 'takeaway', sec.takeaway) : null);
    s.appendChild(head);
    const items = el('div', 'items');
    (sec.items || []).forEach(it => {
      const signal = SIGNALS[it.signal] ? it.signal : 'neutral';
      const card = el('article', 'item ' + signal);
      const top = el('div', 'item-top');
      add(top, el('h3', null, it.headline), el('span', 'chip ' + signal, SIGNALS[signal]));
      add(card, top,
          it.so_what ? el('p', 'so-what', it.so_what) : null,
          it.detail ? el('p', 'detail', it.detail) : null,
          sourcesList(it.sources));
      items.appendChild(card);
    });
    if (!(sec.items || []).length) items.appendChild(el('p', 'empty', 'Nothing material in this period.'));
    s.appendChild(items);
    root.appendChild(s);
  });

  // ── watchlist ──
  if (data.watchlist && data.watchlist.length) {
    const s = el('section', 'topic');
    add(s, add(el('div', 'topic-head'), el('h2', null, 'Watchlist')));
    const wrap = el('div', 'table-wrap');
    const table = el('table');
    const thead = el('thead');
    add(thead, add(el('tr'), el('th', null, 'What'), el('th', null, 'Why it matters'), el('th', null, 'Next')));
    const tbody = el('tbody');
    data.watchlist.forEach(w => add(tbody, add(el('tr'), el('td', null, w.item), el('td', null, w.why), el('td', 'mono', w.next || '—'))));
    add(table, thead, tbody);
    add(s, add(wrap, table));
    root.appendChild(s);
  }

  // ── coverage ──
  const c = data.coverage || {};
  const footer = el('footer');
  add(footer,
      el('p', null, 'Based on ' + (c.articles ?? '?') + ' articles from ' + (c.feeds ?? '?') + ' feeds and ' + (c.publishers ?? '?') + ' publishers'
                    + (c.summaryOnly ? ', of which ' + c.summaryOnly + ' could be read from the feed summary only' : '') + '.'),
      c.notes ? el('p', null, c.notes) : null,
      el('p', null, 'Source text is quoted from public feeds and publisher pages; every claim links to its article. Figures are as published by the source and were not independently verified.'));
  root.appendChild(footer);
})();
</script>
</body>
</html>
""";

    private const string DeckTemplate = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Aviation Market Deck</title>
<script src="https://cdn.jsdelivr.net/gh/gitbrent/pptxgenjs/dist/pptxgen.bundle.js"></script>
<style>
  :root { --bg: #f4f3ef; --fg: #142030; --muted: #5b6573; --line: #d9d5cb; --accent: #0e4c7a; --on-accent: #ffffff; }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #10161e; --fg: #e6ebf1; --muted: #98a3b1; --line: #28313d; --accent: #6cb4e8; --on-accent: #0b1622; }
  }
  body { margin: 0; padding: 32px 16px; background: var(--bg); color: var(--fg);
         font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; line-height: 1.5; }
  main { max-width: 680px; margin: 0 auto; display: grid; gap: 20px; }
  .kicker { margin: 0; font: 600 12px/1.4 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
  h1 { margin: 0; font-size: 32px; line-height: 1.15; text-wrap: balance; }
  p { margin: 0; }
  .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
  button { font: 600 15px/1 system-ui, sans-serif; padding: 12px 18px; border-radius: 8px; border: 0;
           background: var(--accent); color: var(--on-accent); cursor: pointer; }
  button:disabled { opacity: .5; cursor: default; }
  button:focus-visible { outline: 2px solid var(--fg); outline-offset: 2px; }
  #status { color: var(--muted); font-size: 14px; }
  ol { margin: 0; padding: 0; list-style: none; border-top: 1px solid var(--line); counter-reset: s; }
  li { counter-increment: s; display: grid; grid-template-columns: 2.5em 1fr; padding: 10px 0; border-bottom: 1px solid var(--line); }
  li::before { content: counter(s, decimal-leading-zero); color: var(--muted); font: 13px ui-monospace, Menlo, monospace; padding-top: 2px; }
  .note { font-size: 13px; color: var(--muted); }
</style>
</head>
<body>
<main>
  <p class="kicker" id="kicker">PowerPoint deck &middot; 16:9</p>
  <h1 id="title">Aviation Market Deck</h1>
  <p id="headline"></p>
  <div class="actions">
    <button id="download" type="button" disabled>Download .pptx</button>
    <span id="status" role="status">Building the deck&hellip;</span>
  </div>
  <ol id="slides"></ol>
  <p class="note">Native PowerPoint text, shapes, charts and tables - every element is editable. Set in Segoe UI with Arial as fallback.</p>
</main>

<!-- The deck's content. render-report replaces the marker with the report JSON - the same JSON the
     HTML report template reads, so one data file makes both. -->
<script id="report-data" type="application/json">/*REPORT_DATA*/</script>

<script>
  // ── Flight Deck design system ──────────────────────────────────────────────
  // 13.333 x 7.5 in. Navy ground for title and divider slides, warm paper for content slides,
  // one accent, signal colours only on the signal bar of an item.
  const T = {
    navy: '0B2236', paper: 'F7F6F2', ink: '17202B', muted: '5D6673', line: 'DCD8CE', accent: '1F7AB8', accentSoft: 'DCEAF5',
    pos: '1D7A46', neg: 'B3361F', watch: 'B07400', neutral: '7A8491',
    head: 'Segoe UI', body: 'Segoe UI', mono: 'Consolas',
  };
  const SIGNAL = { positive: [T.pos, 'POSITIVE'], negative: [T.neg, 'NEGATIVE'], watch: [T.watch, 'WATCH'], neutral: [T.neutral, 'CONTEXT'] };

  function clip(s, n) { s = String(s == null ? '' : s); return s.length <= n ? s : s.slice(0, n - 1).trimEnd() + '…'; }

  function frame(pptx, slide, title, kicker, page) {
    slide.background = { color: T.paper };
    slide.addShape(pptx.ShapeType.rect, { x: 0, y: 0, w: 13.333, h: 0.12, fill: { color: T.accent }, line: { color: T.accent } });
    if (kicker) slide.addText(kicker.toUpperCase(), { x: 0.6, y: 0.38, w: 12, h: 0.3, margin: 0, fontFace: T.mono, fontSize: 11, color: T.accent, bold: true, charSpacing: 2 });
    slide.addText(clip(title, 90), { x: 0.6, y: 0.68, w: 12.1, h: 0.75, margin: 0, fontFace: T.head, fontSize: 28, bold: true, color: T.ink, valign: 'top' });
    slide.addShape(pptx.ShapeType.line, { x: 0.6, y: 6.92, w: 12.13, h: 0, line: { color: T.line, width: 0.75 } });
    slide.addText('Aviation Market Intelligence', { x: 0.6, y: 6.98, w: 6, h: 0.3, margin: 0, fontFace: T.body, fontSize: 10, color: T.muted });
    slide.addText(String(page), { x: 11.73, y: 6.98, w: 1, h: 0.3, margin: 0, fontFace: T.mono, fontSize: 10, color: T.muted, align: 'right' });
  }

  window.buildDeck = async function (data) {
    const pptx = new PptxGenJS();
    pptx.layout = 'LAYOUT_WIDE';
    pptx.title = data.title || 'Aviation market brief';
    pptx.company = 'Aviation Market Intelligence';
    const kind = data.kind === 'weekly' ? 'Weekly market report' : 'Daily market brief';
    const period = data.period && data.period.label ? data.period.label : '';
    const titles = [];
    let page = 1;

    // 1 ── title
    {
      const s = pptx.addSlide();
      s.background = { color: T.navy };
      s.addShape(pptx.ShapeType.rect, { x: 0.6, y: 1.55, w: 1.2, h: 0.08, fill: { color: T.accent }, line: { color: T.accent } });
      s.addText((kind + (period ? ' · ' + period : '')).toUpperCase(), { x: 0.6, y: 1.8, w: 12, h: 0.4, margin: 0, fontFace: T.mono, fontSize: 14, color: '9CC7E6', bold: true, charSpacing: 2 });
      s.addText(clip(data.title || kind, 80), { x: 0.6, y: 2.3, w: 11.5, h: 1.6, margin: 0, fontFace: T.head, fontSize: 44, bold: true, color: 'FFFFFF', valign: 'top' });
      if (data.headline) s.addText(clip(data.headline, 220), { x: 0.6, y: 4.1, w: 10.8, h: 1.3, margin: 0, fontFace: T.body, fontSize: 18, color: 'D5E2EE', valign: 'top' });
      s.addText(clip([data.audience, data.generatedAt ? 'Generated ' + data.generatedAt : null].filter(Boolean).join(' · '), 120),
                { x: 0.6, y: 6.6, w: 12, h: 0.35, margin: 0, fontFace: T.body, fontSize: 11, color: '8FA6BC' });
      titles.push(data.title || kind);
    }

    // 2 ── summary + KPIs
    if ((data.summary && data.summary.length) || (data.metrics && data.metrics.length)) {
      const s = pptx.addSlide(); page++;
      const title = data.kind === 'weekly' ? 'The week in brief' : 'What matters today';
      frame(pptx, s, title, kind, page); titles.push(title);
      const bullets = (data.summary || []).slice(0, 5).map(b => ({ text: clip(b, 230), options: { bullet: { indent: 18 }, paraSpaceAfter: 10 } }));
      if (bullets.length) s.addText(bullets, { x: 0.6, y: 1.65, w: 7.6, h: 5.0, margin: 0, fontFace: T.body, fontSize: 15, color: T.ink, valign: 'top' });
      (data.metrics || []).slice(0, 4).forEach((m, i) => {
        const y = 1.65 + i * 1.27;
        s.addShape(pptx.ShapeType.rect, { x: 8.75, y, w: 3.98, h: 1.1, fill: { color: 'FFFFFF' }, line: { color: T.line, width: 0.75 } });
        s.addText(clip(m.value, 12), { x: 8.95, y: y + 0.1, w: 3.6, h: 0.5, margin: 0, fontFace: T.head, fontSize: 24, bold: true, color: T.accent });
        s.addText(clip(m.label + (m.note ? ' · ' + m.note : ''), 60), { x: 8.95, y: y + 0.62, w: 3.6, h: 0.38, margin: 0, fontFace: T.body, fontSize: 11, color: T.muted });
      });
    }

    // 3 ── activity charts (native, editable)
    const charts = data.charts || {};
    const left = (charts.byCategory || []).slice(0, 10);
    const right = (data.kind === 'weekly' ? charts.byDay : charts.byPublisher) || [];
    if (left.length || right.length) {
      const s = pptx.addSlide(); page++;
      const title = 'Where the news came from';
      frame(pptx, s, title, 'Coverage', page); titles.push(title);
      const opts = { catAxisLabelFontFace: T.body, catAxisLabelFontSize: 10, valAxisLabelFontSize: 9, valAxisLabelColor: T.muted, catAxisLabelColor: T.ink,
                     showValue: true, dataLabelFontSize: 9, dataLabelColor: T.ink, valGridLine: { color: T.line, size: 0.5 }, showLegend: false };
      if (left.length) {
        s.addText('Articles by category', { x: 0.6, y: 1.55, w: 6, h: 0.35, margin: 0, fontFace: T.body, fontSize: 13, bold: true, color: T.ink });
        s.addChart(pptx.ChartType.bar, [{ name: 'Articles', labels: left.map(r => clip(r.label, 34)).reverse(), values: left.map(r => Number(r.value) || 0).reverse() }],
                   Object.assign({ x: 0.6, y: 1.95, w: 6.1, h: 4.8, barDir: 'bar', chartColors: [T.accent] }, opts));
      }
      if (right.length) {
        const label = data.kind === 'weekly' ? 'Articles per day' : 'Most active publishers';
        s.addText(label, { x: 7.0, y: 1.55, w: 5.7, h: 0.35, margin: 0, fontFace: T.body, fontSize: 13, bold: true, color: T.ink });
        //A horizontal bar chart draws its first category at the bottom, so it is reversed to read top-down.
        const ordered = data.kind === 'weekly' ? right.slice(0, 10) : right.slice(0, 10).reverse();
        s.addChart(pptx.ChartType.bar, [{ name: 'Articles', labels: ordered.map(r => clip(r.label, 24)), values: ordered.map(r => Number(r.value) || 0) }],
                   Object.assign({ x: 7.0, y: 1.95, w: 5.73, h: 4.8, barDir: data.kind === 'weekly' ? 'col' : 'bar', chartColors: ['7FA7C4'] }, opts));
      }
    }

    // 4..n ── one slide per section, up to four items each
    (data.sections || []).forEach(sec => {
      const items = (sec.items || []).slice(0, 4);
      if (!items.length) return;
      const s = pptx.addSlide(); page++;
      frame(pptx, s, sec.title, 'Section', page); titles.push(sec.title);
      if (sec.takeaway) s.addText(clip(sec.takeaway, 160), { x: 0.6, y: 1.42, w: 12.1, h: 0.45, margin: 0, fontFace: T.body, fontSize: 14, italic: true, color: T.muted });
      //Cards keep their size whatever the count, so two items read as two cards rather than two empty boxes.
      const top = 2.0, gap = 0.15, h = Math.min(1.55, (6.75 - top - gap * (items.length - 1)) / items.length);
      items.forEach((it, i) => {
        const y = top + i * (h + gap);
        const [color, word] = SIGNAL[it.signal] || SIGNAL.neutral;
        s.addShape(pptx.ShapeType.rect, { x: 0.6, y, w: 12.13, h, fill: { color: 'FFFFFF' }, line: { color: T.line, width: 0.75 } });
        s.addShape(pptx.ShapeType.rect, { x: 0.6, y, w: 0.09, h, fill: { color }, line: { color } });
        s.addText(word, { x: 11.2, y: y + 0.12, w: 1.4, h: 0.25, margin: 0, fontFace: T.mono, fontSize: 9, bold: true, color, align: 'right' });
        s.addText(clip(it.headline, 95), { x: 0.85, y: y + 0.1, w: 10.3, h: 0.36, margin: 0, fontFace: T.head, fontSize: 15, bold: true, color: T.ink, valign: 'top' });
        const body = [it.so_what, h >= 1.5 ? it.detail : null].filter(Boolean).join(' ');
        if (body) s.addText(clip(body, h >= 1.5 ? 260 : 130), { x: 0.85, y: y + 0.5, w: 11.7, h: h - 0.9, margin: 0, fontFace: T.body, fontSize: 12, color: T.ink, valign: 'top' });
        const src = (it.sources || [])[0];
        if (src) {
          const text = clip((src.publisher ? src.publisher + ': ' : '') + (src.title || src.url), 110) + (src.published ? ' · ' + src.published : '');
          const run = /^https?:\/\//i.test(src.url || '') ? [{ text, options: { hyperlink: { url: src.url } } }] : [{ text }];
          s.addText(run, { x: 0.85, y: y + h - 0.34, w: 11.7, h: 0.26, margin: 0, fontFace: T.body, fontSize: 10, color: T.muted });
        }
      });
    });

    // ── watchlist
    if (data.watchlist && data.watchlist.length) {
      const s = pptx.addSlide(); page++;
      frame(pptx, s, 'Watchlist', 'What to follow next', page); titles.push('Watchlist');
      const head = ['What', 'Why it matters', 'Next'].map(t => ({ text: t, options: { bold: true, color: 'FFFFFF', fill: { color: T.navy } } }));
      const rows = data.watchlist.slice(0, 8).map(w => [clip(w.item, 60), clip(w.why, 140), clip(w.next || '—', 40)]);
      s.addTable([head, ...rows], { x: 0.6, y: 1.65, w: 12.13, colW: [3.4, 6.53, 2.2], fontFace: T.body, fontSize: 12, color: T.ink,
                                    border: { type: 'solid', pt: 0.5, color: T.line }, fill: { color: 'FFFFFF' }, valign: 'middle', margin: 0.08 });
    }

    // ── sources appendix
    const all = [];
    const seen = new Set();
    (data.sections || []).forEach(sec => (sec.items || []).forEach(it => (it.sources || []).forEach(src => {
      const key = src.url || src.title;
      if (key && !seen.has(key)) { seen.add(key); all.push(src); }
    })));
    if (all.length) {
      const s = pptx.addSlide(); page++;
      frame(pptx, s, 'Sources', 'Appendix', page); titles.push('Sources');
      const runs = all.slice(0, 22).map(src => ({
        text: clip((src.publisher ? src.publisher + ' — ' : '') + (src.title || src.url), 120) + (src.published ? '  (' + src.published + ')' : ''),
        options: Object.assign({ breakLine: true }, /^https?:\/\//i.test(src.url || '') ? { hyperlink: { url: src.url } } : {}),
      }));
      s.addText(runs, { x: 0.6, y: 1.6, w: 12.1, h: 5.1, margin: 0, fontFace: T.body, fontSize: 12, color: T.ink, valign: 'top', paraSpaceAfter: 4 });
      if (all.length > 22) s.addText('+ ' + (all.length - 22) + ' more in the HTML report', { x: 0.6, y: 6.6, w: 6, h: 0.25, margin: 0, fontFace: T.body, fontSize: 9, color: T.muted });
    }

    return { pptx, titles };
  };
</script>
<script>
  const button = document.getElementById('download');
  const status = document.getElementById('status');
  let deckFile = null;
  let fileName = 'aviation-market-brief.pptx';

  // Built once when the page opens: a mistake in the deck shows up here, not on the click, and the
  // click only has to hand over a file that already exists.
  (async () => {
    try {
      const data = JSON.parse(document.getElementById('report-data').textContent);
      const kind = data.kind === 'weekly' ? 'Weekly market report' : 'Daily market brief';
      document.title = data.title || kind;
      document.getElementById('kicker').textContent = 'PowerPoint deck · 16:9 · ' + kind + (data.period && data.period.label ? ' · ' + data.period.label : '');
      document.getElementById('title').textContent = data.title || kind;
      document.getElementById('headline').textContent = data.headline || '';
      fileName = (data.fileName || (data.title || kind)).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') + '.pptx';

      const { pptx, titles } = await window.buildDeck(data);
      const list = document.getElementById('slides');
      titles.forEach(t => { const li = document.createElement('li'); li.textContent = t; list.appendChild(li); });

      deckFile = await pptx.write({ outputType: 'blob' });
      button.disabled = false;
      status.textContent = 'Ready - ' + titles.length + ' slides, ' + Math.round(deckFile.size / 1024) + ' KB.';
    } catch (err) {
      status.textContent = 'The deck could not be built: ' + (err && err.message ? err.message : err);
    }
  })();

  button.addEventListener('click', () => {
    if (!deckFile) return;
    const url = URL.createObjectURL(deckFile);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  });
</script>
</body>
</html>
""";

    private const string SampleDaily = """
{
  "kind": "daily",
  "title": "Aviation Daily Brief",
  "period": { "from": "2026-10-05", "to": "2026-10-06", "label": "Tuesday 6 October 2026" },
  "generatedAt": "2026-10-06 18:00 UTC",
  "audience": "Market intelligence desk",
  "headline": "An emergency directive on HX-35 fan blades lands on an engine supply chain that is already the narrowbody market's tightest constraint.",
  "summary": [
    "Regulator mandates ultrasonic fan blade inspections on about 380 HX-35 engines within 50 cycles; expect added pressure on shop capacity already running at ~125-day turnarounds.",
    "Northwind Air firms 24 Meridian M-220s (+12 options) to upgauge regional-jet routes from 2028; the same engine type is on the order.",
    "Meridian lifts M-220 output to 9/month and keeps a 12/month target for end-2027, naming titanium forgings as the binding risk.",
    "Q3 traffic shows transatlantic fares falling for the first time since 2024 while domestic unit revenue keeps rising on constrained capacity."
  ],
  "metrics": [
    { "label": "Articles reviewed", "value": "11", "note": "last 24 h" },
    { "label": "Feeds with news", "value": "6", "note": "of 7 monitored" },
    { "label": "Engines under the new AD", "value": "~380", "note": "HX-35, per the directive" },
    { "label": "M-220 backlog", "value": "1,140", "note": "aircraft, per Meridian" }
  ],
  "charts": {
    "byCategory": [
      { "label": "Regulators, public agencies and R&D", "value": 2 },
      { "label": "Airlines and commercial aviation", "value": 2 },
      { "label": "MRO and aviation supply chain", "value": 2 },
      { "label": "Manufacturers and company announcements", "value": 1 },
      { "label": "Airline company announcements", "value": 1 },
      { "label": "Airports and air traffic management", "value": 1 }
    ],
    "byPublisher": [
      { "label": "Aero Trade Weekly", "value": 3 },
      { "label": "European Air Safety Bulletin", "value": 2 },
      { "label": "MRO & Supply Chain Review", "value": 2 },
      { "label": "Northwind Air", "value": 1 },
      { "label": "Meridian Aerostructures", "value": 1 }
    ]
  },
  "sections": [
    {
      "title": "Safety & regulation",
      "takeaway": "One emergency directive with direct fleet and MRO consequences.",
      "items": [
        {
          "headline": "Emergency AD: repeat ultrasonic inspections on HX-35 fan blades",
          "so_what": "~380 engines must be inspected within 50 cycles and every 400 thereafter; six hours per on-wing check adds overnight maintenance load for M-220 operators.",
          "detail": "Root cause traced so far to a shot-peening deviation at a sub-tier supplier; blade replacement expected as terminating action.",
          "signal": "negative",
          "sources": [ { "title": "Airworthiness directive mandates repeat fan blade inspections on HX-35 engines", "publisher": "European Air Safety Bulletin", "url": "http://127.0.0.1:8099/articles/ad-hx35-fan-blade-inspection", "published": "2026-10-06" } ]
        }
      ]
    },
    {
      "title": "Airlines",
      "takeaway": "Upgauging and long-thin international routes on narrowbodies.",
      "items": [
        {
          "headline": "Northwind Air firms 24 Meridian M-220s, options for 12 more",
          "so_what": "Replaces 31 regional jets on dense domestic routes from Q2 2028; capex guidance unchanged.",
          "detail": "HX-35 engines selected with a ten-year maintenance agreement - now exposed to the new fan blade directive.",
          "signal": "positive",
          "sources": [ { "title": "Northwind Air firms up order for 24 Meridian M-220 aircraft", "publisher": "Northwind Air", "url": "http://127.0.0.1:8099/articles/northwind-orders-24-m220", "published": "2026-10-06" } ]
        },
        {
          "headline": "Transatlantic yields soften as capacity returns",
          "so_what": "Load factors held near 87% but fares fell year on year; expect off-peak transatlantic trims and redeployment to leisure markets.",
          "signal": "watch",
          "sources": [ { "title": "Q3 traffic: transatlantic yields soften as capacity returns", "publisher": "Aero Trade Weekly", "url": "http://127.0.0.1:8099/articles/q3-traffic-transatlantic-yields-soften", "published": "2026-10-05" } ]
        }
      ]
    },
    {
      "title": "Manufacturers & supply chain",
      "takeaway": "Rates are rising, but forgings and engine hot sections still set the pace.",
      "items": [
        {
          "headline": "Meridian lifts M-220 production to 9 a month",
          "so_what": "Second final assembly line open; 12/month target for end-2027 reaffirmed with titanium forgings as the main risk.",
          "signal": "positive",
          "sources": [ { "title": "Meridian lifts M-220 production to nine aircraft a month", "publisher": "Meridian Aerostructures", "url": "http://127.0.0.1:8099/articles/meridian-raises-m220-rate", "published": "2026-10-05" } ]
        },
        {
          "headline": "Engine shop turnaround passes 120 days",
          "so_what": "HPT blades and vanes are the most common hold; spare engine lease rates up ~40% in a year.",
          "signal": "negative",
          "sources": [ { "title": "Engine shop turnaround stretches past 120 days as parts shortages bite", "publisher": "MRO & Supply Chain Review", "url": "http://127.0.0.1:8099/articles/engine-shop-visit-backlog-120-days", "published": "2026-10-05" } ]
        }
      ]
    }
  ],
  "watchlist": [
    { "item": "HX-35 AD root cause", "why": "Determines whether blade replacement becomes a fleet-wide campaign.", "next": "Directive review" },
    { "item": "Northwind capital markets day", "why": "Updated fleet plan and M-220 financing mix.", "next": "November" },
    { "item": "Dublin slot confirmation", "why": "Gates Northwind's first transatlantic schedule.", "next": "Summer 2027 slot conference" }
  ],
  "coverage": {
    "articles": 11, "feeds": 6, "publishers": 6, "summaryOnly": 1,
    "notes": "Aero Trade Weekly's analysis piece is behind a subscription; it is cited from its feed summary only."
  }
}
""";

    // </templates>
}

return new AviationNewsDesk();

