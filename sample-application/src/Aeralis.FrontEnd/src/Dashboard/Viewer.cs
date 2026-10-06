using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Aeralis.FrontEnd.Api;

namespace Aeralis.FrontEnd.Dashboard
{
    /// <summary>
    /// Whose desk the demo is currently looking at.
    ///
    /// The demo has no real login, so every view that shows "my" work has to be told whose.
    /// That used to be a field on the home page, which was fine while the home page was the
    /// only such view; the sidebar's Inbox is a second one, and a second copy of the choice
    /// would let the two disagree about who "I" am — the home page counting one engineer's
    /// urgent work orders and the inbox listing another's.
    ///
    /// So the choice lives here, once, and both read it. The engineer list is fetched once
    /// per session and kept: it is a dozen rows that do not change while the tab is open, and
    /// the user picker, the greeting and the inbox all want it.
    /// </summary>
    internal static class Viewer
    {
        private static UserDto[] _users;
        private static UserDto _current;

        /// <summary>Fires after <see cref="Set"/>, so a view already on screen can rebuild itself.</summary>
        public static System.Action<UserDto> Changed;

        /// <summary>The engineer currently being viewed as, or null before the first load.</summary>
        public static UserDto Current => _current;

        /// <summary>
        /// Every engineer the demo can be viewed as, loaded once. Callers that only need the
        /// list — the picker modal — take it from here rather than calling the endpoint again.
        /// </summary>
        public static async Task<UserDto[]> UsersAsync()
        {
            if (_users is object) return _users;

            var response = await Api.Api.UsersAsync();
            _users = response?.Users ?? new UserDto[0];

            return _users;
        }

        /// <summary>
        /// The engineer to render as, choosing the first one on first use. Every view starts
        /// with this rather than assuming somebody else has already chosen.
        /// </summary>
        public static async Task<UserDto> EnsureAsync()
        {
            if (_current is object) return _current;

            var users = await UsersAsync();
            if (users.Length > 0) _current = users.FirstOrDefault(u => u.Uid == CurrentUser.UID);

            return _current;
        }

        /// <summary>Switches whose desk is being looked at, and tells whoever is listening.</summary>
        public static void Set(UserDto user)
        {
            if (user is null) return;

            _current = user;
            Changed?.Invoke(user);
        }
    }
}
