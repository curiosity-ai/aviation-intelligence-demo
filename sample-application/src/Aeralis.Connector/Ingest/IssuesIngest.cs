using Curiosity.Library;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using DocNode = Curiosity.Library.Node;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// The issue history of every document: one node per issue, hung off the document it belongs to
/// and off the finding that required it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentsIngest"/> maps the document as it stands, which is the last issue in its
/// file. This maps every issue, including that one, so the history is addressable: what the
/// maintenance task said before the modification, what it says now, and which finding of which
/// closed work order moved it.
/// </para>
/// <para>
/// That is what makes a pre-analysis falsifiable. A work order claims it found an impact on a
/// document; the issue raised against that finding is the evidence it was acted on, and the
/// issue before it is what the finding was complaining about.
/// </para>
/// </remarks>
internal static class IssuesIngest
{
    public const int Version = 2;   // bumped: an issue now links to the Status it was in

    /// <summary>The identity of an issue: the document and the letter together.</summary>
    public static string IssueId(string docId, string letter) => $"{docId}:{letter}";

    /// <summary>The issue node type that holds one issue of each document type.</summary>
    private static string NodeType(DocType docType) => docType switch
    {
        DocType.MOD => nameof(DocumentIssueSchema.ModificationIssue),
        DocType.SB => nameof(DocumentIssueSchema.ServiceBulletinIssue),
        DocType.IO => nameof(DocumentIssueSchema.InternalOrderIssue),
        DocType.PAWO => nameof(DocumentIssueSchema.PreAnalysisWorkOrderIssue),
        DocType.AMM => nameof(DocumentIssueSchema.AmmTaskIssue),
        DocType.IPC => nameof(DocumentIssueSchema.IpcFigureIssue),
        DocType.SRM => nameof(DocumentIssueSchema.SrmRepairIssue),
        DocType.TSM => nameof(DocumentIssueSchema.TsmProcedureIssue),
        DocType.WDM => nameof(DocumentIssueSchema.WdmSheetIssue),
        DocType.FCOM => nameof(DocumentIssueSchema.FcomEntryIssue),
        _ => "",
    };

    /// <summary>The document node type an issue belongs to.</summary>
    private static string DocumentNodeType(DocType docType) => docType switch
    {
        DocType.MOD => nameof(DocumentSchema.Modification),
        DocType.SB => nameof(DocumentSchema.ServiceBulletin),
        DocType.IO => nameof(DocumentSchema.InternalOrder),
        DocType.PAWO => nameof(DocumentSchema.PreAnalysisWorkOrder),
        DocType.AMM => nameof(DocumentSchema.AmmTask),
        DocType.IPC => nameof(DocumentSchema.IpcFigure),
        DocType.SRM => nameof(DocumentSchema.SrmRepair),
        DocType.TSM => nameof(DocumentSchema.TsmProcedure),
        DocType.WDM => nameof(DocumentSchema.WdmSheet),
        DocType.FCOM => nameof(DocumentSchema.FcomEntry),
        _ => "",
    };

