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
    // One renderer per node type the connector writes. Discovered by reflection - a public,
    // non-abstract INodeRenderer with a parameterless constructor joins the application by
    // existing - so nothing registers them. The style mirrors config/schemas/nodes/styles, so the
    // workspace's own pages and this front end show a type the same way.

    /// <summary>A news article: the headline, the excerpt under it, and how much of it could be read.</summary>
    public sealed class ArticleRenderer : NodeRendererBase
    {
        public ArticleRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Article.Type,
            DisplayName = "Article",
            LabelField  = N.Article.Title,
            Color       = "#0e4c7a",
            Icon        = "fi-rr-newspaper",
        })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result  = NodeResult.For(this, node);
            var excerpt = GetValueForProperty(node, N.Article.Excerpt);

            if (!string.IsNullOrWhiteSpace(excerpt)) result.SetContent(TextBlock(excerpt.Length > 280 ? excerpt.Substring(0, 279) + "…" : excerpt).Small().Secondary());

            //Whether the body was read from the page or is only the feed's summary is the first thing
            //an analyst needs before quoting an article.
            var source = GetValueForProperty(node, N.Article.ContentSource);
            if (!string.IsNullOrWhiteSpace(source)) result.AddFooterEntry(InlineLabel(source).Tooltip("Where the article text came from"));

            var language = GetValueForProperty(node, N.Article.Language);
            if (!string.IsNullOrWhiteSpace(language) && language != "en") result.AddFooterEntry(InlineLabel(language.ToUpperInvariant()).Tooltip("Language"));

            //Who and what the article is about, as entity linking found it in the title and body.
            result.AddFooterEntry(NeighborsLinks(node.UID, N.EntityTypes, E._Mentions, maxItemsToShow: 6, wrap: true));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Article.Title;
            yield return N.Article.Url;
            yield return N.Article.Author;
            yield return N.Article.SiteName;
            yield return N.Article.Language;
            yield return N.Article.ContentSource;
            yield return N.Article.ExtractionNote;
            yield return N.Article.WordCount;
            yield return N.Article.Excerpt;
            yield return N.Article.Body;
        }
    }

    /// <summary>A monitored feed: what it is for, and how its last fetch went.</summary>
    public sealed class FeedRenderer : NodeRendererBase
    {
        public FeedRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Feed.Type,
            DisplayName = "Feed",
            LabelField  = N.Feed.Name,
            Color       = "#c2660a",
            Icon        = "fi-rr-rss",
        })
        {
        }

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = NodeResult.For(this, node);
            var use    = GetValueForProperty(node, N.Feed.MarketUse);

            if (!string.IsNullOrWhiteSpace(use)) result.SetContent(TextBlock(use).Small().Secondary());

            var status = GetValueForProperty(node, N.Feed.LastFetchStatus);
            if (!string.IsNullOrWhiteSpace(status)) result.AddFooterEntry(InlineLabel(status).Tooltip("Last fetch"));

            var access = GetValueForProperty(node, N.Feed.AccessNote);
            if (!string.IsNullOrWhiteSpace(access)) result.AddFooterEntry(InlineLabel(access).Tooltip("Article access"));

            return result;
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Feed.Name;
            yield return N.Feed.FeedUrl;
            yield return N.Feed.MarketUse;
            yield return N.Feed.CoverageFocus;
            yield return N.Feed.ExpectedLanguage;
            yield return N.Feed.AccessNote;
            yield return N.Feed.Format;
            yield return N.Feed.LastFetchStatus;
            yield return N.Feed.LastFetchError;
            yield return N.Feed.LastItemCount;
            yield return N.Feed.LastItemAt;
            yield return N.Feed.EvidencePage;
            yield return N.Feed.DirectoryStatus;
            yield return N.Feed.DirectoryAge;
            yield return N.Feed.OverlapNote;
        }
    }

    public sealed class PublisherRenderer : NodeRendererBase
    {
        public PublisherRenderer() : base(new SchemaStyleInfo { Name = N.Publisher.Type, DisplayName = "Publisher", LabelField = N.Publisher.Name + "|" + N.Publisher.Domain, Color = "#5b4b8a", Icon = "fi-rr-building" })
        {
        }

        protected override IEnumerable<string> GetDisplayPropertyNames(Node node)
        {
            yield return N.Publisher.Name;
            yield return N.Publisher.Domain;
        }
    }

    public sealed class FeedCategoryRenderer : NodeRendererBase
    {
        public FeedCategoryRenderer() : base(new SchemaStyleInfo { Name = N.FeedCategory.Type, DisplayName = "Category", LabelField = N.FeedCategory.Name, Color = "#1d7a46", Icon = "fi-rr-folder" })
        {
        }
    }

    public sealed class SourceTypeRenderer : NodeRendererBase
    {
        public SourceTypeRenderer() : base(new SchemaStyleInfo { Name = N.SourceType.Type, DisplayName = "Source type", LabelField = N.SourceType.Name, Color = "#6a6f78", Icon = "fi-rr-id-badge" })
        {
        }
    }

    public sealed class PriorityRenderer : NodeRendererBase
    {
        public PriorityRenderer() : base(new SchemaStyleInfo { Name = N.Priority.Type, DisplayName = "Priority", LabelField = N.Priority.Name, Color = "#b3361f", Icon = "fi-rr-flag" })
        {
        }
    }

    // The five vocabularies entity linking resolves mentions to. Their articles arrive over
    // _AppearsIn, which the base's tabs show with the structured edges - never filtered out.

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

    public sealed class TagRenderer : NodeRendererBase
    {
        public TagRenderer() : base(new SchemaStyleInfo { Name = N.Tag.Type, DisplayName = "Tag", LabelField = N.Tag.Name, Color = "#7a8491", Icon = "fi-rr-tags" })
        {
        }
    }
}
