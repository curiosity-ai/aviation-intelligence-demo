// Reference template: which properties are really references, and to what. See ../SKILL.md §7.
//
// The mapping CANNOT be derived. That CategoryL2 is reached by InCategoryL2 rather than
// InCategory, and that Status means the DocumentStatus vocabulary rather than any of the other
// thirty-seven, is a decision the CONNECTOR made. It is written out here, once, against the
// generated N and E, so a rename on either side breaks the build.
//
// Adding a property to this table is the SECOND HALF of promoting a string to a relationship in
// the connector. The first half is the edge; without this, the new node is reachable only from
// the 360 view.

using App.FrontEnd.Schema;

namespace App.FrontEnd.Renderers
{
    internal sealed class PropertyLink
    {
        public PropertyLink(string edgeType, params string[] nodeTypes)
        {
            EdgeType  = edgeType;
            NodeTypes = nodeTypes;
        }

        public string   EdgeType  { get; }
        public string[] NodeTypes { get; }
    }

    internal static class PropertyLinks
    {
        /// <summary>
        /// The PER-TYPE table wins, and the shared envelope is the fallback — not the other way
        /// round. Some types use an envelope property's NAME for something else and reach it
        /// over a different edge; matching the envelope first would silently send them down an
        /// edge that does not exist for them, and the row would go blank.
        /// </summary>
        public static PropertyLink For(string nodeType, string property)
            => PerType(nodeType, property) ?? Envelope(property);

        /// <summary>
        /// Properties every record carries, linked the same way on all of them.
        ///
        /// A type that has the property but NOT the edge simply finds no neighbours, and the
        /// renderer falls back to showing the value — which is why the property stays on the
        /// node in the connector.
        /// </summary>
        private static PropertyLink Envelope(string property)
        {
            switch (property)
            {
                case N.Case.CategoryL4:     return new PropertyLink(E.InCategory,   N.Category.Type);
                case N.Case.CategoryL2:     return new PropertyLink(E.InCategoryL2, N.Category.Type);
                case N.Case.Partition:      return new PropertyLink(E.InPartition,  N.Partition.Type);
                case N.Case.Keywords:       return new PropertyLink(E.HasKeyword,   N.Keyword.Type);

                // Every status-like property points at N.Status.Type, keyed by the value alone,
                // over a DIFFERENT edge each — so a row still shows one answer while the node
                // itself gathers everything in that state across every record type.
                case N.Case.Status:         return new PropertyLink(E.HasDocumentStatus, N.Status.Type);

                default: return null;
            }
        }

        private static PropertyLink PerType(string nodeType, string property)
        {
            switch (nodeType)
            {
                case N.Case.Type:       return Case(property);
                case N.Change.Type:     return Change(property);
                case N.PartNumber.Type: return PartNumber(property);
                default:                return null;
            }
        }

        private static PropertyLink Case(string property)
        {
            switch (property)
            {
                // The workflow status is a different FIELD from the document status, so it
                // travels over its own edge — the same Status node at the far end, reached by
                // the edge that says which field it came from.
                case N.Case.WorkflowStatus: return new PropertyLink(E.HasWorkflowStatus, N.Status.Type);
                case N.Case.Priority:       return new PropertyLink(E.HasPriority,       N.Classification.Type);
                // A property naming another record: the graph already holds it, so link it.
                case N.Case.SubjectId:      return new PropertyLink(E.CaseOf,            N.Change.Type);
                default:                    return null;
            }
        }

        private static PropertyLink Change(string property)
        {
            switch (property)
            {
                case N.Change.RiskLevel: return new PropertyLink(E.HasRiskLevel, N.Classification.Type);
                default:                 return null;
            }
        }

        private static PropertyLink PartNumber(string property)
        {
            switch (property)
            {
                // Lifecycle is a state, so it reaches Status — over its OWN edge, not the
                // document-status one.
                case N.PartNumber.Lifecycle: return new PropertyLink(E.HasLifecycle, N.Status.Type);
                case N.PartNumber.Category:  return new PropertyLink(E.InCategory,   N.Category.Type);
                default:                     return null;
            }
        }
    }
}
