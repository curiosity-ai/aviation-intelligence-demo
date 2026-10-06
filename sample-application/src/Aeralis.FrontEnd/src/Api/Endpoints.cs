namespace Aeralis.FrontEnd.Api
{
    /// <summary>
    /// Every workspace endpoint this front-end calls, as a path constant.
    ///
    /// The matching server-side implementations live in <c>src/Aeralis.Endpoints/</c>
    /// in the format a workspace definitions import expects, and their paths must stay
    /// identical to the values here — an endpoint is resolved by path, not by name.
    /// </summary>
    internal static class Endpoints
    {
        /// <summary>
        /// Counts for the dashboard's four metric tiles, for one user: items in their
        /// inbox, urgent actions, work orders they own, and work orders nobody owns.
        /// Body: <c>{ "userName": "mlefevre" }</c>. Read-only.
        /// </summary>
        public const string DashboardSummary = "aeralis/dashboard/summary";

        /// <summary>
        /// The "My performance" block: pre-analyses completed per week over the last
        /// twelve weeks, the mean hours per work order over the last month, and the
        /// validated / rejected / delegated split for that month. All computed from
        /// <c>WorkflowStatus</c>, <c>CompletedDate</c> and <c>HoursSpent</c> on the
        /// work orders — nothing is stored pre-aggregated. Read-only.
        /// </summary>
        public const string DashboardPerformance = "aeralis/dashboard/performance";

        /// <summary>
        /// The engineers the dashboard's perspective dropdown offers, with their ATA
        /// specialities. The demo has no real login, so the view renders "as" whoever
        /// is picked here. Read-only.
        /// </summary>
        public const string DashboardUsers = "aeralis/dashboard/users";

        /// <summary>
        /// The impacts already confirmed against a work order. Loaded once before the
        /// analysis lists render so every Confirm button starts in the right state.
        /// Body: <c>{ "pawoId": "PAWO-166204-01" }</c>. Read-only.
        /// </summary>
        public const string PawoConfirmedImpacts = "aeralis/pawo/confirmed-impacts";

        /// <summary>
        /// Confirms one suggested document as a real impact of a work order, writing a
        /// <c>ConfirmedImpact</c> edge. Body: <c>{ "pawoId": …, "docId": …, "note": … }</c>.
        /// Not read-only.
        /// </summary>
        public const string PawoConfirmImpact = "aeralis/pawo/confirm-impact";

        /// <summary>
        /// Removes a previously confirmed impact. Body: <c>{ "pawoId": …, "docId": … }</c>.
        /// Not read-only.
        /// </summary>
        public const string PawoRemoveImpact = "aeralis/pawo/remove-impact";

        /// <summary>
        /// Both halves of what has already been decided against a work order — the
        /// confirmed impacts and the rejected candidates — in one call, so the analysis
        /// list can badge every row it draws. Read-only.
        /// </summary>
        public const string PawoImpactDecisions = "aeralis/pawo/impact-decisions";

        /// <summary>
        /// Records one decision — <c>CONFIRMED</c>, <c>REJECTED</c> or <c>NONE</c> — against
        /// one or more candidate documents at once. The two edges are mutually exclusive, so
        /// setting either clears the other. Not read-only.
        /// </summary>
        public const string PawoSetImpactDecision = "aeralis/pawo/set-impact-decision";

        /// <summary>
        /// The issue history of one modification, newest first, for the issue comparison.
        /// Body: <c>{ "uid": … }</c>. Read-only.
        /// </summary>
        public const string ModIssues = "aeralis/mod/issues";

        /// <summary>
        /// The consolidated-summary matrix: rows are ATA 2D chapters, columns are
        /// document types, cells are counts of impacted documents. Read-only.
        /// </summary>
        public const string PawoImpactMatrix = "aeralis/pawo/impact-matrix";

        /// <summary>
        /// Drafts the analysis report and justification for a work order from its
        /// confirmed impacts. Stubbed server-side for now. Not read-only.
        /// </summary>
        public const string PawoDraftReport = "aeralis/pawo/draft-report";

        /// <summary>
        /// Documents this modification may affect — service bulletins, other
        /// modifications and manual content. Mixes text similarity with graph signals:
        /// shared part numbers and FINs, ATA proximity, and hard filters on program and
        /// on the work order's declared search scope. Read-only.
        /// </summary>
        public const string SimilarityAffectedDocuments = "aeralis/similarity/affected-documents";

        /// <summary>
        /// Pre-analyses of similar past changes: finds similar documents first, then
        /// walks to the work orders that analysed them and scores each by the best
        /// score among its documents. Read-only.
        /// </summary>
        public const string SimilarityPreviousAnalysis = "aeralis/similarity/previous-analysis";

        /// <summary>
        /// Folds per-document similarity scores into per-ATA-chapter scores by summing
        /// each document's score onto its ATA 2D. The UI compares the result against the
        /// impact matrix and banners any chapter the matrix does not already cover.
        /// Read-only.
        /// </summary>
        public const string SimilaritySuggestedAta = "aeralis/similarity/suggested-ata";

        /// <summary>
        /// The engineering rules applicable to a work order, by ATA chapter and program.
        /// Read-only.
        /// </summary>
        public const string RulesApplicable = "aeralis/rules/applicable";

        /// <summary>
        /// Proposes a rule against a work order, writing an <c>AppliesRule</c> edge.
        /// Not read-only.
        /// </summary>
        public const string RulesAddToPawo = "aeralis/rules/add-to-pawo";
    }
}
