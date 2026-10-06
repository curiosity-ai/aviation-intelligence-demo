[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/remove-impact")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Un-confirms an impact. The inverse of confirm-impact, and equally a write.

var req = Body.FromJson<TD.ImpactRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
var targetUID = req?.TargetUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (targetUID == default(UID128)) return BadRequest("targetUID is required");

if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

if (!Graph.TryGet(targetUID, out var target))
    return NotFound($"Document '{targetUID}' not found.");

var (lockedPawo, lockedTarget) = await Graph.TryGetLockedAsync(pawoUID, targetUID);

lockedPawo.RemoveUniqueEdge(E.ConfirmedImpact, lockedTarget);
lockedTarget.RemoveUniqueEdge(E.ConfirmedImpactOf, lockedPawo);

await Graph.CommitAsync(lockedPawo, lockedTarget);

return new TD.OkDto { Success = true, Message = "removed" };

