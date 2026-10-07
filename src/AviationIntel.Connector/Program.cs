using System.Diagnostics;
using AviationIntel.Connector;
using AviationIntel.Connector.Extraction;
using AviationIntel.Connector.Ingest;
using Curiosity.Library;
using Microsoft.Extensions.Logging;

// Reads the aviation & aerospace RSS directory, fetches every feed, loads each new article in
// headless Chromium, extracts it with Readability, and writes feeds, publishers and articles
// into a Curiosity workspace.
//
//   dotnet run --project src/AviationIntel.Connector -- --root . --endpoint <url> --token <token>
//   dotnet run --project src/AviationIntel.Connector -- --root . --dry-run --feeds 10
//   dotnet run --project src/AviationIntel.Connector -- --root . --priority Core --since-days 7
//   dotnet run --project src/AviationIntel.Connector -- --root . --no-browser          # feed text only
//   dotnet run --project src/AviationIntel.Connector -- --emit-frontend-schema src/AviationIntel.FrontEnd/src/Schema/N.cs
//
// Options:
//   --directory <csv>         the feed directory (default data/feeds/aviation-aerospace-rss-directory.csv)
//   --feed <text>             only feeds whose name, category or URL contains <text>
//   --priority <Core|...>     only feeds of one directory priority
//   --feeds <n>               only the first <n> feeds (after the filters)
//   --since-days <n>          ignore items older than <n> days (default 14)
//   --max-items <n>           at most <n> items per feed (default 50)
//   --browser-concurrency <n> article pages loaded at once, across hosts (default 4)
//   --chromium <path>         Chromium executable (or CHROMIUM_PATH); default: Playwright's own
//   --no-browser              do not load article pages; bodies come from the feed
//   --reset-cache             forget which articles were already ingested
//   --skip-reference          do not (re)write the reference vocabularies under data/reference
//   --install-browser         download Playwright's Chromium and exit (once per machine)
//   --upload-data             store data/feeds and data/reference in the workspace for the
//                             in-workspace connectors (code/data-connector-tasks/) and exit
//
// The connector writes only what the sources state: the directory's classification of each
// feed and each item's own metadata. Which companies, programmes and airports an article is
// about is left to the workspace's entity recognition.

var emitSchemaTo = Arg("--emit-frontend-schema");

if (emitSchemaTo is object)
{
    SchemaEmitter.Emit(emitSchemaTo);
    return 0;
}

if (Flag("--install-browser"))
{
    //Playwright's own installer, so no PowerShell is needed for playwright.ps1.
    return Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
}

var root     = Path.GetFullPath(Arg("--root") ?? Directory.GetCurrentDirectory());
var endpoint = Arg("--endpoint") ?? Environment.GetEnvironmentVariable("CURIOSITY_ENDPOINT");
var token    = Arg("--token")    ?? Environment.GetEnvironmentVariable("CURIOSITY_TOKEN");

var options = new ConnectorOptions
{
    Root               = root,
    DirectoryCsv       = Path.GetFullPath(Arg("--directory") ?? Path.Combine(root, "data", "feeds", "aviation-aerospace-rss-directory.csv")),
    DryRun             = Flag("--dry-run"),
    NoBrowser          = Flag("--no-browser"),
    ChromiumPath       = Arg("--chromium") ?? Environment.GetEnvironmentVariable("CHROMIUM_PATH"),
    BrowserConcurrency = IntArg("--browser-concurrency", 4),
    MaxItemsPerFeed    = IntArg("--max-items", 50),
    SinceDays          = IntArg("--since-days", 14),
    FeedFilter         = Arg("--feed"),
    PriorityFilter     = Arg("--priority"),
    FeedLimit          = IntArg("--feeds", 0),
};

if (!options.DryRun && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token)))
{
    Console.Error.WriteLine("An endpoint and a token are required unless --dry-run is used.");
    Console.Error.WriteLine("  --endpoint <url> --token <token>, or CURIOSITY_ENDPOINT / CURIOSITY_TOKEN.");
    return 2;
}

if (!File.Exists(options.DirectoryCsv))
{
    Console.Error.WriteLine($"No feed directory at {options.DirectoryCsv}. Pass --root or --directory.");
    return 2;
}

if (Flag("--upload-data"))
{
    using var uploader = Graph.Connect(endpoint!, token!, connectorName: "aviation-rss-data");

    Console.WriteLine("Uploading the connector data for the in-workspace connectors…");
    await ConnectorData.UploadAsync(uploader, root);
    return 0;
}

//The cache is keyed per endpoint, so a dry run or a second workspace never makes another skip work.
var cacheName = options.DryRun ? "dry-run" : new Uri(endpoint!).Authority.Replace(':', '_');
var cachePath = Path.Combine(AppContext.BaseDirectory, "cache", $"aviation-connector-{cacheName}.db");
Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

var termsLedgerPath = Path.ChangeExtension(cachePath, ".terms.json");

if (Flag("--reset-cache") && File.Exists(cachePath))
{
    File.Delete(cachePath);
    Console.WriteLine($"cache reset: {cachePath}");
}

options = options with { TermsLedgerPath = termsLedgerPath };

