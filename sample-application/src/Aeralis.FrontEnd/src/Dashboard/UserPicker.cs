using System;
using System.Linq;
using Tesserae;
using Aeralis.FrontEnd.Api;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace Aeralis.FrontEnd.Dashboard
{
    /// <summary>
    /// Picks whose desk the demo is looking at.
    ///
    /// This was a <see cref="Dropdown"/>, and it showed exactly one engineer — the last one.
    /// <c>Dropdown.Items(params Item[])</c> <em>clears</em> whatever is already there before
    /// adding, so calling it once per user inside a loop left the list holding only the final call's item.
    ///
    /// It is a searchable modal now, and not only to dodge that. The choice is "which of these
    /// engineers am I", and a name on its own does not answer it: engineers in this dataset
    /// differ by the ATA chapters they specialise in, which is exactly what decides whose queue
    /// a work order lands in. So each row carries the same facts the profile card shows — site,
    /// department, role, specialities — and the search box matches on all of them, so "53" finds
    /// the structures specialists and "Neuhafen" finds the site.
    /// </summary>
    internal static class UserPicker
    {
        /// <summary>The control that opens it, saying who is currently selected.</summary>
        public static IComponent Button(UserDto current)
        {
            return Tesserae.UI.Button(current?.DisplayName ?? current?.UserName ?? "Select engineer".t())
                .SetIcon(UIcons.UsersAlt)
                .Small()
                .Tooltip("This demo has no login — pick whose desk to look at".t())
                .OnClick((b, _) => Show());
        }

        public static void Show()
        {
            var body = VStack().WS().Children(
                TextBlock("This demo has no login. Pick the engineer the workspace should render as — their assignments, their inbox and their performance."
                    .t()).Small().Secondary().PB(8));

            var modal = Modal(TextBlock("View as".t()).SemiBold().Large())
                .Width(920.px())
                .LightDismiss()
                .ShowCloseButton();

            modal.Content = body;

            body.Add(Defer(async () =>
            {
                var users = await Viewer.UsersAsync();

                if (users.Length == 0)
                    return TextBlock("No engineers are loaded in this workspace.".t()).Secondary() ;

                var currentUID = Viewer.Current?.Uid ?? UID128.Empty;

                var rows = users.Select(user =>
                {
                    var captured = user;

                    return new Row(captured, captured.Uid == currentUID, () =>
                    {
                        Viewer.Set(captured);
                        modal.Hide();
                    });
                }).ToArray();

                return VStack().WS().Children(
                    HeaderRow(),
                    SearchableList(rows)
                        .WithNoResultsMessage(() => TextBlock("No engineer matches.".t()).Secondary().P(16))
                        .HideSearchBoxIfLessThan(6)
                        .Height(420.px())) ;
            }).WS());

            modal.Show();
        }

        // The column widths the header and every row share. Two constants rather than a table
        // component because the rows are cards a SearchableList filters, not table cells: the
        // grid has to be described by whoever draws a row, so it is described once, here.
        private static readonly UnitSize NameWidth = 220.px();

        private static readonly UnitSize FieldWidth = 150.px();

        private static IComponent HeaderRow()
        {
            return HStack().WS().AlignItemsCenter().PL(8).PR(8).PB(4).Children(
                Label("Engineer".t(), NameWidth),
                Label("Site".t(), FieldWidth),
                Label("Department".t(), FieldWidth),
                Label("Role".t(), FieldWidth),
                Label("ATA specialities".t(), FieldWidth));
        }

        private static IComponent Label(string text, UnitSize width)
            => TextBlock(text).Tiny().SemiBold().Secondary().W(width);

        /// <summary>
        /// One engineer as the list reads them. The row owns both halves of what
        /// <see cref="ISearchableItem"/> asks for: what counts as a match, and how it draws.
        /// </summary>
        private sealed class Row : ISearchableItem
        {
            private readonly bool _isCurrent;
            private readonly Action _select;
            private readonly string _haystack;

            public Row(UserDto user, bool isCurrent, Action select)
            {
                User = user;
                _isCurrent = isCurrent;
                _select = select;

                // Built once rather than per keystroke: SearchableList calls IsMatch for every
                // row on every term of every query, and this list is re-filtered as the user types.
                _haystack = string.Join(" ", new[]
                {
                    user.DisplayName, user.UserName, user.SiteText,
                    user.DepartmentText, user.RoleText, user.AtaSpecialityText,
                }).ToLowerInvariant();
            }

            public UserDto User { get; }

            private string Name => User.DisplayName ?? User.UserName ?? "";

            /// <summary>
            /// Matches on everything the row shows, not only the name — the site, the department,
            /// the role and the ATA chapters. Picking an engineer is usually "who covers 53",
            /// and that question has no name in it.
            /// </summary>
            public bool IsMatch(string searchTerm)
                => string.IsNullOrEmpty(searchTerm)
                   || _haystack.Contains(searchTerm.ToLowerInvariant());

            public IComponent Render()
            {
                var name = VStack().W(NameWidth).Children(
                    HStack().AlignItemsCenter().Children(
                        TextBlock(Name).SemiBold(),
                        _isCurrent ? Badge("viewing".t()).PL(6)  : Empty()),
                    TextBlock(User.UserName ?? "").Tiny().Secondary());

                var row = HStack().WS().AlignItemsCenter().P(8).Children(
                    name,
                    Links(User.SiteUIDs),
                    Links(User.DepartmentUIDs),
                    Links(User.RoleUIDs),
                    Links(User.AtaSpecialityUIDs));

                // The whole row is the target: picking an engineer should not need aim.
                return Card(row).WS().MB(4)
                    .Style(s => s.cursor = "pointer")
                    .OnClick(() => _select());
            }

            /// <summary>
            /// The neighbours themselves rather than their names: a speciality is an ATA chapter,
            /// so rendering it as a link lets the reader open the chapter from the picker instead
            /// of memorising a number. The text the search box matches on is a separate field —
            /// these are for reading, that one is for typing.
            /// </summary>
            private static IComponent Links(UID128[] uids)
                => NeighborsLinks(uids ?? new UID128[0], wrap: true, maxItemsToShow: 3,
                                  showIfNone: () => TextBlock("—".t()).Secondary()).W(FieldWidth);
        }
    }
}
