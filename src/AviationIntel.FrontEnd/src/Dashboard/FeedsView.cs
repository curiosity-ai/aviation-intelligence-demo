using AviationIntel.FrontEnd.Schema;
using Mosaik;
using Mosaik.Schema;
using Tesserae;
using Transpose.Core;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace AviationIntel.FrontEnd.Dashboard
{
    /// <summary>
    /// The monitored feed directory: every feed, faceted by category, priority, source type and
    /// publisher (the edges the connector wrote), with the ones that failed their last fetch
    /// called out above the list.
    /// </summary>
    internal sealed class FeedsView : IComponent
    {
        internal const string Route = "#/feeds";

        private readonly Stack _root = VStack().S().P(16);

        public FeedsView(Parameters state)
        {
            _root.Add(HStack().WS().AlignItemsCenter().PB(12).Children(
                IconTile(UIcons.RssAlt, Theme.Colors.Orange600).Size(34.px()),
                VStack().PL(12).Children(
                    TextBlock("Monitored feeds".t()).Large().SemiBold(),
                    TextBlock("The curated aviation & aerospace RSS directory, with each feed's status on its last fetch".t()).Small().Secondary())));

            _root.Add(Defer(async () =>
            {
                var dashboard = await Api.Api.DashboardAsync();

                if (dashboard is null || dashboard.FeedsFailing == 0) return Empty();

                return Banner(string.Format("{0} feed(s) failed on their last fetch".t(), dashboard.FeedsFailing),
                              "Their articles are missing from today's news until the next run reaches them.".t())
                    .Warning()
                    .WS()
                    .MB(12);
            }).WS());

            _root.Add(SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.SetBeforeTypesFacet(N.Feed.Type))
                .S());
        }

        public dom.HTMLElement Render() => _root.Render();
    }
}
