using System.Linq;
using AviationIntel.FrontEnd.Api;
using AviationIntel.FrontEnd.Components;
using Mosaik;
using Mosaik.Schema;
using Tesserae;
using Transpose.Core;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace AviationIntel.FrontEnd.Dashboard
{
    /// <summary>
    /// Replaces the workspace's home view with the news desk: the two reports one click away, what
    /// arrived in the last day and week, where it came from, and the latest articles.
    /// </summary>
    /// <remarks>
    /// Every number comes from <c>aviation/dashboard</c>, computed live from the graph, and every tile
    /// opens the exact articles it counted (<see cref="NewsView"/>).
    /// </remarks>
    internal sealed class HomeView : IComponent
    {
        private readonly Stack _root = VStack().S().ScrollY().P(16);

        public HomeView(Parameters state)
        {
            var dashboard = Api.Api.DashboardAsync();

            _root.Add(Header());

            _root.Add(Defer(async () =>
            {
                var d = await dashboard;

                if (d is null) return Banner("The news desk could not be loaded".t(), "The aviation/dashboard endpoint did not answer.".t()).Danger().WS();

                return VStack().WS().Children(
                    Notices(d),
                    Counters(d),
                    Grid(ChartColumns).WS().Gap(16.px()).MB(16).Children(
                        Card(Categories(d)).SetTitle("This week by category".t(), UIcons.Folder, Theme.Colors.Green600).WS(),
                        Card(Days(d)).SetTitle("Articles per day".t(), UIcons.ChartHistogram, Theme.Colors.Teal600).WS()),
                    Card(Latest(d)).SetTitle("Latest news".t(), UIcons.Newspaper, Theme.Colors.Blue600).WS().H(560));
            }).WS());
        }

        private static IComponent Header()
        {
            return HStack().WS().AlignItemsCenter().Wrap().PB(16).Children(
                IconTile(UIcons.PlaneAlt, Theme.Colors.Blue600).Size(40.px()),
                VStack().PL(12).Grow().Children(
                    TextBlock("Aviation market intelligence".t()).XLarge().SemiBold(),
                    TextBlock("What moved in commercial aviation and aerospace, from 248 monitored sources".t()).Small().Secondary()),
                HStack().AlignItemsCenter().Children(
                    Button("Daily brief".t()).SetIcon(UIcons.FileChartLine).AI().OnClick(() => AskAssistant.DailyBriefAsync().FireAndForget()).MR(8),
                    Button("Weekly report".t()).SetIcon(UIcons.CalendarLines).AISubtle().OnClick(() => AskAssistant.WeeklyReportAsync().FireAndForget())));
        }

        /// <summary>A failed feed is said in words before it is said in a number: its news is missing from everything below.</summary>
        private static IComponent Notices(DashboardDto d)
        {
            if (d.FeedsFailing == 0) return Empty();

            return Banner(string.Format("{0} feed(s) failed on their last fetch".t(), d.FeedsFailing),
                          "Their articles are missing from the counts and lists below until the connector reaches them again.".t())
                .Warning()
                .Action("See the feeds".t(), () => Router.Navigate(FeedsView.Route))
                .WS()
                .MB(16);
        }

        //As many columns as fit, each at least this wide: four tiles and two charts side by side on a
        //desk, one under the other on a phone.
        private static readonly UnitSize TileColumns  = new UnitSize("repeat(auto-fit, minmax(min(220px, 100%), 1fr))");
        private static readonly UnitSize ChartColumns = new UnitSize("repeat(auto-fit, minmax(min(380px, 100%), 1fr))");

        private static IComponent Counters(DashboardDto d)
        {
            return Grid(TileColumns).WS().Gap(16.px()).MB(16).Children(
                Counter(NewsSets.Today,       d.Articles24h.ToString(), string.Format("from {0} feeds".t(), d.FeedsWithNews24h), () => Router.Navigate(NewsView.RouteFor(NewsSets.Today))),
                Counter(NewsSets.Week,        d.Articles7d.ToString(),  string.Format("from {0} publishers".t(), d.Publishers7d), () => Router.Navigate(NewsView.RouteFor(NewsSets.Week))),
                Card(Metric("Feeds healthy".t(), (d.Feeds - d.FeedsFailing) + " / " + d.Feeds)
                        .SetIcon(UIcons.RssAlt, d.FeedsFailing == 0 ? Theme.Colors.Green600 : Theme.Colors.Orange600)
                        .ValueFirst()
                        .OnClick(() => Router.Navigate(FeedsView.Route))).WS(),
                Counter(NewsSets.SummaryOnly, d.SummaryOnly7d.ToString(), "last 7 days, not readable in full".t(), () => Router.Navigate(NewsView.RouteFor(NewsSets.SummaryOnly))));
        }

        private static IComponent Counter(string setId, string value, string note, System.Action open)
        {
            var set = NewsSets.All.First(s => s.Id == setId);

            return Card(Metric(set.Label.t(), value)
                    .SetIcon(set.Icon, set.Color)
                    .ValueFirst()
                    .OnClick(open))
                .WS()
                .Tooltip(set.Description.t() + " - " + note);
        }

        /// <summary>One row per category, its bar drawn to the week's largest category, and the row opens its articles.</summary>
        private static IComponent Categories(DashboardDto d)
        {
            var categories = d.Categories ?? new CategoryCountDto[0];

            if (categories.Length == 0) return TextBlock("No articles in the last seven days.".t()).Secondary();

            var max  = categories.Max(c => c.Count7d);
            var list = VStack().WS();

            //One legend for every bar, rather than one under each.
            list.Add(HStack().AlignItemsCenter().PB(10).Children(
                ContributionBar().Max(1).Thickness(0.px()).ShowValues(false)
                    .Add("last 24 h".t(), 0, Theme.Colors.Blue600)
                    .Add("earlier this week".t(), 0, Theme.Colors.Blue300)));

            foreach (var category in categories)
            {
                var captured = category;

                list.Add(HStack().WS().AlignItemsCenter().PB(6).Children(
                    Button(captured.Name).Link().Ellipsis().Small().W(300.px()).Tooltip(captured.Name)
                        .OnClick(() => Router.Navigate(NewsView.RouteFor(NewsSets.CategoryPrefix + captured.CategoryUID))),
                    ContributionBar().Max(max).Thickness(10.px()).HideLegend()
                        .Add("last 24 h".t(), captured.Count24h, Theme.Colors.Blue600)
                        .Add("earlier this week".t(), captured.Count7d - captured.Count24h, Theme.Colors.Blue300)
                        .Grow(),
                    TextBlock(captured.Count7d.ToString()).Small().SemiBold().W(36.px()).PL(8)));
            }

            return list;
        }

        private static IComponent Days(DashboardDto d)
        {
            var days   = d.Days ?? new DayCountDto[0];
            var values = days.Select(x => (double)x.Count).ToArray();

            return Metric("articles in 7 days".t(), d.Articles7d.ToString())
                .ValueFirst()
                .ChangeInHeader()
                .Change(TextBlock(days.Length > 0 ? days[0].Day + " to " + days[days.Length - 1].Day : "").Small().Secondary())
                .Chart(Sparkline(values, width: 300, height: 90, color: Theme.Colors.Teal600).WS());
        }

        private static IComponent Latest(DashboardDto d)
        {
            var uids = d.Latest24hUIDs ?? new UID128[0];

            //A card on the landing page, not the page's search: it must not take the caret. NoForceFocus stops the
            //box re-focusing itself on blur; un-registering it as the active search box - once mounted, and again
            //each time the user leaves it - stops CommandManager focusing it whenever the window regains focus.
            return SearchArea()
                .SearchBox(box =>
                 {
                     box.NoForceFocus().WhenMounted(() => CommandManager.UnsetActiveSearchBox(box));
                     box.Render().addEventListener("focusout", _ => CommandManager.UnsetActiveSearchBox(box));
                 })
                .OnSearch(sr => sr.WithTargetUIDs(uids)
                                  .MarkAsNoResultsIf(uids.Length == 0)
                                  .WithSortMode(SortModeEnum.RecentFirst))
                .S();
        }

        public dom.HTMLElement Render() => _root.Render();
    }
}
