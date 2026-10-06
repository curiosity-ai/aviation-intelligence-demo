[tools: Curiosity.ChatAITools.UID("ConsuLt111111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Consult")]
[tools: Curiosity.ChatAITools.Description("Reads the full content of one item attached to this conversation, by its UID.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-book-open-cover")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The consult tool is the one built-in an admin can edit, and what is editable is only the
// per-node-type readers: the tool surface — the single `consult` entry point, the UID parsing,
// the access and chat-context checks and the switch on the node's type — is generated around
// these methods at compile time (`_ChatAITool.ConsultTool.BuildToolSource`). So there is no
// class and no [Tool] method here, only methods following the fixed schema
//
//     object Consult<NodeType>(ToolScope scope, UID128 uid) { ... }
//
// A node type with no reader is reported to the model as "not consultable", which is why every
// document type in this workspace has one: an engineer attaches whatever they are looking at,
// and a type the assistant cannot read is a question it has to answer by guessing.
//
// **Every reader returns its attachments.** A document in this dataset is not only its fields —
// the connector renders each one to a published PDF and hangs it off the node, and that PDF is
// what an engineer actually reads. So each response carries an `attachments` array of
// `{ uid, name, contentType, pages }`, and those uids are consultable in turn: consult the
// document, then consult its attachment to read the published text. The system prompt says so
// too, but a model that only ever sees fields will only ever reason about fields.
//
// Helper methods must NOT start with "Consult" — that prefix is what marks a reader, and a
// helper carrying it would fail the schema check at save time.

// -------------------------------------------------------------------------------------------
// helpers
// -------------------------------------------------------------------------------------------

/// <summary>Every field named, skipping the ones this node has nothing for.</summary>
Dictionary<string, string> Fields(ToolScope scope, UID128 uid, string[] names)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);

    if (!scope.Graph.TryGet(uid, out var node)) return values;

    foreach (var name in names)
    {
        var value = node[name]?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(value)) values[name] = value;
    }

    return values;
}

/// <summary>
/// The files hanging off this node. These are the published PDFs the connector rendered, and
/// their uids are consultable — reading one is how the model gets the document as it was
/// actually published rather than as a set of fields.
/// </summary>
object[] Attachments(ToolScope scope, UID128 uid)
{
    var files = new List<object>();

    foreach (var fileUID in scope.Q().StartAt(uid).Out(nodeType: N._FileEntry.Type, edgeType: E._HasAttachment).AsUIDEnumerable())
    {
        if (!scope.Graph.TryGet(fileUID, out var file)) continue;

        files.Add(new
        {
            uid = fileUID.ToString(),
            name = file.GetString(N._FileEntry.OriginalName),
            contentType = file.GetString(N._FileEntry.ContentType),
            pages = scope.Q().StartAt(fileUID).Out(_Document.Type).Count(),
            consultable = true,
        });
    }

    return files.ToArray();
}

/// <summary>
/// The reference data this document points at, grouped by what kind of thing it is.
///
/// A pre-analysis turns on exactly these: two documents sharing a part number, a FIN, a zone or
/// an ATA chapter is the entity half of the retrieval story, and it is the half a model cannot
/// infer from prose. Capped per type, because a modification can touch dozens of parts and the
/// answer is about the overlap, not the inventory.
/// </summary>
Dictionary<string, string[]> Related(ToolScope scope, UID128 uid)
{
    var related = new Dictionary<string, string[]>(StringComparer.Ordinal);

    string[] Of(string nodeType, string keyField)
    {
        var keys = new List<string>();

        foreach (var neighbour in scope.Q().StartAt(uid).Out(nodeType: nodeType).AsEnumerable())
        {
            var key = neighbour.GetString(keyField);
            if (!string.IsNullOrWhiteSpace(key) && !keys.Contains(key)) keys.Add(key);
            if (keys.Count >= 25) break;
        }

        return keys.ToArray();
    }

    void Add(string label, string nodeType, string keyField)
    {
        var values = Of(nodeType, keyField);
        if (values.Length > 0) related[label] = values;
    }

    Add("ataChapters", N.AtaChapter.Type, N.AtaChapter.Code);
    Add("partNumbers", N.PartNumber.Type, N.PartNumber.Pn);
    Add("fins", N.Fin.Type, N.Fin.FinId);
    Add("drawings", N.Drawing.Type, N.Drawing.DrawingNumber);
    Add("zones", N.Zone.Type, N.Zone.ZoneId);
    Add("software", N.SoftwareLoad.Type, N.SoftwareLoad.SoftwarePn);
    Add("aircraftTypes", N.AircraftType.Type, N.AircraftType.Name);
    Add("models", N.AircraftModel.Type, N.AircraftModel.Name);
    Add("status", N.Status.Type, N.Status.Value);

    return related;
}

