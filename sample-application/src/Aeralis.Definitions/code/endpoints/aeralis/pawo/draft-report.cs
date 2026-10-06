[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/draft-report")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Drafts the analysis report from the confirmed impacts.
//
// STUB. The real implementation calls ChatAI with the work order's findings summary and
// its confirmed impacts; until then this returns a deterministic skeleton so the button
// and the round trip can be exercised without pretending to be an AI answer.

var req = Body.FromJson<TD.DraftReportRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var pawoId = pawo.GetString(N.PreAnalysisWorkOrder.DocId) ?? pawoUID.ToString();

var impacts = Q().StartAt(pawoUID)
    .Out(edgeType: E.ConfirmedImpact)
    .AsEnumerable()
    .Select(n => n.GetString(N.Modification.DocId) ?? n.UID.ToString())
    .ToList();

var text = impacts.Count == 0
    ? "No impacts have been confirmed yet, so there is nothing to justify. Confirm the impacts found in the Analysis tab first."
    : $"Pre-analysis of {pawoId} confirmed {impacts.Count} impacted document(s): {string.Join(", ", impacts)}. "
      + "[DRAFT — replace with the generated justification once the AI endpoint is implemented.]";

return new TD.DraftReportDto { Text = text };

