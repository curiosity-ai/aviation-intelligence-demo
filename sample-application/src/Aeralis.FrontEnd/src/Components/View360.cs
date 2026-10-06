using Mosaik;
using Mosaik.Components;
using Tesserae;
using Aeralis.FrontEnd.Schema;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using Node = Mosaik.Schema.Node;

namespace Aeralis.FrontEnd.Components
{
    /// <summary>
    /// The 360 view: everything of any kind that touches this node, in one faceted
    /// search area.
    ///
    /// The query deliberately passes **no edge-type filter**. Structured edges written by
    /// the connector and NLP edges written by the workspace pipeline (<c>_AppearsIn</c>,
    /// <c>_Mentions</c>) both belong here — the whole point of the view is that the user
    /// does not have to know which mechanism found a connection.
    /// </summary>
    internal static class View360
    {
        public static IComponent For(Node node, string[] targetTypes)
        {
            return SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(node.UID).Out(nodeTypes: targetTypes).TakeAll()));
        }

        /// <summary>
        /// The same view centred one hop away: on the modification a work order analyses
        /// rather than on the work order itself.
        ///
        /// The hop is expressed inside the target query, so the view is still one server
        /// call — and it is an edge traversal rather than a lookup by the subject's
        /// document identifier, which is what the graph is for.
        /// </summary>
        public static IComponent ForSubjectOf(UID128 workOrderUID, string[] targetTypes)
        {
            return SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(workOrderUID)
                        .Out(new[] { N.Modification.Type }, new[] { E.PreAnalysisOf })
                        .Out(nodeTypes: targetTypes)
                        .TakeAll()));
        }
    }
}
