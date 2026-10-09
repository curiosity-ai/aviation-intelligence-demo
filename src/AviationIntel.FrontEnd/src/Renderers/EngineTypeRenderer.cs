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
    /// <summary>An aircraft engine or engine family, with who builds it.</summary>
    public sealed class EngineTypeRenderer : NodeRendererBase
    {
        public EngineTypeRenderer() : base(new SchemaStyleInfo { Name = N.EngineType.Type, DisplayName = "Engine type", LabelField = N.EngineType.Name, Color = "#8a4b0f", Icon = "fi-rr-engine" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Organisation.Type, E.EngineBuiltBy, maxItemsToShow: 2));
            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.EngineType.Name;
            yield return N.EngineType.WikidataId;
        }
    }
}
