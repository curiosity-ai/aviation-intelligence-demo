[schema: Curiosity.Schemas.Key("FeedUrl")]

public class Feed
{
    public string FeedUrl { get; set; }

    public string AccessNote { get; set; }

    public string CoverageFocus { get; set; }

    public string DirectoryAge { get; set; }

    public string DirectoryFeedTitle { get; set; }

    public string DirectoryNote { get; set; }

    public string DirectoryStatus { get; set; }

    public string EvidencePage { get; set; }

    public string ExpectedLanguage { get; set; }

    public string Format { get; set; }

    public string LastFetchError { get; set; }

    public string LastFetchStatus { get; set; }

    public Time LastItemAt { get; set; }

    public int LastItemCount { get; set; }

    public string MarketUse { get; set; }

    public string Name { get; set; }

    public string OverlapNote { get; set; }

    public string ResolvedFeedUrl { get; set; }
}
