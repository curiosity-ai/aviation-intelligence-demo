using AviationIntel.FrontEnd.Api;
using AviationIntel.FrontEnd.Components;
using Mosaik;
using Mosaik.Schema;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace AviationIntel.FrontEnd.Dashboard
{
    /// <summary>
    /// One page for every set of articles the home page counts: <c>#/news?set=today</c>,
    /// <c>week</c>, <c>summary-only</c> or <c>category:&lt;uid&gt;</c>. The list is a search area over
    /// exactly the UIDs the dashboard endpoint counted, so it is searchable and faceted and shows
    /// the same number as the tile that opened it.
    /// </summary>
    internal sealed class NewsView : IComponent
    {
        internal const string Route        = "#/news";
        internal const string SetParameter = "set";

        private readonly Stack _root = VStack().S().P(16);

        public NewsView(Parameters state)
        {
            var set = state is object && state.TryGetValue(SetParameter, out var requested) && !string.IsNullOrEmpty(requested)
                ? requested
                : NewsSets.Today;

            _root.Add(Defer(async () =>
            {
                var dashboard   = await Api.Api.DashboardAsync();
                var description = NewsSets.Describe(set, dashboard);
                var uids        = NewsSets.UIDsOf(set, dashboard);

                var header = HStack().WS().AlignItemsCenter().PB(12).Children(
                    IconTile(description.Icon, description.Color).Size(34.px()),
                    VStack().PL(12).Grow().Children(
                        TextBlock(description.Label.t()).Large().SemiBold(),
                        TextBlock(string.Format("{0} - {1}".t(), description.Description.t(), uids.Length)).Small().Secondary()),
                    Button("Ask the assistant".t()).SetIcon(UIcons.Sparkles).OnClick(() =>
                        AskAssistant.AskAsync(string.Format("Summarise what matters for the market in these articles: {0} ({1}).", description.Label, description.Description)).FireAndForget()));

                var list = SearchArea()
                    .WithFacets()
                    .OnSearch(sr => sr.WithTargetUIDs(uids)
                                      .MarkAsNoResultsIf(uids.Length == 0)
                                      .WithSortMode(SortModeEnum.RecentFirst))
                    .S();

                return VStack().S().Children(header, list);
            }).S());
        }

        internal static string RouteFor(string set) => Route + "?" + SetParameter + "=" + set;

        internal static bool IsShowing(string set)
        {
            var hash = window.location.hash ?? "";

            if (!hash.StartsWith(Route)) return false;

            return hash.Contains(SetParameter + "=" + set) || (set == NewsSets.Today && !hash.Contains(SetParameter + "="));
        }

        public HTMLElement Render() => _root.Render();
    }
}
