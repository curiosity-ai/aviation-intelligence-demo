[endpoint: Curiosity.Endpoints.Path("aeralis/rules/applicable")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// The engineering rules that bind this work order: those attached to its ATA chapter,
// marked with whether they have already been proposed against it.
//
// The chapter is reached by edge rather than by reading the work order's Ata2d string and
// looking a node up by that key — the connector links all three ATA levels precisely so
// this is one hop.

var req = Body.FromJson<TD.PawoRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var proposed = Q().StartAt(pawoUID)
    .Out(N.Rule.Type, E.AppliesRule)
    .AsUIDEnumerable()
    .ToHashSet();

var rules = Q().StartAt(pawoUID)
    .Out(N.AtaChapter.Type, E.InAta2d)
    .Out(N.Rule.Type, E.HasRule)
    .AsEnumerable()
    .Select(r => new TD.RuleDto
    {
        Uid = r.UID,
        Title = r.GetString(N.Rule.Title) ?? "",
        Text = r.GetString(N.Rule.Text) ?? "",
        Category = r.GetString(N.Rule.Category) ?? "",
        Severity = r.GetString(N.Rule.Severity) ?? "",
        Source = r.GetString(N.Rule.Source) ?? "",
        AlreadyProposed = proposed.Contains(r.UID),
    })
    .OrderBy(r => r.Severity == "MANDATORY" ? 0 : r.Severity == "RECOMMENDED" ? 1 : 2)
    .ToArray();

return new TD.RulesDto { Rules = rules };

