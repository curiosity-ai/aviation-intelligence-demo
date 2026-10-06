// Reference template — definitions path: code/endpoints/app/similarity/affected-documents.cs
//
// The records the change under analysis may affect.
//
// Built as a SIMILARITY SCENARIO rather than a hand-rolled loop: the engine fuses the signals,
// keeps the per-signal attribution in ScoreInfo.Components (so the UI can say *why* something
// surfaced), manages the candidate pool, and applies the rules after fusion.
//
// The scenario's own SimilarityResult is what comes back — that is the type
// SearchArea.WithSimilarityEngine consumes on the other side, so returning a hand-built
// projection of it deserialises into an EMPTY RESULT with no error anywhere.
//
// Every graph signal below leans on metadata being NODES. HasCommonNeighborsWith answers
// "shares a category / part / system with the seed" in one hop, which is only possible because
// the connector links those dimensions instead of storing them as strings on the record.

[endpoint: Curiosity.Endpoints.Path("app/similarity/affected-documents")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

var req     = Body.FromJson<TD.CaseRequest>();
var caseUID = req?.CaseUID ?? default(UID128);

if (caseUID == default(UID128))                                       return BadRequest("caseUID is required");
if (!Graph.TryGet(caseUID, out var c) || c.Type != N.Case.Type)       return NotFound($"Case '{caseUID}' not found.");

// The subject of the analysis, BY EDGE. SubjectId is still on the node as a label, but the
// relationship is what the graph is for.
var seedUID = Q().StartAt(caseUID).Out(N.Change.Type, E.CaseOf).AsUIDEnumerable().FirstOrDefault();
if (seedUID == default(UID128)) return NotFound($"Case '{caseUID}' analyses nothing.");

var subject      = Graph.Get(seedUID);
var partitionUID = Q().StartAt(seedUID).Out(N.Partition.Type, E.InPartition).AsUIDEnumerable().FirstOrDefault();

// Built from the fields that were deliberately indexed SEPARATELY, so which sections
// participate is a choice rather than an accident of concatenation.
var seedText = string.Join(" ",
    subject.GetString(N.Change.Title),
    subject.GetString(N.Change.Abstract),
    subject.GetString(N.Change.DescriptionText));

var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(400)
        .MaxCandidatesPerSignal(150)
        // A candidate list that appears after eight silent seconds reads as broken.
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    // What the change READS like. The strongest single signal, and the only one that can reach
    // a record sharing no identifier at all.
    .AddSignal("SimilarText", s => s
        .Describe("Embedding similarity over title, abstract and description")
        .Weight(1.0f)
        .Limit(150)
        .FromAsync(async ctx => await ctx.Graph.Query()
            .StartAtSimilarTextAsync(seedText, count: 150,
                                     nodeTypes: TD.ImpactableTypes,
                                     applyCutoff: false)))

    // Same part. Two records touching one identified thing are related whatever they say.
    .AddSignal("SharedPart", s => s
        .Describe("Shares a part number with the change")
        .Weight(0.9f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.PartNumber.Type, E.UsesPart)))

    // Category proximity: ONE SIGNAL PER LEVEL, narrowest first, so the weights order
    // themselves and the attribution says which level matched. A single signal with a branch
    // inside it would say only "category".
    .AddSignal("SameCategoryL4", s => s
        .Describe("Same detailed category")
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.Category.Type, E.InCategory)))

    .AddSignal("SameCategoryL2", s => s
        .Describe("Same top-level category")
        .Weight(0.2f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.Category.Type, E.InCategoryL2)))

    // Already decided: the user has seen it, so stop showing it near the top. A NEGATIVE
    // SIGNAL rather than a rule, because it should DEMOTE, not hide — the list stays a
    // complete picture of what the search believes is related.
    .AddNegativeSignal("AlreadyDecided", s => s
        .Describe("Already confirmed or rejected")
        .From(ctx => ctx.Graph.Query()
            .StartAt(caseUID)
            .Out(TD.ImpactableTypes, null, new[] { E.ConfirmedImpact, E.RejectedImpact })))

    .Fuse(Fusion.Sum)
    .FuseFinal(Fusion.Discount)

    // Partition isolation is ABSOLUTE in this corpus, so it is a filter, not a weight.
    // Anything with exceptions belongs in a signal instead.
    .AddRule("SamePartition", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates)
            .IsRelatedTo(partitionUID)
            .AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

return result;
