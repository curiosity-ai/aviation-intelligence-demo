[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/impact-decisions")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Both halves of what the engineer has already decided, in one call.
//
// The analysis list badges every row it draws with the decision already taken against it,
// so it needs the confirmed set and the rejected set before the first row renders — two
// calls would let the list paint with one of them missing. `confirmed-impacts` still
// answers the confirmed set on its own for callers that only want that.
//
// Only UIDs come back: the caller renders these documents through the workspace's own
// renderers, so a title copied into this response would be a second, staler copy of one.

var req = Body.FromJson<TD.PawoRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var confirmed = Q().StartAt(pawoUID)
    .Out(edgeType: E.ConfirmedImpact)
    .AsUIDEnumerable()
    .ToArray();

var rejected = Q().StartAt(pawoUID)
    .Out(edgeType: E.RejectedImpact)
    .AsUIDEnumerable()
    .ToArray();

return new TD.ImpactDecisionsDto { ConfirmedUids = confirmed, RejectedUids = rejected };

