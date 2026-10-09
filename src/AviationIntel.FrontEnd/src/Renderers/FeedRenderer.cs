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

            result.AddFooterEntry(NeighborsLinks(node.UID, N.FeedCategory.Type, E.FeedInCategory, maxItemsToShow: 1));
            result.AddFooterEntry(NeighborsLinks(node.UID, N.Publisher.Type, E.PublishedBy, maxItemsToShow: 1));

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
}
