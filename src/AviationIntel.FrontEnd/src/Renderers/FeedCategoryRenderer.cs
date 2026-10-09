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
    /// <summary>A category of the feed directory, and the feeds an analyst filed under it.</summary>
    public sealed class FeedCategoryRenderer : NodeRendererBase
    {
        public FeedCategoryRenderer() : base(new SchemaStyleInfo { Name = N.FeedCategory.Type, DisplayName = "Category", LabelField = N.FeedCategory.Name, Color = "#1d7a46", Icon = "fi-rr-folder" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Feed.Type, E.CategoryHasFeed, maxItemsToShow: 3, wrap: true));
            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.FeedCategory.Name;
        }
    }
}
