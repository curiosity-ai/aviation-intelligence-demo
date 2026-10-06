using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Components.Nodes;
using Mosaik.Helpers;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Schema;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;
// Aliased rather than imported: `Mosaik.Components.NodeRendering` also declares a
// `NeighborsLinks`, and a plain using of that namespace makes every use of the one this
// file actually wants ambiguous.
using NodeRendererDefinitionPreview = Mosaik.Components.NodeRendering.NodeRendererDefinitionPreview;

namespace Aeralis.FrontEnd.Renderers
{
    /// <summary>
    /// The shared shape for every Aeralis node type: the card a node is drawn as in a
    /// result list, the modal it opens into, then the properties overview, the graph
    /// around it, a 360 view and whatever relationship tabs it has.
    ///
    /// Derives from <see cref="NodeRendererBase"/> so a subclass only states what is
    /// different about its type — the node type name, what its card says, and how broadly
    /// its 360 view should look.
    /// </summary>
    public abstract class AeralisRendererBase : NodeRendererBase
    {
        /// <summary>Past this a value is a paragraph, not a label, and gets its own box.</summary>
        private const int LONG_VALUE_THRESHOLD = 200;

        private const int LONG_VALUE_MAX_HEIGHT = 160;

        /// <summary>
        /// A footer entry is read at a glance, so it is a code or a vocabulary term.
        /// Anything longer than this is prose that wandered into the wrong list, and is
        /// dropped rather than allowed to push the card's real footer off the row.
        /// </summary>
        private const int MAX_FOOTER_VALUE_LENGTH = 48;

        private static readonly string[] NO_PROPERTIES = new string[0];

        protected AeralisRendererBase(SchemaStyleInfo style) : this(style, null) { }

        /// <param name="bodyContentProperty">
        /// The one property drawn as the body of the card — the abstract, for every type
        /// that has one. Null leaves the card body empty, which is right for the reference
        /// types: a part number has a description, not a summary, and it is already in the
        /// footer or the title.
        /// </param>
        /// <param name="footerProperties">
        /// What the footer says, in reading order. See <see cref="WithFooter"/>.
        /// </param>
        protected AeralisRendererBase(SchemaStyleInfo style,
                                       string bodyContentProperty,
                                       params string[] footerProperties)
            : base(style)
        {
            BodyContentProperty = bodyContentProperty;
            FooterProperties = footerProperties ?? NO_PROPERTIES;
            ShortDisplayName = DisplayName;
        }

        /// <summary>The node property drawn as the card body, or null for a bodyless card.</summary>
        protected string BodyContentProperty { get; }

        /// <summary>
        /// What the card's icon tile says, in place of <see cref="NodeRendererBase.DisplayName"/>.
        /// The tile is a 34px square that clips what does not fit, so a type whose name is
        /// "Aircraft Model" has to say something shorter there than it says in a facet list or a
        /// modal header — those have room for the truth, and keep it.
        ///
        /// <para>Up to five characters, four preferred; two words at most, separated by
        /// <c>'\n'</c>, each of them under the same limit. The tile's text is laid out with
        /// ordinary <c>white-space</c>, so the newline is a wrap opportunity rather than a hard
        /// break — it says where the break is meant to fall, and the tile's own flex layout puts
        /// each word on its own line because neither fits beside the other. Established
        /// abbreviations are what a reader expects to see there ("A/C" for aircraft, "DWG" for
        /// drawing), so use them.</para>
        ///
        /// <para>Defaults to <c>DisplayName</c>, which is right for every type already known by
        /// its abbreviation — MOD, SB, AMM, FCOM. Set it in the constructor body where it is
        /// not.</para>
        /// </summary>
        protected string ShortDisplayName { get; set; }

        /// <summary>
        /// What one node's tile says, for a type whose name is not the whole answer. Defaults to
        /// <see cref="ShortDisplayName"/>, which is what every type wants where the type name
        /// says it all.
        ///
        /// <para>Override it where one node type holds things a reader has to tell apart — an
        /// <c>AtaChapter</c> is a 2D chapter, a 4D section or a 6D subject, and a tile reading
        /// "ATA" leaves all three looking identical in a mixed list. Same budget as
        /// <see cref="ShortDisplayName"/>, and it is called once per card, so keep it to reading
        /// properties off the node.</para>
        /// </summary>
        protected virtual string ShortDisplayNameFor(Node node) => ShortDisplayName;

