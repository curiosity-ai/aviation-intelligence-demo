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
    /// <summary>A category or keyword a publisher put on its own articles, and the latest of them.</summary>
    public sealed class TagRenderer : NodeRendererBase
    {
        public TagRenderer() : base(new SchemaStyleInfo { Name = N.Tag.Type, DisplayName = "Tag", LabelField = N.Tag.Name, Color = "#7a8491", Icon = "fi-rr-tags" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Article.Type, E.TagOf, maxItemsToShow: 2, wrap: true));
            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Tag.Name;
        }
    }
}
