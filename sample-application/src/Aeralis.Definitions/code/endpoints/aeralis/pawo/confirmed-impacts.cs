[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/confirmed-impacts")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Loaded once before the analysis lists render, so every Confirm button starts in the
// right state. A button that starts wrong is worse than no button.
//
// Only UIDs come back. The caller already renders these documents through the workspace's
// renderers, so a title copied into this response would be a second, staler copy of one.

var req = Body.FromJson<TD.PawoRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var uids = Q().StartAt(pawoUID)
    .Out(edgeType: E.ConfirmedImpact)
    .AsUIDEnumerable()
    .ToArray();

return new TD.ConfirmedImpactsDto { Uids = uids };

