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
    /// <summary>A large airport with scheduled service, from OurAirports.</summary>
    public sealed class AirportRenderer : NodeRendererBase
    {
        public AirportRenderer() : base(new SchemaStyleInfo { Name = N.Airport.Type, DisplayName = "Airport", LabelField = N.Airport.Name, Color = "#4a6b2a", Icon = "fi-rr-plane-departure" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            var iata   = GetValueForProperty(node, N.Airport.IataCode);
            var place  = GetValueForProperty(node, N.Airport.Municipality);

            if (!string.IsNullOrWhiteSpace(iata))  result.AddFooterEntry(InlineLabel(iata).Tooltip("IATA code"));
            if (!string.IsNullOrWhiteSpace(place)) result.AddFooterEntry(InlineLabel(place + ", " + GetValueForProperty(node, N.Airport.Country)));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Airport.Name;
            yield return N.Airport.IcaoCode;
            yield return N.Airport.IataCode;
            yield return N.Airport.Municipality;
            yield return N.Airport.Country;
        }
    }
}
