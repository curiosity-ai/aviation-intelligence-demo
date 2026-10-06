// Reference template: the shared renderer base. See ../SKILL.md §2, §3, §6–§9.
//
// One abstract base carries everything common — the card, the footer, the icon tile, the tab
// set, property-as-relationship rendering, the modal size — so a concrete renderer states only
// what is different about its type and is typically ten lines.
//
// It MUST be abstract: auto-discovery instantiates every non-abstract INodeRenderer it finds,
// and would try to register this one for a node type that does not exist.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Components.Nodes;
using Mosaik.Helpers;
using Mosaik.Schema;
using Tesserae;
using App.FrontEnd.Schema;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;
// Aliased rather than imported: Mosaik.Components.NodeRendering also declares a
// NeighborsLinks, and a plain using of that namespace makes every use of the one this file
// actually wants ambiguous (CS0104).
using NodeRendererDefinitionPreview = Mosaik.Components.NodeRendering.NodeRendererDefinitionPreview;

namespace App.FrontEnd.Renderers
{
    public abstract class AppRendererBase : NodeRendererBase
    {
        /// <summary>Past this a value is a paragraph, not a label, and gets its own box.</summary>
        private const int LONG_VALUE_THRESHOLD  = 200;
        private const int LONG_VALUE_MAX_HEIGHT = 160;

        /// <summary>
        /// A footer entry is read at a glance, so it is a code or a vocabulary term. Anything
        /// longer is prose that wandered into the wrong list, and is dropped rather than
        /// allowed to push the card's real footer off the row.
        /// </summary>
        private const int MAX_FOOTER_VALUE_LENGTH = 48;

        protected AppRendererBase(SchemaStyleInfo style) : this(style, null) { }

        /// <param name="bodyContentProperty">
        /// The ONE property drawn as the card body — the abstract, for every type that has one.
        /// Null leaves the body empty, which is right for reference types: a part number has a
        /// description, not a summary.
        /// </param>
        /// <param name="footerProperties">What the footer says, in reading order.</param>
        protected AppRendererBase(SchemaStyleInfo style, string bodyContentProperty, params string[] footerProperties)
            : base(style)
        {
            BodyContentProperty = bodyContentProperty;
            FooterProperties    = footerProperties ?? new string[0];
            ShortDisplayName    = DisplayName;
        }

        protected string   BodyContentProperty { get; }
        protected string[] FooterProperties    { get; }

        /// <summary>
        /// What the card's icon tile says, in place of <see cref="NodeRendererBase.DisplayName"/>.
        /// The tile is a ~34px square that CLIPS what does not fit and does not auto-size its
        /// text, so a type whose name is "Aircraft Model" has to say something shorter there
        /// than it says in a facet list or a modal header.
        ///
        /// <para>Up to five characters, four preferred; at most two words separated by '\n',
        /// each under the same limit. The newline is a wrap opportunity — it says where the
        /// break is meant to fall. Use established abbreviations ("A/C", "DWG").</para>
        ///
        /// <para>Defaults to DisplayName, which is right for every type already known by its
        /// abbreviation. Set it in the constructor body where it is not.</para>
        /// </summary>
        protected string ShortDisplayName { get; set; }

        /// <summary>
        /// Override where ONE node type holds things a reader has to tell apart — a category
        /// node that is a level-2, level-4 or level-6 entry, where a tile reading "CAT" leaves
        /// all three identical in a mixed list. Called once per card: read properties, do not
        /// query.
        /// </summary>
        protected virtual string ShortDisplayNameFor(Node node) => ShortDisplayName;

        /// <summary>Two lines of five characters do not fit at the size the tile picks for one.</summary>
        private static TextSize? TileTextSize(string tileText)
            => (tileText is object && tileText.Contains("\n")) ? TextSize.Tiny : (TextSize?)null;

        /// <summary>The node types this type's 360 view surfaces. Everything, by default.</summary>
        protected virtual string[] Related360Types => N.All360Types;

        protected virtual string View360Label => "360 View";

