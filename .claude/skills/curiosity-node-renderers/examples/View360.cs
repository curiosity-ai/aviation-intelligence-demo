// Reference template: the 360 view. See ../SKILL.md §9.

using Mosaik;
using Mosaik.Components;
using Tesserae;
using App.FrontEnd.Schema;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using Node = Mosaik.Schema.Node;

namespace App.FrontEnd.Components
{
    /// <summary>
    /// Everything of any kind that touches this node, in one faceted search area.
    ///
    /// The query deliberately passes NO EDGE-TYPE FILTER. Structured edges written by the
    /// connector and NLP edges written by the workspace pipeline (_AppearsIn, _Mentions) both
    /// belong here — the whole point of the view is that the user does not have to know which
    /// mechanism found a connection.
    /// </summary>
    internal static class View360
    {
        public static IComponent For(Node node, string[] targetTypes)
            => SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(node.UID).Out(nodeTypes: targetTypes).TakeAll()));

        /// <summary>
        /// The same view centred ONE HOP AWAY: on the change a case analyses rather than on the
        /// case itself.
        ///
        /// The hop is expressed inside the target query, so this is still one server call — and
        /// it is an edge traversal rather than a lookup by the subject's business identifier,
        /// which is what the graph is for.
        /// </summary>
        public static IComponent ForSubjectOf(UID128 caseUID, string[] targetTypes)
            => SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(caseUID)
                        .Out(new[] { N.Change.Type }, new[] { E.CaseOf })
                        .Out(nodeTypes: targetTypes)
                        .TakeAll()));
    }
}
