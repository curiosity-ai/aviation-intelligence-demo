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
    // One of the five vocabularies entity linking resolves mentions to (with AircraftType, EngineType,
    // LaunchVehicle and Airport). Their articles arrive over _AppearsIn, which the base's tabs show with
    // the structured edges - never filtered out.

    /// <summary>An airline, manufacturer, authority, agency or other organisation from Wikidata.</summary>
    public sealed class OrganisationRenderer : NodeRendererBase
    {
        public OrganisationRenderer() : base(new SchemaStyleInfo { Name = N.Organisation.Type, DisplayName = "Organisation", LabelField = N.Organisation.Name, Color = "#0b6e6e", Icon = "fi-rr-corporate" })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result  = NodeResult.For(this, node);
            var kind    = GetValueForProperty(node, N.Organisation.Kind);
            var country = GetValueForProperty(node, N.Organisation.Country);

            if (!string.IsNullOrWhiteSpace(kind))    result.AddFooterEntry(InlineLabel(kind).Tooltip("Kind"));
            if (!string.IsNullOrWhiteSpace(country)) result.AddFooterEntry(InlineLabel(country).Tooltip("Country"));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Organisation.Name;
            yield return N.Organisation.Kind;
            yield return N.Organisation.Country;
            yield return N.Organisation.IcaoCode;
            yield return N.Organisation.WikidataId;
        }
    }
}
