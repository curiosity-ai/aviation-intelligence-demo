using System.Threading.Tasks;
using UID;

namespace Aeralis.FrontEnd.Api
{
    /// <summary>
    /// One method per workspace endpoint.
    ///
    /// Calls go through <see cref="Mosaik.API.Endpoints.CallAsync{T}"/>, which takes the
    /// endpoint path and handles the <c>endpoints/run/…</c> routing, the body encoding
    /// and the auto-login retry itself — so this layer only has to say which endpoint
    /// and with what.
    ///
    /// Every parameter that names something is a <see cref="UID128"/>. The front-end
    /// already holds the node it is looking at, so sending its UID costs nothing and
    /// spares the endpoint from re-deriving a node from a document identifier — which it
    /// could only do by trying each candidate type in turn.
    ///
    /// <c>readOnlyCompatible</c> mirrors the endpoint's own
    /// <c>[endpoint: Curiosity.Endpoints.ReadOnly]</c> marking: true for the queries,
    /// false for the four that write.
    /// </summary>
    internal static class Api
    {
        // --- dashboard ----------------------------------------------------------

        internal static Task<DashboardSummaryDto> DashboardSummaryAsync(UID128 userUID)
            => Mosaik.API.Endpoints.CallAsync<DashboardSummaryDto>(
                Endpoints.DashboardSummary,
                new UserRequest { UserUID = userUID },
                readOnlyCompatible: true);

        internal static Task<PerformanceDto> DashboardPerformanceAsync(UID128 userUID)
            => Mosaik.API.Endpoints.CallAsync<PerformanceDto>(
                Endpoints.DashboardPerformance,
                new UserRequest { UserUID = userUID },
                readOnlyCompatible: true);

        internal static Task<UsersDto> UsersAsync()
            => Mosaik.API.Endpoints.CallAsync<UsersDto>(
                Endpoints.DashboardUsers,
                readOnlyCompatible: true);

        // --- confirmed impacts --------------------------------------------------

        internal static Task<ConfirmedImpactsDto> ConfirmedImpactsAsync(UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<ConfirmedImpactsDto>(
                Endpoints.PawoConfirmedImpacts,
                new PawoRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        internal static Task<OkDto> ConfirmImpactAsync(UID128 pawoUID, UID128 targetUID, string note)
            => Mosaik.API.Endpoints.CallAsync<OkDto>(
                Endpoints.PawoConfirmImpact,
                new ImpactRequest
                {
                    PawoUID = pawoUID, TargetUID = targetUID, Note = note,
                });

        internal static Task<OkDto> RemoveImpactAsync(UID128 pawoUID, UID128 targetUID)
            => Mosaik.API.Endpoints.CallAsync<OkDto>(
                Endpoints.PawoRemoveImpact,
                new ImpactRequest { PawoUID = pawoUID, TargetUID = targetUID });

        /// <summary>
        /// Both decision sets in one call. The analysis list badges its rows from these, and
        /// a row badged from a stale half is worse than one badged from neither.
        /// </summary>
        internal static Task<ImpactDecisionsDto> ImpactDecisionsAsync(UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<ImpactDecisionsDto>(
                Endpoints.PawoImpactDecisions,
                new PawoRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        /// <summary>
        /// One decision for a whole selection. Batched because the list is multi-select —
        /// one request per row would turn a single gesture into a request per row.
        /// </summary>
        internal static Task<OkDto> SetImpactDecisionAsync(UID128 pawoUID, UID128[] targetUIDs, string decision)
            => Mosaik.API.Endpoints.CallAsync<OkDto>(
                Endpoints.PawoSetImpactDecision,
                new ImpactDecisionRequest
                {
                    PawoUID = pawoUID, TargetUIDs = targetUIDs, Decision = decision,
                });

        /// <summary>The modification's issue history, newest first.</summary>
        internal static Task<IssuesDto> ModIssuesAsync(UID128 modUID)
            => Mosaik.API.Endpoints.CallAsync<IssuesDto>(
                Endpoints.ModIssues,
                new NodeRequest { Uid = modUID },
                readOnlyCompatible: true);

        internal static Task<ImpactMatrixDto> ImpactMatrixAsync(UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<ImpactMatrixDto>(
                Endpoints.PawoImpactMatrix,
                new PawoRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        internal static Task<DraftReportDto> DraftReportAsync(UID128 pawoUID, string existingText)
            => Mosaik.API.Endpoints.CallAsync<DraftReportDto>(
                Endpoints.PawoDraftReport,
                new DraftReportRequest { PawoUID = pawoUID, ExistingText = existingText });

        // --- similarity ---------------------------------------------------------

        internal static Task<SuggestedAtaListDto> SuggestedAtaAsync(UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<SuggestedAtaListDto>(
                Endpoints.SimilaritySuggestedAta,
                new PawoRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        /// <summary>
        /// Both similarity lists share a shape, so the caller passes the endpoint path
        /// rather than there being two near-identical methods.
        ///
        /// The shape is the engine's own <c>SimilarityResult</c>, returned whole: it goes
        /// to <c>SearchArea.WithSimilarityEngine</c>, which renders each hit as a result
        /// card with its score and per-signal breakdown underneath. Nothing here trims or
        /// re-orders it — the scenario's own candidate limits are the limits.
        /// </summary>
        internal static Task<Mosaik.GraphDB.Similarity.SimilarityResult> SimilarityAsync(
            string endpointPath, UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<Mosaik.GraphDB.Similarity.SimilarityResult>(
                endpointPath,
                new SimilarityRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        // --- rules --------------------------------------------------------------

        internal static Task<RulesDto> ApplicableRulesAsync(UID128 pawoUID)
            => Mosaik.API.Endpoints.CallAsync<RulesDto>(
                Endpoints.RulesApplicable,
                new PawoRequest { PawoUID = pawoUID },
                readOnlyCompatible: true);

        internal static Task<OkDto> ProposeRuleAsync(UID128 pawoUID, UID128 ruleUID, string justification)
            => Mosaik.API.Endpoints.CallAsync<OkDto>(
                Endpoints.RulesAddToPawo,
                new RuleRequest
                {
                    PawoUID = pawoUID, RuleUID = ruleUID,
                    Justification = justification,
                });
    }
}
