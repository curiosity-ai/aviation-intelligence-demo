using System;
using System.Linq;
using System.Threading.Tasks;
using AviationIntel.FrontEnd.Components;
using Mosaik;
using Mosaik.Components;
using Mosaik.Helpers;
using Mosaik.Schema;
using Tesserae;
using Transpose.Core;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using API = Mosaik.API;

namespace AviationIntel.FrontEnd.Dashboard
{
    /// <summary>
    /// The analyst's desk notes: collaboratively edited markdown documents (<c>_EditingDocument</c>s in the "Desk notes" category)
    /// that sit beside the daily brief and the weekly report - the editor's commentary an analyst adds before the report is issued.
    /// Everyone who opens one sees the others' carets; the Aviation Market Intelligence assistant, docked beside the editor, drafts
    /// into the very same document through the news desk's <c>doc</c> command while the analyst keeps typing, and both edits merge.
    /// </summary>
    /// <remarks>
    /// A desk note is an addition to a report, never a copy of it: the report stays the JSON the two templates render, and the note
    /// carries only commentary that cites the articles it draws on. #/desknotes lists them, #/desknotes?doc=&lt;uid&gt; edits one.
    /// </remarks>
    internal sealed class DeskNotesView : IComponent
    {
        internal const string Route = "#/desknotes";

        private const string CATEGORY = "Desk notes";

        private readonly Stack _root = VStack().S();

        public DeskNotesView(Parameters state)
        {
            _root.Add(state.TryGetValue(RouteStateKeys.Document, out var doc) && UID128.TryParse(doc, out var documentUID)
                ? EditorPage(documentUID)
                : ListPage());
        }

        public dom.HTMLElement Render() => _root.Render();

        internal static string Open(UID128 documentUID) => Route + "?" + RouteStateKeys.Document + "=" + documentUID;

        internal static bool IsShowing() => (Transpose.Core.dom.window.location.hash ?? "").StartsWith(Route);

        /// <summary>The note on today's daily brief, made (with a heading to start from) the first time it is opened.</summary>
        public static Task OpenDailyAsync()
        {
            var today = DateTime.UtcNow;

            return OpenAsync(
                "Desk note " + today.ToString("yyyy-MM-dd"),
                "# Desk note: " + today.ToString("dddd d MMMM yyyy") + "\n\nCommentary on the daily brief, last 24 hours to " + today.ToString("yyyy-MM-dd") + " (UTC).\n\n");
        }

        /// <summary>The note on the weekly report of the seven days to today.</summary>
        public static Task OpenWeeklyAsync()
        {
            var today = DateTime.UtcNow;

            return OpenAsync(
                "Desk note, week to " + today.ToString("yyyy-MM-dd"),
                "# Desk note: week to " + today.ToString("d MMMM yyyy") + "\n\nCommentary on the weekly report, seven days to " + today.ToString("yyyy-MM-dd") + " (UTC).\n\n");
        }

        private static async Task OpenAsync(string name, string initialText)
        {
            var category = await CategoryAsync();
            var existing = (await API.Editing.ListDocuments(category)).FirstOrDefault(d => d.Name == name);

            var uid = existing is object
                ? existing.UID
                : (await API.Editing.CreateDocument(new EditingDocumentCreateRequest
                {
                    Name        = name,
                    Language    = "markdown",
                    CategoryUID = category,
                    InitialText = initialText,
                })).UID;

            Router.Navigate(Open(uid));
        }

        // Categories are per user: found by name, made on first use.
        private static async Task<UID128> CategoryAsync()
        {
            var existing = (await API.Editing.ListCategories()).FirstOrDefault(c => c.Name == CATEGORY);

            return existing is object ? existing.UID : (await API.Editing.CreateCategory(new EditingCategoryCreateRequest { Name = CATEGORY })).UID;
        }

        private static IComponent Page(string title, IComponent content)
            => VStack().S().Style(s => s.minHeight = "0").Children(
                HStack().WS().AlignItemsCenter().P(16).PB(8).Children(
                    IconTile(UIcons.FileEdit, Theme.Colors.Blue600).Size(34.px()),
                    VStack().PL(12).Children(
                        TextBlock(title).Large().SemiBold(),
                        TextBlock("The analyst's commentary on a brief or a report: written together, with the assistant, before it is issued".t()).Small().Secondary())),
                content.Grow());

