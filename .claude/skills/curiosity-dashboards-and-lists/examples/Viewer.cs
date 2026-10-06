// Reference template: "who am I rendering as". See curiosity-frontend-setup §6.
//
// It starts as a field on the home page, and that is fine right up until a SECOND view shows
// "my" work — at which point the two can disagree about who "I" am. Extract it the first time,
// not the second.

using System;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Tesserae;
using App.FrontEnd.Api;
using TNT;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace App.FrontEnd.Dashboard
{
    internal static class Viewer
    {
        private static UserDto   _current;
        private static UserDto[] _users;

        /// <summary>Raised when the viewer changes; every view showing "my" work re-renders.</summary>
        public static event Action Changed;

        /// <summary>The current engineer, choosing the first on first use.</summary>
        public static async Task<UserDto> EnsureAsync()
        {
            if (_current is object) return _current;

            var users = await UsersAsync();
            _current  = users.Length > 0 ? users[0] : null;

            return _current;
        }

        /// <summary>The list, fetched once per session.</summary>
        public static async Task<UserDto[]> UsersAsync()
            => _users ?? (_users = (await Api.UsersAsync())?.Users ?? new UserDto[0]);

        public static void Set(UserDto user)
        {
            _current = user;
            Changed?.Invoke();
        }

        /// <summary>
        /// A searchable list in a modal rather than a Dropdown, because engineers differ in
        /// ways worth showing — site, department, role, specialities — and the search box has
        /// to match all of them, so typing a category code finds the people who cover it.
        ///
        /// <para>The endpoint returns each dimension TWICE: as UIDs, which is what renders (so
        /// a speciality opens its own page), and as text, which is what IsMatch reads. Nobody
        /// types a UID.</para>
        ///
        /// <para>If you do reach for a Dropdown somewhere: hand it ALL of its items in ONE
        /// Items(...) call. That call CLEARS what is already there before adding, so calling it
        /// once per item inside a loop leaves a dropdown holding only the last one.</para>
        /// </summary>
        public static void ShowPicker()
        {
            Defer(async () =>
            {
                var users = await UsersAsync();
                var list  = SearchableList<Row>();

                foreach (var user in users) list.Add(new Row(user));

                Modal("Render as".t()).SetContent(list.S()).Show();

                return Nothing();
            });
        }

        private sealed class Row : ISearchableItem
        {
            private readonly UserDto _user;

            public Row(UserDto user) { _user = user; }

            /// <summary>Matched against the TEXT the endpoint sent, never against a UID.</summary>
            public bool IsMatch(string term)
                => Contains(_user.DisplayName, term)
                || Contains(_user.SiteLabel, term)
                || Array.Exists(_user.SpecialityLabels ?? new string[0], s => Contains(s, term));

            public IComponent Render()
                => Card(VStack().Children(
                        TextBlock(_user.DisplayName).SemiBold(),
                        // Rendered from UIDs, so each speciality is a link to its own page.
                        new NeighborsLinks(_user.SpecialityUIDs, wrap: true, maxItemsToShow: 6)))
                    .Cursor("pointer")
                    .OnClick(() => { Set(_user); Modal.HideAll(); });

            private static bool Contains(string value, string term)
                => value is object && value.ToLowerInvariant().Contains(term.ToLowerInvariant());
        }
    }
}
