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
    /// <summary>What kind of source a feed is (as the directory classifies it), and the feeds of that kind.</summary>
    public sealed class SourceTypeRenderer : NodeRendererBase
    {
        public SourceTypeRenderer() : base(new SchemaStyleInfo { Name = N.SourceType.Type, DisplayName = "Source type", LabelField = N.SourceType.Name, Color = "#6a6f78", Icon = "fi-rr-id-badge" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Feed.Type, E.SourceTypeHasFeed, maxItemsToShow: 3, wrap: true));
            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.SourceType.Name;
        }
    }
}