/// <summary>One document, however it is typed: its fields, what it references, and its files.</summary>
object Document(ToolScope scope, UID128 uid, string kind, string titleField, string[] names)
{
    if (!scope.Graph.TryGet(uid, out var node)) return new { ok = false, error = kind + " not found" };

    var title = node.GetString(titleField);
    scope.SetToolCallDisplayName(string.IsNullOrWhiteSpace(title) ? "Reading a " + kind : "Reading '" + title + "'");

    return new
    {
        ok = true,
        uid = uid.ToString(),
        type = node.Type,
        kind = kind,
        fields = Fields(scope, uid, names),
        related = Related(scope, uid),
        attachments = Attachments(scope, uid),
    };
}

// -------------------------------------------------------------------------------------------
// documents
// -------------------------------------------------------------------------------------------

object ConsultModification(ToolScope scope, UID128 uid) => Document(scope, uid, "modification dossier", N.Modification.Title, new[]
{
    N.Modification.DocId, N.Modification.Title, N.Modification.Abstract, N.Modification.ProgramLetter, N.Modification.Revision, N.Modification.Status, N.Modification.Ata2d, N.Modification.Ata4d, N.Modification.Ata6d, N.Modification.Keywords, N.Modification.ReasonText, N.Modification.DescriptionText, N.Modification.EffectsText, N.Modification.EmbodimentNoteText, N.Modification.OpenPointsText, N.Modification.ModNumber, N.Modification.ModClass, N.Modification.ChangeCategory, N.Modification.Trigger, N.Modification.EmbodimentStatus, N.Modification.IsCandidate, N.Modification.WeightImpactKg, N.Modification.EstimatedManhours, N.Modification.EstimatedCostKeur, N.Modification.RiskLevel, N.Modification.CertificationImpact, N.Modification.Interchangeability,
});

object ConsultServiceBulletin(ToolScope scope, UID128 uid) => Document(scope, uid, "service bulletin", N.ServiceBulletin.Title, new[]
{
    N.ServiceBulletin.DocId, N.ServiceBulletin.Title, N.ServiceBulletin.Abstract, N.ServiceBulletin.ProgramLetter, N.ServiceBulletin.Revision, N.ServiceBulletin.Status, N.ServiceBulletin.Ata2d, N.ServiceBulletin.Ata4d, N.ServiceBulletin.Ata6d, N.ServiceBulletin.Keywords, N.ServiceBulletin.PlanningReasonText, N.ServiceBulletin.DescriptionText, N.ServiceBulletin.AccomplishmentSummaryText, N.ServiceBulletin.EffectivityNoteText, N.ServiceBulletin.MaterialNoteText, N.ServiceBulletin.SbType, N.ServiceBulletin.Compliance, N.ServiceBulletin.ReasonCode, N.ServiceBulletin.AdLinked, N.ServiceBulletin.AdReference, N.ServiceBulletin.Manhours, N.ServiceBulletin.DowntimeCategory, N.ServiceBulletin.KitNumber, N.ServiceBulletin.WeightChangeKg,
});

