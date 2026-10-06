[endpoint: Curiosity.Endpoints.Path("aeralis/similarity/suggested-ata")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Folds per-document similarity scores into per-chapter scores.
//
// Runs the same scenario as affected-documents, then walks each scoring document to the
// ATA 2D chapter it is linked to and sums the scores per chapter. That walk is a graph
// hop rather than a string comparison because the connector links ATA at all three
// levels — which is also what lets a chapter with no documents of its own still pick up
// score from its children.
//
// A chapter ranking highly here but absent from the impact matrix is the whole point:
// it is a part of the aircraft the analysis has not looked at, and the UI banners it.
//
// Chapters come back as UIDs, and nothing else: the caller renders them with the
// workspace's own ATA labels rather than a title copied through this response.

var req = Body.FromJson<TD.PawoRequest>();
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
    subject.GetString(N.Modification.DescriptionText));

var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o.MaxCandidates(300).MaxCandidatesPerSignal(150))

    .AddSignal("SimilarText", s => s
        .Weight(1.0f)
        .FromAsync(async ctx =>
            (await ctx.Graph.Query()
                .StartAtSimilarTextAsync(seedText, count: 150,
                                         nodeTypes: TD.ImpactableTypes,
                                         applyCutoff: false))))

    .AddSignal("SharedPart", s => s
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.PartNumber.Type, E.UsesPart)))

    .AddSignal("SharedFin", s => s
        .Weight(0.9f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.ImpactableTypes)
            .HasCommonNeighborsWith(seedUID, N.Fin.Type, E.UsesFin)))

    .Fuse(Fusion.Sum)

    .AddRule("SameProgram", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates)
            .IsRelatedTo(programUID)
            .AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

// Sum each document's score onto its ATA 2D chapter.
var scores = new Dictionary<UID128, double>();
var counts = new Dictionary<UID128, int>();

foreach (var kv in result.Scores)
{
    foreach (var chapter in Q().StartAt(kv.Key).Out(N.AtaChapter.Type, E.InAta2d).AsUIDEnumerable())
    {
        scores[chapter] = scores.GetValueOrDefault(chapter) + kv.Value.Score;
        counts[chapter] = counts.GetValueOrDefault(chapter) + 1;
    }
}

// What the impact matrix already covers, so the rest can be marked as new.
var alreadyImpacted = Q()
    .StartAt(pawoUID)
    .Out(TD.ImpactableTypes, null, new[] { E.ConfirmedImpact })
    .Out(N.AtaChapter.Type, E.InAta2d)
    .AsUIDEnumerable()
    .ToHashSet();

var chapters = scores
    .OrderByDescending(kv => kv.Value)
    .Take(12)
    .Select(kv => new TD.SuggestedAtaDto
    {
        AtaUID = kv.Key,
        Score = kv.Value,
        DocumentCount = counts.GetValueOrDefault(kv.Key),
        IsNew = !alreadyImpacted.Contains(kv.Key),
    })
    .ToArray();

return new TD.SuggestedAtaListDto { Chapters = chapters };

