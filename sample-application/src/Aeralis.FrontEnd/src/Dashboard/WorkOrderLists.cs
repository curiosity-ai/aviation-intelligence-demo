using System.Threading.Tasks;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Api;
using Aeralis.FrontEnd.Schema;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace Aeralis.FrontEnd.Dashboard
{
    /// <summary>
    /// The five ways an engineer looks at their work orders — open, urgent, owned, unowned,
    /// everything — as one <see cref="SegmentedPivot"/>.
    ///
    /// It lives in its own file because two surfaces show it: the home page, under the
    /// counters that summarise it, and the <see cref="InboxView"/> the sidebar opens, where
    /// it is the whole page. Building it twice would let them drift, and it is the *same*
    /// list — the home page's counters and the inbox's rows have to agree, because both the
    /// banner and every counter card send the reader from one to the other.
    ///
    /// **There is one segment per counter**, which is why <c>Inbox</c> and <c>Mine</c> are
    /// both here and are not the same set: the inbox is the work orders on this engineer's
    /// desk that are *not finished*, and "PAWOs I own" is everything ever assigned to them.
    /// The home page shows both numbers, so both have to open onto the list they counted — a
    /// card whose number says 7 opening a list of 34 is a card that lied.
    ///
    /// Every segment except <c>Mine</c> and <c>All</c> is <c>WithTargetUIDs</c> over the very
    /// arrays the dashboard summary counted, for the same reason. Those two are queries,
    /// because they are unbounded.
    /// </summary>
    internal static class WorkOrderLists
    {
        internal const string InboxSegment = "inbox";
        internal const string UrgentSegment = "urgent";
        internal const string MineSegment = "mine";
        internal const string UnassignedSegment = "unassigned";
        internal const string AllSegment = "all";

        /// <summary>The segment a bare <c>#/inbox</c> opens on — the queue itself.</summary>
        internal const string DefaultSegment = InboxSegment;

        /// <summary>
        /// Every segment, in the order the pivot and the sidebar both show them, with the label
        /// and the mark each is known by. One list, so the three surfaces that offer these
        /// cannot drift: a sidebar entry naming a segment the pivot does not have would
        /// navigate to nothing, and a counter card whose colour disagrees with its sidebar
        /// entry is two names for one thing.
        /// </summary>
        internal static readonly Segment[] Segments = new[]
        {
            new Segment(InboxSegment, "My inbox", UIcons.Inbox, Theme.Colors.Blue600),
            new Segment(UrgentSegment, "Urgent actions", UIcons.Flame, Theme.Colors.Red500),
            new Segment(MineSegment, "PAWOs I own", UIcons.ClipboardList, Theme.Colors.Purple600),
            new Segment(UnassignedSegment, "Unassigned", UIcons.UserAdd, Theme.Colors.Orange500),
            new Segment(AllSegment, "All work orders", UIcons.Layers, Theme.Colors.Teal600),
        };

        /// <summary>One way of looking at the work orders: its id, its name and how it is marked.</summary>
        internal sealed class Segment
        {
            public Segment(string id, string label, UIcons icon, string color)
            {
                Id = id;
                Label = label;
                Icon = icon;
                Color = color;
            }

            public string Id { get; }
            public string Label { get; }
            public UIcons Icon { get; }
            public string Color { get; }
        }

        /// <summary>
        /// Builds the pivot for one engineer. <paramref name="summary"/> is the *already
        /// started* summary call, awaited by the segments that need its UID arrays — the
        /// caller has usually issued it for its own counters, and a second call could only
        /// return a different answer.
        /// </summary>
        public static SegmentedPivot For(UID128 userUID, Task<DashboardSummaryDto> summary)
        {
            return SegmentedPivot()
                .SegmentedPivot(InboxSegment, () => TextBlock(LabelOf(InboxSegment).t()),
                                () => FromSummary(summary, s => s?.InboxUIDs))
                .SegmentedPivot(UrgentSegment, () => TextBlock(LabelOf(UrgentSegment).t()),
                                () => FromSummary(summary, s => s?.UrgentUIDs))
                .SegmentedPivot(MineSegment, () => TextBlock(LabelOf(MineSegment).t()), () => Mine(userUID))
                .SegmentedPivot(UnassignedSegment, () => TextBlock(LabelOf(UnassignedSegment).t()),
                                () => FromSummary(summary, s => s?.UnassignedUIDs))
                .SegmentedPivot(AllSegment, () => TextBlock(LabelOf(AllSegment).t()), () => Everything());
        }

        internal static string LabelOf(string segmentId)
        {
            foreach (var segment in Segments)
            {
                if (segment.Id == segmentId) return segment.Label;
            }

            return segmentId;
        }

        /// <summary>
        /// The work orders assigned to this engineer, followed from their user node along the
        /// assignment edge — the same edge the summary tile counts, so the list and the number
        /// above it can never disagree.
        /// </summary>
        private static IComponent Mine(UID128 userUID)
        {
            return SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(userUID)
                        .Out(nodeTypes: new[] { N.PreAnalysisWorkOrder.Type },
                             edgeTypes: new[] { E.HasAssignment })
                        .TakeAll()))
                .S();
        }

        /// <summary>
        /// A segment that is exactly the work orders one of the summary counters counted — the
        /// same UIDs, so opening a number never shows a different set from the one the tile
        /// added up.
        /// </summary>
        private static IComponent FromSummary(Task<DashboardSummaryDto> summary,
                                              System.Func<DashboardSummaryDto, UID128[]> pick)
        {
            return Defer(async () =>
            {
                var uids = pick(await summary) ?? new UID128[0];

                return SearchArea()
                    .WithFacets()
                    .SearchFor(new SearchRequest()
                        .WithTargetUIDs(uids)
                        .MarkAsNoResultsIf(uids.Length == 0))
                    .S() ;
            }).S();
        }

        private static IComponent Everything()
        {
            return SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(N.PreAnalysisWorkOrder.Type).TakeAll()))
                .S();
        }
    }
}