object ConsultInternalOrder(ToolScope scope, UID128 uid) => Document(scope, uid, "internal order", N.InternalOrder.Title, new[]
{
    N.InternalOrder.DocId, N.InternalOrder.Title, N.InternalOrder.Abstract, N.InternalOrder.ProgramLetter, N.InternalOrder.Status, N.InternalOrder.Ata2d, N.InternalOrder.Ata4d, N.InternalOrder.Ata6d, N.InternalOrder.Keywords, N.InternalOrder.RequestText, N.InternalOrder.BackgroundText, N.InternalOrder.ExpectedOutcomeText, N.InternalOrder.RequestCategory, N.InternalOrder.Priority, N.InternalOrder.IoStatus, N.InternalOrder.RequesterType, N.InternalOrder.Funding, N.InternalOrder.BudgetKeur, N.InternalOrder.AircraftCount, N.InternalOrder.ClosureNote,
});

object ConsultPreAnalysisWorkOrder(ToolScope scope, UID128 uid) => Document(scope, uid, "pre-analysis work order", N.PreAnalysisWorkOrder.Title, new[]
{
    N.PreAnalysisWorkOrder.DocId, N.PreAnalysisWorkOrder.Title, N.PreAnalysisWorkOrder.Abstract, N.PreAnalysisWorkOrder.ProgramLetter, N.PreAnalysisWorkOrder.Status, N.PreAnalysisWorkOrder.Ata2d, N.PreAnalysisWorkOrder.Ata4d, N.PreAnalysisWorkOrder.Ata6d, N.PreAnalysisWorkOrder.Keywords, N.PreAnalysisWorkOrder.ObjectiveText, N.PreAnalysisWorkOrder.ScopeNoteText, N.PreAnalysisWorkOrder.MethodNoteText, N.PreAnalysisWorkOrder.FindingsSummaryText, N.PreAnalysisWorkOrder.NextStepsText, N.PreAnalysisWorkOrder.SubjectMod, N.PreAnalysisWorkOrder.SourceIo, N.PreAnalysisWorkOrder.PawoStatus, N.PreAnalysisWorkOrder.Recommendation, N.PreAnalysisWorkOrder.Conclusion, N.PreAnalysisWorkOrder.HeroScenario, N.PreAnalysisWorkOrder.IsHistorical, N.PreAnalysisWorkOrder.AnalysisRound, N.PreAnalysisWorkOrder.LeadDepartment, N.PreAnalysisWorkOrder.LeadSite, N.PreAnalysisWorkOrder.WorkflowStatus, N.PreAnalysisWorkOrder.Outcome, N.PreAnalysisWorkOrder.OpenedDate, N.PreAnalysisWorkOrder.CompletedDate, N.PreAnalysisWorkOrder.DueDate, N.PreAnalysisWorkOrder.HoursSpent, N.PreAnalysisWorkOrder.EstimatedManhours, N.PreAnalysisWorkOrder.IsUrgent, N.PreAnalysisWorkOrder.FindingCount, N.PreAnalysisWorkOrder.BlockingFindingCount,
});

object ConsultAmmTask(ToolScope scope, UID128 uid) => Document(scope, uid, "maintenance task", N.AmmTask.Title, new[]
{
    N.AmmTask.DocId, N.AmmTask.Title, N.AmmTask.Abstract, N.AmmTask.ProgramLetter, N.AmmTask.Status, N.AmmTask.Ata2d, N.AmmTask.Ata4d, N.AmmTask.Ata6d, N.AmmTask.Keywords, N.AmmTask.PurposeText, N.AmmTask.JobSetupNoteText, N.AmmTask.ProcedureText, N.AmmTask.CloseOutText, N.AmmTask.TaskNumber, N.AmmTask.TaskType, N.AmmTask.Manhours, N.AmmTask.Personnel, N.AmmTask.Steps, N.AmmTask.Warnings, N.AmmTask.Cautions,
});

object ConsultIpcFigure(ToolScope scope, UID128 uid) => Document(scope, uid, "illustrated-parts figure", N.IpcFigure.Title, new[]
{
    N.IpcFigure.DocId, N.IpcFigure.Title, N.IpcFigure.Abstract, N.IpcFigure.ProgramLetter, N.IpcFigure.Status, N.IpcFigure.Ata2d, N.IpcFigure.Ata4d, N.IpcFigure.Ata6d, N.IpcFigure.Keywords, N.IpcFigure.PurposeText, N.IpcFigure.FigureNoteText, N.IpcFigure.Figure, N.IpcFigure.FigureTitle, N.IpcFigure.SheetCount, N.IpcFigure.NextHigherAssembly,
});

