[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/set-impact-decision")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Records what the engineer decided about one or more candidate documents: they are a real
// impact, they are not, or the decision is taken back.
//
// **Batched, because the decision is.** The analysis list is multi-select — an engineer
// sweeps a page of suggestions and confirms or rejects them in one gesture — and one
// request per row would turn that gesture into thirty round trips against a graph that has
// to lock each pair anyway.
//
// **Both answers are written.** ConfirmedImpact was the only edge here; a rejection was
// simply the absence of one, which cannot be told apart from a suggestion nobody has looked
// at yet. RejectedImpact is what makes the analysis resumable: a dismissed candidate stays
// dismissed and says so on its row.
//
// The two edges are mutually exclusive by construction — setting either clears the other —
// so a document can never be both, whatever order the buttons are pressed in.

var req = Body.FromJson<TD.ImpactDecisionRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
var targetUIDs = req?.TargetUIDs ?? new UID128[0];
var decision = (req?.Decision ?? "").ToUpperInvariant();

if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (targetUIDs.Length == 0) return BadRequest("at least one targetUID is required");

if (decision != TD.ImpactDecision.Confirmed
    && decision != TD.ImpactDecision.Rejected
    && decision != TD.ImpactDecision.None)
{
    return BadRequest($"decision must be {TD.ImpactDecision.Confirmed}, {TD.ImpactDecision.Rejected} or {TD.ImpactDecision.None}");
}

if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

int applied = 0;
int skipped = 0;

foreach (var targetUID in targetUIDs)
{
    if (targetUID == default(UID128)) { skipped++; continue; }

    // A candidate list can surface anything the similarity engine ranked, so the type is
    // checked per target rather than trusted: an impact is only meaningful against a
    // document a pre-analysis can actually impact.
    if (!Graph.TryGet(targetUID, out var target) || !TD.ImpactableTypes.Contains(target.Type))
    {
        skipped++;
        continue;
    }

    var (lockedPawo, lockedTarget) = await Graph.TryGetLockedAsync(pawoUID, targetUID);

    lockedPawo.RemoveUniqueEdge(E.ConfirmedImpact, lockedTarget);
    lockedTarget.RemoveUniqueEdge(E.ConfirmedImpactOf, lockedPawo);
    lockedPawo.RemoveUniqueEdge(E.RejectedImpact, lockedTarget);
    lockedTarget.RemoveUniqueEdge(E.RejectedImpactOf, lockedPawo);

    if (decision == TD.ImpactDecision.Confirmed)
    {
        lockedPawo.AddUniqueEdge(E.ConfirmedImpact, lockedTarget);
        lockedTarget.AddUniqueEdge(E.ConfirmedImpactOf, lockedPawo);
    }
    else if (decision == TD.ImpactDecision.Rejected)
    {
        lockedPawo.AddUniqueEdge(E.RejectedImpact, lockedTarget);
        lockedTarget.AddUniqueEdge(E.RejectedImpactOf, lockedPawo);
    }

    await Graph.CommitAsync(lockedPawo, lockedTarget);
    applied++;
}

Logger.LogInformation("Impact decision {Decision} applied to {Applied} target(s) on {PawoUID}, {Skipped} skipped",
                      decision, applied, pawoUID, skipped);

return new TD.OkDto { Success = applied > 0, Message = $"{decision.ToLowerInvariant()}:{applied}" };