        // ----- the list ---------------------------------------------------------------------------

        private IComponent ListPage()
        {
            var body = VStack().WS().Children(
                TextBlock("A desk note is a document the desk writes together and the assistant can draft into: open one, then ask the assistant beside it for the \"so what\" of the day or the week, with its sources, while you keep typing. The brief and the report themselves are not changed.".t()).Secondary().PB(12),
                HStack().AlignItemsCenter().PB(16).Children(
                    Button("Note on today's brief".t()).Primary().SetIcon(UIcons.FileEdit).OnClickSpinWhile(OpenDailyAsync).MR(8),
                    Button("Note on this week's report".t()).SetIcon(UIcons.CalendarLines).OnClickSpinWhile(OpenWeeklyAsync)),
                Defer(async () =>
                {
                    var documents = await API.Editing.ListDocuments(await CategoryAsync());

                    if (documents.Length == 0) return TextBlock("No desk notes yet. Open one for today or for this week.".t()).Secondary();

                    return VStack().WS().Children(documents.OrderByDescending(d => d.Updated).Select(d =>
                        Button(d.Name).SetIcon(UIcons.FileEdit).OnClick(() => Router.Navigate(Open(d.UID)))));
                }, Skeleton().WS().H(120)).WS());

            return Page("Desk notes".t(), VStack().WS().ScrollY().P(16).Children(body));
        }

        // ----- the editor, the assistant beside it ------------------------------------------------------

        private IComponent EditorPage(UID128 documentUID)
        {
            var config = new CollaborativeEditorConfiguration
            {
                // Markdown previews as a page by default. Side by side only where the window has room for it beside the assistant.
                PreviewDebounce = TimeSpan.FromMilliseconds(300),
                InitialMode     = Transpose.Core.dom.window.innerWidth >= 1800 ? CollaborativeEditorMode.SideBySide : CollaborativeEditorMode.Edit,
                ShowHistory     = true,
            };

            var editor = new CollaborativeEditor(documentUID, config).S();
            var back   = Button("All desk notes".t()).SetIcon(UIcons.AngleLeft).OnClick(() => Router.Navigate(Route));

            var assistant = new EmbeddedAssistant(new EmbeddedAssistantOptions
            {
                PageKey        = "desknote",
                PageTitle      = "Desk note",
                WelcomeTitle   = "What should this desk note say?".t(),
                Placeholder    = "Ask the assistant to draft, tighten or check the desk note".t(),
                Docked         = true,
                Context        = Task.FromResult(new AssistantContext
                {
                    Key         = documentUID.ToString(),
                    Label       = "the desk note".t(),
                    Description = string.Format("a shared desk note document (uid {0}) the analyst has open next to you and is editing now. Work in THIS document with the news desk's `doc` command (doc cat {0} to read it, doc append / doc replace to change it): your edits appear live under your name and merge with what the analyst types. The note is commentary on the brief or report named in its heading; it must carry only what the articles say, each point with a link to the article it comes from, found with the `news` and `article` commands. Never invent an article, a figure or a source.", documentUID),
                }),
                WelcomePrompts = new[]
                {
                    "Draft the \"so what\" of today's news for the desk note, with a source for every point.".t(),
                    "Add a short paragraph on what regulators announced this week, citing the articles.".t(),
                    "Tighten this note to five lines and keep my edits.".t(),
                },
            });

            IComponent body = Transpose.Core.dom.window.innerWidth >= 1100
                ? (IComponent)Grid(1.fr(), 440.px()).S().Gap(16.px()).Children(editor, VStack().WS().Children(assistant))
                : VStack().S().Children(editor.Grow(), assistant.MT(12));

            return Page("Desk notes".t(), VStack().S().P(16).PT(0).Style(s => s.minHeight = "0").Children(HStack().PB(8).Children(back), body.Grow()));
        }
    }
}