object ConsultSrmRepair(ToolScope scope, UID128 uid) => Document(scope, uid, "structural repair", N.SrmRepair.Title, new[]
{
    N.SrmRepair.DocId, N.SrmRepair.Title, N.SrmRepair.Abstract, N.SrmRepair.ProgramLetter, N.SrmRepair.Status, N.SrmRepair.Ata2d, N.SrmRepair.Ata4d, N.SrmRepair.Ata6d, N.SrmRepair.Keywords, N.SrmRepair.PurposeText, N.SrmRepair.DamageAssessmentText, N.SrmRepair.RepairProcedureText, N.SrmRepair.InspectionRequirementsText, N.SrmRepair.RepairNumber, N.SrmRepair.RepairCategory, N.SrmRepair.DamageType, N.SrmRepair.NdtMethod, N.SrmRepair.WeightPenaltyKg, N.SrmRepair.StaticStrengthRestoredPct, N.SrmRepair.RequiresOemApproval,
});

object ConsultTsmProcedure(ToolScope scope, UID128 uid) => Document(scope, uid, "troubleshooting procedure", N.TsmProcedure.Title, new[]
{
    N.TsmProcedure.DocId, N.TsmProcedure.Title, N.TsmProcedure.Abstract, N.TsmProcedure.ProgramLetter, N.TsmProcedure.Status, N.TsmProcedure.Ata2d, N.TsmProcedure.Ata4d, N.TsmProcedure.Ata6d, N.TsmProcedure.Keywords, N.TsmProcedure.PurposeText, N.TsmProcedure.FaultDescriptionText, N.TsmProcedure.IsolationProcedureText, N.TsmProcedure.DispatchNoteText, N.TsmProcedure.ProcedureNumber, N.TsmProcedure.FaultSymptom, N.TsmProcedure.FaultCodes, N.TsmProcedure.IsolationSteps, N.TsmProcedure.MelItem, N.TsmProcedure.Dispatchable,
});

object ConsultWdmSheet(ToolScope scope, UID128 uid) => Document(scope, uid, "wiring diagram sheet", N.WdmSheet.Title, new[]
{
    N.WdmSheet.DocId, N.WdmSheet.Title, N.WdmSheet.Abstract, N.WdmSheet.ProgramLetter, N.WdmSheet.Status, N.WdmSheet.Ata2d, N.WdmSheet.Ata4d, N.WdmSheet.Ata6d, N.WdmSheet.Keywords, N.WdmSheet.PurposeText, N.WdmSheet.SheetNoteText, N.WdmSheet.RoutingNoteText, N.WdmSheet.SheetNumber, N.WdmSheet.SheetTitle, N.WdmSheet.DiagramType, N.WdmSheet.WireCount, N.WdmSheet.WireGaugeRange, N.WdmSheet.Harnesses,
});

object ConsultFcomEntry(ToolScope scope, UID128 uid) => Document(scope, uid, "flight-crew entry", N.FcomEntry.Title, new[]
{
    N.FcomEntry.DocId, N.FcomEntry.Title, N.FcomEntry.Abstract, N.FcomEntry.ProgramLetter, N.FcomEntry.Status, N.FcomEntry.Ata2d, N.FcomEntry.Ata4d, N.FcomEntry.Ata6d, N.FcomEntry.Keywords, N.FcomEntry.PurposeText, N.FcomEntry.DescriptionText, N.FcomEntry.CrewProcedureText, N.FcomEntry.LimitationNoteText, N.FcomEntry.Section, N.FcomEntry.SectionTitle, N.FcomEntry.EntryNumber, N.FcomEntry.FlightPhase, N.FcomEntry.OperationalImpact, N.FcomEntry.CrewAction, N.FcomEntry.AffectsQrh, N.FcomEntry.AffectsMel, N.FcomEntry.Systems,
});

