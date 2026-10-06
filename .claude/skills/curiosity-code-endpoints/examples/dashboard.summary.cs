// Reference template — definitions path: code/endpoints/app/dashboard/summary.cs
//
// The four numbers behind the home page's metric tiles, for one user — and the UID array
// behind each of them, so the tile and the list it opens are provably the same set.

[endpoint: Curiosity.Endpoints.Path("app/dashboard/summary")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

var req     = Body.FromJson<TD.UserRequest>();
var userUID = req?.UserUID ?? default(UID128);

if (userUID == default(UID128))              return BadRequest("userUID is required");
if (!Graph.TryGet(userUID, out var user))    return NotFound($"User '{userUID}' not found.");

// Ownership is an EDGE and only an edge — there is no AssignedToUserName property to compare
// against. That is what guarantees this count and the "mine" list on the page agree: both are
// this traversal.
var owned = Q().StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsEnumerable().ToList();

// "In my inbox" and "mine" are DELIBERATELY different sets: the inbox is what is on the desk
// and not finished; "mine" is everything ever assigned. Both numbers are on the home page, so
// both need their own list — a card whose number says 7 opening a list of 34 is a card that lied.
var inbox  = owned.Where(c => c.GetString(N.Case.WorkflowStatus) != TD.STATUS_VALIDATED).ToList();
var urgent = inbox.Where(c => c.GetBool(N.Case.IsUrgent)).ToList();

// "Unowned" is "no user points at it" — not a flag beside the record that a second write
// could leave stale.
var assigned = new HashSet<UID128>(Q().StartAt(N._User.Type)
                                      .Out(N.Case.Type, E.HasAssignment)
                                      .AsUIDEnumerable());

var unassigned = Q().StartAt(N.Case.Type)
                    .AsEnumerable()
                    .Where(c => !assigned.Contains(c.UID))
                    .ToList();

return new TD.SummaryDto
{
    InboxCount      = inbox.Count,
    InboxUIDs       = inbox.Select(c => c.UID).ToArray(),

    UrgentCount     = urgent.Count,
    UrgentUIDs      = urgent.Select(c => c.UID).ToArray(),

    OwnedCount      = owned.Count,

    UnassignedCount = unassigned.Count,
    UnassignedUIDs  = unassigned.Select(c => c.UID).ToArray(),
};
