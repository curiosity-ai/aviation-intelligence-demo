// Reference template — see ../SKILL.md §6 and §9.
//
//   dotnet run --project src/App.Connector -- --root . --endpoint <url> --token <token>
//   dotnet run --project src/App.Connector -- --root . --dry-run
//   dotnet run --project src/App.Connector -- --root . --reset-cache
//   dotnet run --project src/App.Connector -- --emit-frontend-schema src/App.FrontEnd/src/Schema/N.cs
//
// Only structured relationships are written. Anything the application is meant to discover
// through similarity or entity recognition is deliberately left unlinked; NLP relationships
// arrive later as _AppearsIn / _Mentions from the workspace pipeline.

using System.Diagnostics;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using App.Connector;
using App.Connector.Ingest;

// Regenerating the shared schema constants needs no endpoint and no corpus — it reads this
// assembly's own [Node] classes — so it runs before everything else and exits.
var emitSchemaTo = Arg("--emit-frontend-schema");
if (emitSchemaTo is object)
{
    SchemaEmitter.Emit(emitSchemaTo);
    return 0;
}

var root       = Path.GetFullPath(Arg("--root") ?? Directory.GetCurrentDirectory());
var endpoint   = Arg("--endpoint") ?? Environment.GetEnvironmentVariable("CURIOSITY_ENDPOINT");
var token      = Arg("--token")    ?? Environment.GetEnvironmentVariable("CURIOSITY_TOKEN");
var dryRun     = Flag("--dry-run");
var resetCache = Flag("--reset-cache");

if (!dryRun && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token)))
{
    Console.Error.WriteLine("An endpoint and a token are required unless --dry-run is used.");
    return 2;
}

var cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "app-connector.db");
Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
if (resetCache && File.Exists(cachePath)) File.Delete(cachePath);

var stopwatch     = Stopwatch.StartNew();
var loggerFactory = LoggerFactory.Create(f => f.AddConsole());

// In a dry run the endpoint is never contacted: WithDryRun makes every add and link a no-op,
// so the whole mapping still executes and the counters still add up.
var graph = Graph
    .Connect(endpoint ?? "http://localhost", token ?? "dry-run", connectorName: "app")
    .WithDryRun(dryRun)
    .WithLoggingFactory(loggerFactory);

var cache = HashCache.Initialize(cachePath);
using var ctx = new ConnectorContext(graph, cache, root, dryRun);

if (!dryRun)
{
    await SyncSchemaAsync(graph);
    graph.SetAutoCommitCost(everyNodes: 10_000, everyEdges: 50_000);
}

try
{
    // Dependency-shaped, though Node.FromKey means a link never blocks on its target existing.
    await Step("categories",  () => CategoriesIngest.RunAsync(ctx));
    await Step("part numbers",() => PartNumbersIngest.RunAsync(ctx));

    IReadOnlyList<UsersIngest.LoadedUser> users = [];
    await Step("users",       async () => users = await UsersIngest.RunAsync(ctx));

    // Ownership and derived workflow state are decided BEFORE the documents are written, so
    // each node is created once with everything on it. A second AddOrUpdate carrying only the
    // computed fields would blank everything the document pass wrote — see SKILL.md §4.
    var assignments = AssignmentPass.Compute(ctx, users);

    await Step("cases",       () => CasesIngest.RunAsync(ctx, assignments));
    await Step("assignment",  () => AssignmentPass.LinkAsync(ctx, assignments));
    await Step("attachments", () => AttachmentsIngest.RunAsync(ctx));
}
catch (Exception ex)
{
    // Pending hashes are dropped when the cache is disposed without a commit, so the next run
    // re-ingests whatever this run failed to land.
    Console.Error.WriteLine($"FAILED during ingestion: {ex.GetType().Name} — {ex.Message}");
    return Finish(1);
}

stopwatch.Stop();
ctx.PrintSummary();
Console.WriteLine($"TOTAL {ctx.TotalNodes:N0} nodes, {ctx.TotalEdges:N0} edges in "
                  + $"{stopwatch.Elapsed.TotalSeconds:F1} s{(dryRun ? "  (dry run)" : "")}");
return Finish(0);

// Ending the process is not automatic: Graph.Connect leaves a foreground worker running, so
// simply returning parks the run. Teardown gets a deadline because Dispose blocks indefinitely
// when the endpoint is unreachable — exactly the dry-run case. Nothing is at risk: every
// handler awaits its commits before returning.
int Finish(int exitCode)
{
    var teardown = Task.Run(() => { graph.Dispose(); cache.Dispose(); });
    if (!teardown.Wait(TimeSpan.FromSeconds(15)))
        Console.WriteLine("teardown did not finish in 15 s — ending the process anyway");

    Console.Out.Flush();
    Environment.Exit(exitCode);
    return exitCode; // not reached
}

async Task Step(string name, Func<Task> action)
{
    var before = (ctx.TotalNodes, ctx.TotalEdges);
    var sw     = Stopwatch.StartNew();
    await action();
    sw.Stop();
    Console.WriteLine($"  {name,-16} +{ctx.TotalNodes - before.TotalNodes,7:N0} nodes "
                      + $"+{ctx.TotalEdges - before.TotalEdges,8:N0} edges  {sw.ElapsedMilliseconds,6:N0} ms");
}

static async Task SyncSchemaAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<CategoriesIngest.Category>(overwrite: true);
    await graph.CreateNodeSchemaAsync<PartNumbersIngest.PartNumber>(overwrite: true);
    await graph.CreateNodeSchemaAsync<CasesIngest.Case>(overwrite: true);
    await graph.CreateNodeSchemaAsync<CasesIngest.CaseFinding>(overwrite: true);

    await graph.CreateNodeSchemaAsync<MetaLinks.Status>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetaLinks.Classification>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetaLinks.Keyword>(overwrite: true);

    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

string? Arg(string name)
{
    var argv  = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(argv, name);
    return index >= 0 && index + 1 < argv.Length ? argv[index + 1] : null;
}

bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);
