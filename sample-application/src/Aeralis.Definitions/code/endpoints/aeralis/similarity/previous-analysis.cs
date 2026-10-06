[endpoint: Curiosity.Endpoints.Path("aeralis/similarity/previous-analysis")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Pre-analyses of similar past changes — "has anyone already worked this out?"
//
// The candidates are work orders, but the *evidence* is mostly about the modifications
// they analysed: a work order's own text describes the search that was run, not the
// change. So the signals come at it from both directions — the orders' own findings
// text, and the orders attached to modifications that resemble this one.
//
// Returns the scenario's `SimilarityResult` unchanged, which is what the search area on
// the other end consumes.

var req = Body.FromJson<TD.SimilarityRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");


var seedUID = Q().StartAt(pawoUID)
    .Out(N.Modification.Type, E.PreAnalysisOf)
    .AsUIDEnumerable()
    .FirstOrDefault();

if (seedUID == default(UID128)) return NotFound($"Work order '{pawoUID}' analyses no modification.");

var subject = Graph.Get(seedUID);
var programUID = Q().StartAt(seedUID).Out(N.Program.Type, E.InProgram).AsUIDEnumerable().FirstOrDefault();

var seedText = string.Join(" ",
    subject.GetString(N.Modification.Title),
    subject.GetString(N.Modification.Abstract),
    subject.GetString(N.Modification.ReasonText));

var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(200)
        .MaxCandidatesPerSignal(100)
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    // Modifications that read like this one, then the orders that analysed them.
    // The traversal drops the per-UID scores (they survive narrowing, not expansion),
    // so this source contributes by rank — which is the right weight for a two-hop
    // inference anyway.
    .AddSignal("AnalysedSimilarMod", s => s
        .Describe("Analysed a modification similar to this one")
        .Weight(1.0f)
        .FromAsync(async ctx =>
            (await ctx.Graph.Query()
                .StartAtSimilarTextAsync(seedText, count: 100,
                                         nodeTypes: new[] { N.Modification.Type },
                                         applyCutoff: false))
            .Out(N.PreAnalysisWorkOrder.Type, E.HasPreAnalysis)))

    // The orders' own conclusions. A past analysis that reached the same conclusion is
    // worth reading even when its subject looks different.
    .AddSignal("SimilarFindings", s => s
        .Describe("Reached similar findings")
        .Weight(0.6f)
        .FromAsync(async ctx =>
            (await ctx.Graph.Query()
                .StartAtSimilarTextAsync(seedText, count: 100,
                                         nodeTypes: new[] { N.PreAnalysisWorkOrder.Type },
                                         applyCutoff: false))))

    // Orders whose subject shares hardware with ours — the entity route to the same
    // question, and the one that survives completely different wording.
    .AddSignal("SubjectSharesPart", s => s
        .Describe("Analysed a modification sharing a part number")
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(N.Modification.Type)
            .HasCommonNeighborsWith(seedUID, N.PartNumber.Type, E.UsesPart)
            .Out(N.PreAnalysisWorkOrder.Type, E.HasPreAnalysis)))

    .AddSignal("SubjectSharesFin", s => s
        .Describe("Analysed a modification sharing a FIN")
        .Weight(0.9f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(N.Modification.Type)
            .HasCommonNeighborsWith(seedUID, N.Fin.Type, E.UsesFin)
            .Out(N.PreAnalysisWorkOrder.Type, E.HasPreAnalysis)))

    // Same chapter of the aircraft.
    .AddSignal("SameAta4d", s => s
        .Describe("Same ATA 4D chapter")
        .Weight(0.4f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(N.PreAnalysisWorkOrder.Type)
            .HasCommonNeighborsWith(seedUID, N.AtaChapter.Type, E.InAta4d)))

    .Fuse(Fusion.Sum)

    .AddRule("SameProgram", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates)
            .IsRelatedTo(programUID)
            .AsUIDEnumerable()))

    // A still-open order has no conclusion to learn from.
    .AddRule("ClosedOnly", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates)
            .Where(n => n.GetBool(N.PreAnalysisWorkOrder.IsHistorical))
            .AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

return result;

