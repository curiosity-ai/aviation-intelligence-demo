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
    /// <summary>A rocket model, series or family, with who builds it.</summary>
    public sealed class LaunchVehicleRenderer : NodeRendererBase
    {
        public LaunchVehicleRenderer() : base(new SchemaStyleInfo { Name = N.LaunchVehicle.Type, DisplayName = "Launch vehicle", LabelField = N.LaunchVehicle.Name, Color = "#3d3a8c", Icon = "fi-rr-rocket-lunch" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Organisation.Type, E.LaunchVehicleBuiltBy, maxItemsToShow: 2));
            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.LaunchVehicle.Name;
            yield return N.LaunchVehicle.WikidataId;
        }
    }
}
