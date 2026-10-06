using UID;

namespace Aeralis.FrontEnd.Api
{
    // Request and response shapes for the workspace endpoints. Kept flat — no
    // inheritance, no polymorphism, no nested generics beyond arrays — because Transpose
    // has limited runtime reflection and a hierarchy here would fail at run time rather
    // than at compile time.
    //
    // These are ordinary classes: API.Endpoints.CallAsync deserialises the response
    // normally, so the [ObjectLiteral] treatment is neither needed nor wanted.
    //
    // Every class below is declared identically inside `TD` in
    // `src/Aeralis.Endpoints/code/endpoints/aeralis/_lib/aeralis-helpers.cs`, and the
    // endpoints return those types rather than anonymous objects. The endpoint result is
    // serialised with Newtonsoft's default resolver and read back the same way, so the two
    // declarations *are* the wire format — keep them in step.
    //
    // Everything that identifies something is a **UID128**, not a document id and not a
    // code. A UID names a node, so a response can be handed straight to `NeighborsLinks` or
    // to a `SearchArea` with `WithTargetUIDs` and rendered by the workspace's own renderers,
    // and a request never asks the server to guess which type an identifier belongs to.
    //
    // `UID128` rather than `string` on both sides: here it is a JS string at run time (see
    // the note in `Mosaik.FrontEnd.Core/.../UID128.cs`), and server-side it is a struct with
    // a `[JsonConverter]` that reads and writes the same plain string. So the wire format is
    // identical either way, and neither end parses or stringifies at the boundary.

    internal class UserRequest
    {
        public UID128 UserUID { get; set; }
    }

    internal class PawoRequest
    {
        public UID128 PawoUID { get; set; }
    }

    internal class ImpactRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128 TargetUID { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Addresses one node and nothing else. Used by the endpoints that read a history.</summary>
    internal class NodeRequest
    {
        public UID128 Uid { get; set; }
    }

    /// <summary>The three answers a candidate impact can be given. Uppercase on the wire.</summary>
    internal static class ImpactDecision
    {
        public const string Confirmed = "CONFIRMED";
        public const string Rejected = "REJECTED";
        public const string None = "NONE";
    }

    /// <summary>
    /// One decision applied to many candidates at once. The analysis list is multi-select,
    /// so the engineer decides about a page of suggestions in one gesture and the request
    /// carries the whole gesture rather than one row of it.
    /// </summary>
    internal class ImpactDecisionRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128[] TargetUIDs { get; set; }
        public string Decision { get; set; }
    }

    internal class RuleRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128 RuleUID { get; set; }
        public string Justification { get; set; }
    }

    internal class SimilarityRequest
    {
        public UID128 PawoUID { get; set; }
        public string Query { get; set; }
    }

    internal class DraftReportRequest
    {
        public UID128 PawoUID { get; set; }
        public string ExistingText { get; set; }
    }

    // -----------------------------------------------------------------------------
    // responses
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The four metric tiles. Each carries the work orders it counted, so a tile can be
    /// turned into the list behind it without a second call.
    /// </summary>
    internal class DashboardSummaryDto
    {
        public int InboxCount { get; set; }
        public int UrgentCount { get; set; }
        public int OwnedCount { get; set; }
        public int UnassignedCount { get; set; }

        public UID128[] InboxUIDs { get; set; }
        public UID128[] UrgentUIDs { get; set; }
        public UID128[] OwnedUIDs { get; set; }
        public UID128[] UnassignedUIDs { get; set; }
    }

    /// <summary>One bar of the throughput chart.</summary>
    internal class WeeklyThroughputDto
    {
        public string WeekStarting { get; set; }
        public int Completed { get; set; }
    }

    internal class PerformanceDto
    {
        public WeeklyThroughputDto[] Throughput { get; set; }
        public double AverageHoursPerPawo { get; set; }
        public int LastMonthTotal { get; set; }
        public double ValidatedPercent { get; set; }
        public double RejectedPercent { get; set; }
        public double DelegatedPercent { get; set; }
    }

    /// <summary>
    /// One engineer. Where they sit, what they do and which chapters they specialise in are
    /// nodes, so they arrive as UIDs and render through the workspace's own labels.
    /// </summary>
    internal class UserDto
    {
        public UID128 Uid { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public UID128[] AtaSpecialityUIDs { get; set; }
        public UID128[] SiteUIDs { get; set; }
        public UID128[] DepartmentUIDs { get; set; }
        public UID128[] RoleUIDs { get; set; }

        // The same four dimensions as text, so the picker's search box has something to match
        // on. Rendering still goes through the UIDs above; these are only ever read.
        public string AtaSpecialityText { get; set; }
        public string SiteText { get; set; }
        public string DepartmentText { get; set; }
        public string RoleText { get; set; }
    }

    internal class UsersDto
    {
        public UserDto[] Users { get; set; }
    }

    internal class ConfirmedImpactsDto
    {
        public UID128[] Uids { get; set; }
    }

    /// <summary>
    /// Both halves of what has been decided against a work order. A rejected candidate is
    /// not the same as one nobody has looked at, so the list needs both sets before it
    /// draws its first row.
    /// </summary>
    internal class ImpactDecisionsDto
    {
        public UID128[] ConfirmedUids { get; set; }
        public UID128[] RejectedUids { get; set; }
    }

    /// <summary>One issue of a document, as the issue picker reads it.</summary>
    internal class IssueDto
    {
        public UID128 Uid { get; set; }
        public string IssueId { get; set; }
        public string IssueLetter { get; set; }
        public string IssueReason { get; set; }
        public string Status { get; set; }
        public bool IsLatest { get; set; }
    }

    internal class IssuesDto
    {
        public IssueDto[] Issues { get; set; }
    }

    /// <summary>One cell of the ATA-by-document-type impact matrix.</summary>
    internal class ImpactMatrixCellDto
    {
        public UID128 AtaUID { get; set; }
        public string DocType { get; set; }
        public int Count { get; set; }
    }

    internal class ImpactMatrixDto
    {
        public UID128[] AtaUIDs { get; set; }
        public string[] DocTypes { get; set; }
        public ImpactMatrixCellDto[] Cells { get; set; }
    }

    /// <summary>An ATA chapter the similarity pass suggests, with its summed score.</summary>
    internal class SuggestedAtaDto
    {
        public UID128 AtaUID { get; set; }
        public double Score { get; set; }
        public int DocumentCount { get; set; }

        /// <summary>True when the impact matrix does not already cover this chapter.</summary>
        public bool IsNew { get; set; }
    }

    internal class SuggestedAtaListDto
    {
        public SuggestedAtaDto[] Chapters { get; set; }
    }

    internal class RuleDto
    {
        public UID128 Uid { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public string Source { get; set; }
        public bool AlreadyProposed { get; set; }
    }

    internal class RulesDto
    {
        public RuleDto[] Rules { get; set; }
    }

    internal class DraftReportDto
    {
        public string Text { get; set; }
    }

    internal class OkDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
