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
    /// <summary>The company or site behind one or more feeds: its domain, and the feeds it publishes.</summary>
    public sealed class PublisherRenderer : NodeRendererBase
    {
        public PublisherRenderer() : base(new SchemaStyleInfo { Name = N.Publisher.Type, DisplayName = "Publisher", LabelField = N.Publisher.Name + "|" + N.Publisher.Domain, Color = "#5b4b8a", Icon = "fi-rr-building" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            var domain = GetValueForProperty(node, N.Publisher.Domain);

            if (!string.IsNullOrWhiteSpace(domain)) result.AddFooterEntry(InlineLabel(domain).Tooltip("Domain"));
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Feed.Type, E.PublisherOfFeed, maxItemsToShow: 3, wrap: true));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Publisher.Name;
            yield return N.Publisher.Domain;
        }
    }
}
