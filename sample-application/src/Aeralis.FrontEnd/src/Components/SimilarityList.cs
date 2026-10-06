using System.Collections.Generic;
using System.Threading.Tasks;
using Mosaik.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace Aeralis.FrontEnd.Components
{
    /// <summary>
    /// A similarity-driven candidate list: every document the engine thinks this work order
    /// might touch, each row carrying the decision already taken about it and the commands to
    /// take one.
    ///
    /// The list is driven by the workspace's similarity engine through
    /// <see cref="SearchArea.WithSimilarityEngine"/>, so scores render themselves rather than
    /// being re-implemented here — which is also why the endpoints return the engine's own
    /// <c>SimilarityResult</c> and not a projection of it.
    ///
    /// Two nodes are excluded from every list: the work order that seeded the similarity,
    /// because it is by definition the closest match to itself, and the modification it
    /// analyses, because that is the *change* rather than something the change affects. Both
    /// otherwise rank near the top of "possible affected documents" as rows nobody can confirm
    /// or reject, in the two places the best real candidates should be.
    ///
    /// Selection is on, because deciding about candidates one at a time is the slow way to do
    /// the only job this screen has. The commands come from <see cref="ImpactDecisions"/>,
    /// which also owns the badges, so a row's badge and the command that set it can never
    /// disagree about what "confirmed" means.
    /// </summary>
    internal static class SimilarityList
    {
        /// <param name="subjectMod">
        /// The *already started* lookup of the modification under analysis. The caller builds
        /// both lists at once and hands the same task to each, so the query runs once — and so
        /// the two lists cannot end up excluding different things.
        /// </param>
        public static IComponent For(UID128 pawoUID, string endpointPath, Task<UID128> subjectMod)
        {
            return Defer(async () =>
            {
                var decisions = await ImpactDecisions.LoadAsync(pawoUID, subjectMod);
                var excluded = await ExcludedAsync(pawoUID, subjectMod);

                var summary = TextBlock(Summary(decisions)).Small().Secondary();

                SearchArea area = null;

                // Re-running the search is what repaints the badges: the rows are built by the
                // renderer, and the decision a row shows is read while it is being built.
                System.Action onChanged = () =>
                {
                    summary.Text = Summary(decisions);
                    area?.TriggerSearch();
                };

                area = SearchArea()
                    .WithFacets()
                    .WithSimilarityEngine(_ => Api.Api.SimilarityAsync(endpointPath, pawoUID))
                    .OnSearch(sr => sr.WithExcludeUIDs(excluded))
                    .Renderer(r => r
                        .WithSelection()
                        .CustomizeResult(result => decisions.Customize(result, onChanged)));

                return VStack().S().Children(summary.PB(4), area.S()) ;
            }).S();
        }

        /// <summary>
        /// The nodes that can never be candidates: the seed and the change it analyses.
        ///
        /// Excluding the modification is a no-op in the previous-analysis list, where the
        /// results are work orders and a modification could not appear anyway. It is applied to
        /// both regardless — one exclusion set for both lists is one thing to be right about,
        /// and a per-list rule would be a second place for the two to drift apart.
        /// </summary>
        private static async Task<UID128[]> ExcludedAsync(UID128 pawoUID, Task<UID128> subjectMod)
        {
            var excluded = new List<UID128> { pawoUID };

            if (subjectMod is object)
            {
                var modUID = await subjectMod;
                if (modUID != UID128.Empty) excluded.Add(modUID);
            }

            return excluded.ToArray();
        }

        private static string Summary(ImpactDecisions decisions)
            => string.Format("{0} confirmed · {1} rejected".t(),
                             decisions.ConfirmedCount, decisions.RejectedCount);
    }
}