        /// <summary>
        /// Two lines of five characters do not fit a 34px tile at the size it picks for one line
        /// (about 12px), so a two-line name is drawn a step smaller. One-line names keep the
        /// tile's own sizing, which scales with the tile.
        /// </summary>
        private static TextSize? TileTextSize(string tileText) =>
            (tileText is object && tileText.Contains("\n")) ? TextSize.Tiny : (TextSize?)null;

        /// <summary>The node properties drawn in the card footer, in reading order.</summary>
        protected string[] FooterProperties { get; }

        /// <summary>
        /// The node types this type's 360 view will surface. Defaults to everything,
        /// which is right for the document types; a reference type narrows it.
        /// </summary>
        protected virtual string[] Related360Types => N.All360Types;

        /// <summary>The label on the 360 tab. Reference types read better with their own wording.</summary>
        protected virtual string View360Label => "360 View";

        /// <summary>
        /// Whether this type has a published file to offer a tab for. True for the ten document
        /// types, which is what <c>AttachmentsIngest</c> renders a file for — a PDF for nine of
        /// them, the printed manual page for an IPC figure — and, through
        /// <see cref="AeralisIssueRendererBase"/>, for their ten issue types. The reference
        /// types have none: a part number is a row in a catalogue, not a publication.
        ///
        /// <para>Read off the generated <see cref="N.DocumentTypes"/> rather than listed here,
        /// so a document type the connector grows arrives with its tab already on.</para>
        /// </summary>
        protected virtual bool HasPublishedFile => Array.IndexOf(N.DocumentTypes, NodeType) >= 0;

        /// <summary>
        /// How large the preview modal opens. The base is 80×80, which is a preview of a
        /// handful of properties; these nodes open onto a tabbed view with a graph, a
        /// faceted 360 search and — on the work order — a split screen inside each tab,
        /// none of which is usable at that size.
        /// </summary>
        protected virtual UnitSize ModalWidth => 92.vw();

        protected virtual UnitSize ModalHeight => 90.vh();

        // ---------------------------------------------------------------------
        // The card
        // ---------------------------------------------------------------------

        /// <summary>
        /// The card a node is drawn as wherever it appears in a list — search results,
        /// a 360 view, the confirmed-impact list on a work order.
        ///
        /// The base draws every property the node has, as a stack of labelled rows. For
        /// these types that is thirty rows of a MOD dossier in a row of a result list: the
        /// list stops being scannable, and the one thing the reader wants — *what is this
        /// change about* — is somewhere in the middle of it. So the body is the abstract
        /// and nothing else, and everything needed to place the document is in the footer.
        /// </summary>
        public override OmniResult<Node> CompactView(Node node)
        {
            return WithFooter(WithTileIcon(NodeResult.For(this, node), node).SetContent(CreateCompactViewFor(node)), node);
        }

        /// <summary>
        /// Puts the type's lettered tile on a result — the card in a list and the modal it opens
        /// into alike. The modal used to take whatever <see cref="NodeResult.For"/> left there,
        /// so a MOD opened from a card carrying "MOD" into a sheet that did not, and the one
        /// place the reader is looking hardest at a single document was the one place its mark
        /// was missing.
        /// </summary>
        private OmniResult<Node> WithTileIcon(OmniResult<Node> result, Node node)
        {
            var tileText = ShortDisplayNameFor(node);

            return result.SetIcon(tileText, Color, TileTextSize(tileText));
        }

        /// <summary>
        /// The body of the card: <see cref="BodyContentProperty"/> if the node has a value
        /// for it, otherwise nothing at all — <c>SetContent(null)</c> collapses the content
        /// row rather than leaving a gap.
        /// </summary>
        protected virtual IComponent CreateCompactViewFor(Node node)
        {
            if (string.IsNullOrEmpty(BodyContentProperty)) return null;

            var value = GetValueForProperty(node, BodyContentProperty);
            if (value is null) return null;

            return TextBlock(value).Tiny().Secondary();
        }

