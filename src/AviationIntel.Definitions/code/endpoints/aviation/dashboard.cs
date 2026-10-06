[endpoint: Curiosity.Endpoints.Path("aviation/dashboard")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

// The home page's numbers, computed from the graph on every call - the connector stores
// articles and feeds, never counts.
//
// Every count travels with the UIDs it counted, so a tile on the home page opens exactly the
// articles it added up rather than re-running a query that could disagree with it.

var now = DateTimeOffset.UtcNow;
var dayAgo = now.AddDays(-1);
var weekAgo = now.AddDays(-7);

var week = Q().StartAt(N.Article.Type)
              .WhereTimestamp(new Time(weekAgo), new Time(now.AddHours(1)), true)
              .SortByTimestamp(oldestFirst: false)
              .AsEnumerable()
              .Select(n => new { n.UID, At = n.Timestamp.ToDateTimeOffset(), Content = n.GetString(N.Article.ContentSource) })
              .ToList();

var today = week.Where(a => a.At >= dayAgo).ToList();

UID128[] Out(UID128 from, string type, string edge) => Q().StartAt(from).Out(type, edge).AsUIDEnumerable().ToArray();

var categoryOf = week.ToDictionary(a => a.UID, a => Out(a.UID, N.FeedCategory.Type, E.ArticleInCategory));
var feedsOf = week.ToDictionary(a => a.UID, a => Out(a.UID, N.Feed.Type, E.AppearedIn));
var publisherOf = week.ToDictionary(a => a.UID, a => Out(a.UID, N.Publisher.Type, E.ArticleFromPublisher));

var categories = Q().StartAt(N.FeedCategory.Type).AsEnumerable()
                    .Select(c => new CategoryCountDto
                    {
                        CategoryUID = c.UID,
                        Name = c.GetString(N.FeedCategory.Name),
                        Count24h = today.Count(a => categoryOf[a.UID].Contains(c.UID)),
                        Count7d = week.Count(a => categoryOf[a.UID].Contains(c.UID)),
                        ArticleUIDs7d = week.Where(a => categoryOf[a.UID].Contains(c.UID)).Select(a => a.UID).ToArray(),
                    })
                    .Where(c => c.Count7d > 0)
                    .OrderByDescending(c => c.Count7d)
                    .ThenBy(c => c.Name)
                    .ToArray();

var feeds = Q().StartAt(N.Feed.Type).AsEnumerable().ToList();
var failing = feeds.Where(f => f.GetString(N.Feed.LastFetchStatus) is not ("OK" or "Empty")).Select(f => f.UID).ToArray();

var days = Enumerable.Range(0, 7)
                     .Select(i => now.UtcDateTime.Date.AddDays(-6 + i))
                     .Select(d => new DayCountDto
                     {
                         Day = d.ToString("yyyy-MM-dd"),
                         Count = week.Count(a => a.At.UtcDateTime.Date == d),
                     })
                     .ToArray();

var summaryOnly = week.Where(a => a.Content == "Feed summary only").Select(a => a.UID).ToArray();

return new DashboardDto
{
    GeneratedAt = now.ToString("yyyy-MM-dd HH:mm") + " UTC",
    Articles24h = today.Count,
    Articles7d = week.Count,
    Feeds = feeds.Count,
    FeedsWithNews24h = today.SelectMany(a => feedsOf[a.UID]).Distinct().Count(),
    FeedsFailing = failing.Length,
    Publishers7d = week.SelectMany(a => publisherOf[a.UID]).Distinct().Count(),
    SummaryOnly7d = summaryOnly.Length,
    Latest24hUIDs = today.Select(a => a.UID).ToArray(),
    Week7dUIDs = week.Select(a => a.UID).ToArray(),
    SummaryOnlyUIDs7d = summaryOnly,
    FailingFeedUIDs = failing,
    FeedUIDs = feeds.Select(f => f.UID).ToArray(),
    Categories = categories,
    Days = days,
};

public class DashboardDto
{
    public string GeneratedAt { get; set; }
    public int Articles24h { get; set; }
    public int Articles7d { get; set; }
    public int Feeds { get; set; }
    public int FeedsWithNews24h { get; set; }
    public int FeedsFailing { get; set; }
    public int Publishers7d { get; set; }
    public int SummaryOnly7d { get; set; }
    public UID128[] Latest24hUIDs { get; set; }
    public UID128[] Week7dUIDs { get; set; }
    public UID128[] SummaryOnlyUIDs7d { get; set; }
    public UID128[] FailingFeedUIDs { get; set; }
    public UID128[] FeedUIDs { get; set; }
    public CategoryCountDto[] Categories { get; set; }
    public DayCountDto[] Days { get; set; }
}

public class CategoryCountDto
{
    public UID128 CategoryUID { get; set; }
    public string Name { get; set; }
    public int Count24h { get; set; }
    public int Count7d { get; set; }
    public UID128[] ArticleUIDs7d { get; set; }
}

public class DayCountDto
{
    public string Day { get; set; }
    public int Count { get; set; }
}

