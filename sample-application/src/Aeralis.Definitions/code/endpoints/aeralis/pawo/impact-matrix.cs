[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/impact-matrix")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// ATA 2D down the side, document type across the top, counts of impacted documents in
// the cells.
//
// The rows are ATA **chapters**, reached by walking each impacted document's InAta2d edge,
// and they travel as UIDs. Grouping on the node rather than on a code string is what makes
// the row a thing the UI can link to and the user can open.

var req = Body.FromJson<TD.PawoRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var impacted = new List<(UID128 Ata, string Type)>();

foreach (var document in Q().StartAt(pawoUID).Out(edgeType: E.ConfirmedImpact).AsEnumerable())
{
    var type = document.Type;

    foreach (var chapter in Q().StartAt(document.UID).Out(N.AtaChapter.Type, E.InAta2d).AsUIDEnumerable())
        impacted.Add((chapter, type));
}

var atas = impacted.Select(x => x.Ata).Distinct().ToList();

var cells = new List<TD.ImpactMatrixCellDto>();
foreach (var ata in atas)
{
    foreach (var type in TD.MatrixDocTypes)
    {
        cells.Add(new TD.ImpactMatrixCellDto
        {
            AtaUID = ata,
            DocType = type,
            Count = impacted.Count(x => x.Ata == ata && x.Type == type),
        });
    }
}

return new TD.ImpactMatrixDto
{
    AtaUIDs = atas.ToArray(),
    DocTypes = TD.MatrixDocTypes,
    Cells = cells.ToArray(),
};

