// Reference template: the concrete renderers. See ../SKILL.md §1–§5.
//
// EVERY NODE TYPE NEEDS ONE. A type without a renderer has no LabelField and no icon, and
// shows as a raw UID wherever it is reachable — a neighbour link, a search result, a graph
// node, a property row. That includes the key-only metadata types at the bottom of this file,
// which are five lines each and are the most commonly forgotten thing on the whole project.
//
// Nothing here is registered anywhere: auto-discovery reflects over the assembly for
// non-abstract INodeRenderer implementations WITH A PUBLIC PARAMETER-LESS CONSTRUCTOR.

using Mosaik.Schema;
using App.FrontEnd.Schema;
using Tesserae;
using Node = Mosaik.Schema.Node;

namespace App.FrontEnd.Renderers
{
    /// <summary>
    /// The unit of work. In a real application this type gets an elaborate renderer with its
    /// own tabs instead of the base set — see ../SKILL.md §11.
    /// </summary>
    public sealed class CaseRenderer : AppRendererBase
    {
        public CaseRenderer() : base(
            new SchemaStyleInfo
            {
                Type        = N.Case.Type,
                // The discipline's abbreviation, UPPER CASE, where one exists. The type name is
                // read hundreds of times a session in facets, rows and graph nodes, and nobody
                // asks for "the Pre-Analysis Work Order".
                DisplayName = "CASE",
                Icon        = UIcons.ClipboardList.ToString(),
                // Identifier AND title: the `|` form takes the FIRST field with a value, so
                // Title|DocId would show the title and never the identifier — the wrong way
                // round, because the id alone does not say what the record is and the title
                // alone does not say which of four similar ones it is.
                LabelField  = LabelFields.IdAndTitle(N.Case.DocId, N.Case.Title),
            },
            N.Case.Abstract,                                                  // card body
            N.Case.WorkflowStatus, N.Case.CategoryL4, N.Case.Partition)        // card footer
        { }
    }

    public sealed class ChangeRenderer : AppRendererBase
    {
        public ChangeRenderer() : base(
            new SchemaStyleInfo
            {
                Type        = N.Change.Type,
                DisplayName = "CHG",
                Icon        = UIcons.Screw.ToString(),
                LabelField  = LabelFields.IdAndTitle(N.Change.DocId, N.Change.Title),
            },
            N.Change.Abstract,
            N.Change.Status, N.Change.RiskLevel, N.Change.CategoryL4, N.Change.Partition)
        { }
    }

    /// <summary>
    /// A reference type: no card body (a part number has a description, not a summary), and a
    /// narrowed 360 view — it shows the records that USE it, not everything.
    /// </summary>
    public sealed class PartNumberRenderer : AppRendererBase
    {
        public PartNumberRenderer() : base(
            new SchemaStyleInfo
            {
                Type        = N.PartNumber.Type,
                DisplayName = "PN",
                Icon        = UIcons.Screw.ToString(),
                // A fallback after any field that can legitimately be empty: with none of them
                // set the label silently becomes "<uid> [Display Name]".
                LabelField  = N.PartNumber.Description + "|" + N.PartNumber.Pn,
            },
            bodyContentProperty: null,
            N.PartNumber.PnType, N.PartNumber.Lifecycle, N.PartNumber.Category)
        {
            ShortDisplayName = "PN";
        }

        protected override string[] Related360Types => new[] { N.Case.Type, N.Change.Type, N.Manual.Type };

        protected override string View360Label => "Used by";
    }

    /// <summary>
    /// One node type holding several hierarchy levels, so the TILE is a function of the node:
    /// a tile reading "CAT" would leave a level-2 and a level-6 entry looking identical in a
    /// mixed list.
    /// </summary>
    public sealed class CategoryRenderer : AppRendererBase
    {
        public CategoryRenderer() : base(
            new SchemaStyleInfo
            {
                Type        = N.Category.Type,
                DisplayName = "CAT",
                Icon        = UIcons.Apps.ToString(),
                // The interpolated form, for a label that needs two fields at once.
                LabelField  = "$CAT {Code} — {Title}",
            },
            bodyContentProperty: null,
            N.Category.Level, N.Category.Domain)
        { }

        protected override string ShortDisplayNameFor(Node node)
            => "L" + (node.GetString(N.Category.Level) ?? "?");
    }

    // -------------------------------------------------------------------------------------
    // The key-only metadata types.
    //
    // Five lines each, and every one of them is reachable from a NeighborsLinks, a facet or a
    // property row — so every one of them needs to be here or it draws as a raw UID.
    // -------------------------------------------------------------------------------------

    public sealed class StatusRenderer : AppRendererBase
    {
        public StatusRenderer() : base(new SchemaStyleInfo
        {
            Type        = N.Status.Type,
            DisplayName = "STATE",
            Icon        = UIcons.Flag.ToString(),
            // The key is canonical; Display is what a reader should see. Where you keep a
            // machine-shaped key, this is what makes the UI read properly anyway.
            LabelField  = N.Status.Display + "|" + N.Status.Value,
        }) { ShortDisplayName = "STATE"; }
    }

    public sealed class ClassificationRenderer : AppRendererBase
    {
        public ClassificationRenderer() : base(new SchemaStyleInfo
        {
            Type        = N.Classification.Type,
            DisplayName = "CLASS",
            Icon        = UIcons.Tags.ToString(),
            // Keyed "{Kind}:{Value}", so the label says both rather than showing the raw key.
            LabelField  = "${Value} ({Kind})",
        }) { ShortDisplayName = "CLASS"; }
    }

    public sealed class KeywordRenderer : AppRendererBase
    {
        public KeywordRenderer() : base(new SchemaStyleInfo
        {
            Type        = N.Keyword.Type,
            DisplayName = "KEY",
            Icon        = UIcons.Label.ToString(),
            LabelField  = N.Keyword.Text,
        }) { ShortDisplayName = "KEY"; }
    }

    /// <summary>
    /// A child node. Note the label: a field that exists but holds a PARAGRAPH is technically
    /// valid and useless — labelling a finding by its rationale produced link text a screen
    /// wide.
    /// </summary>
    public sealed class CaseFindingRenderer : AppRendererBase
    {
        public CaseFindingRenderer() : base(new SchemaStyleInfo
        {
            Type        = N.CaseFinding.Type,
            DisplayName = "FIND",
            Icon        = UIcons.Search.ToString(),
            LabelField  = "${Severity} finding on {CaseId}",
        }) { ShortDisplayName = "FIND"; }
    }
}
