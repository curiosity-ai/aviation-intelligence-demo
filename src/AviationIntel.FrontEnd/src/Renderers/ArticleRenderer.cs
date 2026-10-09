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
    // One renderer per node type the connector writes, one file each. Discovered by reflection - a
    // public, non-abstract INodeRenderer with a parameterless constructor joins the application by
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
}
