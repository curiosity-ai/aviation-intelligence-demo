namespace AviationIntel.Connector;

/// <summary>
/// Every edge name in the graph, declared in both directions and registered in one call via
/// <c>CreateEdgeSchemaAsync(typeof(Edges))</c>.
/// </summary>
/// <remarks>
/// Only what the sources state is written here. Which companies, aircraft programmes, airports
/// or regulators an article is about is <b>not</b> - that is what entity recognition discovers
/// from the text, and arrives as <c>_Mentions</c> / <c>_AppearsIn</c>.
/// </remarks>
public static class Edges
{
    // --- the directory -------------------------------------------------------------
    public const string FeedInCategory  = nameof(FeedInCategory);
    public const string CategoryHasFeed = nameof(CategoryHasFeed);

    public const string FeedOfSourceType = nameof(FeedOfSourceType);
    public const string SourceTypeHasFeed = nameof(SourceTypeHasFeed);

    public const string FeedHasPriority = nameof(FeedHasPriority);
    public const string PriorityOfFeed  = nameof(PriorityOfFeed);

    public const string PublishedBy     = nameof(PublishedBy);
    public const string PublisherOfFeed = nameof(PublisherOfFeed);

    // --- articles ------------------------------------------------------------------
    // An article can arrive through several feeds (a publisher-wide feed and a topic feed of
    // the same newsroom); it is one node with an edge per feed, never one node per feed.
    public const string AppearedIn     = nameof(AppearedIn);
    public const string FeedHasArticle = nameof(FeedHasArticle);

    public const string ArticleFromPublisher = nameof(ArticleFromPublisher);
    public const string PublisherHasArticle  = nameof(PublisherHasArticle);

    // The category is the feed's, as the directory states it - carried onto the article so a
    // category facet and the reports' sections are one hop rather than two.
    public const string ArticleInCategory  = nameof(ArticleInCategory);
    public const string CategoryHasArticle = nameof(CategoryHasArticle);

    // The publisher's own tags (<category> in RSS), normalised.
    public const string TaggedWith = nameof(TaggedWith);
    public const string TagOf      = nameof(TagOf);
}
