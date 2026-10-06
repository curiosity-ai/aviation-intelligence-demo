[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/confirm-impact")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Confirms one suggested document as a real impact. Deliberately NOT read-only: this
// writes the ConfirmedImpact edge, which is the one impact edge the connector never
// creates — a confirmed impact is a human judgement, not a derived fact.
//
// The target arrives as a UID, so there is no sweep over the impactable types trying each
// in turn to work out what a document identifier refers to: the UID already says.

var req = Body.FromJson<TD.ImpactRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
var targetUID = req?.TargetUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (targetUID == default(UID128)) return BadRequest("targetUID is required");

if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

if (!Graph.TryGet(targetUID, out var target) || !TD.ImpactableTypes.Contains(target.Type))
    return NotFound($"Document '{targetUID}' is not a document a pre-analysis can impact.");

var (lockedPawo, lockedTarget) = await Graph.TryGetLockedAsync(pawoUID, targetUID);

lockedPawo.AddUniqueEdge(E.ConfirmedImpact, lockedTarget);
lockedTarget.AddUniqueEdge(E.ConfirmedImpactOf, lockedPawo);

await Graph.CommitAsync(lockedPawo, lockedTarget);

Logger.LogInformation("Confirmed impact {TargetUID} on {PawoUID}", targetUID, pawoUID);
return new TD.OkDto { Success = true, Message = "confirmed" };