Console.WriteLine("Aviation Market Intelligence — RSS connector");
Console.WriteLine($"Directory: {options.DirectoryCsv}");
Console.WriteLine($"Mode:      {(options.DryRun ? "DRY RUN (no writes)" : endpoint)}");
Console.WriteLine($"Articles:  {(options.NoBrowser ? "feed text only" : $"Chromium + Readability, {options.BrowserConcurrency} at once")}");
Console.WriteLine();

var stopwatch     = Stopwatch.StartNew();
//In a dry run the library still announces the connector to the (absent) endpoint and retries in
//the background; that noise says nothing about the mapping, so it is silenced.
var loggerFactory = LoggerFactory.Create(f => f.AddConsole().SetMinimumLevel(options.DryRun ? LogLevel.None : LogLevel.Warning));

var graph = Graph.Connect(endpoint ?? "http://localhost", token ?? "dry-run", connectorName: "aviation-rss")
                 .WithDryRun(options.DryRun)
                 .WithLoggingFactory(loggerFactory);

var cache = HashCache.Initialize(cachePath);
using var ctx = new ConnectorContext(graph, cache, options);

ArticleExtractor? extractor = null;

try
{
    if (!options.DryRun)
    {
        Console.WriteLine("Synchronising schema…");
        await SyncSchemaAsync(graph);
        graph.SetAutoCommitCost(everyNodes: 5_000, everyEdges: 20_000);
    }

    // The vocabularies first: an article that arrives before the entity it names still gets linked
    // once the spotter is rebuilt, but there is no reason to make it wait.
    if (!Flag("--skip-reference")) await Step("reference", () => ReferenceIngest.RunAsync(ctx));

    if (!options.NoBrowser) extractor = await ArticleExtractor.StartAsync(options);

    var rows = FeedDirectoryIngest.Load(ctx);
    Console.WriteLine($"  {rows.Count} feeds selected");

    IReadOnlyDictionary<string, FeedFetcher.FetchResult> fetched = new Dictionary<string, FeedFetcher.FetchResult>();

    // Feeds are fetched before any feed node is written, so each Feed is written once with this
    // run's status on it rather than patched by a second AddOrUpdate.
    await Step("fetch feeds", async () => fetched = await FeedFetcher.FetchAllAsync(ctx, rows, extractor));
    await Step("directory",   () => FeedDirectoryIngest.WriteAsync(ctx, rows, fetched));
    await Step("articles",    () => ArticlesIngest.RunAsync(ctx, rows, fetched, extractor));
}
catch (Exception ex)
{
    // Pending hashes are dropped when the cache is disposed without a commit, so the next run
    // re-ingests whatever this one failed to land.
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED: {ex.GetType().Name} — {ex.Message}");
    return await FinishAsync(1);
}

stopwatch.Stop();
ctx.PrintSummary();

Console.WriteLine();
Console.WriteLine($"TOTAL  {ctx.TotalNodes:N0} nodes, {ctx.TotalEdges:N0} edges in {stopwatch.Elapsed.TotalSeconds:F1} s"
                + (options.DryRun ? "  (dry run — nothing written)" : ""));

return await FinishAsync(0);

// Graph.Connect leaves a foreground worker running and Dispose blocks indefinitely when the
// endpoint is unreachable (the dry-run case), so teardown gets a deadline and the process exits
// explicitly. Every step has already awaited its commits by now.
async Task<int> FinishAsync(int exitCode)
{
    if (extractor is not null) await extractor.DisposeAsync();

    var teardown = Task.Run(() =>
    {
        graph.Dispose();
        cache.Dispose();
    });

    if (!teardown.Wait(TimeSpan.FromSeconds(15))) Console.WriteLine("teardown did not finish in 15 s — ending the process anyway");

    Console.Out.Flush();
    Environment.Exit(exitCode);
    return exitCode;
}

async Task Step(string name, Func<Task> action)
{
    var before = (ctx.TotalNodes, ctx.TotalEdges);
    var sw     = Stopwatch.StartNew();

    Console.WriteLine($"  {name}…");
    await action();
    sw.Stop();

    Console.WriteLine($"  {name,-14} +{ctx.TotalNodes - before.TotalNodes,6:N0} nodes +{ctx.TotalEdges - before.TotalEdges,7:N0} edges  {sw.Elapsed.TotalSeconds,6:F1} s");
}

static async Task SyncSchemaAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<FeedDirectoryIngest.Feed>(overwrite: true);
    await graph.CreateNodeSchemaAsync<FeedDirectoryIngest.FeedCategory>(overwrite: true);
    await graph.CreateNodeSchemaAsync<FeedDirectoryIngest.SourceType>(overwrite: true);
    await graph.CreateNodeSchemaAsync<FeedDirectoryIngest.Priority>(overwrite: true);
    await graph.CreateNodeSchemaAsync<FeedDirectoryIngest.Publisher>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ArticlesIngest.Article>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ArticlesIngest.Tag>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ReferenceIngest.Organisation>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ReferenceIngest.AircraftType>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ReferenceIngest.EngineType>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ReferenceIngest.LaunchVehicle>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ReferenceIngest.Airport>(overwrite: true);

    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

string? Arg(string name)
{
    var argv  = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(argv, name);

    return index >= 0 && index + 1 < argv.Length ? argv[index + 1] : null;
}

int IntArg(string name, int fallback) => int.TryParse(Arg(name), out var value) ? value : fallback;

bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);