        /// <summary>
        /// Whether this type has a published file to offer a tab for. Read off the GENERATED
        /// N.DocumentTypes rather than a hand-kept list, so a record type the connector grows
        /// arrives with its tab already on.
        /// </summary>
        protected virtual bool HasPublishedFile => Array.IndexOf(N.DocumentTypes, NodeType) >= 0;

        /// <summary>
        /// The base opens the preview at 80×80, which is right for a handful of properties.
        /// These nodes open onto a tab strip with a graph, a faceted search and sometimes a
        /// split screen inside a tab, none of which is usable at that size.
        /// </summary>
        protected virtual UnitSize ModalWidth  => 92.vw();
        protected virtual UnitSize ModalHeight => 90.vh();

        // -----------------------------------------------------------------------------------
        // the card
        // -----------------------------------------------------------------------------------

        public override OmniResult<Node> CompactView(Node node)
            => WithFooter(WithTileIcon(NodeResult.For(this, node), node).SetContent(CreateCompactViewFor(node)), node);

        /// <summary>
        /// Puts the type's lettered tile on the card AND on the modal it opens. The modal used
        /// to take whatever NodeResult.For left there, so the one place the reader is looking
        /// hardest at a single record was the one place its mark was missing.
        /// </summary>
        private OmniResult<Node> WithTileIcon(OmniResult<Node> result, Node node)
        {
            var tileText = ShortDisplayNameFor(node);
            return result.SetIcon(tileText, Color, TileTextSize(tileText));
        }

        /// <summary>
        /// The base draws EVERY property as a stack of labelled rows — thirty rows of a rich
        /// record inside one row of a result list, with the one thing the reader wants
        /// somewhere in the middle. So: one property, or nothing. SetContent(null) collapses
        /// the row rather than leaving a gap.
        /// </summary>
        protected virtual IComponent CreateCompactViewFor(Node node)
        {
            if (string.IsNullOrEmpty(BodyContentProperty)) return null;

            var value = GetValueForProperty(node, BodyContentProperty);

            return value is null ? null : TextBlock(value).Tiny().Secondary();
        }

        /// <summary>
        /// The two or three facts that place a record without opening it.
        ///
        /// These are read from the node's own PROPERTIES rather than walked as edges, and
        /// deliberately so: a result list draws dozens of cards at once and a footer that
        /// queried the graph would be dozens of round-trips deep before the first row appeared.
        /// The properties named are the same values the edges were built from, so the footer
        /// says what the graph says, synchronously.
        ///
        /// NodeResult.For has already put the timestamp here via SetFooterEntries, which
        /// CLEARS — so these are APPENDED after it. Never reach for the setter here.
        /// </summary>
        protected OmniResult<Node> WithFooter(OmniResult<Node> result, Node node)
        {
            foreach (var propertyName in FooterProperties)
            {
                var value = GetValueForProperty(node, propertyName);
                if (value is null || value.Length > MAX_FOOTER_VALUE_LENGTH) continue;

                // Unlabelled is the point at a glance; the tooltip is for the one reader in ten
                // who has to ask which of two similar codes they are looking at.
                result.AddFooterEntry(InlineLabel(value).Tooltip(propertyName.InsertSpaceBeforeUpperCase()));
            }

            return result;
        }

        public override async Task<OmniResult<Node>> PreviewAsync(Node node, Parameters state)
        {
            // Keep the base's branch: a workspace-configured renderer still wins.
            var custom = GetCustomRendererIfAny();
            if (custom is object)
                return await NodeRendererDefinitionPreview.WithoutRenderedItemTracking.PreviewAsync(custom, node, state);

            IReadOnlyList<RelationshipGroup> groups = BuildRelationshipGroups(await LoadSummaryAsync(node));

            return WithFooter(WithTileIcon(NodeResult.For(this, node), node), node)
                .SetModalContent(new NodeView(this, node, state, groups))
                .ModalSize(ModalWidth, ModalHeight);
        }

        // -----------------------------------------------------------------------------------
        // the tabs
        // -----------------------------------------------------------------------------------

