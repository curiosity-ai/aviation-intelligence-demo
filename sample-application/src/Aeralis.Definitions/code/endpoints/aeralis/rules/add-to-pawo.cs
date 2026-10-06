[endpoint: Curiosity.Endpoints.Path("aeralis/rules/add-to-pawo")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// Proposes a rule against a work order. A write, so not read-only.

var req = Body.FromJson<TD.RuleRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
var ruleUID = req?.RuleUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (ruleUID == default(UID128)) return BadRequest("ruleUID is required");

if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");
if (!Graph.TryGet(ruleUID, out var rule) || rule.Type != N.Rule.Type)
    return NotFound($"Rule '{ruleUID}' not found.");

var (lockedPawo, lockedRule) = await Graph.TryGetLockedAsync(pawoUID, ruleUID);

lockedPawo.AddUniqueEdge(E.AppliesRule, lockedRule);
lockedRule.AddUniqueEdge(E.RuleAppliedBy, lockedPawo);

await Graph.CommitAsync(lockedPawo, lockedRule);

Logger.LogInformation("Proposed rule {RuleUID} on {PawoUID}", ruleUID, pawoUID);
return new TD.OkDto { Success = true, Message = "proposed" };

