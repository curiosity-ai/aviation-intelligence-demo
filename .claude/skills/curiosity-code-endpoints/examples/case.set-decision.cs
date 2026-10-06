// Reference template — definitions path: code/endpoints/app/case/set-decision.cs
//
// A WRITE endpoint. Note what is NOT here: [Curiosity.Endpoints.ReadOnly]. The runtime hands a
// read-only endpoint a read-only scope, so the write below would fail at RUN TIME, not at
// import — which makes this the easiest mistake on the project to ship.
//
// This is also the one edge the connector never writes. Everything else the graph knows is
// derived from the corpus; a confirmed impact is a human judgement made in the UI — which is
// exactly what makes it meaningful to compare against the ground-truth findings the connector
// DOES write.

[endpoint: Curiosity.Endpoints.Path("app/case/set-decision")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

var req      = Body.FromJson<TD.DecisionRequest>();
var caseUID  = req?.CaseUID ?? default(UID128);
var targets  = req?.TargetUIDs ?? new UID128[0];
var decision = req?.Decision ?? TD.DECISION_NONE;

if (caseUID == default(UID128))     return BadRequest("caseUID is required");
if (targets.Length == 0)            return BadRequest("at least one targetUID is required");

if (decision != TD.DECISION_CONFIRMED && decision != TD.DECISION_REJECTED && decision != TD.DECISION_NONE)
    return BadRequest($"decision must be {TD.DECISION_CONFIRMED}, {TD.DECISION_REJECTED} or {TD.DECISION_NONE}");

// Check the TYPE, not just that the UID resolves.
if (!Graph.TryGet(caseUID, out var c) || c.Type != N.Case.Type)
    return NotFound($"Case '{caseUID}' not found.");

// Batched on purpose: the candidate list is multi-select, and one request per row would turn a
// single gesture into twenty round-trips.
foreach (var targetUID in targets)
{
    if (!Graph.TryGet(targetUID, out var target)) continue;

    // The two edges are mutually exclusive by construction, so setting either clears the other.
    // Without that, a candidate could end up both confirmed and rejected and no reader could
    // say which decision was the later one.
    Graph.Unlink(c, target, E.ConfirmedImpact, E.ConfirmedImpactOf);
    Graph.Unlink(c, target, E.RejectedImpact,  E.RejectedImpactOf);

    if (decision == TD.DECISION_CONFIRMED) Graph.Link(c, target, E.ConfirmedImpact, E.ConfirmedImpactOf);
    if (decision == TD.DECISION_REJECTED)  Graph.Link(c, target, E.RejectedImpact,  E.RejectedImpactOf);
}

await Graph.CommitPendingAsync();

// Return a VALUE, not a JSON string: the controller serialises whatever the script returns, so
// returning x.ToJson() sends it as text/plain and lets the shape drift from the DTO the front
// end deserialises.
return new TD.OkDto { Ok = true };
