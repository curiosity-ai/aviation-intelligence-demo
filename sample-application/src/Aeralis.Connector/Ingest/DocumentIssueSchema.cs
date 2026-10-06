using Curiosity.Library;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// One node type per document type, holding a single <b>issue</b> of it.
///
/// A document file is an array of issues, oldest first, and the last one is the document as it
/// stands. <see cref="DocumentSchema"/> holds that current state, keyed by identifier alone —
/// which is what everything else in the graph links to. These hold the history behind it, keyed
/// by identifier and issue letter together, so a superseded issue is addressable, searchable,
/// and can carry the source and artwork it was published from.
///
/// Each class carries the envelope and the prose of its counterpart in
/// <see cref="DocumentSchema"/>, which is what differs between issues and what a reader
/// comparing two of them is looking at. The type-specific measurements — man-hours, mass, risk
/// level — stay on the document node, which is the state anything else in the graph reasons
/// about. The duplication between the two families is unavoidable: Curiosity reads the schema
/// off the concrete <c>[Node]</c> type, so a shared base class would be invisible to it.
///
/// An issue after the first exists because a completed pre-analysis found something the document
/// had to say differently. <c>RaisedByPawo</c> and <c>RaisedByFinding</c> name that finding, so
/// every change in the history is traceable to the work order that required it.
/// </summary>
internal static class DocumentIssueSchema
{

    /// <summary>One issue of a <see cref="DocumentSchema.Modification"/>.</summary>
    [Node]
    internal sealed class ModificationIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string ReasonText { get; set; } = "";
        [Property] public string DescriptionText { get; set; } = "";
        [Property] public string EffectsText { get; set; } = "";
        [Property] public string EmbodimentNoteText { get; set; } = "";
        [Property] public string OpenPointsText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.ServiceBulletin"/>.</summary>
    [Node]
    internal sealed class ServiceBulletinIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PlanningReasonText { get; set; } = "";
        [Property] public string DescriptionText { get; set; } = "";
        [Property] public string AccomplishmentSummaryText { get; set; } = "";
        [Property] public string EffectivityNoteText { get; set; } = "";
        [Property] public string MaterialNoteText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.InternalOrder"/>.</summary>
    [Node]
    internal sealed class InternalOrderIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string RequestText { get; set; } = "";
        [Property] public string BackgroundText { get; set; } = "";
        [Property] public string ExpectedOutcomeText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.PreAnalysisWorkOrder"/>.</summary>
    [Node]
    internal sealed class PreAnalysisWorkOrderIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string ObjectiveText { get; set; } = "";
        [Property] public string ScopeNoteText { get; set; } = "";
        [Property] public string MethodNoteText { get; set; } = "";
        [Property] public string FindingsSummaryText { get; set; } = "";
        [Property] public string NextStepsText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.AmmTask"/>.</summary>
    [Node]
    internal sealed class AmmTaskIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string JobSetupNoteText { get; set; } = "";
        [Property] public string ProcedureText { get; set; } = "";
        [Property] public string CloseOutText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.IpcFigure"/>.</summary>
    [Node]
    internal sealed class IpcFigureIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string FigureNoteText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.SrmRepair"/>.</summary>
    [Node]
    internal sealed class SrmRepairIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string DamageAssessmentText { get; set; } = "";
        [Property] public string RepairProcedureText { get; set; } = "";
        [Property] public string InspectionRequirementsText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.TsmProcedure"/>.</summary>
    [Node]
    internal sealed class TsmProcedureIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string FaultDescriptionText { get; set; } = "";
        [Property] public string IsolationProcedureText { get; set; } = "";
        [Property] public string DispatchNoteText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.WdmSheet"/>.</summary>
    [Node]
    internal sealed class WdmSheetIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string SheetNoteText { get; set; } = "";
        [Property] public string RoutingNoteText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>One issue of a <see cref="DocumentSchema.FcomEntry"/>.</summary>
    [Node]
    internal sealed class FcomEntryIssue
    {
        /// <summary>The document and the issue together, e.g. <c>MOD-160142:B</c>.</summary>
        [Key] public string IssueId { get; set; } = "";

        [Property] public string DocId { get; set; } = "";
        [Property] public string IssueLetter { get; set; } = "";

        /// <summary>The issue this one replaced. Empty for an original issue.</summary>
        [Property] public string Supersedes { get; set; } = "";

        /// <summary>What changed, in the register a change record uses.</summary>
        [Property] public string IssueReason { get; set; } = "";

        [Property] public string RaisedByPawo { get; set; } = "";
        [Property] public string RaisedByFinding { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";

        /// <summary>Whether this is the issue the document is currently at.</summary>
        [Property] public bool IsLatest { get; set; }

        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string DescriptionText { get; set; } = "";
        [Property] public string CrewProcedureText { get; set; } = "";
        [Property] public string LimitationNoteText { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }
}
