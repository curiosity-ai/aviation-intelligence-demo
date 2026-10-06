using UID;

namespace AviationIntel.FrontEnd.Api
{
    // The front-end half of the wire contract of aviation/dashboard. Flat, and every identifier
    // a UID128 - the server-side classes in the endpoint carry the same names.

    public sealed class DashboardDto
    {
        public string             GeneratedAt       { get; set; }
        public int                Articles24h       { get; set; }
        public int                Articles7d        { get; set; }
        public int                Feeds             { get; set; }
        public int                FeedsWithNews24h  { get; set; }
        public int                FeedsFailing      { get; set; }
        public int                Publishers7d      { get; set; }
        public int                SummaryOnly7d     { get; set; }
        public UID128[]           Latest24hUIDs     { get; set; }
        public UID128[]           Week7dUIDs        { get; set; }
        public UID128[]           SummaryOnlyUIDs7d { get; set; }
        public UID128[]           FailingFeedUIDs   { get; set; }
        public UID128[]           FeedUIDs          { get; set; }
        public CategoryCountDto[] Categories        { get; set; }
        public DayCountDto[]      Days              { get; set; }
    }

    public sealed class CategoryCountDto
    {
        public UID128   CategoryUID   { get; set; }
        public string   Name          { get; set; }
        public int      Count24h      { get; set; }
        public int      Count7d       { get; set; }
        public UID128[] ArticleUIDs7d { get; set; }
    }

    public sealed class DayCountDto
    {
        public string Day   { get; set; }
        public int    Count { get; set; }
    }
}
