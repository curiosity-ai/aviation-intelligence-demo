using System.Diagnostics;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using Aeralis.Connector;
using Aeralis.Connector.Ingest;

// Ingests the Aeralis MOD pre-analysis dataset into a Curiosity workspace graph.
//
//   dotnet run --project src/Aeralis.Connector -- --root . --endpoint <url> --token <token>
//   dotnet run --project src/Aeralis.Connector -- --root . --dry-run
//   dotnet run --project src/Aeralis.Connector -- --root . --reset-cache
//   dotnet run --project src/Aeralis.Connector -- --root . --dry-run --no-attachments
//   dotnet run --project src/Aeralis.Connector -- --emit-frontend-schema src/Aeralis.FrontEnd/src/Schema/N.cs
//
// Rendering the document attachments is the expensive part of a run: --no-attachments skips
// it, --attachment-threads sets how many documents are rendered at once, and
// --attachments-out <dir> also writes every generated file to disk.
//
// Only structured relationships are written. Anything the application is meant to
// discover through similarity or entity recognition is deliberately left unlinked;
// NLP relationships arrive later as _AppearsIn / _Mentions from the workspace pipeline.

// Regenerating the front-end's schema constants needs no endpoint and no dataset — it
// reads this assembly's own [Node] classes — so it runs before everything else and exits.
var emitSchemaTo = Arg("--emit-frontend-schema");
if (emitSchemaTo is object)
{
    Aeralis.Connector.SchemaEmitter.Emit(emitSchemaTo);
    return 0;
}

var root = Path.GetFullPath(Arg("--root") ?? Directory.GetCurrentDirectory());
var endpoint = Arg("--endpoint") ?? Environment.GetEnvironmentVariable("CURIOSITY_ENDPOINT");
var token = Arg("--token") ?? Environment.GetEnvironmentVariable("CURIOSITY_TOKEN");
var dryRun = Flag("--dry-run");
var resetCache = Flag("--reset-cache");
var noAttachments = Flag("--no-attachments");
var attachmentsOut = Arg("--attachments-out");
var attachmentThreads = int.TryParse(Arg("--attachment-threads"), out var threads)
    ? threads
    : Math.Max(1, Environment.ProcessorCount / 2);

if (!dryRun && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token)))
{
    Console.Error.WriteLine("An endpoint and a token are required unless --dry-run is used.");
    Console.Error.WriteLine("  --endpoint <url> --token <token>, or CURIOSITY_ENDPOINT / CURIOSITY_TOKEN.");
    return 2;
}

if (!Directory.Exists(Path.Combine(root, "data", "reference")))
{
    Console.Error.WriteLine($"No dataset found under {root}. Pass --root pointing at the repository.");
    return 2;
}

var cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "aeralis-connector.db");
Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
if (resetCache && File.Exists(cachePath))
{
    File.Delete(cachePath);
    Console.WriteLine($"cache reset: {cachePath}");
}

Console.WriteLine("Aeralis Technical Data — graph connector");
Console.WriteLine($"Root:  {root}");
Console.WriteLine($"Mode:  {(dryRun ? "DRY RUN (no writes)" : endpoint)}");
Console.WriteLine();

var stopwatch = Stopwatch.StartNew();

var loggerFactory = LoggerFactory.Create(f => f.AddConsole());

var clientHandler = new HttpClientHandler()
{
    ServerCertificateCustomValidationCallback = (a, b, c, d) => true
};

// In dry-run the endpoint is never contacted: WithDryRun makes every add and link a
// no-op, so the whole mapping still executes and the counters still add up.
var graph = Graph
    .Connect(endpoint ?? "http://localhost", token ?? "dry-run", connectorName: "aeralis", clientHandler: clientHandler)
    .WithDryRun(dryRun)
    .WithLoggingFactory(loggerFactory);

var cache = HashCache.Initialize(cachePath);
using var ctx = new ConnectorContext(graph, cache, root, dryRun);

if (!dryRun)
{
    Console.WriteLine("Synchronising schema…");
    await SyncSchemaAsync(graph);
    graph.SetAutoCommitCost(everyNodes: 10_000, everyEdges: 50_000);
}

try
{
    await Step("programs", () => ProgramsIngest.RunAsync(ctx));
    await Step("ata chapters", () => AtaChaptersIngest.RunAsync(ctx));
    await Step("zones", () => ZonesIngest.RunAsync(ctx));
    await Step("suppliers", () => SuppliersIngest.RunAsync(ctx));
    await Step("operators", () => OperatorsIngest.RunAsync(ctx));
    await Step("part numbers", () => PartNumbersIngest.RunAsync(ctx));
    await Step("fins", () => FinsIngest.RunAsync(ctx));
    await Step("drawings", () => DrawingsIngest.RunAsync(ctx));
    await Step("software", () => SoftwareIngest.RunAsync(ctx));
    await Step("msn registry", () => MsnIngest.RunAsync(ctx));
    await Step("rules", () => RulesIngest.RunAsync(ctx));

    IReadOnlyList<UsersIngest.LoadedUser> users = [];
    await Step("users", async () => users = await UsersIngest.RunAsync(ctx));

    // Who owns which work order is decided before the documents are written, so each work
    // order node is created once with its workflow state already on it. Writing it in a
    // second pass would mean a second AddOrUpdate carrying only those few fields, and the
    // graph applies a schema object as the node's whole content — everything the document
    // pass had just written would be blanked. The ownership itself is never a property:
    // AssignmentPass.LinkAsync writes it as edges, once both ends exist.
    var assignments = AssignmentPass.Plan(ctx, users);

    await Step("documents", () => DocumentsIngest.RunAsync(ctx, assignments));
    await Step("issues", () => IssuesIngest.RunAsync(ctx));
    await Step("assignment", () => AssignmentPass.LinkAsync(ctx, assignments));

    // Last, and by far the longest: every document is rendered to the file it would be
    // published as and uploaded as an attachment of its node. Skippable, because the rest of
    // the mapping runs in seconds and this does not.
    if (!noAttachments)
        await Step("attachments", () => AttachmentsIngest.RunAsync(ctx, attachmentThreads, attachmentsOut));
}
catch (Exception ex)
{
    // Pending hashes are dropped when the cache is disposed without a commit, so the
    // next run re-ingests whatever this run failed to land.
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAILED during ingestion: {ex.GetType().Name} — {ex.Message}");
    return Finish(1);
}

