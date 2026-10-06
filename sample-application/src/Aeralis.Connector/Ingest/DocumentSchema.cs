using Curiosity.Library;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// The graph schema for the nine document types plus the child nodes minted from them.
///
/// The envelope fields are repeated on every class rather than inherited: Curiosity reads
/// the schema off the concrete <c>[Node]</c> type, and an explicit list is what makes the
/// schema legible in the workspace's schema editor.
///
/// The document sections are flattened into one property each (<c>ReasonText</c>,
/// <c>DescriptionText</c>, …) so the search index and the similarity engine can weight
/// them separately instead of seeing one undifferentiated blob.
/// </summary>
internal static class DocumentSchema
{
    [Node]
    internal sealed class Modification
    {
        [Key] public string DocId { get; set; } = "";
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
        [Property] public int ModNumber { get; set; }
        [Property] public string ModClass { get; set; } = "";
        [Property] public string ChangeCategory { get; set; } = "";
        [Property] public string Trigger { get; set; } = "";
        [Property] public string EmbodimentStatus { get; set; } = "";
        [Property] public bool IsCandidate { get; set; }
        [Property] public double WeightImpactKg { get; set; }
        [Property] public double EstimatedManhours { get; set; }
        [Property] public double EstimatedCostKeur { get; set; }
        [Property] public string RiskLevel { get; set; } = "";
        [Property] public string CertificationImpact { get; set; } = "";
        [Property] public string Interchangeability { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class ServiceBulletin
    {
        [Key] public string DocId { get; set; } = "";
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
        [Property] public string SbType { get; set; } = "";
        [Property] public string Compliance { get; set; } = "";
        [Property] public string ReasonCode { get; set; } = "";
        [Property] public bool AdLinked { get; set; }
        [Property] public string AdReference { get; set; } = "";
        [Property] public double Manhours { get; set; }
        [Property] public string DowntimeCategory { get; set; } = "";
        [Property] public string KitNumber { get; set; } = "";
        [Property] public double WeightChangeKg { get; set; }
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class InternalOrder
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string RequestText { get; set; } = "";
        [Property] public string BackgroundText { get; set; } = "";
        [Property] public string ExpectedOutcomeText { get; set; } = "";
        [Property] public string RequestCategory { get; set; } = "";
        [Property] public string Priority { get; set; } = "";
        [Property] public string IoStatus { get; set; } = "";
        [Property] public string RequesterType { get; set; } = "";
        [Property] public string Funding { get; set; } = "";
        [Property] public double BudgetKeur { get; set; }
        [Property] public int AircraftCount { get; set; }
        [Property] public string ClosureNote { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class PreAnalysisWorkOrder
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
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
        [Property] public string SubjectMod { get; set; } = "";
        [Property] public string SourceIo { get; set; } = "";
        [Property] public string PawoStatus { get; set; } = "";
        [Property] public string Recommendation { get; set; } = "";
        [Property] public string Conclusion { get; set; } = "";
        [Property] public bool HeroScenario { get; set; }
        [Property] public bool IsHistorical { get; set; }
        [Property] public int AnalysisRound { get; set; }
        [Property] public string LeadDepartment { get; set; } = "";
        [Property] public string LeadSite { get; set; } = "";

        // --- workflow state the dashboard computes its metrics from -------------
        [Property] public string WorkflowStatus { get; set; } = "";
        [Property] public string Outcome { get; set; } = "";
        [Property] public DateTimeOffset? OpenedDate { get; set; }
        [Property] public DateTimeOffset? CompletedDate { get; set; }
        [Property] public DateTimeOffset? DueDate { get; set; }
        [Property] public double HoursSpent { get; set; }
        [Property] public double EstimatedManhours { get; set; }
        [Property] public bool IsUrgent { get; set; }

        // Who owns the work order is not here on purpose: it is the AssignedTo /
        // DelegatedTo edge to the _User. A name duplicated into a property is a second
        // copy of the same fact, and the two drift the moment either is rewritten.
        [Property] public int FindingCount { get; set; }
        [Property] public int BlockingFindingCount { get; set; }

        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class AmmTask
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string JobSetupNoteText { get; set; } = "";
        [Property] public string ProcedureText { get; set; } = "";
        [Property] public string CloseOutText { get; set; } = "";
        [Property] public string TaskNumber { get; set; } = "";
        [Property] public string TaskType { get; set; } = "";
        [Property] public double Manhours { get; set; }
        [Property] public int Personnel { get; set; }
        [Property] public int Steps { get; set; }
        [Property] public int Warnings { get; set; }
        [Property] public int Cautions { get; set; }
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class IpcFigure
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string FigureNoteText { get; set; } = "";
        [Property] public string Figure { get; set; } = "";
        [Property] public string FigureTitle { get; set; } = "";
        [Property] public int SheetCount { get; set; }
        [Property] public string NextHigherAssembly { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class SrmRepair
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string DamageAssessmentText { get; set; } = "";
        [Property] public string RepairProcedureText { get; set; } = "";
        [Property] public string InspectionRequirementsText { get; set; } = "";
        [Property] public string RepairNumber { get; set; } = "";
        [Property] public string RepairCategory { get; set; } = "";
        [Property] public string DamageType { get; set; } = "";
        [Property] public string NdtMethod { get; set; } = "";
        [Property] public double WeightPenaltyKg { get; set; }
        [Property] public double StaticStrengthRestoredPct { get; set; }
        [Property] public bool RequiresOemApproval { get; set; }
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class TsmProcedure
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string FaultDescriptionText { get; set; } = "";
        [Property] public string IsolationProcedureText { get; set; } = "";
        [Property] public string DispatchNoteText { get; set; } = "";
        [Property] public string ProcedureNumber { get; set; } = "";
        [Property] public string FaultSymptom { get; set; } = "";
        [Property] public string FaultCodes { get; set; } = "";
        [Property] public int IsolationSteps { get; set; }
        [Property] public string MelItem { get; set; } = "";
        [Property] public bool Dispatchable { get; set; }
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    [Node]
    internal sealed class WdmSheet
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string SheetNoteText { get; set; } = "";
        [Property] public string RoutingNoteText { get; set; } = "";
        [Property] public string SheetNumber { get; set; } = "";
        [Property] public string SheetTitle { get; set; } = "";
        [Property] public string DiagramType { get; set; } = "";
        [Property] public int WireCount { get; set; }
        [Property] public string WireGaugeRange { get; set; } = "";
        [Property] public string Harnesses { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>
    /// A Flight Crew Operating Manual entry. The only document written for the crew, which
    /// is exactly why it matters here: it shares a system with the maintenance documents and
    /// almost none of their vocabulary, so it is reachable by similarity and not by entity.
    /// </summary>
    [Node]
    internal sealed class FcomEntry
    {
        [Key] public string DocId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Abstract { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Ata4d { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Keywords { get; set; } = "";
        [Property] public string PurposeText { get; set; } = "";
        [Property] public string DescriptionText { get; set; } = "";
        [Property] public string CrewProcedureText { get; set; } = "";
        [Property] public string LimitationNoteText { get; set; } = "";
        [Property] public string Section { get; set; } = "";
        [Property] public string SectionTitle { get; set; } = "";
        [Property] public string EntryNumber { get; set; } = "";
        [Property] public string FlightPhase { get; set; } = "";
        [Property] public string OperationalImpact { get; set; } = "";
        [Property] public string CrewAction { get; set; } = "";
        [Property] public bool AffectsQrh { get; set; }
        [Property] public bool AffectsMel { get; set; }
        [Property] public string Systems { get; set; } = "";
        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    // ---------------------------------------------------------------------------
    // child nodes — nested in the source JSON, first-class in the graph
    // ---------------------------------------------------------------------------

    /// <summary>One row of an IPC figure. Carries the pre/post-MOD effectivity signal.</summary>
    [Node]
    internal sealed class IpcItem
    {
        [Key] public string ItemId { get; set; } = "";
        [Property] public string Figure { get; set; } = "";
        [Property] public string ItemName { get; set; } = "";
        [Property] public string PartNumber { get; set; } = "";
        [Property] public string Nomenclature { get; set; } = "";
        [Property] public double QtyPerAssy { get; set; }
        [Property] public string Effectivity { get; set; } = "";
        [Property] public string Vendor { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
    }

    [Node]
    internal sealed class RepairDrawing
    {
        [Key] public string Reference { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public int Sheets { get; set; }
        [Property] public string ProgramLetter { get; set; } = "";
    }

    /// <summary>
    /// One expected finding of a pre-analysis. Ground truth for evaluation — linked as
    /// <c>FindingTargets</c>, which is not the same claim as a confirmed impact.
    /// </summary>
    [Node]
    internal sealed class ExpectedFinding
    {
        [Key] public string FindingKey { get; set; } = "";
        [Property] public string FindingId { get; set; } = "";
        [Property] public string PawoId { get; set; } = "";
        [Property] public string TargetDocId { get; set; } = "";
        [Property] public string TargetDocType { get; set; } = "";
        [Property] public string FindingType { get; set; } = "";
        [Property] public string Severity { get; set; } = "";
        [Property] public string Discovery { get; set; } = "";
        [Property] public string Rationale { get; set; } = "";
        [Property] public string Resolution { get; set; } = "";
        [Property] public string SimilarityNote { get; set; } = "";
        [Property] public bool IsSemanticOnly { get; set; }
        [Property] public string ProgramLetter { get; set; } = "";
    }

    [Node]
    internal sealed class SbRevision
    {
        [Key] public string RevisionKey { get; set; } = "";
        [Property] public string SbId { get; set; } = "";
        [Property] public string Revision { get; set; } = "";
        [Property] public string Reason { get; set; } = "";
        [Timestamp] public DateTimeOffset Date { get; set; }
    }

    /// <summary>An applicability range, kept whole so it can be traversed without exploding into MSNs.</summary>
    [Node]
    internal sealed class MsnRangeApplicability
    {
        [Key] public string RangeKey { get; set; } = "";
        [Property] public string DocId { get; set; } = "";
        [Property] public string FromMsn { get; set; } = "";
        [Property] public string ToMsn { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
    }
}
