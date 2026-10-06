using System.Linq;
using System.Threading.Tasks;
using AviationIntel.FrontEnd.Api;
using Tesserae;
using UID;
using static Tesserae.UI;

namespace AviationIntel.FrontEnd.Dashboard
{
    /// <summary>
    /// The named sets of articles the home page counts and the news page lists. One description
    /// per set - the tile, the sidebar entry and the page header all read it - so a number and the
    /// list it opens can never be described two different ways.
    /// </summary>
    internal static class NewsSets
    {
        internal const string Today       = "today";
        internal const string Week        = "week";
        internal const string SummaryOnly = "summary-only";

        /// <summary>A category's articles of the last seven days: <c>category:&lt;uid&gt;</c>.</summary>
        internal const string CategoryPrefix = "category:";

        internal sealed class NewsSet
        {
            public NewsSet(string id, string label, string description, UIcons icon, string color)
            {
                Id          = id;
                Label       = label;
                Description = description;
                Icon        = icon;
                Color       = color;
            }

            public string Id          { get; }
            public string Label       { get; }
            public string Description { get; }
            public UIcons Icon        { get; }
            public string Color       { get; }
        }

        internal static readonly NewsSet[] All =
        {
            new NewsSet(Today,       "Last 24 hours",     "Every article published in the last 24 hours",                   UIcons.Bolt,             Theme.Colors.Blue600),
            new NewsSet(Week,        "Last 7 days",       "Every article published in the last seven days",                 UIcons.CalendarLines,    Theme.Colors.Teal600),
            new NewsSet(SummaryOnly, "Summary only",      "Articles of the last seven days only known from their feed summary", UIcons.EyeCrossed,  Theme.Colors.Orange600),
        };

        internal static NewsSet Describe(string id, DashboardDto dashboard)
        {
            if (id is object && id.StartsWith(CategoryPrefix))
            {
                var category = Category(id, dashboard);
                return new NewsSet(id, category?.Name ?? "Category", "Articles of the last seven days in this category", UIcons.Folder, Theme.Colors.Green600);
            }

            return All.FirstOrDefault(s => s.Id == id) ?? All[0];
        }

        /// <summary>The UIDs the dashboard counted for a set - the very same ones, never a re-query.</summary>
        internal static UID128[] UIDsOf(string id, DashboardDto dashboard)
        {
            if (dashboard is null) return new UID128[0];

            if (id is object && id.StartsWith(CategoryPrefix)) return Category(id, dashboard)?.ArticleUIDs7d ?? new UID128[0];

            switch (id)
            {
                case Week:        return dashboard.Week7dUIDs        ?? new UID128[0];
                case SummaryOnly: return dashboard.SummaryOnlyUIDs7d ?? new UID128[0];
                default:          return dashboard.Latest24hUIDs     ?? new UID128[0];
            }
        }

        private static CategoryCountDto Category(string id, DashboardDto dashboard)
        {
            var uid = id.Substring(CategoryPrefix.Length);
            return (dashboard?.Categories ?? new CategoryCountDto[0]).FirstOrDefault(c => c.CategoryUID.ToString() == uid);
        }
    }
}
