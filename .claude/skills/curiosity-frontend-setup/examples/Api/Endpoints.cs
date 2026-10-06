// Reference template: one const per endpoint path, each XML-documented.
//
// The matching server-side implementations live in the definitions folder under
// code/endpoints/, and their Path attributes must stay IDENTICAL to the values here — an
// endpoint is resolved by path, not by name.

namespace App.FrontEnd.Api
{
    internal static class Endpoints
    {
        /// <summary>
        /// The four numbers behind the home page's metric tiles, for one user — inbox,
        /// urgent, owned, unassigned — each with the UID array it counted, so opening a
        /// number never shows a different set from the one the tile added up.
        /// Body: <c>{ "userUID": … }</c>. Read-only.
        /// </summary>
        public const string DashboardSummary = "app/dashboard/summary";

        /// <summary>
        /// The "My performance" block: cases completed per week over the last twelve weeks,
        /// mean hours per case over the last month, and the validated / rejected / delegated
        /// split for that month. All computed live from <c>WorkflowStatus</c>,
        /// <c>CompletedDate</c> and <c>HoursSpent</c> — nothing is stored pre-aggregated.
        /// Read-only.
        /// </summary>
        public const string DashboardPerformance = "app/dashboard/performance";

        /// <summary>
        /// The people the perspective picker offers, each dimension returned as UIDs (what
        /// renders) and as text (what the search box matches). Read-only.
        /// </summary>
        public const string DashboardUsers = "app/dashboard/users";

        /// <summary>
        /// Both halves of what has already been decided against a case — confirmed and
        /// rejected — in ONE call, so every row can be badged before the first one draws.
        /// Read-only.
        /// </summary>
        public const string CaseDecisions = "app/case/decisions";

        /// <summary>
        /// Records one decision — <c>CONFIRMED</c>, <c>REJECTED</c> or <c>NONE</c> — against a
        /// whole selection at once. The two edges are mutually exclusive, so setting either
        /// clears the other. NOT read-only.
        /// </summary>
        public const string CaseSetDecision = "app/case/set-decision";

        /// <summary>
        /// Records the change under analysis may affect. Mixes embedding similarity with graph
        /// signals — shared parts, category proximity — and filters hard on partition.
        /// Returns the similarity engine's own result. Read-only.
        /// </summary>
        public const string SimilarityAffectedDocuments = "app/similarity/affected-documents";
    }
}
