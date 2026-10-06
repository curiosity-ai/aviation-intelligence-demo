[endpoint: Curiosity.Endpoints.Path("aeralis/_lib/aeralis-helpers")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

// Shared types for the aeralis endpoints. Imported, never called — it is a library, and
// its AdminOnly access mode is what makes that explicit.
//
// The DTOs below are the wire contract, and the front-end declares the *same* classes in
// `src/Aeralis.FrontEnd/src/Api/Dto.cs`. Both ends therefore name the same fields, and a
// rename that only lands on one side stops compiling rather than silently deserialising
// to null: the endpoint result is serialised by Newtonsoft with the default resolver, so
// what the front-end reads back is exactly these property names.
//
// Everything crossing the wire is a **UID**, never a document identifier or a code. A UID
// addresses a node; `MOD-166204` or `53` addresses whatever the receiver decides to look
// it up as. Sending the UID means the endpoints stop guessing types from strings, and it
// means the front-end can hand a response straight to `NeighborsLinks` or to a
// `SearchArea` with `WithTargetUIDs` and get the workspace's own labels and renderers.
//
// They are typed `UID128`, not `string`, on both sides. Here it is a struct carrying a
// `[JsonConverter]` that reads and writes a plain JSON string; in the front-end a `UID128`
// *is* a JS string at run time (see the note in `Mosaik.FrontEnd.Core/.../UID128.cs`). So
// the wire format is a string either way, and neither end has to parse or stringify at the
// boundary — a UID handed to the wrong parameter is a compile error instead.

public static class TD
{
    /// <summary>The dataset's fixed "today", so the demo's relative windows never drift.</summary>
    public static readonly Time Today = new Time(new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Every document type a pre-analysis can decide is impacted.</summary>
    public static readonly string[] ImpactableTypes =
    {
        N.ServiceBulletin.Type, N.Modification.Type, N.AmmTask.Type, N.IpcFigure.Type,
        N.SrmRepair.Type, N.TsmProcedure.Type, N.WdmSheet.Type, N.FcomEntry.Type,
    };

    /// <summary>The columns of the impact matrix, in the order it reads.</summary>
    public static readonly string[] MatrixDocTypes =
    {
        N.AmmTask.Type, N.IpcFigure.Type, N.ServiceBulletin.Type, N.SrmRepair.Type,
        N.WdmSheet.Type, N.TsmProcedure.Type, N.FcomEntry.Type,
    };

    // --- requests -------------------------------------------------------------

    public class UserRequest { public UID128 UserUID { get; set; } }

    public class PawoRequest { public UID128 PawoUID { get; set; } }

    public class ImpactRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128 TargetUID { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Addresses one node and nothing else. Used by the endpoints that read a history.</summary>
    public class NodeRequest { public UID128 Uid { get; set; } }

    /// <summary>The three answers a candidate impact can be given. Uppercase on the wire.</summary>
    public static class ImpactDecision
    {
        public const string Confirmed = "CONFIRMED";
        public const string Rejected = "REJECTED";
        public const string None = "NONE";
    }

    /// <summary>
    /// One decision applied to many candidates at once, because the analysis list is
    /// multi-select and the engineer decides about a page of suggestions in one gesture.
    /// </summary>
    public class ImpactDecisionRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128[] TargetUIDs { get; set; }
        public string Decision { get; set; }
    }

    public class RuleRequest
    {
        public UID128 PawoUID { get; set; }
        public UID128 RuleUID { get; set; }
        public string Justification { get; set; }
    }

    public class SimilarityRequest
    {
        public UID128 PawoUID { get; set; }
        public string Query { get; set; }
    }

    public class DraftReportRequest
    {
        public UID128 PawoUID { get; set; }
        public string ExistingText { get; set; }
    }

    // --- responses ------------------------------------------------------------

    /// <summary>
    /// The four metric tiles. Each carries the work orders it counted, so the dashboard can
    /// list them without a second call and without re-deriving the same predicate client-side.
    /// </summary>
    public class DashboardSummaryDto
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
    public class WeeklyThroughputDto
    {
        public string WeekStarting { get; set; }
        public int Completed { get; set; }
    }

    public class PerformanceDto
    {
        public WeeklyThroughputDto[] Throughput { get; set; }
        public double AverageHoursPerPawo { get; set; }
        public int LastMonthTotal { get; set; }
        public double ValidatedPercent { get; set; }
        public double RejectedPercent { get; set; }
        public double DelegatedPercent { get; set; }
    }

    /// <summary>
    /// One engineer. Only the name is spelled out — where they sit, what they do and which
    /// chapters they specialise in are all nodes now, so they travel as UIDs the front-end
    /// renders with the workspace's own labels.
    /// </summary>
    public class UserDto
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

    public class UsersDto
    {
        public UserDto[] Users { get; set; }
    }

    /// <summary>The documents confirmed as impacted, as UIDs. Nothing else is needed: the
    /// caller renders them through the workspace's renderers.</summary>
    public class ConfirmedImpactsDto
    {
        public UID128[] Uids { get; set; }
    }

    /// <summary>
    /// Both halves of what has been decided against a work order. A rejected candidate is
    /// not the same as one nobody has looked at, so the list needs both sets to badge its
    /// rows honestly.
    /// </summary>
    public class ImpactDecisionsDto
    {
        public UID128[] ConfirmedUids { get; set; }
        public UID128[] RejectedUids { get; set; }
    }

    /// <summary>One issue of a document, as the issue picker reads it.</summary>
    public class IssueDto
    {
        public UID128 Uid { get; set; }
        public string IssueId { get; set; }
        public string IssueLetter { get; set; }
        public string IssueReason { get; set; }
        public string Status { get; set; }
        public bool IsLatest { get; set; }
    }

    public class IssuesDto
    {
        public IssueDto[] Issues { get; set; }
    }

    /// <summary>One cell of the ATA-by-document-type impact matrix.</summary>
    public class ImpactMatrixCellDto
    {
        public UID128 AtaUID { get; set; }
        public string DocType { get; set; }
        public int Count { get; set; }
    }

    public class ImpactMatrixDto
    {
        public UID128[] AtaUIDs { get; set; }
        public string[] DocTypes { get; set; }
        public ImpactMatrixCellDto[] Cells { get; set; }
    }

    /// <summary>An ATA chapter the similarity pass suggests, with its summed score.</summary>
    public class SuggestedAtaDto
    {
        public UID128 AtaUID { get; set; }
        public double Score { get; set; }
        public int DocumentCount { get; set; }

        /// <summary>True when the impact matrix does not already cover this chapter.</summary>
        public bool IsNew { get; set; }
    }

    public class SuggestedAtaListDto
    {
        public SuggestedAtaDto[] Chapters { get; set; }
    }

    /// <summary>
    /// An engineering rule. The text is carried because the card renders it verbatim; the
    /// rule itself is addressed by UID, so proposing it needs no second lookup by code.
    /// </summary>
    public class RuleDto
    {
        public UID128 Uid { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public string Source { get; set; }
        public bool AlreadyProposed { get; set; }
    }

    public class RulesDto
    {
        public RuleDto[] Rules { get; set; }
    }

    public class DraftReportDto
    {
        public string Text { get; set; }
    }

    public class OkDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}

