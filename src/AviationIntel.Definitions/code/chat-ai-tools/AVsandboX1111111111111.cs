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
        SandboxMount.Files("/templates", "report templates: report.html, deck.html, the JSON contract in README.md and a daily and a weekly sample",
            SandboxFile.Text("README.md", TemplatesReadme),
            SandboxFile.Text("report.html", ReportTemplate),
            SandboxFile.Text("deck.html", DeckTemplate),
            SandboxFile.Text("sample-daily.json", SampleDaily), SandboxFile.Text("sample-weekly.json", SampleWeekly)),
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

Both write to `/artifacts/`; then `publish <name>.html`. `sample-daily.json` (Tuesday 6 October 2026) and
`sample-weekly.json` (the week to that day) are complete examples, written from real articles in the
corpus: every figure in them is quoted from the article it cites.
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

    // ~6px per character at 11px: a 24-character label needs ~145px plus the gap before the bar.
    const labelW = 165, barW = 170, rowH = 22, pad = 6, valueW = 30;
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
      label.textContent = String(r.label).length > 24 ? String(r.label).slice(0, 23) + '…' : String(r.label);
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
  "fileName": "aviation-daily-2026-10-06",
  "period": { "from": "2026-10-06", "to": "2026-10-06", "label": "Tuesday 6 October 2026" },
  "generatedAt": "2026-10-07 03:30 UTC",
  "audience": "Market intelligence desk",
  "headline": "The FAA has ruled the 737 MAX flight-computer glitch not a safety concern, putting MAX 10 certification back on track for Ryanair and Alaska.",
  "summary": [
    "An FAA review board found the U14 flight-management software issue on some 737 MAX jets not safety-critical; Leeham now sees MAX 10 certification back on track.",
    "Air cargo is heading into peak season on rising rates: Hong Kong-North America averaged $7.02/kg in September, 31% up on a year earlier, with jet fuel more than 100% higher year on year.",
    "airBaltic plans 500-700 job cuts, a smaller A220-300 fleet and more ACMI capacity under a new business plan.",
    "Boeing's $14.7 billion seeker contract from Lockheed Martin funds a seven-year plan to triple PAC-3 MSE seeker production."
  ],
  "metrics": [
    { "label": "Articles reviewed", "value": "439", "note": "6 Oct, UTC day" },
    { "label": "Feeds with news", "value": "94", "note": "of 248 monitored" },
    { "label": "Publishers", "value": "69" },
    { "label": "Summary only", "value": "30", "note": "not read beyond the feed" }
  ],
  "charts": {
    "byCategory": [
      { "label": "Regulators, public agencies and R&D", "value": 139 },
      { "label": "Defense aviation and aerospace", "value": 75 },
      { "label": "Aviation industry and OEM analysis", "value": 56 },
      { "label": "Space and satellite industry", "value": 34 },
      { "label": "Regional aviation", "value": 28 },
      { "label": "Cargo and logistics", "value": 25 },
      { "label": "Business aviation and rotorcraft", "value": 21 },
      { "label": "Airlines and commercial aviation", "value": 18 }
    ],
    "byPublisher": [
      { "label": "arXiv instrumentation for astrophysics", "value": 93 },
      { "label": "Aviation Week", "value": 40 },
      { "label": "NASA Technology Transfer", "value": 12 },
      { "label": "AeroTime", "value": 11 },
      { "label": "Air Cargo Week", "value": 11 },
      { "label": "Air Journal", "value": 11 },
      { "label": "Aviation24.be", "value": 10 },
      { "label": "The Loadstar", "value": 10 }
    ]
  },
  "sections": [
    {
      "title": "Safety & regulation",
      "takeaway": "The MAX 10's late software scare has been closed by the regulator itself.",
      "items": [
        {
          "headline": "FAA board clears 737 MAX flight-computer software issue",
          "so_what": "With the U14 flight-management glitch judged not safety-critical, the MAX 10 can join the MAX 7 in clearing its last regulatory hurdle - capacity Ryanair and Alaska Airlines have on order.",
          "detail": "The glitch can occur when pilots load a new flight plan for a go-around. The FAA's Corrective Action Review Board concluded on October 2 that pilots retain full control and the indications are clear; Alaska had expected certification by the end of September.",
          "signal": "positive",
          "sources": [
            { "title": "Boeing's Path to Max 10 Certification Looks Clear Again", "publisher": "Leeham News and Analysis", "url": "https://leehamnews.com/2026/10/06/max-10-certification-unlikely-to-be-significantly-delayed", "published": "2026-10-06", "uid": "W7HPkoQtnz7JB13p2y7cCf" }
          ]
        }
      ]
    },
    {
      "title": "Airlines",
      "takeaway": "Premium capacity up at British Airways, headcount down at airBaltic, and cargo yields rising into the peak.",
      "items": [
        {
          "headline": "British Airways turns 75% of its A380 seats premium",
          "so_what": "All 12 BA A380s are being refitted with more than 35,000 parts each, shifting the type towards premium cabins as part of a £7 billion transformation plan.",
          "detail": "BA says punctuality is at levels not seen since 2012, with Q3 2026 its best ever departure punctuality within 15 minutes at Heathrow.",
          "signal": "positive",
          "sources": [
            { "title": "Fewer seats, more luxury: step into the new British Airways A380 interiors", "publisher": "Aircraft Interiors International", "url": "https://www.aircraftinteriorsinternational.com/news/airline-news/fewer-seats-more-luxury-step-into-the-new-british-airways-a380-interiors.html", "published": "2026-10-06", "uid": "D9KCo6wuBtgR6JY3zQ1XSY" }
          ]
        },
        {
          "headline": "airBaltic to cut 500-700 roles and shrink its A220-300 fleet",
          "so_what": "500-700 of about 3,000 staff go as the carrier leans on year-round ACMI partners instead of its own aircraft - less Baltic capacity under its own operation.",
          "detail": "Unions are being consulted on reduced workloads for pilots and cabin crew; the new structures for the rest of the business are due by mid-October.",
          "signal": "negative",
          "sources": [
            { "title": "airBaltic plans up to 700 job cuts as it reshapes business", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/06/airbaltic-plans-up-to-700-job-cuts-as-it-reshapes-business", "published": "2026-10-06", "uid": "1Agsr2EtZ85K7NZU9EAiXF" }
          ]
        },
        {
          "headline": "Air freight rates edge up into the fourth-quarter peak",
          "so_what": "Hong Kong-North America averaged $7.02/kg in September (31% up year on year) and Hong Kong-Europe $4.80/kg (up 8.8%); the Baltic Air Freight Index jumped 5% in the week to October 5.",
          "detail": "TAC Index attributes the year-on-year rise to jet fuel prices more than 100% higher and to data-centre demand to the US; the EU's €3 charge on low-value parcels is weighing on Asia-Europe volumes.",
          "signal": "watch",
          "sources": [
            { "title": "Airfreight rates edge up in September", "publisher": "Air Cargo News", "url": "https://www.aircargonews.net/data/2026/10/airfreight-rates-edge-up-in-september", "published": "2026-10-06", "uid": "ESPtNbyoLwxhy4paA3Fgdg" }
          ]
        },
        {
          "headline": "Delta recovers from a global technology issue",
          "so_what": "Delta says systems are stabilising after a limited issue with data connectivity to some systems; the scale of any disruption is not stated.",
          "signal": "watch",
          "sources": [
            { "title": "Update on global technology issue", "publisher": "Delta newsroom", "url": "https://news.delta.com/update-global-technology-issue", "published": "2026-10-06", "uid": "G1VcFQuVKJcQWBeGaSUvqJ", "note": "summary only" }
          ]
        }
      ]
    },
    {
      "title": "Manufacturers & supply chain",
      "takeaway": "A new regional-aircraft assembly line opens in Germany.",
      "items": [
        {
          "headline": "Deutsche Aircraft opens D328eco final assembly in Leipzig",
          "so_what": "The more than €100 million site is designed for up to 48 aircraft a year and moves the D328eco from development to serial production.",
          "signal": "positive",
          "sources": [
            { "title": "Deutsche Aircraft opens Leipzig plant for D328eco production", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/06/deutsche-aircraft-opens-leipzig-plant-for-d328eco-production", "published": "2026-10-06", "uid": "HpjHUgFx4i5io6J9udj7ey" }
          ]
        }
      ]
    },
    {
      "title": "Defence & space",
      "takeaway": "Missile-defence demand reaches the supply chain, and Northrop flies its own collaborative combat aircraft.",
      "items": [
        {
          "headline": "Boeing wins $14.7 billion to triple PAC-3 MSE seeker output",
          "so_what": "A seven-year plan to triple seeker production for Lockheed Martin's Patriot interceptor, after seeker deliveries already rose more than 30% in 2025.",
          "detail": "Work starts while final terms, including pricing, are negotiated; Boeing has invested more than $200 million in its Huntsville seeker production since 2024.",
          "signal": "positive",
          "sources": [
            { "title": "Boeing wins $14.7 billion contract to triple Patriot missile seeker production", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/boeing-patriot-missile-seeker-contract-production", "published": "2026-10-06", "uid": "R5xgeRDvunc9KwgCNDCS76" }
          ]
        },
        {
          "headline": "Northrop's YFQ-48A Talon Blue flies fully autonomously",
          "so_what": "An internally funded CCA flying taxi to landing on its own, as the US Air Force targets 500 autonomous aircraft in service by 2032 - though Northrop has no CCA production contract yet.",
          "signal": "watch",
          "sources": [
            { "title": "Northrop Grumman's YFQ-48A CCA completes first fully autonomous flight", "publisher": "Defense News", "url": "https://www.defensenews.com/industry/techwatch/2026/10/06/northrop-grummans-yfq-48a-cca-completes-first-fully-autonomous-flight", "published": "2026-10-06", "uid": "TxMQ7dRXPrwLXkac2JB1cX" }
          ]
        },
        {
          "headline": "Morocco's first F-16 Block 72 flies; first full-rate MH-139A order",
          "so_what": "Two US programmes advance: Morocco's first F-16 Block 72 is in flight test towards first delivery, and the MH-139A's first four full-rate helicopters take the total under contract to 42.",
          "detail": "Boeing has delivered 27 MH-139As. In Morocco, an MRO facility of more than 8,000 m² at Benslimane, with Maintenance Aero Maroc, is to service C-130s and later F-16s.",
          "signal": "positive",
          "sources": [
            { "title": "Lockheed Martin Announces First Flight of Morocco's First F 16 Block 72", "publisher": "Lockheed Martin", "url": "https://news.lockheedmartin.com/2026-10-06-Lockheed-Martin-Announces-First-Flight-of-Moroccos-First-F-16-Block-72", "published": "2026-10-06", "uid": "DkkNeUBiiovhzgVqpkeoSp" },
            { "title": "Boeing wins first full-rate MH-139A Grey Wolf order", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/06/boeing-wins-first-full-rate-mh-139a-grey-wolf-order", "published": "2026-10-06", "uid": "SqxB9YUCkJkVemCYY6Zwd2" }
          ]
        }
      ]
    },
    {
      "title": "Sustainability",
      "takeaway": "Airlines want CORSIA, not the EU ETS, to be the global carbon framework.",
      "items": [
        {
          "headline": "IATA marks ten years of CORSIA as the EU reviews its ETS",
          "so_what": "More than 130 countries take part and CORSIA should have mitigated about 200 million tonnes of CO2 by year-end; IATA wants the EU's ETS review - which considers covering destinations within 5,000 km - to reinforce CORSIA, not overlap it.",
          "detail": "From 2027 CORSIA is estimated to cover about 85% of global emissions, with up to $120 billion in climate finance over the life of the scheme.",
          "signal": "watch",
          "sources": [
            { "title": "Ten Years on from CORSIA Agreement, Governments Must Strengthen the Global Framework for Aviation Emissions", "publisher": "IATA", "url": "https://www.iata.org/en/pressroom/2026-releases/10-06-ten-years-corsia-governments-strengthen-framework-aviation-emissions", "published": "2026-10-06", "uid": "QC3eHQBHBRc5xwWSQsnJwg" }
          ]
        }
      ]
    }
  ],
  "watchlist": [
    { "item": "737 MAX 10 amended type certificate", "why": "Releases MAX 10 deliveries to Ryanair and Alaska Airlines.", "next": "FAA decision, no date given" },
    { "item": "airBaltic restructuring", "why": "How much capacity moves to ACMI partners.", "next": "New structures by mid-October" },
    { "item": "Air cargo peak season", "why": "Whether rates keep pace with jet fuel.", "next": "October TAC Index" },
    { "item": "EU ETS review", "why": "A 5,000 km scope would overlap CORSIA on long-haul routes.", "next": "EU review in progress" }
  ],
  "coverage": {
    "articles": 439,
    "feeds": 94,
    "publishers": 69,
    "summaryOnly": 30,
    "notes": "12 feeds failed on their last fetch: the ten Airbus newsroom feeds answer with a bot check, and Cargo Facts and Air Cargo Next refuse the connector. 93 of the 439 articles are arXiv preprints and were not considered. Delta's update is cited from its feed summary only."
  }
}
""";

    private const string SampleWeekly = """
{
  "kind": "weekly",
  "title": "Aviation Weekly Market Report",
  "fileName": "aviation-weekly-2026-10-06",
  "period": { "from": "2026-09-30", "to": "2026-10-06", "label": "Week to Tuesday 6 October 2026" },
  "generatedAt": "2026-10-07 03:45 UTC",
  "audience": "Market intelligence desk",
  "headline": "Supply-side risk eased for both OEMs: labour deals ratified at Airbus Spain and Boeing, and the FAA closed the 737 MAX software scare - while Middle East airspace risk widened.",
  "summary": [
    "Airbus workers in Spain ended months of strikes (76.76% backed the deal) and SPEEA members ratified four-year contracts with Boeing on October 1.",
    "An FAA review board found the 737 MAX U14 software issue not safety-critical on October 2, putting MAX 10 certification back on track for customers such as Ryanair and Alaska.",
    "Houthis told airlines to avoid Saudi airspace on October 5, after EASA had already advised avoiding part of the Jeddah FIR on September 30.",
    "Air cargo enters peak season on fuel-driven rates: Hong Kong-North America averaged $7.02/kg in September, 31% up year on year, with IATA putting August jet fuel 79.2% above a year earlier.",
    "AerCap signed 85 leases and bought 20 737 MAX in Q3, while KLM took its first A350-900 and British Airways turned 75% of its A380 seats premium."
  ],
  "metrics": [
    { "label": "Articles reviewed", "value": "1,227", "note": "30 Sep - 6 Oct" },
    { "label": "Feeds with news", "value": "156", "note": "of 248 monitored" },
    { "label": "Publishers", "value": "90" },
    { "label": "Summary only", "value": "76", "note": "not read beyond the feed" }
  ],
  "charts": {
    "byCategory": [
      { "label": "Regulators, public agencies and R&D", "value": 321 },
      { "label": "Defense aviation and aerospace", "value": 293 },
      { "label": "Space and satellite industry", "value": 121 },
      { "label": "Aviation industry and OEM analysis", "value": 116 },
      { "label": "MRO and aviation supply chain", "value": 79 },
      { "label": "Regional aviation", "value": 75 },
      { "label": "Drones and advanced air mobility", "value": 53 },
      { "label": "Cargo and logistics", "value": 50 }
    ],
    "byDay": [
      { "label": "2026-09-30", "value": 125 },
      { "label": "2026-10-01", "value": 162 },
      { "label": "2026-10-02", "value": 180 },
      { "label": "2026-10-03", "value": 29 },
      { "label": "2026-10-04", "value": 47 },
      { "label": "2026-10-05", "value": 245 },
      { "label": "2026-10-06", "value": 439 }
    ]
  },
  "sections": [
    {
      "title": "Safety & regulation",
      "takeaway": "One certification risk closed; one airspace risk opened.",
      "items": [
        {
          "headline": "FAA board clears 737 MAX flight-computer software issue",
          "so_what": "With the U14 glitch judged not safety-critical, the MAX 10 can join the MAX 7 in clearing its last regulatory hurdle - capacity Ryanair and Alaska Airlines have on order.",
          "detail": "The FAA's Corrective Action Review Board concluded on October 2 that pilots retain full control and the indications are clear.",
          "signal": "positive",
          "sources": [
            { "title": "Boeing's Path to Max 10 Certification Looks Clear Again", "publisher": "Leeham News and Analysis", "url": "https://leehamnews.com/2026/10/06/max-10-certification-unlikely-to-be-significantly-delayed", "published": "2026-10-06", "uid": "W7HPkoQtnz7JB13p2y7cCf" }
          ]
        },
        {
          "headline": "Houthis warn airlines off Saudi airspace",
          "so_what": "Adds Saudi skies to the Middle East routes carriers must plan around, on top of EASA's September 30 advice to avoid part of the Jeddah FIR at all altitudes.",
          "detail": "The warning named no airports, routes or timetable. EASA still recommends against flights over Iran and Jordan at any altitude.",
          "signal": "negative",
          "sources": [
            { "title": "Houthis warn airlines to avoid Saudi airspace as Yemen conflict escalates", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/houthis-airlines-saudi-airspace-warning", "published": "2026-10-05", "uid": "4Cf8mcCZoaf2quGHZM3Mfw" }
          ]
        }
      ]
    },
    {
      "title": "Airlines",
      "takeaway": "Premium long-haul growth at the network carriers, retrenchment in the Baltics, and cargo yields rising into the peak.",
      "items": [
        {
          "headline": "British Airways turns 75% of its A380 seats premium",
          "so_what": "All 12 BA A380s are being refitted with more than 35,000 parts each as part of a £7 billion transformation plan.",
          "signal": "positive",
          "sources": [
            { "title": "Fewer seats, more luxury: step into the new British Airways A380 interiors", "publisher": "Aircraft Interiors International", "url": "https://www.aircraftinteriorsinternational.com/news/airline-news/fewer-seats-more-luxury-step-into-the-new-british-airways-a380-interiors.html", "published": "2026-10-06", "uid": "D9KCo6wuBtgR6JY3zQ1XSY" }
          ]
        },
        {
          "headline": "KLM takes its first A350-900",
          "so_what": "331 seats in three classes, including 34 business suites, for long-haul routes from Schiphol such as Toronto - as KLM continues renewing its long-haul fleet.",
          "signal": "positive",
          "sources": [
            { "title": "KLM takes delivery of first A350-900", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/05/klm-takes-first-a350-900", "published": "2026-10-05", "uid": "9bNALaj2GXigkLNUEsJYsS" }
          ]
        },
        {
          "headline": "Delta-LATAM joint venture up 31% in capacity in four years",
          "so_what": "Combined capacity rose about 31% in 2026 compared with 2023, across 28 joint routes and 17.4 million customers since October 2022.",
          "signal": "positive",
          "sources": [
            { "title": "Delta and LATAM mark four years of Joint Venture with 31% increase in capacity and 17.4 million customers carried between North and South America", "publisher": "Delta newsroom", "url": "https://news.delta.com/delta-and-latam-mark-four-years-joint-venture-31-increase-capacity-and-174-million-customers", "published": "2026-10-05", "uid": "JssrFRCZAm6fp5mabYkFnY" }
          ]
        },
        {
          "headline": "airBaltic to cut 500-700 roles and shrink its A220-300 fleet",
          "so_what": "500-700 of about 3,000 staff go as the carrier leans on year-round ACMI partners instead of its own aircraft.",
          "signal": "negative",
          "sources": [
            { "title": "airBaltic plans up to 700 job cuts as it reshapes business", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/06/airbaltic-plans-up-to-700-job-cuts-as-it-reshapes-business", "published": "2026-10-06", "uid": "1Agsr2EtZ85K7NZU9EAiXF" }
          ]
        },
        {
          "headline": "Air cargo heads into peak on fuel-driven rates",
          "so_what": "Hong Kong-North America averaged $7.02/kg in September (31% up year on year); Cirium finds no major hub has run dry since the Strait of Hormuz closed, but smaller airports have.",
          "detail": "IATA put August jet fuel 79.2% above a year earlier and demand up 4.4%. CH Robinson warns that if freighter capacity follows technology demand to the US, Europe-bound shippers will have fewer options.",
          "signal": "watch",
          "sources": [
            { "title": "Airfreight rates edge up in September", "publisher": "Air Cargo News", "url": "https://www.aircargonews.net/data/2026/10/airfreight-rates-edge-up-in-september", "published": "2026-10-06", "uid": "ESPtNbyoLwxhy4paA3Fgdg" },
            { "title": "Major hubs unaffected by jet fuel supply issues", "publisher": "Air Cargo News", "url": "https://www.aircargonews.net/business/2026/10/major-hubs-unaffected-by-jet-fuel-supply-issues", "published": "2026-10-05", "uid": "F85uW33zAYHGb1WdKmH7sF" },
            { "title": "Peak season pressure to develop unevenly across origins and cargo types", "publisher": "Air Cargo News", "url": "https://www.aircargonews.net/freight-forwarder/2026/10/peak-season-pressure-to-develop-unevenly-across-origins-and-cargo-types", "published": "2026-10-05", "uid": "GE21uAZewnrJ9SQ1iCSAHC" }
          ]
        }
      ]
    },
    {
      "title": "Manufacturers & supply chain",
      "takeaway": "Labour peace at both OEMs and new final assembly capacity in Germany, against a narrowbody backlog the duopoly still owns.",
      "items": [
        {
          "headline": "Airbus Spain and Boeing's SPEEA engineers ratify new contracts",
          "so_what": "Removes the risk of strikes deepening production problems at both manufacturers: 76.76% of voting Airbus staff in Spain backed the deal, and SPEEA ratified Boeing's four-year offers.",
          "detail": "Airbus Spain staff had begun an indefinite strike on August 25; Airbus has more than 14,000 staff in Spain.",
          "signal": "positive",
          "sources": [
            { "title": "Relief for both Airbus and Boeing as thousands of workers back new contracts", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/airbus-boeing-workers-vote-new-contract-proposals", "published": "2026-10-02", "uid": "fzdZkH71Qu8jUTyJj77hBC" }
          ]
        },
        {
          "headline": "Deutsche Aircraft opens D328eco final assembly in Leipzig",
          "so_what": "The more than €100 million site is designed for up to 48 aircraft a year and moves the D328eco into serial production.",
          "signal": "positive",
          "sources": [
            { "title": "Deutsche Aircraft opens Leipzig plant for D328eco production", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/06/deutsche-aircraft-opens-leipzig-plant-for-d328eco-production", "published": "2026-10-06", "uid": "HpjHUgFx4i5io6J9udj7ey" }
          ]
        },
        {
          "headline": "Narrowbody backlog of 13,500: Airbus 56%, Boeing 36%",
          "so_what": "Airbus holds 56% of the global backlog of latest-generation narrowbodies and Boeing 36%, the context for Leeham's look at Comac's 1,100-aircraft backlog.",
          "detail": "Leeham counts orders for over 20,500 of the newest-generation narrowbodies, about 7,000 delivered. The analysis itself is behind a subscription; only the free opening was read.",
          "signal": "neutral",
          "sources": [
            { "title": "About That 1,100-Aircraft Comac Backlog", "publisher": "Leeham News and Analysis", "url": "https://leehamnews.com/2026/10/05/about-that-1100-aircraft-comac-backlog", "published": "2026-10-05", "uid": "2UiPFMpPBicLj8wdY6g1X2", "note": "subscription; opening paragraphs only" }
          ]
        }
      ]
    },
    {
      "title": "Finance & leasing",
      "takeaway": "The largest lessor kept recycling its portfolio into new-technology narrowbodies, and a Turkish low-cost carrier closed a European acquisition.",
      "items": [
        {
          "headline": "AerCap signs 85 leases and buys 20 737 MAX in Q3",
          "so_what": "Purchases of 32 aircraft included eight A320neo-family jets and 20 737 MAX, while 65 aircraft were sold - and about $783 million went on share buybacks.",
          "detail": "Leases covered 16 wide-bodies, 45 narrow-bodies, 13 engines and 11 helicopters; financing transactions came to about $900 million.",
          "signal": "positive",
          "sources": [
            { "title": "AerCap reports strong Q3 transactions", "publisher": "AviTrader", "url": "https://avitrader.com/2026/10/05/aercap-reports-strong-q3-transactions", "published": "2026-10-05", "uid": "HmjDhX17bBuig1tf5Bbhgy" }
          ]
        },
        {
          "headline": "Pegasus completes its takeover of Smartwings",
          "so_what": "The Turkish carrier now controls the Czech Republic's largest airline group, including the CSA Czech Airlines brand, after giving up some Prague-Antalya slots to win approval.",
          "detail": "Pegasus had offered €154 million for 100% of the group in December 2025.",
          "signal": "neutral",
          "sources": [
            { "title": "Pegasus Airlines completes its acquisition of Czech group Smartwings", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/pegasus-airlines-completes-its-acquisition-of-czech-group-smartwings", "published": "2026-10-02", "uid": "TQ9nNoByv8QC9b3V73kdxt" }
          ]
        }
      ]
    },
    {
      "title": "Defence & space",
      "takeaway": "Missile-defence and fighter demand is reaching production lines, and India's transport competition is shaping industrial bids.",
      "items": [
        {
          "headline": "Boeing wins $14.7 billion to triple PAC-3 MSE seeker output",
          "so_what": "A seven-year plan to triple seeker production for Lockheed Martin's Patriot interceptor, after seeker deliveries already rose more than 30% in 2025.",
          "signal": "positive",
          "sources": [
            { "title": "Boeing wins $14.7 billion contract to triple Patriot missile seeker production", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/boeing-patriot-missile-seeker-contract-production", "published": "2026-10-06", "uid": "R5xgeRDvunc9KwgCNDCS76" }
          ]
        },
        {
          "headline": "F-16 deliveries move: Taiwan's first Block 70s, Morocco's first Block 72 flies",
          "so_what": "Taiwan received the first two of 66 Block 70s - deliveries were due to begin in 2023 and were held up by the F-16 line's relocation, supply-chain and software problems; Morocco's first Block 72 is in flight test.",
          "detail": "More Taiwanese jets are due before the end of 2026 and the rest in 2027-2028; the two delivered arrived without their Viper Shield electronic-warfare systems.",
          "signal": "positive",
          "sources": [
            { "title": "Taiwan receives first two of 66 F-16V Block 70 jets amid rising Chinese pressure", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/taiwan-receives-first-two-of-66-f-16v-block-70-jets-amid-rising-chinese-pressure", "published": "2026-10-02", "uid": "NTxzNh8KmDbaSrZQ3DzUb3" },
            { "title": "Lockheed Martin Announces First Flight of Morocco's First F 16 Block 72", "publisher": "Lockheed Martin", "url": "https://news.lockheedmartin.com/2026-10-06-Lockheed-Martin-Announces-First-Flight-of-Moroccos-First-F-16-Block-72", "published": "2026-10-06", "uid": "DkkNeUBiiovhzgVqpkeoSp" }
          ]
        },
        {
          "headline": "Embraer and Mahindra pick Nagpur for a C-390 line",
          "so_what": "Positions the C-390 against Lockheed Martin and Tata's C-130J-30 for India's 60-aircraft Medium Transport Aircraft programme, with a site ready to build on if India selects it.",
          "signal": "watch",
          "sources": [
            { "title": "Embraer, Mahindra pick Nagpur for potential C-390 assembly line", "publisher": "AeroTime", "url": "https://www.aerotime.aero/articles/embraer-mahindra-nagpur-c390-assembly-line-india", "published": "2026-10-02", "uid": "4ni49ZJLye9GbLbCYaf6eh" }
          ]
        },
        {
          "headline": "Airbus completes the first 32 OneWeb replacement satellites",
          "so_what": "The first tranche of 669 spacecraft Eutelsat has on order, heading to SpaceX for launch, towards 1,300 OneWeb satellites in orbit by 2030.",
          "signal": "positive",
          "sources": [
            { "title": "Airbus Completes First 32 OneWeb Replenishment Satellites for Eutelsat Fleet Refresh", "publisher": "SatNews", "url": "https://satnews.com/2026/10/05/airbus-completes-first-32-oneweb-replenishment-satellites-for-eutelsat-fleet-refresh", "published": "2026-10-05", "uid": "KgFYMbdnjQm7JUwwQQgCWZ" }
          ]
        }
      ]
    },
    {
      "title": "Sustainability",
      "takeaway": "Airlines want CORSIA, not the EU ETS, to be the global carbon framework.",
      "items": [
        {
          "headline": "IATA marks ten years of CORSIA as the EU reviews its ETS",
          "so_what": "More than 130 countries take part and CORSIA should have mitigated about 200 million tonnes of CO2 by year-end; IATA wants the EU's ETS review - which considers covering destinations within 5,000 km - to reinforce CORSIA, not overlap it.",
          "signal": "watch",
          "sources": [
            { "title": "Ten Years on from CORSIA Agreement, Governments Must Strengthen the Global Framework for Aviation Emissions", "publisher": "IATA", "url": "https://www.iata.org/en/pressroom/2026-releases/10-06-ten-years-corsia-governments-strengthen-framework-aviation-emissions", "published": "2026-10-06", "uid": "QC3eHQBHBRc5xwWSQsnJwg" }
          ]
        }
      ]
    }
  ],
  "watchlist": [
    { "item": "737 MAX 10 amended type certificate", "why": "Releases MAX 10 deliveries to Ryanair and Alaska Airlines.", "next": "FAA decision, no date given" },
    { "item": "Saudi airspace", "why": "Whether the Houthi warning turns into attacks on routes carriers still use.", "next": "EASA guidance updates" },
    { "item": "India Medium Transport Aircraft", "why": "60 aircraft between the C-390 and the C-130J-30.", "next": "Selection after the August 12 request for proposals" },
    { "item": "Taiwan F-16 Block 70 deliveries", "why": "Tests whether the F-16 line has recovered its schedule.", "next": "More jets before the end of 2026" },
    { "item": "airBaltic restructuring", "why": "How much capacity moves to ACMI partners.", "next": "New structures by mid-October" },
    { "item": "EU ETS review", "why": "A 5,000 km scope would overlap CORSIA on long-haul routes.", "next": "EU review in progress" }
  ],
  "coverage": {
    "articles": 1227,
    "feeds": 156,
    "publishers": 90,
    "summaryOnly": 76,
    "notes": "12 feeds failed on their last fetch: the ten Airbus newsroom feeds answer with a bot check, and Cargo Facts and Air Cargo Next refuse the connector, so Airbus news reaches this report only through trade titles. 93 arXiv preprints were not considered. Leeham's Comac analysis is behind a subscription and is cited from its free opening only."
  }
}
""";

    // </templates>
}

return new AviationNewsDesk();

