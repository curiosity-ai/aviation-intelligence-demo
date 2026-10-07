using AviationIntel.FrontEnd.Components;
using AviationIntel.FrontEnd.Dashboard;
using Mosaik;
using Tesserae;
using static TNT.T;

namespace AviationIntel.FrontEnd
{
    internal static class AviationIntelApp
    {
        private static void Main()
        {
            Mosaik.Admin.LazyLoad();

            // Renderers are not registered here: every non-abstract INodeRenderer with a
            // parameterless constructor is discovered by reflection.
            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        private static void Configure(AppSettings settings)
        {
            settings.HomeView              = state => new HomeView(state);
            settings.AssistantView         = AviationChatView.Create;
            settings.DisableOnboardingTips = true;

            // The news desk's own pages, directly under the workspace's header entries: the sets
            // the home page counts, and the feed directory. Each entry is selected for its own
            // set, not for the route they share.
            AppSidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode != AppSidebar.Mode.Default) return;

                sidebar.AddContent(new SidebarSeparator("AVIATION_NEWS", "News desk".t()));

                foreach (var set in NewsSets.All)
                {
                    var captured = set;
                    var item     = new SidebarButton("AVIATION_" + captured.Id.ToUpperInvariant(), captured.Icon, captured.Label.t())
                        .OnClick(() => Router.Navigate(NewsView.RouteFor(captured.Id)));

                    refresh.Add(() => item.IsSelected = NewsView.IsShowing(captured.Id));
                    sidebar.AddContent(item);
                }

                var feeds = new SidebarButton("AVIATION_FEEDS", UIcons.RssAlt, "Monitored feeds".t())
                    .OnClick(() => Router.Navigate(FeedsView.Route));

                refresh.Add(() => feeds.IsSelected = (Transpose.Core.dom.window.location.hash ?? "").StartsWith(FeedsView.Route));
                sidebar.AddContent(feeds);
            };
        }

        private static void OnLoad()
        {
            Router.Register(NewsView.Route,  state => App.ShowDefault(new NewsView(state)));
            Router.Register(FeedsView.Route, state => App.ShowDefault(new FeedsView(state)));
        }
    }
}
