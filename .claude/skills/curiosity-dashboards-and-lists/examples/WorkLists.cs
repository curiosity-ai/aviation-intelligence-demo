// Reference template: ONE description of the segments, read by three surfaces.
// See ../SKILL.md §3.
//
// The counter cards on the home page, the sidebar entries and the pivot tabs on the list page
// are three RENDERINGS of one list. Describing them once is the difference between three
// surfaces that agree and three places to keep in step by hand — and they must agree, because
// every counter card and the urgent banner send the reader from one to the other.

using System.Collections.Generic;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Tesserae;
using App.FrontEnd.Api;
using App.FrontEnd.Schema;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace App.FrontEnd.Dashboard
{
    internal sealed class WorkSegment
    {
        public string Id    { get; set; }
        public string Label { get; set; }
        public string Icon  { get; set; }
        public string Color { get; set; }
    }

    internal static class WorkLists
    {
        /// <summary>
        /// Note that <c>inbox</c> and <c>mine</c> are DELIBERATELY different sets — the inbox is
        /// the work on the desk that is not finished, "mine" is everything ever assigned. Both
        /// numbers are on the home page, so both need a segment: a card whose number says 7
        /// opening a list of 34 is a card that lied.
        /// </summary>
        public static readonly WorkSegment[] Segments =
        {
            new WorkSegment { Id = "inbox",      Label = "My inbox",   Icon = UIcons.Inbox.ToString(),    Color = Theme.Colors.Blue500   },
            new WorkSegment { Id = "urgent",     Label = "Urgent",     Icon = UIcons.Flame.ToString(),    Color = Theme.Colors.Red500    },
            new WorkSegment { Id = "mine",       Label = "Mine",       Icon = UIcons.User.ToString(),     Color = Theme.Colors.Green500  },
            new WorkSegment { Id = "unassigned", Label = "Unassigned", Icon = UIcons.UserAdd.ToString(),  Color = Theme.Colors.Orange500 },
            new WorkSegment { Id = "all",        Label = "All",        Icon = UIcons.List.ToString(),     Color = Theme.Colors.Neutral500},
        };

        public static WorkSegment ById(string id)
        {
            foreach (var s in Segments) if (s.Id == id) return s;
            return Segments[0];
        }

        /// <summary>
        /// A pivot with one segment per counter.
        ///
        /// <c>mine</c> and <c>all</c> scope a search area with a QUERY; the rest use the very
        /// UID ARRAYS the summary endpoint counted — so opening a number never shows a
        /// different set from the one the tile added up.
        /// </summary>
        public static IComponent For(UID128 userUID, SummaryDto summary, string selectedId)
        {
            var pivot = SegmentedPivot();

            foreach (var segment in Segments)
            {
                var captured = segment;

                pivot.Pivot(captured.Id, captured.Label.t(), () => ListFor(captured.Id, userUID, summary),
                            selected: captured.Id == selectedId);
            }

            return pivot.S();
        }

        private static IComponent ListFor(string segmentId, UID128 userUID, SummaryDto summary)
        {
            switch (segmentId)
            {
                case "inbox":      return Of(summary.InboxUIDs);
                case "urgent":     return Of(summary.UrgentUIDs);
                case "unassigned": return Of(summary.UnassignedUIDs);

                case "mine":
                    // Ownership is an edge, so "mine" is a traversal — never a comparison
                    // against a name stored beside the record.
                    return SearchArea().WithFacets().OnSearch(sr => sr.WithTargetQuery(
                        Mosaik.API.Query.StartAt(userUID).Out(N.Case.Type, E.HasAssignment).TakeAll()));

                default:
                    return SearchArea().WithFacets().OnSearch(sr => sr.WithTargetQuery(
                        Mosaik.API.Query.StartAt(N.Case.Type).TakeAll()));
            }
        }

        /// <summary>
        /// A list of exactly these UIDs, each row drawn by that node type's OWN registered
        /// renderer — which is why the endpoint returns UIDs and never titles.
        /// </summary>
        private static IComponent Of(UID128[] uids)
            => SearchArea().WithFacets().SearchFor(new SearchRequest().WithTargetUIDs(uids)).S();
    }
}
