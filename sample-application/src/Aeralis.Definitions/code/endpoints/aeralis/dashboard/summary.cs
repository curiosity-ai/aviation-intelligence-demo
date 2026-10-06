[endpoint: Curiosity.Endpoints.Path("aeralis/dashboard/summary")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// The four metric tiles. Counts come from the work orders themselves — the connector
// stores state, not pre-aggregated numbers, so these move when the data does.
//
// Ownership is read as an edge, not as a name on the work order. That is where the
// connector puts it, and it means "mine" is one traversal from the user rather than a
// string comparison that a rename would quietly break.

var req = Body.FromJson<TD.UserRequest>();
var userUID = req?.UserUID ?? default(UID128);
if (userUID == default(UID128)) return BadRequest("userUID is required");
if (!Graph.TryGet(userUID, out var user) || user.Type != N._User.Type)
    return NotFound($"User '{userUID}' not found.");

var mine = Q().StartAt(userUID)
    .Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment)
    .AsEnumerable()
    .Where(p => !p.GetBool(N.PreAnalysisWorkOrder.IsHistorical))
    .ToList();

// An unassigned work order is one with no AssignedTo edge at all — again the edge, not a
// flag beside it that a second write could leave stale.
var assigned = Q().StartAt(N._User.Type)
    .Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment)
    .AsUIDEnumerable()
    .ToHashSet();

var unassigned = Q().StartAt(N.PreAnalysisWorkOrder.Type)
    .AsEnumerable()
    .Where(p => !p.GetBool(N.PreAnalysisWorkOrder.IsHistorical) && !assigned.Contains(p.UID))
    .Select(p => p.UID)
    .ToArray();

var inbox = mine.Where(p => (p.GetString(N.PreAnalysisWorkOrder.WorkflowStatus) ?? "") != "VALIDATED")
                .Select(p => p.UID).ToArray();
var urgent = mine.Where(p => p.GetBool(N.PreAnalysisWorkOrder.IsUrgent))
                 .Select(p => p.UID).ToArray();
var owned = mine.Select(p => p.UID).ToArray();

return new TD.DashboardSummaryDto
{
    InboxCount = inbox.Length,
    UrgentCount = urgent.Length,
    OwnedCount = owned.Length,
    UnassignedCount = unassigned.Length,

    InboxUIDs = inbox,
    UrgentUIDs = urgent,
    OwnedUIDs = owned,
    UnassignedUIDs = unassigned,
};