        protected override IEnumerable<NodeTab> BuildTabs(Node node, IReadOnlyList<RelationshipGroup> groups, NodeView nodeView)
        {
            yield return new NodeTab
            {
                Id = "properties", Label = "Overview".t(),
                TabTitle   = SegmentTitle("Overview".t()),
                TabContent = () => RenderPropertiesSegment(nodeView.Node),
            };

            if (HasPublishedFile) yield return PublishedFileTab(nodeView.Node.UID);

            yield return new NodeTab
            {
                Id = "graph", Label = "Graph".t(), Scrolls = false,
                TabTitle   = SegmentTitle("Graph".t()),
                // Reuse the base's segment: it already handles the mount-then-load-then-fit
                // sequence. Do not build a graph explorer by hand inside a Defer.
                TabContent = () => RenderGraphSegment(nodeView.Node, nodeView.OpenGraphNodePeek),
            };

            yield return new NodeTab
            {
                Id = "view360", Label = View360Label.t(), Scrolls = false,
                TabTitle   = SegmentTitle(View360Label.t()),
                TabContent = () => Components.View360.For(nodeView.Node, Related360Types),
            };
        }

        // -----------------------------------------------------------------------------------
        // properties — a reference renders as the thing it references
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The base renders each value as text, which is right when a value is a value. Most of
        /// these are not: a category code is a category, a status is a status node, keywords
        /// are keywords. As text they are dead ends — you can read "53" and not ask what else
        /// is in it. <see cref="PropertyLinks"/> says which are which.
        ///
        /// The property itself is untouched on the node: it is what the search index reads, and
        /// it is what this falls back to when the edge is missing, so a row never goes blank
        /// because a link failed to resolve.
        /// </summary>
        protected override IComponent RenderProperties(Node node)
        {
            var grid = DetailsGrid().WS().LabelWidth(160.px());

            foreach (var propertyName in GetDisplayPropertyNames(node))
                grid.Row(propertyName.InsertSpaceBeforeUpperCase(), PropertyCell(node, propertyName));

            return grid;
        }

        private IComponent PropertyCell(Node node, string propertyName)
        {
            var link  = PropertyLinks.For(NodeType, propertyName);
            var value = GetValueForProperty(node, propertyName);

            if (link is object)
                return new NeighborsLinks(node.UID, link.NodeTypes, link.EdgeType,
                                          wrap: true, maxItemsToShow: 12,
                                          showIfNone: () => PlainValue(value));

            // Same split the base makes: a paragraph is too big for a row of labels.
            if (value is object && (value.Length > LONG_VALUE_THRESHOLD || value.Split('\n').Length > 4))
                return VStack().WS().MaxHeight(LONG_VALUE_MAX_HEIGHT.px()).ScrollY()
                    .Style(s => s.setProperty("overflow-wrap", "anywhere"))
                    .Children(TextBlock(value, selectable: true));

            return PlainValue(value);
        }

        private static IComponent PlainValue(string value) => InlineLabel(value).NoMark();

        // -----------------------------------------------------------------------------------
        // the published file
        // -----------------------------------------------------------------------------------

        protected NodeTab PublishedFileTab(UID128 nodeUID) => new NodeTab
        {
            Id = "publishedfile", Label = "Consult".t(), Scrolls = false,
            TabTitle   = SegmentTitle("Consult".t()),
            TabContent = () => Defer(async () => await AttachedFileBodyAsync(nodeUID) ?? NoPublishedFile()).S(),
        };

        /// <summary>
        /// The workspace's OWN file view for whatever file hangs off a node — the real PDF,
        /// scrollable, searchable and zoomable, not a picture of its first pages. Rather than
        /// re-implementing a viewer, the registered file renderer is asked for the body it
        /// would put in a preview sheet, and that body is placed in the tab.
        ///
        /// <para>The three flags are what make it a BODY rather than a sheet: no header and no
        /// footer, because the card already says what this is and the tab is not a modal; no
        /// hotkeys, because the PDF's find shortcut must not take it from the page it is
        /// embedded in. Footer off is also what puts Download in the viewer's own toolbar.</para>
        ///
        /// <para>Because the connector attaches per revision AND hangs the current one off the
        /// record, this same walk gives a revision the file of that revision and a record the
        /// file of its latest — without either renderer knowing which it is looking at.</para>
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
            => TextBlock("No published file is attached to this record.".t()).Secondary();
    }
}
