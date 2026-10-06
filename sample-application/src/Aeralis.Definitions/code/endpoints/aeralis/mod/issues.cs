[endpoint: Curiosity.Endpoints.Path("aeralis/mod/issues")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// The issue history of one modification, newest first.
//
// The issue comparison used to offer a hard-coded "Revision A / B / C" that pointed at
// nothing. The graph has held the real history all along — one ModificationIssue node per
// issue, chained by SupersedesIssue and each with the PDF that was published for it — so
// the picker is filled from here instead.
//
// Ordered newest first so the caller can select the first entry that is not the current one
// and land on "the issue before this one", which is the comparison anybody opening this tab
// actually wants.

var req = Body.FromJson<TD.NodeRequest>();
var uid = req?.Uid ?? default(UID128);
if (uid == default(UID128)) return BadRequest("uid is required");

if (!Graph.TryGet(uid, out var mod) || mod.Type != N.Modification.Type)
    return NotFound($"Modification '{uid}' not found.");

var issues = Q().StartAt(uid)
    .Out(N.ModificationIssue.Type, E.HasIssue)
    .AsEnumerable()
    .Select(i => new TD.IssueDto
    {
        Uid = i.UID,
        IssueId = i.GetString(N.ModificationIssue.IssueId) ?? "",
        IssueLetter = i.GetString(N.ModificationIssue.IssueLetter) ?? "",
        IssueReason = i.GetString(N.ModificationIssue.IssueReason) ?? "",
        Status = i.GetString(N.ModificationIssue.Status) ?? "",
        IsLatest = i.GetBool(N.ModificationIssue.IsLatest),
    })
    // Issue letters are single letters ascending with age, so descending is newest first.
    .OrderByDescending(i => i.IssueLetter)
    .ToArray();

return new TD.IssuesDto { Issues = issues };

