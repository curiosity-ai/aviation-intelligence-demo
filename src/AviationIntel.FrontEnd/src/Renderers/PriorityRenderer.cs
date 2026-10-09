using System.Collections.Generic;
using AviationIntel.FrontEnd.Schema;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using static Tesserae.UI;
using static Mosaik.UI;
using Node = Mosaik.Schema.Node;

namespace AviationIntel.FrontEnd.Renderers
{
    /// <summary>A monitoring priority from the directory: its rank, and the feeds that carry it.</summary>
    public sealed class PriorityRenderer : NodeRendererBase
    {
        public PriorityRenderer() : base(new SchemaStyleInfo { Name = N.Priority.Type, DisplayName = "Priority", LabelField = N.Priority.Name, Color = "#b3361f", Icon = "fi-rr-flag" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            var rank   = GetValueForProperty(node, N.Priority.Rank);

            if (!string.IsNullOrWhiteSpace(rank)) result.AddFooterEntry(InlineLabel("Rank " + rank).Tooltip("1 is read first"));
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Feed.Type, E.PriorityOfFeed, maxItemsToShow: 3, wrap: true));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Priority.Name;
            yield return N.Priority.Rank;
        }
    }
}
