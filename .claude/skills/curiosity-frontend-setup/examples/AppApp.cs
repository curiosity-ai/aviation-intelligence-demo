// Reference template: the entry point. See ../SKILL.md §2 and §5.

using Mosaik;
using Mosaik.Helpers;
using App.FrontEnd.Chat;
using App.FrontEnd.Dashboard;
using Tesserae;
using TNT;
using static TNT.T;

namespace App.FrontEnd
{
    internal static class AppApp
    {
        /// <summary>
        /// The work-list page. Registered in TWO places, and it has to be both:
        /// <see cref="App.Sidebar"/> for the ways in, <see cref="Router"/> for the URL behind
        /// them — a sidebar entry whose route is not registered navigates to nothing.
        /// </summary>
        internal const string InboxRoute = "#/inbox";

        private static void Main()
        {
            Mosaik.Admin.LazyLoad();

            // Renderers are NOT registered here. App.AutoDiscoverViews reflects over the loaded
            // assemblies for non-abstract INodeRenderer implementations with a parameter-less
            // constructor, so every renderer is picked up simply by existing.
            App.Initialize(Configure, OnLoad).FireAndForget();
        }

        /// <summary>Runs before the app is built: settings and sidebar hooks.</summary>
        private static void Configure(App.DefaultSettings settings)
        {
            settings.HomeView              = state => new DashboardView(state);
            settings.AssistantView         = AppChatView.Create;
            settings.DisableOnboardingTips = true;

            // One entry per way of looking at the work, in the same order and under the same
            // names the home page's counter cards use — the cards and these are two ways to the
            // same lists, and a reader who clicks "Urgent" on the home page has to find
            // "Urgent" in the sidebar afterwards.
            App.Sidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
            {
                if (mode != App.Sidebar.Mode.Default) return;

                sidebar.AddContent(new SidebarSeparator("APP_WORK", "Work".t()));

                foreach (var segment in WorkLists.Segments)
                {
                    var captured = segment;

                    var item = new SidebarButton("APP_" + captured.Id.ToUpperInvariant(),
                                                 captured.Icon, captured.Label.t())
                        .OnClick(() => Router.Navigate(InboxView.RouteFor(captured.Id)));

                    // Per SEGMENT, not per route: testing whether the hash contains "#/inbox"
                    // would light all of them at once, since every one is the same page with a
                    // different query.
                    refresh.Add(() => item.IsSelected = InboxView.IsShowing(captured.Id));

                    sidebar.AddContent(item);
                }
            };
        }

        /// <summary>Runs after the app is built: route registration.</summary>
        private static void OnLoad()
        {
            Router.Register(InboxRoute, state => App.ShowDefault(new InboxView(state)));
        }
    }
}
