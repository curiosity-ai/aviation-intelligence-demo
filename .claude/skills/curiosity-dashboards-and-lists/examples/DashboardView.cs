// Reference template: the home page. Installed with settings.HomeView in Configure().
// See ../SKILL.md §2.

using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Tesserae;
using App.FrontEnd.Api;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace App.FrontEnd.Dashboard
{
    /// <summary>
    /// Order of the page: what is waiting IN WORDS, then the same thing in DIGITS, then this
    /// person's own throughput, then the lists.
    /// </summary>
    internal sealed class DashboardView : IComponent
    {
        private readonly IComponent _content;

        public DashboardView(Parameters state)
        {
            _content = Defer(async () =>
            {
                var user = await Viewer.EnsureAsync();

                // Fetched ONCE per engineer and awaited by the greeting, the banner and every
                // tile. Two fetches is two chances for the sentence and the digits to disagree.
                var summary     = await Api.DashboardSummaryAsync(user.Uid);
                var performance = await Api.DashboardPerformanceAsync(user.Uid);

                return VStack().S().P(16).Children(
                    Header(user),
                    Notice(summary),
                    Counters(summary),
                    Performance(performance),
                    WorkLists.For(user.Uid, summary, selectedId: "inbox"));
            }).S();
        }

        public HTMLElement Render() => _content.Render();

        private static IComponent Header(UserDto user)
            => HStack().WS().Children(
                TextBlock(string.Format("Good morning, {0}".t(), user.DisplayName)).XLarge().SemiBold(),
                Grow(),
                Button("Switch engineer".t()).SetIcon(UIcons.UserSwitch).OnClick(() => Viewer.ShowPicker()));

        /// <summary>
        /// Whatever is actually waiting, said in words before it is said in digits, and its
        /// action selects the matching segment below — so the number always has a list behind
        /// it. NOTHING WAITING MEANS NO STRIP AT ALL: a banner that is always there reads as
        /// chrome.
        /// </summary>
        private static IComponent Notice(SummaryDto summary)
        {
            if (summary.UrgentCount > 0)
                return Banner(string.Format("{0} urgent items still open".t(), summary.UrgentCount))
                    .Danger()
                    .SetIcon(UIcons.Flame)
                    .SetBadge("Urgent".t())
                    .Action("Show them".t(), () => Router.Navigate(InboxView.RouteFor("urgent")));

            if (summary.UnassignedCount > 0)
                return Banner(string.Format("{0} items are waiting for an owner".t(), summary.UnassignedCount))
                    .Primary()
                    .Action("Show them".t(), () => Router.Navigate(InboxView.RouteFor("unassigned")));

            return Nothing();
        }

        /// <summary>
        /// One card per number, each a Metric leading with an IconTile and drawn ValueFirst, so
        /// the digits read first and the words under them only say what was counted.
        ///
        /// <para>Colour is what the number MEANS — red is late, amber is nobody's yet — so the
        /// row is read by colour before it is read by digit. The whole card is the click
        /// target, and EVERY CARD OPENS THE LIST IT COUNTED: a number on a dashboard is half a
        /// question whose other half already exists as a segment of the list page.</para>
        ///
        /// <para>The label, icon and colour come from WorkLists.Segments rather than being
        /// written out here.</para>
        /// </summary>
        private static IComponent Counters(SummaryDto summary)
        {
            var counts = new[]
            {
                ("inbox",      summary.InboxCount),
                ("urgent",     summary.UrgentCount),
                ("mine",       summary.OwnedCount),
                ("unassigned", summary.UnassignedCount),
            };

            var row = HStack().WS().Wrap();

            foreach (var (segmentId, count) in counts)
            {
                var segment = WorkLists.ById(segmentId);

                row.Children(
                    Card(Metric(count.ToString(), segment.Label.t())
                            .ValueFirst()
                            .SetIcon(new IconTile(segment.Icon).Background(segment.Color)))
                        .MinWidth(180.px())
                        .Cursor("pointer")
                        .OnClick(() => Router.Navigate(InboxView.RouteFor(segment.Id)))
                        .M(4));
            }

            return row;
        }

        /// <summary>
        /// Three numbers, each drawn as WHAT IT IS. All computed live server-side from the
        /// workflow status, the completion date and the recorded effort — never hard-coded, and
        /// never put on the page at all if the data cannot support it.
        /// </summary>
        private static IComponent Performance(PerformanceDto p)
        {
            // A SERIES: the only question anyone asks of one is whether it is going up. So a
            // sparkline with the total as the value — not a grid of twelve week/count rows.
            var throughput = Card(
                Metric(Sum(p.PerWeek).ToString(), "Completed, last 12 weeks".t())
                    .Chart(Sparkline(Counts(p.PerWeek)))
                    .ChangeInHeader(Change(p.PerWeek)));

            var hours = Card(
                Metric(p.AvgHoursPerCase.ToString("0.0"), "Avg hours per case".t())
                    .ValueFirst()
                    .SetIcon(new IconTile(UIcons.Stopwatch.ToString()).Background(Theme.Colors.Blue500)));

            // A SHARE: one metric whose value is the success share and whose chart is the split.
            var outcomes = Card(
                Metric(p.ValidatedPct.ToString("0") + "%", "Validated last month".t())
                    .Chart(ContributionBar()
                        .Max(100)
                        .Add("Validated".t(), p.ValidatedPct, Theme.Colors.Green500)
                        .Add("Rejected".t(),  p.RejectedPct,  Theme.Colors.Red500)
                        .Add("Delegated".t(), p.DelegatedPct, Theme.Colors.Orange500))
                    .ChangeInHeader(string.Format("{0} cases".t(), p.MonthTotal)));

            return VStack().WS().Children(
                TextBlock("My performance".t()).Large().SemiBold().PT(16).PB(8),
                HStack().WS().Wrap().Children(throughput.M(4), hours.M(4), outcomes.M(4)));
        }

        private static int      Sum(WeekPointDto[] w)    { var t = 0; foreach (var p in w) t += p.Count; return t; }
        private static double[] Counts(WeekPointDto[] w) { var a = new double[w.Length]; for (var i = 0; i < w.Length; i++) a[i] = w[i].Count; return a; }
        private static string   Change(WeekPointDto[] w) => w.Length < 2 ? "" : (w[w.Length - 1].Count - w[w.Length - 2].Count).ToString("+#;-#;0");
    }
}