stopwatch.Stop();
ctx.PrintSummary();

Console.WriteLine();
Console.WriteLine($"TOTAL  {ctx.TotalNodes:N0} nodes, {ctx.TotalEdges:N0} edges "
                  + $"in {stopwatch.Elapsed.TotalSeconds:F1} s"
                  + (dryRun ? "  (dry run — nothing written)" : ""));
return Finish(0);

// Ending the process is not automatic here. Graph.Connect leaves a foreground worker
// running, so simply returning parks the run: the ingest finishes in seconds and then
// the runtime waits on a thread that never completes. Disposing does not release it
// either — verified by leaking both the graph and the cache, after which the process
// still would not exit.
//
// Teardown also gets a deadline rather than an open-ended wait, because Graph.Dispose()
// blocks indefinitely when the endpoint is not reachable, which is exactly the dry-run
// case (it points at http://localhost). Nothing is at risk: every handler awaits
// CommitPendingAsync and CommitCacheAsync before it returns, so the work is durable
// well before this point.
int Finish(int exitCode)
{
    var teardown = Task.Run(() =>
    {
        graph.Dispose();
        cache.Dispose();
    });

    if (!teardown.Wait(TimeSpan.FromSeconds(15)))
    {
        Console.WriteLine("teardown did not finish in 15 s — ending the process anyway");
    }

    Console.Out.Flush();
    Environment.Exit(exitCode);
    return exitCode; // not reached
}

async Task Step(string name, Func<Task> action)
{
    var before = (ctx.TotalNodes, ctx.TotalEdges);
    var sw = Stopwatch.StartNew();
    await action();
    sw.Stop();
    Console.WriteLine($"  {name,-16} +{ctx.TotalNodes - before.TotalNodes,7:N0} nodes "
                      + $"+{ctx.TotalEdges - before.TotalEdges,8:N0} edges  {sw.ElapsedMilliseconds,6:N0} ms");
}

static async Task SyncSchemaAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<ProgramsIngest.Program>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ProgramsIngest.AircraftType>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ProgramsIngest.AircraftModel>(overwrite: true);
    await graph.CreateNodeSchemaAsync<AtaChaptersIngest.AtaChapter>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ZonesIngest.Zone>(overwrite: true);
    await graph.CreateNodeSchemaAsync<ZonesIngest.AccessPanel>(overwrite: true);
    await graph.CreateNodeSchemaAsync<SuppliersIngest.Supplier>(overwrite: true);
    await graph.CreateNodeSchemaAsync<OperatorsIngest.Operator>(overwrite: true);
    await graph.CreateNodeSchemaAsync<PartNumbersIngest.PartNumber>(overwrite: true);
    await graph.CreateNodeSchemaAsync<FinsIngest.Fin>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DrawingsIngest.Drawing>(overwrite: true);
    await graph.CreateNodeSchemaAsync<SoftwareIngest.SoftwareLoad>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MsnIngest.Msn>(overwrite: true);
    await graph.CreateNodeSchemaAsync<RulesIngest.Rule>(overwrite: true);

    await graph.CreateNodeSchemaAsync<MetadataSchema.EngineType>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Department>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Site>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.AtaDomain>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.MajorZone>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Keyword>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Classification>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.AircraftSystem>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.FaultCode>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.MelItem>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Harness>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.AirworthinessDirective>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.ServiceKit>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.ModNumber>(overwrite: true);
    await graph.CreateNodeSchemaAsync<MetadataSchema.Status>(overwrite: true);

    await graph.CreateNodeSchemaAsync<DocumentSchema.Modification>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.ServiceBulletin>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.InternalOrder>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.PreAnalysisWorkOrder>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.AmmTask>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.IpcFigure>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.SrmRepair>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.TsmProcedure>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.WdmSheet>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.FcomEntry>(overwrite: true);

    await graph.CreateNodeSchemaAsync<DocumentSchema.IpcItem>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.RepairDrawing>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.ExpectedFinding>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.SbRevision>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentSchema.MsnRangeApplicability>(overwrite: true);

    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.ModificationIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.ServiceBulletinIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.InternalOrderIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.PreAnalysisWorkOrderIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.AmmTaskIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.IpcFigureIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.SrmRepairIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.TsmProcedureIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.WdmSheetIssue>(overwrite: true);
    await graph.CreateNodeSchemaAsync<DocumentIssueSchema.FcomEntryIssue>(overwrite: true);

    await graph.CreateNodeSchemaAsync<AttachmentsIngest.DocumentationSource>(overwrite: true);

    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

string? Arg(string name)
{
    var argv = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(argv, name);
    return index >= 0 && index + 1 < argv.Length ? argv[index + 1] : null;
}

bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);