        /// <summary>
        /// The footer, on both the card and the modal it opens — the two or three facts
        /// that place a document without opening it: where it stands (status), what it is
        /// about (ATA), and which fleet it belongs to (programme, aircraft type).
        ///
        /// These are read from the node's own properties rather than walked as edges, and
        /// deliberately so: a result list draws dozens of these cards at once and a footer
        /// that queried the graph would be dozens of round-trips deep before the first row
        /// appeared. The properties named here are the same values the edges are built
        /// from — <c>Status</c> *is* the classification the card links to in its Overview —
        /// so the footer says the same thing the graph does, synchronously.
        ///
        /// <see cref="NodeResult.For"/> has already put the timestamp in the footer via
        /// <c>SetFooterEntries</c>, which clears whatever was there; these are appended
        /// after it, so the order is date first, then the type's own facts.
        /// </summary>
        protected OmniResult<Node> WithFooter(OmniResult<Node> result, Node node)
        {
            foreach (var propertyName in FooterProperties)
            {
                var value = GetValueForProperty(node, propertyName);
                if (value is null || value.Length > MAX_FOOTER_VALUE_LENGTH) continue;

                // The value alone is unlabelled — "RELEASED", "53-10-00", "C" — which is
                // the point at a glance, and the tooltip is there for the one reader in
                // ten who has to ask which of the two ATA codes they are looking at.
                result.AddFooterEntry(InlineLabel(value).Tooltip(propertyName.InsertSpaceBeforeUpperCase()));
            }

            return result;
        }

        /// <summary>
        /// The modal a card opens into. Identical to the base except for the footer and
        /// the size — see <see cref="ModalWidth"/>.
        /// </summary>
        public override async Task<OmniResult<Node>> PreviewAsync(Node node, Parameters state)
        {
            var custom = GetCustomRendererIfAny();
            if (custom is object)
            {
                return await NodeRendererDefinitionPreview.WithoutRenderedItemTracking.PreviewAsync(custom, node, state);
            }

            IReadOnlyList<RelationshipGroup> groups = BuildRelationshipGroups(await LoadSummaryAsync(node));
            NodeView body = new NodeView(this, node, state, groups);

            return WithFooter(WithTileIcon(NodeResult.For(this, node), node), node)
                .SetModalContent(body)
                .ModalSize(ModalWidth, ModalHeight);
        }

        // ---------------------------------------------------------------------
        // The tabs
        // ---------------------------------------------------------------------

        /// <summary>
        /// The properties overview, with every property that is really a reference drawn as
        /// the thing it refers to.
        ///
        /// The base implementation renders each value as text, which is right when a value
        /// is a value. Most of these are not: <c>Ata2d</c> is a chapter, <c>Trigger</c> is a
        /// classification, <c>Keywords</c> is a list of keywords, <c>SubjectMod</c> is
        /// another document. As text they are dead ends — you can read "53" and not ask what
        /// else is in chapter 53. <see cref="PropertyLinks"/> says which are which, and each
        /// one becomes the neighbours on the far side of its edge.
        ///
        /// The property itself is untouched on the node: it is what the search index reads,
        /// and it is what this falls back to when the edge is missing, so a row never goes
        /// blank because a link failed to resolve.
        /// </summary>
        protected override IComponent RenderProperties(Node node)
        {
            var grid = DetailsGrid().WS().Class("msk-properties-grid").LabelWidth(160.px());

            foreach (var propertyName in GetDisplayPropertyNames(node))
            {
                grid.Row(propertyName.InsertSpaceBeforeUpperCase(), PropertyCell(node, propertyName));
            }

            return grid;
        }

        private IComponent PropertyCell(Node node, string propertyName)
        {
            var link = PropertyLinks.For(NodeType, propertyName);
            var value = GetValueForProperty(node, propertyName);

            if (link is object)
            {
                return new NeighborsLinks(node.UID, link.NodeTypes, link.EdgeType,
                                          wrap: true, maxItemsToShow: 12,
                                          showIfNone: () => PlainValue(value));
            }

            // Same split the base makes: a paragraph is too big to sit on a row of labels.
            if (value is object && (value.Length > LONG_VALUE_THRESHOLD || value.Split('\n').Length > 4))
            {
                return VStack().WS().MaxHeight(LONG_VALUE_MAX_HEIGHT.px()).ScrollY()
                    .Style(s => s.setProperty("overflow-wrap", "anywhere"))
                    .Children(TextBlock(value, selectable: true));
            }

            return PlainValue(value);
        }

