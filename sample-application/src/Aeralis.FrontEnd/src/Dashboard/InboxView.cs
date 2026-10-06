using Tesserae;
using TNT;
using Transpose.Core;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace Aeralis.FrontEnd.Dashboard
{
    /// <summary>
    /// The engineer's work orders, as a page of its own behind the sidebar's entries.
    ///
    /// The home page answers "how am I doing"; this answers "what is in front of me". They
    /// showed the same lists, but on the home page those lists sat under a profile card, four
    /// counters and a performance block, in a card — which is not where anybody works through a
    /// queue. Here the pivot is the page.
    ///
    /// It is deliberately the *same* component (<see cref="WorkOrderLists"/>), not a second copy
    /// of the queries: every counter card and the urgent banner send the reader straight here
    /// with a segment already selected, and a reader who arrives expecting the count they just
    /// read has to find that count.
    /// </summary>
    internal sealed class InboxView : IComponent
    {
        /// <summary>
        /// Which segment to open on, taken from the URL — <c>#/inbox?segment=urgent</c>. Every
        /// way in builds that link through <see cref="RouteFor"/>, so a number on the home page
        /// and the list that opens are one navigation apart and cannot disagree.
        /// </summary>
        internal const string SegmentParameter = "segment";

        private readonly Stack _root = VStack().S().P(16);

        public InboxView(Parameters state)
        {
            var segment = state is object && state.TryGetValue(SegmentParameter, out var requested)
                          && !string.IsNullOrEmpty(requested)
                ? requested
                : WorkOrderLists.DefaultSegment;

            _root.Add(Defer(async () =>
            {
                var user = await Viewer.EnsureAsync();

                if (user is null)
                    return TextBlock("No engineers are loaded in this workspace.".t()).Secondary();

                var summary = Api.Api.DashboardSummaryAsync(user.Uid);
                var lists = WorkOrderLists.For(user.Uid, summary);

                var header = HStack().WS().AlignItemsCenter().PB(12).Children(
                    IconTile(Mark(segment).Icon, Mark(segment).Color).Size(34.px()),
                    VStack().PL(12).Children(
                        TextBlock(WorkOrderLists.LabelOf(segment).t()).Large().SemiBold(),
                        TextBlock(string.Format("Pre-analysis work orders on {0}'s desk".t(),
                                                user.DisplayName ?? user.UserName)).Small().Secondary()));

                // Selected after the pivot exists, not while it is being described: a segment
                // selected before its content has been laid out renders into nothing.
                if (segment != WorkOrderLists.DefaultSegment) lists.Select(segment, refresh: true);

                return VStack().S().Children(header, lists.S());
            }).S());
        }

        /// <summary>
        /// The URL for one segment. The one place it is built — the counter cards, the urgent
        /// banner and the sidebar all call this, so none of them can spell it differently.
        /// </summary>
        internal static string RouteFor(string segment)
            => AeralisApp.InboxRoute + "?" + SegmentParameter + "=" + segment;

        /// <summary>
        /// Whether the browser is currently on that segment, for the sidebar's selected state.
        ///
        /// A bare <c>#/inbox</c> counts as the default segment, because that is what the view
        /// opens on — otherwise landing on the inbox with no query would light no entry at all
        /// while showing one of them.
        /// </summary>
        internal static bool IsShowing(string segment)
        {
            var hash = window.location.hash ?? "";

            if (!hash.Contains(AeralisApp.InboxRoute)) return false;

            if (hash.Contains(SegmentParameter + "=" + segment)) return true;

            return segment == WorkOrderLists.DefaultSegment
                   && !hash.Contains(SegmentParameter + "=");
        }

        private static WorkOrderLists.Segment Mark(string segmentId)
        {
            foreach (var segment in WorkOrderLists.Segments)
            {
                if (segment.Id == segmentId) return segment;
            }

            return WorkOrderLists.Segments[0];
        }

        public dom.HTMLElement Render() => _root.Render();
    }
}
