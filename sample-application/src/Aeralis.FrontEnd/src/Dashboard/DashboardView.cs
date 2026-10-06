using System.Linq;
using System.Threading.Tasks;
using Tesserae;
using Aeralis.FrontEnd.Api;
using TNT;
using Transpose.Core;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace Aeralis.FrontEnd.Dashboard
{
    /// <summary>
    /// Replaces the workspace's default home view.
    ///
    /// Everything here is computed live from the graph — the connector stores work-order
    /// state, not pre-aggregated metrics, so these numbers move when the data does.
    ///
    /// The page opens on the engineer rather than on a title. "MOD Pre-Analysis" told a reader
    /// who had just navigated to the MOD pre-analysis workspace where they were; the greeting
    /// tells them what is waiting. Whose desk it is lives in <see cref="Viewer"/>, shared with
    /// the sidebar's inbox, and is switched from the profile card's own header.
    ///
    /// Every number is a <see cref="Metric"/> with a tile in front of it: the colour is what
    /// the number *means* — red is work that is late, green is work that came out validated —
    /// so the row can be read by colour before it is read by digit.
    /// </summary>
    internal sealed class DashboardView : IComponent
    {
        private readonly Stack _root = VStack().S().ScrollY().P(16);
        private readonly Stack _body = VStack().WS();

        public DashboardView(Parameters state)
        {
            _root.Add(_body);

            _root.Add(Defer(async () =>
            {
                var user = await Viewer.EnsureAsync();
                if (user is object) Show(user);

                return Empty();
            }));

            // Switching engineer from the picker rebuilds the page rather than reloading it:
            // every section defers its own call, so a rebuild is the calls, not the chrome.
            Viewer.Changed = Show;
        }

        /// <summary>
        /// Rebuilds every section for one engineer.
        ///
        /// The summary is fetched once and handed to the greeting, the notice strip, the
        /// counter tiles and the work lists. They say the same four numbers — as a sentence,
        /// as a banner, as digits and as rows — and a second call could only make them disagree.
        /// </summary>
        private void Show(UserDto user)
        {
            _body.Clear();

            var summary = Api.Api.DashboardSummaryAsync(user.Uid);

            _body.Add(Profile(user, summary));
            _body.Add(Notices(summary));
            _body.Add(MetricsRow(summary));
            _body.Add(PerformanceSection(user.Uid));
            _body.Add(WorkLists(user.Uid, summary));
        }

        // -------------------------------------------------------------------------
        // Who is looking, and what is waiting for them
        // -------------------------------------------------------------------------

        /// <summary>
        /// Where this engineer sits and what they specialise in — all of it neighbours of their
        /// user node, so it renders as links to those nodes rather than as text the endpoint had
        /// to spell out — under a greeting that says how much is on their desk.
        ///
        /// The card's own header carries the engineer's name and the control that changes it, so
        /// "who am I looking at" and "look at someone else" are the same place on the screen.
        /// </summary>
        private static IComponent Profile(UserDto user, Task<DashboardSummaryDto> summary)
        {
            var greeting = VStack().WS();

            greeting.Add(Defer(async () =>
            {
                var s = await summary;

                return VStack().WS().Children(
                    TextBlock(Greeting(user, s?.InboxCount ?? 0)).Large().SemiBold(),
                    TextBlock(SubGreeting(s)).Small().Secondary()) ;
            }).WS());

            return Card(VStack().WS().Children(
                greeting.PB(16),
                HStack().WS().Wrap().Children(
                    Field("Site".t(), user.SiteUIDs),
                    Field("Department".t(), user.DepartmentUIDs),
                    Field("Role".t(), user.RoleUIDs),
                    Field("ATA specialities".t(), user.AtaSpecialityUIDs))))
                .SetTitle(Header(user))
                .WS()
                .MB(16);
        }

        /// <summary>
        /// The card header: who is being rendered as, and the way to change it. The picker sits
        /// here rather than at the top of the page because it only ever means "change *this*".
        /// </summary>
        private static IComponent Header(UserDto user)
        {
            return HStack().WS().AlignItemsCenter().Children(
                IconTile(UIcons.CircleUser, Theme.Colors.Blue600).Size(28.px()),
                TextBlock(user.DisplayName ?? user.UserName).SemiBold().PL(8).Grow(),
                TextBlock("viewing as".t()).Tiny().Secondary().PR(8),
                UserPicker.Button(user));
        }

        /// <summary>
        /// A greeting that reads like one, and says the one number the engineer came for.
        ///
        /// The time of day is the browser's, and the wording varies inside each part of the day
        /// so the page does not read as a template that happens to know the hour. The count is
        /// the inbox — work orders on their desk that are not finished — because that is what
        /// "ongoing" means here, and it is the same number the first tile shows.
        /// </summary>
        private static string Greeting(UserDto user, int ongoing)
        {
            var name = FirstName(user);
            var hour = System.DateTime.Now.Hour;

            // Varied by the day of the month rather than at random: a greeting that changes
            // every time the page defers is noise, and this demo is re-shown all day.
            var variant = System.DateTime.Now.Day % 3;

            string opening;

            if (hour < 5) opening = variant == 0 ? "Still up, {0}?".t() : variant == 1 ? "Late one, {0}.".t() : "Burning the midnight oil, {0}.".t();
            else if (hour < 12) opening = variant == 0 ? "Good morning, {0}.".t() : variant == 1 ? "Morning, {0}.".t() : "Hi {0}, early start.".t();
            else if (hour < 14) opening = variant == 0 ? "Good afternoon, {0}.".t() : variant == 1 ? "Hi {0}, hope lunch was good.".t() : "Afternoon, {0}.".t();
            else if (hour < 18) opening = variant == 0 ? "Good afternoon, {0}.".t() : variant == 1 ? "Hi {0}.".t() : "Afternoon, {0}.".t();
            else if (hour < 22) opening = variant == 0 ? "Good evening, {0}.".t() : variant == 1 ? "Evening, {0}.".t() : "Hi {0}, winding down?".t();
            else opening = variant == 0 ? "Good evening, {0}.".t() : variant == 1 ? "Still here, {0}?".t() : "Late shift, {0}.".t();

            var tail = ongoing == 0
                ? "Nothing is open on your desk.".t()
                : ongoing == 1
                    ? "You have 1 pre-analysis ongoing.".t()
                    : string.Format("You have {0} pre-analyses ongoing.".t(), ongoing);

            return string.Format(opening, name) + " " + tail;
        }

        /// <summary>The line under the greeting: the rest of the desk, in one sentence.</summary>
        private static string SubGreeting(DashboardSummaryDto summary)
        {
            var urgent = summary?.UrgentCount ?? 0;
            var owned = summary?.OwnedCount ?? 0;
            var unassigned = summary?.UnassignedCount ?? 0;

            if (urgent > 0)
                return string.Format("{0} of them urgent, out of {1} you own.".t(), urgent, owned);

            if (unassigned > 0)
                return string.Format("Nothing urgent. {0} work order(s) are still waiting for an owner.".t(), unassigned);

            return string.Format("{0} work order(s) in total on your name.".t(), owned);
        }

        /// <summary>
        /// The name to greet by. <c>DisplayName</c> is "Marie Lefèvre"; a greeting wants
        /// "Marie", and falls back to the whole thing when there is nothing to split.
        /// </summary>
        private static string FirstName(UserDto user)
        {
            var display = user.DisplayName;
            if (string.IsNullOrEmpty(display)) return user.UserName ?? "";

            var space = display.IndexOf(' ');

            return space > 0 ? display.Substring(0, space) : display;
        }

        private static IComponent Field(string label, UID128[] uids)
        {
            return VStack().PR(32).PB(8).Children(
                TextBlock(label).Tiny().Secondary(),
                NeighborsLinks(uids ?? new UID128[0], wrap: true, maxItemsToShow: 6,
                               showIfNone: () => TextBlock("—".t()).Secondary()));
        }

        // -------------------------------------------------------------------------
        // Notices
        // -------------------------------------------------------------------------

        /// <summary>
        /// What is waiting on this engineer, said in words before it is said in digits, with the
        /// way through to the list attached to it.
        ///
        /// The action navigates to the inbox rather than selecting a segment on this page: the
        /// inbox is where a queue is worked through, and a banner that scrolls you down to a
        /// 520px card is not the same offer as one that opens the queue.
        /// </summary>
        private static IComponent Notices(Task<DashboardSummaryDto> summary)
        {
            var host = VStack().WS();

            host.Add(Defer(async () =>
            {
                var s = await summary;

                var urgent = s?.UrgentCount ?? 0;
                var unassigned = s?.UnassignedCount ?? 0;

                if (urgent > 0)
                {
                    return Banner(
                        string.Format("{0} urgent action(s) waiting on you".t(), urgent),
                        "These pre-analyses are marked urgent and are still open on your desk.".t())
                        .Danger()
                        .SetIcon(UIcons.Flame)
                        .SetBadge("Urgent".t())
                        .Action("Review them".t(), () => OpenInbox(WorkOrderLists.UrgentSegment))
                        .WS() ;
                }

                if (unassigned > 0)
                {
                    return Banner(
                        string.Format("{0} work order(s) are waiting for an owner".t(), unassigned),
                        "Nothing is urgent on your desk. These pre-analyses have nobody assigned to them yet.".t())
                        .Primary()
                        .SetIcon(UIcons.UserAdd)
                        .Action("See them".t(), () => OpenInbox(WorkOrderLists.UnassignedSegment))
                        .WS() ;
                }

                return Empty();
            }).WS());

            return host.MB(16);
        }

        private static void OpenInbox(string segment) => Router.Navigate(InboxView.RouteFor(segment));

        // -------------------------------------------------------------------------
        // Counters
        // -------------------------------------------------------------------------

        /// <summary>
        /// The four counters, one card each rather than four numbers sharing one. Each leads
        /// with a tile whose colour is the meaning of the number, and each is
        /// <c>ValueFirst</c>, so the digits read first and the words under them only say what
        /// was counted.
        ///
        /// **Every card opens the list it counted.** A number on a dashboard is a question the
        /// reader immediately wants the other half of — "seven urgent, which seven?" — and the
        /// answer already exists as a segment of the inbox. So the card is the way there, and
        /// the label, the icon and the colour come from
        /// <see cref="WorkOrderLists.Segments"/>: the card, the sidebar entry and the pivot tab
        /// for one list are then three renderings of one description rather than three places
        /// to keep in step by hand.
        /// </summary>
        private static IComponent MetricsRow(Task<DashboardSummaryDto> summary)
        {
            return Defer(async () =>
            {
                var s = await summary;

                var row = Grid(1.fr(), 1.fr(), 1.fr(), 1.fr()).WS().Gap(16.px());

                row.Add(Counter(WorkOrderLists.InboxSegment, s?.InboxCount));
                row.Add(Counter(WorkOrderLists.UrgentSegment, s?.UrgentCount));
                row.Add(Counter(WorkOrderLists.MineSegment, s?.OwnedCount));
                row.Add(Counter(WorkOrderLists.UnassignedSegment, s?.UnassignedCount));

                return row;
            }).WS().H(COUNTER_ROW_HEIGHT).MB(16);
        }

        /// <summary>
        /// One counter card, described entirely by the segment it opens. The whole card is the
        /// target rather than a link inside it: the number is the thing being clicked, and it
        /// is the biggest thing on the card.
        /// </summary>
        private static IComponent Counter(string segmentId, int? value)
        {
            var segment = SegmentOf(segmentId);

            return Card(Metric(segment.Label.t(), Count(value)).SetIcon(segment.Icon, segment.Color).ValueFirst())
                .WS()
                .Style(style => style.cursor = "pointer")
                .Tooltip(string.Format("Open {0} in the inbox".t(), segment.Label.t()))
                .OnClick(() => OpenInbox(segment.Id));
        }

        private static WorkOrderLists.Segment SegmentOf(string segmentId)
        {
            foreach (var segment in WorkOrderLists.Segments)
            {
                if (segment.Id == segmentId) return segment;
            }

            return WorkOrderLists.Segments[0];
        }

        private const int COUNTER_ROW_HEIGHT = 116;

        private static string Count(int? value) => (value ?? 0).ToString();

        // -------------------------------------------------------------------------
        // Performance
        // -------------------------------------------------------------------------

        /// <summary>
        /// The three numbers this engineer is measured by, each a <see cref="Metric"/> and
        /// nothing else, in three equal columns.
        ///
        /// They were a mixed row before — a metric, a details grid, a second details grid — at
        /// three different widths, so the eye had to work out what kind of thing each box was
        /// before reading it. Three identical cards, each a tile, a number and whatever shape
        /// that number has underneath it, are read as one row.
        /// </summary>
        private static IComponent PerformanceSection(UID128 userUID)
        {
            return Card(Defer(async () =>
            {
                var perf = await Api.Api.DashboardPerformanceAsync(userUID);
                var weeks = perf?.Throughput ?? new WeeklyThroughputDto[0];

                return Grid(1.fr(), 1.fr(), 1.fr()).WS().Gap(16.px()).Children(
                    Card(Throughput(weeks)).WS().H(180),
                    Card(Hours(perf)).WS().H(180),
                    Card(Outcome(perf)).WS().H(180));
            })).SetTitle("My performance".t(), UIcons.ChartHistogram, Theme.Colors.Teal600).WS();
        }

        /// <summary>
        /// Pre-analyses completed per week, as the shape of the twelve weeks rather than their
        /// twelve numbers. The value is the whole window, so the tile still answers "how much
        /// did I get through"; the change beside the title is the last week against the one
        /// before it, which is the only comparison the series invites.
        /// </summary>
        private static Metric Throughput(WeeklyThroughputDto[] weeks)
        {
            var series = weeks.Select(w => (double)w.Completed).ToArray();
            var total = weeks.Sum(w => w.Completed);

            var metric = Metric("Completed per week".t(), total.ToString())
                .SetIcon(UIcons.ChartHistogram, Theme.Colors.Teal600)
                .ValueFirst()
                .Chart(Sparkline(series, width: CHART_WIDTH, height: CHART_HEIGHT,
                                 color: Theme.Colors.Teal600).WS());

            if (weeks.Length >= 2)
            {
                metric.ChangeInHeader()
                      .Change(Trend(weeks[weeks.Length - 1].Completed - weeks[weeks.Length - 2].Completed,
                                    "vs previous week".t()));
            }

            return metric;
        }

        private static Metric Hours(PerformanceDto perf)
        {
            return Metric("Avg hours per PAWO, last month".t(), HoursText(perf?.AverageHoursPerPawo))
                .SetIcon(UIcons.Stopwatch, Theme.Colors.Blue500)
                .ValueFirst()
                .ChangeInHeader()
                .Change(TextBlock(string.Format("{0} work orders".t(), perf?.LastMonthTotal ?? 0))
                    .Small().Foreground(Theme.Secondary.Foreground));
        }

        /// <summary>
        /// The share of last month's work orders that came out validated, with the rest of the
        /// month drawn under it. <c>ChangeInHeader</c> is what puts the bar directly below the
        /// number instead of below a trend line: the value, the bar and its legend then read
        /// straight down the card.
        /// </summary>
        private static Metric Outcome(PerformanceDto perf)
        {
            var validated = Round(perf?.ValidatedPercent);
            var rejected = Round(perf?.RejectedPercent);
            var delegated = Round(perf?.DelegatedPercent);

            return Metric("Validated last month".t(), validated + " %")
                .SetIcon(UIcons.CheckCircle, Theme.Colors.Green600)
                .ValueFirst()
                .ChangeInHeader()
                .Change(TextBlock("of the month's outcomes".t()).Small().Foreground(Theme.Secondary.Foreground))
                .Chart(ContributionBar()
                    .Max(100)
                    .Thickness(8.px())
                    .Decimals(0)
                    .Add("validated".t(), validated, Theme.Colors.Green600)
                    .Add("rejected".t(), rejected, Theme.Colors.Red500)
                    .Add("delegated".t(), delegated, Theme.Colors.Blue500)
                    .WS());
        }

        /// <summary>An arrow and a signed number, green when it went up and red when it did not.</summary>
        private static IComponent Trend(int delta, string against)
        {
            if (delta == 0)
                return TextBlock("unchanged ".t() + against).Small().Foreground(Theme.Secondary.Foreground);

            var up = delta > 0;
            var color = up ? Theme.Colors.Green600 : Theme.Colors.Red500;

            return HStack().AlignItemsCenter().Children(
                Icon(up ? UIcons.ArrowUp : UIcons.ArrowDown).Foreground(color).S(),
                TextBlock((up ? "+" : "") + delta + " " + against).Small().SemiBold().Foreground(color).PL(4));
        }

        private const int CHART_WIDTH = 300;

        private const int CHART_HEIGHT = 60;

        private static int Round(double? value) => (int)(value ?? 0);

        private static string HoursText(double? value) => ((int)(value ?? 0)).ToString() + " h";

        // -------------------------------------------------------------------------
        // Work lists
        // -------------------------------------------------------------------------

        /// <summary>
        /// The same four lists the inbox opens on, kept here as the summary of them. The card's
        /// title links through, so a reader who wants to work rather than skim leaves for the
        /// page that is built for it.
        /// </summary>
        private static IComponent WorkLists(UID128 userUID, Task<DashboardSummaryDto> summary)
        {
            var open = Button("Open inbox".t())
                .SetIcon(UIcons.ArrowUpRightFromSquare)
                .Small()
                .OnClick((b, _) => OpenInbox(WorkOrderLists.MineSegment));

            return Card(WorkOrderLists.For(userUID, summary).S())
                .SetTitle(HStack().WS().AlignItemsCenter().Children(
                    IconTile(UIcons.ListCheck, Theme.Colors.Purple600).Size(28.px()),
                    TextBlock("Work orders".t()).SemiBold().PL(8).Grow(),
                    open))
                .WS()
                .H(520)
                .MT(16);
        }

        public dom.HTMLElement Render() => _root.Render();
    }
}
