// Reference template: one method per endpoint.
//
// EVERY server call goes through this layer. The raw request builder is an implementation
// detail and must never appear in a component, an editor or a renderer — so a reader can tell
// at a glance that a line hits the server.
//
// Every parameter naming something is a UID128: the front end already holds the node it is
// looking at, so sending its UID costs nothing and spares the endpoint from re-deriving a node
// from a business identifier — which it could only do by trying each candidate type in turn.
//
// `readOnlyCompatible` mirrors the endpoint's own [Curiosity.Endpoints.ReadOnly] marking:
// true for the queries, false for the writes.

using System.Threading.Tasks;
using UID;

namespace App.FrontEnd.Api
{
    internal static class Api
    {
        // --- dashboard ------------------------------------------------------------------

        internal static Task<SummaryDto> DashboardSummaryAsync(UID128 userUID)
            => Mosaik.API.Endpoints.CallAsync<SummaryDto>(
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

        // --- decisions ------------------------------------------------------------------

        /// <summary>
        /// Both decision sets in one call. A row badged from a half-loaded answer says
        /// "not reviewed" about something settled last week.
        /// </summary>
        internal static Task<DecisionsDto> DecisionsAsync(UID128 caseUID)
            => Mosaik.API.Endpoints.CallAsync<DecisionsDto>(
                Endpoints.CaseDecisions,
                new CaseRequest { CaseUID = caseUID },
                readOnlyCompatible: true);

        /// <summary>
        /// One decision for a whole selection. Batched because the candidate list is
        /// multi-select — one request per row would turn a single gesture into twenty.
        /// </summary>
        internal static Task<OkDto> SetDecisionAsync(UID128 caseUID, UID128[] targetUIDs, string decision)
            => Mosaik.API.Endpoints.CallAsync<OkDto>(
                Endpoints.CaseSetDecision,
                new DecisionRequest { CaseUID = caseUID, TargetUIDs = targetUIDs, Decision = decision });

        // --- similarity -----------------------------------------------------------------

        /// <summary>
        /// Handed straight to <c>SearchArea.WithSimilarityEngine</c>. The endpoint returns the
        /// engine's own result type, so scores and per-signal attribution render themselves —
        /// which is also why nothing here reshapes the response.
        /// </summary>
        internal static Task<Mosaik.Schema.SimilarityResult> SimilarityAsync(string endpointPath, UID128 caseUID)
            => Mosaik.API.Endpoints.CallAsync<Mosaik.Schema.SimilarityResult>(
                endpointPath,
                new CaseRequest { CaseUID = caseUID },
                readOnlyCompatible: true);
    }
}
