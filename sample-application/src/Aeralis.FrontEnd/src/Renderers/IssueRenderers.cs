using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Schema;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace Aeralis.FrontEnd.Renderers
{
    // One renderer per issue node type. There are ten of them because there are ten document
    // types, and the connector gives each its own issue type rather than one shared
    // `DocumentIssue` — Curiosity reads the schema off the concrete `[Node]` class, so a shared
    // base would be invisible to it and a single type would have to carry the union of every
    // type's prose fields (see `aeralis-graph-connector` §4.5).
    //
    // Every one of them needs a renderer for the same reason every other type does: without one
    // there is no `LabelField`, so the node draws as `<uid> [Display Name]`. That is what the
    // issue comparison used to get on both sides of its split, and what any `HasIssue`
    // traversal still got everywhere else — an issue history rendered as a column of UIDs.
    //
    // They are near-identical by construction, so everything shared lives in
    // `AeralisIssueRendererBase` and each type states only the four things that differ: its
    // node type, what it is called, what its tile says, and which document icon it carries.

    /// <summary>
    /// The shared shape of an issue: one revision of a document, as it read at the time.
    ///
    /// **The tile says what the document says, and that it is an issue of it** — <c>AMM</c> over
    /// <c>ISSUE</c>, through <see cref="AeralisRendererBase.ShortDisplayName"/>. The facet list
    /// and the modal header have room for the truth, so they say <c>AMM issue</c>. The tile used
    /// to carry the document's abbreviation alone and leave the difference to colour, which asks
    /// a reader to know that this shade of the AMM tile means a revision of one.
    ///
    /// **The issue letter is a badge, not a footer entry.** Which revision you are looking at is
    /// the single most important fact about an issue row — it is the whole reason the node
    /// exists separately from its document — and the badge is the one place on a card that is
    /// read before the title.
    /// </summary>
    public abstract class AeralisIssueRendererBase : AeralisRendererBase
    {
        private readonly string _issueLetterField;
        private readonly string _isLatestField;

        /// <param name="tileText">
        /// The document's abbreviation — what kind of thing this is a revision of. It becomes the
        /// first line of the tile, over <c>ISSUE</c>.
        /// </param>
        /// <param name="footerProperties">
        /// Status, then the issue letter, then where the document sits. Deliberately the same
        /// four on every issue type: the measurements that differ between document types stay
        /// on the document node, which is the state anything else reasons about — what differs
        /// between issues is the text.
        /// </param>
        protected AeralisIssueRendererBase(SchemaStyleInfo style,
                                            string tileText,
                                            string issueLetterField,
                                            string isLatestField,
                                            string abstractProperty,
                                            params string[] footerProperties)
            : base(style, abstractProperty, footerProperties)
        {
            ShortDisplayName = tileText + "\nISSUE";
            _issueLetterField = issueLetterField;
            _isLatestField = isLatestField;
        }

        /// <summary>An issue's neighbourhood is what that revision of the document touched.</summary>
        protected override string View360Label => "What This Issue Touches";

        /// <summary>
        /// Every issue type has one: <c>AttachmentsIngest</c> renders a PDF per issue and hangs it
        /// off that issue. The base reads the ten *document* types off <c>N.DocumentTypes</c>,
        /// which the schema emitter builds without the issue snapshots, so the issue side says so
        /// here rather than being listed there.
        /// </summary>
        protected override bool HasPublishedFile => true;

        public override OmniResult<Node> CompactView(Node node)
        {
            var result = base.CompactView(node);

            var letter = GetValueForProperty(node, _issueLetterField);

            if (!string.IsNullOrEmpty(letter))
            {
                // "current" rather than "latest": a reader scanning a history wants to know which
                // row is the document as it stands, and every other row is then history by
                // elimination. Marking the others "superseded" would say the same thing nine times.
                var badge = node.GetBool(_isLatestField)
                    ? Badge("Issue " + letter + " · current").Success()
                    : Badge("Issue " + letter);

                result.SetBadge(badge);
            }

            return result;
        }
    }

    public class ModificationIssueRenderer : AeralisIssueRendererBase
    {
        public ModificationIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ModificationIssue.Type, DisplayName = "MOD issue",
            Icon = UIcons.PencilRuler.ToString(),
            LabelField = LabelFields.IdAndTitle(N.ModificationIssue.IssueId, N.ModificationIssue.Title),
        },
        "MOD", N.ModificationIssue.IssueLetter, N.ModificationIssue.IsLatest,
        N.ModificationIssue.Abstract,
        N.ModificationIssue.Status, N.ModificationIssue.Ata6d, N.ModificationIssue.ProgramLetter)
        { }
    }

    public class ServiceBulletinIssueRenderer : AeralisIssueRendererBase
    {
        public ServiceBulletinIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ServiceBulletinIssue.Type, DisplayName = "SB issue",
            Icon = UIcons.Megaphone.ToString(),
            LabelField = LabelFields.IdAndTitle(N.ServiceBulletinIssue.IssueId, N.ServiceBulletinIssue.Title),
        },
        "SB", N.ServiceBulletinIssue.IssueLetter, N.ServiceBulletinIssue.IsLatest,
        N.ServiceBulletinIssue.Abstract,
        N.ServiceBulletinIssue.Status, N.ServiceBulletinIssue.Ata6d, N.ServiceBulletinIssue.ProgramLetter)
        { }
    }

    public class InternalOrderIssueRenderer : AeralisIssueRendererBase
    {
        public InternalOrderIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.InternalOrderIssue.Type, DisplayName = "IO issue",
            Icon = UIcons.Inbox.ToString(),
            LabelField = LabelFields.IdAndTitle(N.InternalOrderIssue.IssueId, N.InternalOrderIssue.Title),
        },
        "IO", N.InternalOrderIssue.IssueLetter, N.InternalOrderIssue.IsLatest,
        N.InternalOrderIssue.Abstract,
        N.InternalOrderIssue.Status, N.InternalOrderIssue.Ata6d, N.InternalOrderIssue.ProgramLetter)
        { }
    }

    public class PreAnalysisWorkOrderIssueRenderer : AeralisIssueRendererBase
    {
        public PreAnalysisWorkOrderIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.PreAnalysisWorkOrderIssue.Type, DisplayName = "PAWO issue",
            Icon = UIcons.ClipboardList.ToString(),
            LabelField = LabelFields.IdAndTitle(N.PreAnalysisWorkOrderIssue.IssueId, N.PreAnalysisWorkOrderIssue.Title),
        },
        "PAWO", N.PreAnalysisWorkOrderIssue.IssueLetter, N.PreAnalysisWorkOrderIssue.IsLatest,
        N.PreAnalysisWorkOrderIssue.Abstract,
        N.PreAnalysisWorkOrderIssue.Status, N.PreAnalysisWorkOrderIssue.Ata6d,
        N.PreAnalysisWorkOrderIssue.ProgramLetter)
        { }
    }

    public class AmmTaskIssueRenderer : AeralisIssueRendererBase
    {
        public AmmTaskIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AmmTaskIssue.Type, DisplayName = "AMM issue",
            Icon = UIcons.ToolBox.ToString(),
            LabelField = LabelFields.IdAndTitle(N.AmmTaskIssue.IssueId, N.AmmTaskIssue.Title),
        },
        "AMM", N.AmmTaskIssue.IssueLetter, N.AmmTaskIssue.IsLatest,
        N.AmmTaskIssue.Abstract,
        N.AmmTaskIssue.Status, N.AmmTaskIssue.Ata6d, N.AmmTaskIssue.ProgramLetter)
        { }
    }

    public class IpcFigureIssueRenderer : AeralisIssueRendererBase
    {
        public IpcFigureIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.IpcFigureIssue.Type, DisplayName = "IPC issue",
            Icon = UIcons.Boxes.ToString(),
            LabelField = LabelFields.IdAndTitle(N.IpcFigureIssue.IssueId, N.IpcFigureIssue.Title),
        },
        "IPC", N.IpcFigureIssue.IssueLetter, N.IpcFigureIssue.IsLatest,
        N.IpcFigureIssue.Abstract,
        N.IpcFigureIssue.Status, N.IpcFigureIssue.Ata6d, N.IpcFigureIssue.ProgramLetter)
        { }
    }

    public class SrmRepairIssueRenderer : AeralisIssueRendererBase
    {
        public SrmRepairIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.SrmRepairIssue.Type, DisplayName = "SRM issue",
            Icon = UIcons.Patch.ToString(),
            LabelField = LabelFields.IdAndTitle(N.SrmRepairIssue.IssueId, N.SrmRepairIssue.Title),
        },
        "SRM", N.SrmRepairIssue.IssueLetter, N.SrmRepairIssue.IsLatest,
        N.SrmRepairIssue.Abstract,
        N.SrmRepairIssue.Status, N.SrmRepairIssue.Ata6d, N.SrmRepairIssue.ProgramLetter)
        { }
    }

    public class TsmProcedureIssueRenderer : AeralisIssueRendererBase
    {
        public TsmProcedureIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.TsmProcedureIssue.Type, DisplayName = "TSM issue",
            Icon = UIcons.Bug.ToString(),
            LabelField = LabelFields.IdAndTitle(N.TsmProcedureIssue.IssueId, N.TsmProcedureIssue.Title),
        },
        "TSM", N.TsmProcedureIssue.IssueLetter, N.TsmProcedureIssue.IsLatest,
        N.TsmProcedureIssue.Abstract,
        N.TsmProcedureIssue.Status, N.TsmProcedureIssue.Ata6d, N.TsmProcedureIssue.ProgramLetter)
        { }
    }

    public class WdmSheetIssueRenderer : AeralisIssueRendererBase
    {
        public WdmSheetIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.WdmSheetIssue.Type, DisplayName = "WDM issue",
            Icon = UIcons.Plug.ToString(),
            LabelField = LabelFields.IdAndTitle(N.WdmSheetIssue.IssueId, N.WdmSheetIssue.Title),
        },
        "WDM", N.WdmSheetIssue.IssueLetter, N.WdmSheetIssue.IsLatest,
        N.WdmSheetIssue.Abstract,
        N.WdmSheetIssue.Status, N.WdmSheetIssue.Ata6d, N.WdmSheetIssue.ProgramLetter)
        { }
    }

    public class FcomEntryIssueRenderer : AeralisIssueRendererBase
    {
        public FcomEntryIssueRenderer() : base(new SchemaStyleInfo
        {
            Name = N.FcomEntryIssue.Type, DisplayName = "FCOM issue",
            Icon = UIcons.PlaneTail.ToString(),
            LabelField = LabelFields.IdAndTitle(N.FcomEntryIssue.IssueId, N.FcomEntryIssue.Title),
        },
        "FCOM", N.FcomEntryIssue.IssueLetter, N.FcomEntryIssue.IsLatest,
        N.FcomEntryIssue.Abstract,
        N.FcomEntryIssue.Status, N.FcomEntryIssue.Ata6d, N.FcomEntryIssue.ProgramLetter)
        { }
    }
}