// -------------------------------------------------------------------------------------------
// issues
//
// One revision of a document, as it read at the time, with the file that was published for it.
// They matter here because the comparison screen attaches an issue and the current document
// side by side and asks what changed: without a reader the issue is "not consultable" and the
// model can only compare one half of the pair.
// -------------------------------------------------------------------------------------------

object ConsultModificationIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "modification dossier issue", N.ModificationIssue.Title, new[]
{
    N.ModificationIssue.IssueId, N.ModificationIssue.DocId, N.ModificationIssue.IssueLetter, N.ModificationIssue.Supersedes, N.ModificationIssue.IssueReason, N.ModificationIssue.RaisedByPawo, N.ModificationIssue.RaisedByFinding, N.ModificationIssue.FindingType, N.ModificationIssue.IsLatest, N.ModificationIssue.Title, N.ModificationIssue.Abstract, N.ModificationIssue.ProgramLetter, N.ModificationIssue.Revision, N.ModificationIssue.Status, N.ModificationIssue.Ata2d, N.ModificationIssue.Ata4d, N.ModificationIssue.Ata6d, N.ModificationIssue.Keywords, N.ModificationIssue.ReasonText, N.ModificationIssue.DescriptionText, N.ModificationIssue.EffectsText, N.ModificationIssue.EmbodimentNoteText, N.ModificationIssue.OpenPointsText,
});

object ConsultServiceBulletinIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "service bulletin issue", N.ServiceBulletinIssue.Title, new[]
{
    N.ServiceBulletinIssue.IssueId, N.ServiceBulletinIssue.DocId, N.ServiceBulletinIssue.IssueLetter, N.ServiceBulletinIssue.Supersedes, N.ServiceBulletinIssue.IssueReason, N.ServiceBulletinIssue.RaisedByPawo, N.ServiceBulletinIssue.RaisedByFinding, N.ServiceBulletinIssue.FindingType, N.ServiceBulletinIssue.IsLatest, N.ServiceBulletinIssue.Title, N.ServiceBulletinIssue.Abstract, N.ServiceBulletinIssue.ProgramLetter, N.ServiceBulletinIssue.Revision, N.ServiceBulletinIssue.Status, N.ServiceBulletinIssue.Ata2d, N.ServiceBulletinIssue.Ata4d, N.ServiceBulletinIssue.Ata6d, N.ServiceBulletinIssue.Keywords, N.ServiceBulletinIssue.PlanningReasonText, N.ServiceBulletinIssue.DescriptionText, N.ServiceBulletinIssue.AccomplishmentSummaryText, N.ServiceBulletinIssue.EffectivityNoteText, N.ServiceBulletinIssue.MaterialNoteText,
});

object ConsultInternalOrderIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "internal order issue", N.InternalOrderIssue.Title, new[]
{
    N.InternalOrderIssue.IssueId, N.InternalOrderIssue.DocId, N.InternalOrderIssue.IssueLetter, N.InternalOrderIssue.Supersedes, N.InternalOrderIssue.IssueReason, N.InternalOrderIssue.RaisedByPawo, N.InternalOrderIssue.RaisedByFinding, N.InternalOrderIssue.FindingType, N.InternalOrderIssue.IsLatest, N.InternalOrderIssue.Title, N.InternalOrderIssue.Abstract, N.InternalOrderIssue.ProgramLetter, N.InternalOrderIssue.Revision, N.InternalOrderIssue.Status, N.InternalOrderIssue.Ata2d, N.InternalOrderIssue.Ata4d, N.InternalOrderIssue.Ata6d, N.InternalOrderIssue.Keywords, N.InternalOrderIssue.RequestText, N.InternalOrderIssue.BackgroundText, N.InternalOrderIssue.ExpectedOutcomeText,
});

object ConsultPreAnalysisWorkOrderIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "pre-analysis work order issue", N.PreAnalysisWorkOrderIssue.Title, new[]
{
    N.PreAnalysisWorkOrderIssue.IssueId, N.PreAnalysisWorkOrderIssue.DocId, N.PreAnalysisWorkOrderIssue.IssueLetter, N.PreAnalysisWorkOrderIssue.Supersedes, N.PreAnalysisWorkOrderIssue.IssueReason, N.PreAnalysisWorkOrderIssue.RaisedByPawo, N.PreAnalysisWorkOrderIssue.RaisedByFinding, N.PreAnalysisWorkOrderIssue.FindingType, N.PreAnalysisWorkOrderIssue.IsLatest, N.PreAnalysisWorkOrderIssue.Title, N.PreAnalysisWorkOrderIssue.Abstract, N.PreAnalysisWorkOrderIssue.ProgramLetter, N.PreAnalysisWorkOrderIssue.Revision, N.PreAnalysisWorkOrderIssue.Status, N.PreAnalysisWorkOrderIssue.Ata2d, N.PreAnalysisWorkOrderIssue.Ata4d, N.PreAnalysisWorkOrderIssue.Ata6d, N.PreAnalysisWorkOrderIssue.Keywords, N.PreAnalysisWorkOrderIssue.ObjectiveText, N.PreAnalysisWorkOrderIssue.ScopeNoteText, N.PreAnalysisWorkOrderIssue.MethodNoteText, N.PreAnalysisWorkOrderIssue.FindingsSummaryText, N.PreAnalysisWorkOrderIssue.NextStepsText,
});

object ConsultAmmTaskIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "maintenance task issue", N.AmmTaskIssue.Title, new[]
{
    N.AmmTaskIssue.IssueId, N.AmmTaskIssue.DocId, N.AmmTaskIssue.IssueLetter, N.AmmTaskIssue.Supersedes, N.AmmTaskIssue.IssueReason, N.AmmTaskIssue.RaisedByPawo, N.AmmTaskIssue.RaisedByFinding, N.AmmTaskIssue.FindingType, N.AmmTaskIssue.IsLatest, N.AmmTaskIssue.Title, N.AmmTaskIssue.Abstract, N.AmmTaskIssue.ProgramLetter, N.AmmTaskIssue.Revision, N.AmmTaskIssue.Status, N.AmmTaskIssue.Ata2d, N.AmmTaskIssue.Ata4d, N.AmmTaskIssue.Ata6d, N.AmmTaskIssue.Keywords, N.AmmTaskIssue.PurposeText, N.AmmTaskIssue.JobSetupNoteText, N.AmmTaskIssue.ProcedureText, N.AmmTaskIssue.CloseOutText,
});

object ConsultIpcFigureIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "illustrated-parts figure issue", N.IpcFigureIssue.Title, new[]
{
    N.IpcFigureIssue.IssueId, N.IpcFigureIssue.DocId, N.IpcFigureIssue.IssueLetter, N.IpcFigureIssue.Supersedes, N.IpcFigureIssue.IssueReason, N.IpcFigureIssue.RaisedByPawo, N.IpcFigureIssue.RaisedByFinding, N.IpcFigureIssue.FindingType, N.IpcFigureIssue.IsLatest, N.IpcFigureIssue.Title, N.IpcFigureIssue.Abstract, N.IpcFigureIssue.ProgramLetter, N.IpcFigureIssue.Revision, N.IpcFigureIssue.Status, N.IpcFigureIssue.Ata2d, N.IpcFigureIssue.Ata4d, N.IpcFigureIssue.Ata6d, N.IpcFigureIssue.Keywords, N.IpcFigureIssue.PurposeText, N.IpcFigureIssue.FigureNoteText,
});

object ConsultSrmRepairIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "structural repair issue", N.SrmRepairIssue.Title, new[]
{
    N.SrmRepairIssue.IssueId, N.SrmRepairIssue.DocId, N.SrmRepairIssue.IssueLetter, N.SrmRepairIssue.Supersedes, N.SrmRepairIssue.IssueReason, N.SrmRepairIssue.RaisedByPawo, N.SrmRepairIssue.RaisedByFinding, N.SrmRepairIssue.FindingType, N.SrmRepairIssue.IsLatest, N.SrmRepairIssue.Title, N.SrmRepairIssue.Abstract, N.SrmRepairIssue.ProgramLetter, N.SrmRepairIssue.Revision, N.SrmRepairIssue.Status, N.SrmRepairIssue.Ata2d, N.SrmRepairIssue.Ata4d, N.SrmRepairIssue.Ata6d, N.SrmRepairIssue.Keywords, N.SrmRepairIssue.PurposeText, N.SrmRepairIssue.DamageAssessmentText, N.SrmRepairIssue.RepairProcedureText, N.SrmRepairIssue.InspectionRequirementsText,
});

object ConsultTsmProcedureIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "troubleshooting procedure issue", N.TsmProcedureIssue.Title, new[]
{
    N.TsmProcedureIssue.IssueId, N.TsmProcedureIssue.DocId, N.TsmProcedureIssue.IssueLetter, N.TsmProcedureIssue.Supersedes, N.TsmProcedureIssue.IssueReason, N.TsmProcedureIssue.RaisedByPawo, N.TsmProcedureIssue.RaisedByFinding, N.TsmProcedureIssue.FindingType, N.TsmProcedureIssue.IsLatest, N.TsmProcedureIssue.Title, N.TsmProcedureIssue.Abstract, N.TsmProcedureIssue.ProgramLetter, N.TsmProcedureIssue.Revision, N.TsmProcedureIssue.Status, N.TsmProcedureIssue.Ata2d, N.TsmProcedureIssue.Ata4d, N.TsmProcedureIssue.Ata6d, N.TsmProcedureIssue.Keywords, N.TsmProcedureIssue.PurposeText, N.TsmProcedureIssue.FaultDescriptionText, N.TsmProcedureIssue.IsolationProcedureText, N.TsmProcedureIssue.DispatchNoteText,
});

object ConsultWdmSheetIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "wiring diagram sheet issue", N.WdmSheetIssue.Title, new[]
{
    N.WdmSheetIssue.IssueId, N.WdmSheetIssue.DocId, N.WdmSheetIssue.IssueLetter, N.WdmSheetIssue.Supersedes, N.WdmSheetIssue.IssueReason, N.WdmSheetIssue.RaisedByPawo, N.WdmSheetIssue.RaisedByFinding, N.WdmSheetIssue.FindingType, N.WdmSheetIssue.IsLatest, N.WdmSheetIssue.Title, N.WdmSheetIssue.Abstract, N.WdmSheetIssue.ProgramLetter, N.WdmSheetIssue.Revision, N.WdmSheetIssue.Status, N.WdmSheetIssue.Ata2d, N.WdmSheetIssue.Ata4d, N.WdmSheetIssue.Ata6d, N.WdmSheetIssue.Keywords, N.WdmSheetIssue.PurposeText, N.WdmSheetIssue.SheetNoteText, N.WdmSheetIssue.RoutingNoteText,
});

object ConsultFcomEntryIssue(ToolScope scope, UID128 uid) => Document(scope, uid, "flight-crew entry issue", N.FcomEntryIssue.Title, new[]
{
    N.FcomEntryIssue.IssueId, N.FcomEntryIssue.DocId, N.FcomEntryIssue.IssueLetter, N.FcomEntryIssue.Supersedes, N.FcomEntryIssue.IssueReason, N.FcomEntryIssue.RaisedByPawo, N.FcomEntryIssue.RaisedByFinding, N.FcomEntryIssue.FindingType, N.FcomEntryIssue.IsLatest, N.FcomEntryIssue.Title, N.FcomEntryIssue.Abstract, N.FcomEntryIssue.ProgramLetter, N.FcomEntryIssue.Revision, N.FcomEntryIssue.Status, N.FcomEntryIssue.Ata2d, N.FcomEntryIssue.Ata4d, N.FcomEntryIssue.Ata6d, N.FcomEntryIssue.Keywords, N.FcomEntryIssue.PurposeText, N.FcomEntryIssue.DescriptionText, N.FcomEntryIssue.CrewProcedureText, N.FcomEntryIssue.LimitationNoteText,
});