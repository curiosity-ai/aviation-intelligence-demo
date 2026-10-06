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

    public sealed class TagRenderer : NodeRendererBase
    {
        public TagRenderer() : base(new SchemaStyleInfo { Name = N.Tag.Type, DisplayName = "Tag", LabelField = N.Tag.Name, Color = "#7a8491", Icon = "fi-rr-tags" })
        {
        }
    }
}