        private static IComponent PlainValue(string value) => InlineLabel(value).NoMark();

        protected override IEnumerable<NodeTab> BuildTabs(
            Node node, IReadOnlyList<RelationshipGroup> groups, NodeView nodeView)
        {
            yield return new NodeTab
            {
                Id = "properties",
                Label = "Overview".t(),
                TabTitle = SegmentTitle("Overview".t()),
                TabContent = () => RenderPropertiesSegment(nodeView.Node),
            };

            if (HasPublishedFile) yield return PublishedFileTab(nodeView.Node.UID);

            yield return new NodeTab
            {
                Id = "graph",
                Label = "Graph".t(),
                Scrolls = false,
                TabTitle = SegmentTitle("Graph".t()),
                TabContent = () => RenderGraphSegment(nodeView.Node, nodeView.OpenGraphNodePeek),
            };

            yield return new NodeTab
            {
                Id = "view360",
                Label = View360Label.t(),
                Scrolls = false,
                TabTitle = SegmentTitle(View360Label.t()),
                TabContent = () => Components.View360.For(nodeView.Node, Related360Types),
            };
        }

        // ---------------------------------------------------------------------
        // The published file
        // ---------------------------------------------------------------------

        /// <summary>
        /// The document as it was published, in the workspace's own PDF viewer.
        ///
        /// <para>Which file that is comes from the graph rather than from a choice made here.
        /// <c>AttachmentsIngest</c> hangs each rendered PDF off the issue it is of, and — when
        /// that is the issue the document currently stands at — off the document as well. So the
        /// same walk gives an issue the PDF of that revision and a document the PDF of its latest
        /// one, without either renderer knowing which issue it is looking at.</para>
        /// </summary>
        protected NodeTab PublishedFileTab(UID128 nodeUID)
        {
            return new NodeTab
            {
                Id = "publishedfile",
                Label = "Consult".t(),
                Scrolls = false,
                TabTitle = SegmentTitle("Consult".t()),
                TabContent = () => Defer(async () => await AttachedFileBodyAsync(nodeUID) ?? NoPublishedFile()).S(),
            };
        }

        /// <summary>
        /// The workspace's own file view for whatever <c>_FileEntry</c> hangs off a node — the PDF
        /// itself, scrollable, searchable and zoomable, not a picture of its first pages.
        ///
        /// A dossier is the thing being analysed: the engineer needs to *read* the reason for
        /// change and the effects while deciding what the modification touches, and a thumbnail
        /// fan can only be looked at. So rather than re-implementing a viewer, the workspace's
        /// registered <c>_FileEntry</c> renderer is asked for the body it would put in a preview
        /// sheet, and that body is placed in the card.
        /// <para>
        /// The three flags are what makes it a body rather than a sheet: no header and no footer,
        /// because the card already says what this is and the tab around it is not a modal, and no
        /// hotkeys, because the PDF's Ctrl+F must not take the shortcut from the page it is
        /// embedded in. Turning the footer off is also what puts the download button in the
        /// viewer's own toolbar.
        /// </para>
        /// <c>PreviewAsync</c> sets the modal content, and <c>OmniResult.GetModalContentAsync</c>
        /// is the public way to take it back out; it is the
        /// same call <c>NodeResult.PageFromAsync</c> makes to turn a row into a page.
        /// </summary>
        internal static async Task<IComponent> AttachedFileBodyAsync(UID128 ownerUID)
        {
            var files = await Mosaik.API.Query.StartAt(ownerUID)
                .Out(N._FileEntry.Type, E.HasAttachment)
                .TakeAll()
                .GetUIDsAsync();

            if (files?.UIDs is null || files.UIDs.Length == 0) return null;

            var file = await Mosaik.API.Nodes.GetAsync(files.UIDs[0]);
            if (file is null) return null;

            var row = await new _FileEntryRenderer(showHeader: false, showFooter: false, enableHotkeys: false)
                .PreviewAsync(file, null);

            return row is object ? await row.GetModalContentAsync() : null;
        }

        private static IComponent NoPublishedFile()
            => TextBlock("No published file is attached to this document.".t()).Secondary();
    }
}