    public static async Task RunAsync(ConnectorContext ctx)
    {
        var set = DatasetLoader.LoadDocuments(ctx.DatasetRoot);

        foreach (var loaded in set.Issues)
        {
            var doc = loaded.Envelope;
            if (NodeType(doc.DocType).Length == 0) continue;

            var fingerprint = new
            {
                doc.DocId,
                Type = doc.DocType.ToString(),
                Issue = loaded.Issue,
                doc.Revision,
                doc.Title,
                loaded.IsLatest,
                Reason = doc.Issue?.Reason,
                Raised = doc.Issue?.RaisedByPawo,
            };

            ctx.Cache.IfNotCached(Version, fingerprint, _ => Map(ctx, loaded));
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }

    private static void Map(ConnectorContext ctx, LoadedDocument loaded)
    {
        var doc = loaded.Envelope;
        var issue = doc.Issue;
        var letter = loaded.Issue;
        var id = IssueId(doc.DocId, letter);

        var node = doc.DocType switch
        {
            DocType.MOD => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.ModificationIssue(), loaded,
                m => { m.ReasonText = Section(doc, "reason"); m.DescriptionText = Section(doc, "description");
                       m.EffectsText = Section(doc, "effects");
                       m.EmbodimentNoteText = Section(doc, "embodiment_note");
                       m.OpenPointsText = Section(doc, "open_points"); })),
            DocType.SB => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.ServiceBulletinIssue(), loaded,
                m => { m.PlanningReasonText = Section(doc, "planning_reason");
                       m.DescriptionText = Section(doc, "description");
                       m.AccomplishmentSummaryText = Section(doc, "accomplishment_summary");
                       m.EffectivityNoteText = Section(doc, "effectivity_note");
                       m.MaterialNoteText = Section(doc, "material_note"); })),
            DocType.IO => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.InternalOrderIssue(), loaded,
                m => { m.RequestText = Section(doc, "request"); m.BackgroundText = Section(doc, "background");
                       m.ExpectedOutcomeText = Section(doc, "expected_outcome"); })),
            DocType.PAWO => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.PreAnalysisWorkOrderIssue(), loaded,
                m => { m.ObjectiveText = Section(doc, "objective");
                       m.FindingsSummaryText = Section(doc, "findings_summary");
                       m.NextStepsText = Section(doc, "next_steps");
                       m.ScopeNoteText = Section(doc, "scope_note");
                       m.MethodNoteText = Section(doc, "method_note"); })),
            DocType.AMM => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.AmmTaskIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.JobSetupNoteText = Section(doc, "job_setup_note");
                       m.ProcedureText = Section(doc, "procedure");
                       m.CloseOutText = Section(doc, "close_out"); })),
            DocType.IPC => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.IpcFigureIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.FigureNoteText = Section(doc, "figure_note"); })),
            DocType.SRM => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.SrmRepairIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.DamageAssessmentText = Section(doc, "damage_assessment");
                       m.RepairProcedureText = Section(doc, "repair_procedure");
                       m.InspectionRequirementsText = Section(doc, "inspection_requirements"); })),
            DocType.TSM => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.TsmProcedureIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.FaultDescriptionText = Section(doc, "fault_description");
                       m.IsolationProcedureText = Section(doc, "isolation_procedure");
                       m.DispatchNoteText = Section(doc, "dispatch_note"); })),
            DocType.WDM => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.WdmSheetIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.SheetNoteText = Section(doc, "sheet_note");
                       m.RoutingNoteText = Section(doc, "routing_note"); })),
            DocType.FCOM => ctx.Graph.AddOrUpdate(Fill(new DocumentIssueSchema.FcomEntryIssue(), loaded,
                m => { m.PurposeText = Section(doc, "purpose");
                       m.DescriptionText = Section(doc, "description");
                       m.CrewProcedureText = Section(doc, "crew_procedure");
                       m.LimitationNoteText = Section(doc, "limitation_note"); })),
            _ => null,
        };

        if (node is null) return;

        var document = DocNode.FromKey(DocumentNodeType(doc.DocType), doc.DocId);
        ctx.Graph.Link(node, document, Edges.IssueOfDocument, Edges.HasIssue);
        ctx.CountNode(NodeType(doc.DocType));
        ctx.CountEdge(Edges.HasIssue);

        // The issue it replaced, so the history is a chain and not a bag.
        if (!string.IsNullOrEmpty(issue?.Supersedes))
        {
            ctx.Graph.Link(node,
                DocNode.FromKey(NodeType(doc.DocType), IssueId(doc.DocId, issue.Supersedes)),
                Edges.SupersedesIssue, Edges.SupersededByIssue);
            ctx.CountEdge(Edges.SupersedesIssue);
        }

        // The finding that required it. This is the link that makes a pre-analysis
        // falsifiable: the work order claimed an impact, and this is the issue that answered it.
        if (!string.IsNullOrEmpty(issue?.RaisedByPawo) && !string.IsNullOrEmpty(issue.RaisedByFinding))
        {
            var findingKey = $"{issue.RaisedByPawo}#{issue.RaisedByFinding}";
            ctx.Graph.Link(node, DocNode.FromKey(nameof(DocumentSchema.ExpectedFinding), findingKey),
                Edges.IssueRaisedBy, Edges.RequiredIssue);
            ctx.CountEdge(Edges.IssueRaisedBy);
        }

        MetaLinks.Ata(ctx, node, doc.Ata?.Ata6d, doc.Ata?.Ata4d, doc.Ata?.Ata2d);
        ctx.LinkKey(node, nameof(ProgramsIngest.Program), doc.Program.ToString(),
                    Edges.InProgram, Edges.HasDocument);

        // The status the document was in *at this issue*, which is the whole reason an issue
        // is a node: the document node only carries the state it is in now, so "what was
        // still a draft when the modification launched" is a question only the issues answer.
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.DocumentStatus, doc.Status);
    }

    /// <summary>
    /// Fills the members every issue node shares, then lets the caller add the prose of its own
    /// type. Written this way because the section names differ per type and nothing else does.
    /// </summary>
    private static T Fill<T>(T node, LoadedDocument loaded, Action<T> sections) where T : class
    {
        var doc = loaded.Envelope;
        var issue = doc.Issue;
        dynamic target = node;

        target.IssueId = IssueId(doc.DocId, loaded.Issue);
        target.DocId = doc.DocId;
        target.IssueLetter = loaded.Issue;
        target.Supersedes = issue?.Supersedes ?? "";
        target.IssueReason = issue?.Reason ?? "";
        target.RaisedByPawo = issue?.RaisedByPawo ?? "";
        target.RaisedByFinding = issue?.RaisedByFinding ?? "";
        target.FindingType = issue?.FindingType.ToString() ?? "";
        target.IsLatest = loaded.IsLatest;

        target.Title = doc.Title;
        target.Abstract = doc.Abstract;
        target.ProgramLetter = doc.Program.ToString();
        target.Revision = doc.Revision ?? "";
        target.Status = doc.Status ?? "";
        target.Ata2d = doc.Ata?.Ata2d ?? "";
        target.Ata4d = doc.Ata?.Ata4d ?? "";
        target.Ata6d = doc.Ata?.Ata6d ?? "";
        target.Keywords = string.Join(", ", doc.Keywords);
        target.Timestamp = Stamp(issue?.Date ?? doc.Dates?.Released ?? doc.Dates?.Created);

        sections(node);
        return node;
    }

    private static string Section(TechDocument doc, string key) =>
        doc.Sections.TryGetValue(key, out var value) ? value : "";

    private static DateTimeOffset Stamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;
}
