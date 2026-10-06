using Mosaik;
using Mosaik.Helpers;
using Aeralis.FrontEnd.Chat;
using Aeralis.FrontEnd.Dashboard;
using Tesserae;
using TNT;
using static TNT.T;

namespace Aeralis.FrontEnd
{
    internal static class AeralisApp
    {
        /// <summary>
        /// The engineer's work orders, as a page of its own. Registered in two places, and it
        /// has to be both: <see cref="App.Sidebar"/> for the ways in,
        /// <see cref="Router"/> for the URL behind them — a sidebar entry whose route is not
        /// registered navigates to nothing.
        /// </summary>
        internal const string InboxRoute = "#/inbox";

        private static void Main()
        {
            Mosaik.Admin.LazyLoad();

            // Renderers are not registered here. App.AutoDiscoverViews reflects over the
            // loaded assemblies for non-abstract INodeRenderer implementations with a
            // parameter-less constructor, so every renderer in
            // Aeralis.FrontEnd.Renderers is picked up simply by existing.
            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        private static void Configure(App.DefaultSettings settings)
        {
            settings.HomeView = state => new DashboardView(state);

            // Replaces the stock chat page with the pre-analysis one: a single pinned assistant
            // and a pinned capability set, instead of two pickers over choices the engineer has
            // no reason to make. See PreAnalysisChatView.
            settings.AssistantView = PreAnalysisChatView.Create;

            settings.DisableOnboardingTips = true;

            // One entry per way of looking at the work orders, in the same order and under the
            // same names the home page's counter cards use — the cards and these are two ways
            // of reaching the same five lists, and a reader who clicks "Urgent actions" on the
            // home page has to find "Urgent actions" in the sidebar afterwards.
            //
            // They sit directly under the workspace's own header entries, because this is what
            // the demo's user opens first and returns to between work orders.
            App.Sidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode == App.Sidebar.Mode.Default)
                {
                    sidebar.AddContent(new SidebarSeparator("AERALIS_WORK_ORDERS", "Work orders".t()));

                    foreach (var segment in WorkOrderLists.Segments)
                    {
                        var captured = segment;

                        var item = new SidebarButton("AERALIS_" + captured.Id.ToUpperInvariant(),
                                                     captured.Icon, captured.Label.t())
                            .OnClick(() => Router.Navigate(InboxView.RouteFor(captured.Id)));

                        // Per segment, not per route: `Contains("#/inbox")` would light all five at
                        // once, since every one of them is the same page with a different query.
                        refresh.Add(() => item.IsSelected = InboxView.IsShowing(captured.Id));

                        sidebar.AddContent(item);
                    }
                }
            };
        }

        private static void OnLoad()
        {
            Router.Register(InboxRoute, state => App.ShowDefault(new InboxView(state)));
        }
    }
}
