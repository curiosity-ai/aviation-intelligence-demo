[endpoint: Curiosity.Endpoints.Path("aeralis/similarity/affected-documents")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Documents the modification under analysis may affect.
//
// Built as a similarity scenario rather than a hand-rolled loop: the engine fuses the
// signals, keeps the per-signal attribution in ScoreInfo.Components (so the UI can say
// *why* something surfaced), and applies the filters after fusion.
//
// The scenario's own `SimilarityResult` is what comes back — that is the type
// `SearchArea.WithSimilarityEngine` consumes, so returning a hand-built projection of it
// would deserialise into an empty result on the other side. Every hit in it is a UID, and
// the search area resolves each one through the workspace's renderers.
//
// The graph signals below all lean on metadata being nodes. HasCommonNeighborsWith
// answers "shares an ATA chapter / part / FIN with the seed" in one hop, which is only
// possible because the connector links those dimensions instead of storing them as
// strings on the document.

var req = Body.FromJson<TD.SimilarityRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");


// The modification under analysis, by edge. `SubjectMod` is still on the node as a label,
// but the relationship is what the graph is for.
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
    subject.GetString(N.Modification.DescriptionText));

var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(400)
        .MaxCandidatesPerSignal(150)
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    // What the change reads like. The strongest single signal, and the only one that
    // can reach a document sharing no identifier at all.
    .AddSignal("SimilarText", s => s
        .Describe("Embedding similarity over title, abstract and description")
        .Weight(1.0f)
        .Limit(150)
        .FromAsync(async ctx =>
            (await ctx.Graph.Query()
                .StartAtSimilarTextAsync(seedText, count: 150,
                                         nodeTypes: TD.ImpactableTypes,
                                         applyCutoff: false))))

    // Same part. Two documents touching one part number are related whatever they say.
    .AddSignal("SharedPart", s => s
        .Describe("Shares a part number with the modification")
        .Weight(0.9f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.PartNumber.Type, E.UsesPart)))

    // Same FIN — a function in a location, so an even tighter signal than a part.
    .AddSignal("SharedFin", s => s
        .Describe("Shares a FIN with the modification")
        .Weight(1.0f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.Fin.Type, E.UsesFin)))

    // ATA proximity, narrowest first so the weights order themselves.
    .AddSignal("SameAta6d", s => s
        .Describe("Same ATA 6D chapter")
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.AtaChapter.Type, E.InAtaChapter)))

    .AddSignal("SameAta4d", s => s
        .Describe("Same ATA 4D chapter")
        .Weight(0.45f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.AtaChapter.Type, E.InAta4d)))

    .AddSignal("SameAta2d", s => s
        .Describe("Same ATA 2D chapter")
        .Weight(0.2f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.AtaChapter.Type, E.InAta2d)))

    // Physical proximity — the same bay is a real reason to look, even across systems.
    .AddSignal("SharedZone", s => s
        .Describe("Work in the same zone")
        .Weight(0.35f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.Zone.Type, E.InZone)))

    // The same aircraft system, which is how a flight-crew entry reaches the wiring and
    // the loadable software of the system it limits — none of which share vocabulary.
    .AddSignal("SharedSystem", s => s
        .Describe("Covers the same aircraft system")
        .Weight(0.3f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.AircraftSystem.Type, E.InSystem)))

    // Already confirmed: the engineer has seen it, so stop showing it near the top.
    // A negative signal rather than a rule, because it should demote, not hide — the
    // list stays a complete picture of what the search believes is related.
    .AddNegativeSignal("AlreadyConfirmed", s => s
        .Describe("Already confirmed as an impact")
        .From(ctx => ctx.Graph.Query()
            .StartAt(pawoUID)
            .Out(TD.ImpactableTypes, null, new[] { E.ConfirmedImpact })))

    .Fuse(Fusion.Sum)
    .FuseFinal(Fusion.Discount)

    // Program isolation is absolute in this dataset, so it is a filter, not a weight.
    .AddRule("SameProgram", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates)
            .IsRelatedTo(programUID)
            .AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

return result;

