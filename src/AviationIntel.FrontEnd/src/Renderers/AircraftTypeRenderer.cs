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
    /// <summary>An aircraft family or model, with who builds it.</summary>
    public sealed class AircraftTypeRenderer : NodeRendererBase
    {
        public AircraftTypeRenderer() : base(new SchemaStyleInfo { Name = N.AircraftType.Type, DisplayName = "Aircraft type", LabelField = N.AircraftType.Name, Color = "#2c5aa0", Icon = "fi-rr-plane-alt" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            var first  = GetValueForProperty(node, N.AircraftType.FirstFlight);

            result.AddFooterEntry(NeighborsLinks(node.UID, N.Organisation.Type, E.AircraftBuiltBy, maxItemsToShow: 2));
            if (!string.IsNullOrWhiteSpace(first)) result.AddFooterEntry(InlineLabel("First flight " + first));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.AircraftType.Name;
            yield return N.AircraftType.FirstFlight;
            yield return N.AircraftType.WikidataId;
        }
    }
}
