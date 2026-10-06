using Curiosity.Library;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using DocNode = Curiosity.Library.Node;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// Every document under `data/documents/`. One handler because all nine types share the
/// envelope; the per-type payload work is one method each.
///
/// The DTOs are <c>Aeralis.Dataset</c>'s models — that project exists precisely to be
/// the read model for this data, and is already the gate the dataset is validated
/// against, so re-declaring the envelope here would create a second thing to keep in
/// sync. The graph schema classes live in <see cref="DocumentSchema"/>.
/// </summary>
internal static class DocumentsIngest
{
    public const int Version = 4;   // bumped: status fields link to Status instead of Classification

    /// <param name="assignments">
    /// What <see cref="AssignmentPass.Plan"/> decided for each work order. It is applied here,
    /// while the node is being created, rather than by a second pass writing over it: the graph
    /// applies a schema object as the node's whole content, so a later call carrying only the
    /// workflow fields would blank the title, the abstract and every section text this pass
    /// writes. The ownership itself is not written here at all — it is an edge.
    /// </param>
    public static async Task RunAsync(ConnectorContext ctx,
                                      IReadOnlyDictionary<string, AssignmentPass.Assignment> assignments)
    {
        var set = DatasetLoader.LoadDocuments(ctx.DatasetRoot);

        foreach (var problem in set.Problems.Take(10))
            Console.WriteLine($"  document load problem: {problem}");

        foreach (var loaded in set.Documents)
        {
            var doc = loaded.Envelope;
            var assignment = doc.DocType == DocType.PAWO
                ? assignments.GetValueOrDefault(doc.DocId, AssignmentPass.None)
                : AssignmentPass.None;

            // Hash the identity + revision + timestamps: enough to notice a re-authored
            // document, cheap enough not to re-serialise the whole envelope every run.
            var fingerprint = new
            {
                doc.DocId,
                Type = doc.DocType.ToString(),
                doc.Revision,
                doc.Status,
                Created = doc.Dates?.Created,
                Released = doc.Dates?.Released,
                Revised = doc.Dates?.Revised,
                doc.Title,
                Abstract = doc.Abstract.Length,
                Entities = doc.Entities?.TotalCount ?? 0,
                Refs = doc.References.Count,

                // Re-tuning the staffing model has to re-write the work orders it moved, and
                // the cache would otherwise skip them: nothing in the document itself changed.
                Assignment = assignment.Fingerprint,
            };

            ctx.Cache.IfNotCached(Version, fingerprint, _ => Map(ctx, loaded, assignment));
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }

    private static void Map(ConnectorContext ctx, LoadedDocument loaded, AssignmentPass.Assignment assignment)
    {
        var doc = loaded.Envelope;
        var node = doc.DocType switch
        {
            DocType.MOD => MapMod(ctx, doc, loaded.Payload as ModData),
            DocType.SB => MapSb(ctx, doc, loaded.Payload as ServiceBulletinData),
            DocType.IO => MapIo(ctx, doc, loaded.Payload as InternalOrderData),
            DocType.PAWO => MapPawo(ctx, doc, loaded.Payload as PreAnalysisWorkOrderData, assignment),
            DocType.AMM => MapAmm(ctx, doc, loaded.Payload as AmmTaskData),
            DocType.IPC => MapIpc(ctx, doc, loaded.Payload as IpcFigureData),
            DocType.SRM => MapSrm(ctx, doc, loaded.Payload as SrmRepairData),
            DocType.TSM => MapTsm(ctx, doc, loaded.Payload as TsmProcedureData),
            DocType.WDM => MapWdm(ctx, doc, loaded.Payload as WdmSheetData),
            DocType.FCOM => MapFcom(ctx, doc, loaded.Payload as FcomEntryData),
            _ => null,
        };

        if (node is null) return;

        LinkEnvelope(ctx, node, doc);
    }

    // -------------------------------------------------------------------------
    // shared envelope handling
    // -------------------------------------------------------------------------

    private static string Section(TechDocument doc, string key)
        => doc.Sections.TryGetValue(key, out var value) ? value : "";

    private static string Prog(TechDocument doc) => doc.Program.ToString();

    private static DateTimeOffset Stamp(TechDocument doc)
    {
        var value = doc.Dates?.Released ?? doc.Dates?.Created;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;
    }

    private static DateTimeOffset? Date(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>
    /// The links every document carries regardless of type: program, applicability, ATA,
    /// entities and explicit document references. Structured source fields only — nothing
    /// here is inferred from prose.
    /// </summary>
    private static void LinkEnvelope(ConnectorContext ctx, DocNode node, TechDocument doc)
    {
        var program = Prog(doc);
        ctx.LinkKey(node, nameof(ProgramsIngest.Program), program, Edges.InProgram, Edges.HasDocument);

        var ata = doc.Ata;
        if (ata is object) MetaLinks.Ata(ctx, node, ata.Ata6d, ata.Ata4d, ata.Ata2d);

        foreach (var secondary in doc.SecondaryAta)
            ctx.LinkKey(node, nameof(AtaChaptersIngest.AtaChapter), secondary,
                        Edges.AlsoImpactsAta, Edges.AlsoImpactedBy);

        // Dimensions that used to be dropped or flattened to a string.
        MetaLinks.Originator(ctx, node, doc.Originator?.Department, doc.Originator?.Site);
        MetaLinks.Keywords(ctx, node, doc.Keywords);
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.DocumentStatus, doc.Status);

        var appl = doc.Applicability;
        if (appl is object)
        {
            ctx.LinkKeys(node, nameof(ProgramsIngest.AircraftType), appl.AircraftTypes,
                         Edges.AppliesToType, Edges.HasApplicableDocument);
            ctx.LinkKeys(node, nameof(ProgramsIngest.AircraftModel), appl.Models,
                         Edges.AppliesToModel, Edges.HasApplicableDocument);
            ctx.LinkKeys(node, nameof(MsnIngest.Msn), appl.Msns,
                         Edges.AppliesToMsn, Edges.HasApplicableDocument);
            ctx.LinkKeys(node, nameof(MsnIngest.Msn), appl.ExcludedMsns,
                         Edges.ExcludesMsn, Edges.ExcludedByDocument);
            ctx.LinkKeys(node, nameof(OperatorsIngest.Operator), appl.Operators,
                         Edges.AppliesToOperator, Edges.HasApplicableDocument);
            MetaLinks.EngineTypes(ctx, node, appl.EngineTypes);

            foreach (var range in appl.MsnRanges)
            {
                if (string.IsNullOrWhiteSpace(range.From) || string.IsNullOrWhiteSpace(range.To)) continue;
                var rangeNode = ctx.Graph.AddOrUpdate(new DocumentSchema.MsnRangeApplicability
                {
                    RangeKey = $"{doc.DocId}#{range.From}-{range.To}",
                    DocId = doc.DocId,
                    FromMsn = range.From,
                    ToMsn = range.To,
                    ProgramLetter = program,
                });
                ctx.CountNode(nameof(DocumentSchema.MsnRangeApplicability));
                ctx.Graph.Link(node, rangeNode, Edges.AppliesToMsn, Edges.HasApplicableDocument);
                ctx.CountEdge(Edges.AppliesToMsn);

                // The two aircraft the range is bounded by are aircraft, not two strings.
                ctx.LinkKey(rangeNode, nameof(MsnIngest.Msn), range.From, Edges.RangeFromMsn, Edges.StartsRange);
                ctx.LinkKey(rangeNode, nameof(MsnIngest.Msn), range.To, Edges.RangeToMsn, Edges.EndsRange);
            }

            var cond = appl.EmbodimentCondition;
            if (cond is object)
            {
                ctx.LinkKeys(node, nameof(DocumentSchema.Modification), cond.PostMod,
                             Edges.PostModCondition, Edges.PostModConditionOf);
                ctx.LinkKeys(node, nameof(DocumentSchema.Modification), cond.PreMod,
                             Edges.PreModCondition, Edges.PreModConditionOf);
                ctx.LinkKeys(node, nameof(DocumentSchema.ServiceBulletin), cond.PostSb,
                             Edges.PostModCondition, Edges.PostModConditionOf);
                ctx.LinkKeys(node, nameof(DocumentSchema.ServiceBulletin), cond.PreSb,
                             Edges.PreModCondition, Edges.PreModConditionOf);
            }
        }

        var ent = doc.Entities;
        if (ent is object)
        {
            ctx.LinkKeys(node, nameof(PartNumbersIngest.PartNumber), ent.PartNumbers,
                         Edges.UsesPart, Edges.UsedByDocument);
            ctx.LinkKeys(node, nameof(FinsIngest.Fin), ent.Fins, Edges.UsesFin, Edges.UsedByDocument);
            ctx.LinkKeys(node, nameof(DrawingsIngest.Drawing), ent.Drawings,
                         Edges.UsesDrawing, Edges.UsedByDocument);
            ctx.LinkKeys(node, nameof(SoftwareIngest.SoftwareLoad), ent.Software,
                         Edges.UsesSoftware, Edges.UsedByDocument);
            ctx.LinkKeys(node, nameof(ZonesIngest.Zone), ent.Zones, Edges.InZone, Edges.HasZoneDocument);
            ctx.LinkKeys(node, nameof(MsnIngest.Msn), ent.MsnMentions,
                         Edges.MentionsMsn, Edges.MentionedByDocument);
            ctx.LinkKeys(node, nameof(DocumentSchema.RepairDrawing), ent.RepairDrawings,
                         Edges.HasRepairDrawing, Edges.RepairDrawingOf);
        }

        foreach (var reference in doc.References)
        {
            if (string.IsNullOrWhiteSpace(reference.DocId)) continue;
            var targetType = NodeTypeFor(reference.DocType);
            if (targetType is null) continue;

            var (forward, reverse) = EdgesFor(reference.Relation);
            ctx.LinkKey(node, targetType, reference.DocId, forward, reverse);
        }
    }

    private static string? NodeTypeFor(DocType type) => type switch
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
        _ => null,
    };

    /// <summary>Maps a source relation onto its edge pair. Direction matters and is preserved.</summary>
    private static (string Forward, string Reverse) EdgesFor(RelationType relation) => relation switch
    {
        RelationType.SUPERSEDES => (Edges.SupersedesMod, Edges.SupersededByMod),
        RelationType.SUPERSEDED_BY => (Edges.SupersededByMod, Edges.SupersedesMod),
        RelationType.EMBODIED_BY => (Edges.EmbodiedBy, Edges.Embodies),
        RelationType.EMBODIES => (Edges.Embodies, Edges.EmbodiedBy),
        RelationType.REQUIRES => (Edges.RequiresMod, Edges.PrerequisiteOf),
        RelationType.PREREQUISITE_OF => (Edges.PrerequisiteOf, Edges.RequiresMod),
        RelationType.CONCURRENT_WITH => (Edges.ConcurrentWith, Edges.ConcurrentWith),
        RelationType.IMPACTS => (Edges.Impacts, Edges.ImpactedBy),
        RelationType.DOCUMENTED_IN => (Edges.DocumentedIn, Edges.DocumentsItem),
        RelationType.RAISED_BY => (Edges.RaisedBy, Edges.CreatedMod),
        RelationType.CREATED => (Edges.CreatedMod, Edges.RaisedBy),
        RelationType.PRE_ANALYSIS_OF => (Edges.PreAnalysisOf, Edges.HasPreAnalysis),
        RelationType.REPLACES_PART_IN => (Edges.Impacts, Edges.ImpactedBy),
        _ => (Edges.ReferencesDoc, Edges.ReferencedByDoc),
    };

    // -------------------------------------------------------------------------
    // per-type mapping
    // -------------------------------------------------------------------------

    private static DocNode MapMod(ConnectorContext ctx, TechDocument doc, ModData? d)
    {
        var status = doc.Embodiment?.Status ?? EmbodimentStatus.Unknown;
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.Modification
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Revision = doc.Revision ?? "",
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            ReasonText = Section(doc, "reason"),
            DescriptionText = Section(doc, "description"),
            EffectsText = Section(doc, "effects"),
            EmbodimentNoteText = Section(doc, "embodiment_note"),
            OpenPointsText = Section(doc, "open_points"),
            ModNumber = d?.ModNumber ?? 0,
            ModClass = d?.ModClass.ToString() ?? "",
            ChangeCategory = d?.ChangeCategory.ToString() ?? "",
            Trigger = d?.Trigger.ToString() ?? "",
            EmbodimentStatus = status.ToString(),
            IsCandidate = status is EmbodimentStatus.PROPOSAL or EmbodimentStatus.PRE_ANALYSIS
                                    or EmbodimentStatus.LAUNCHED,
            WeightImpactKg = d?.WeightImpactKg ?? 0,
            EstimatedManhours = d?.EstimatedManhours ?? 0,
            EstimatedCostKeur = d?.EstimatedCostKeur ?? 0,
            RiskLevel = d?.RiskLevel.ToString() ?? "",
            CertificationImpact = d?.Certification?.Impact.ToString() ?? "",
            Interchangeability = d?.Interchangeability.ToString() ?? "",
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.Modification));

        if (d is object)
        {
            ctx.LinkKeys(node, nameof(PartNumbersIngest.PartNumber), d.PartsAdded, Edges.AddsPart, Edges.AddedByMod);
            ctx.LinkKeys(node, nameof(PartNumbersIngest.PartNumber), d.PartsRemoved, Edges.RemovesPart, Edges.RemovedByMod);
            ctx.LinkKeys(node, nameof(PartNumbersIngest.PartNumber), d.PartsModified, Edges.ModifiesPart, Edges.ModifiedByMod);
        }

        MetaLinks.ModNumber(ctx, node, d?.ModNumber ?? 0);

        var mod = doc.Embodiment?.Status ?? EmbodimentStatus.Unknown;
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.EmbodimentStatus, mod.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.ChangeCategory, d?.ChangeCategory.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.ModTrigger, d?.Trigger.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.ModClass, d?.ModClass.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.RiskLevel, d?.RiskLevel.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.CertificationImpact, d?.Certification?.Impact.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.Interchangeability, d?.Interchangeability.ToString());

        foreach (var sb in doc.Embodiment?.RetrofitVia ?? [])
            ctx.LinkKey(node, nameof(DocumentSchema.ServiceBulletin), sb, Edges.EmbodiedBy, Edges.Embodies);

        var effectivity = doc.Embodiment?.ProductionEffectivity;
        if (!string.IsNullOrWhiteSpace(effectivity))
            ctx.LinkKey(node, nameof(MsnIngest.Msn), effectivity, Edges.AppliesToMsn, Edges.HasApplicableDocument);

        return node;
    }

    private static DocNode MapSb(ConnectorContext ctx, TechDocument doc, ServiceBulletinData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.ServiceBulletin
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Revision = doc.Revision ?? "",
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PlanningReasonText = Section(doc, "planning_reason"),
            DescriptionText = Section(doc, "description"),
            AccomplishmentSummaryText = Section(doc, "accomplishment_summary"),
            EffectivityNoteText = Section(doc, "effectivity_note"),
            MaterialNoteText = Section(doc, "material_note"),
            SbType = d?.SbType.ToString() ?? "",
            Compliance = d?.Compliance.ToString() ?? "",
            ReasonCode = d?.ReasonCode.ToString() ?? "",
            AdLinked = d?.AdLinked ?? false,
            AdReference = d?.AdReference ?? "",
            Manhours = d?.Manpower?.Manhours ?? 0,
            DowntimeCategory = d?.Manpower?.DowntimeCategory.ToString() ?? "",
            KitNumber = d?.Material?.KitNumber ?? "",
            WeightChangeKg = d?.WeightChangeKg ?? 0,
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.ServiceBulletin));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.SbType, d?.SbType.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.Compliance, d?.Compliance.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.SbReasonCode, d?.ReasonCode.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.DowntimeCategory, d?.Manpower?.DowntimeCategory.ToString());
        MetaLinks.Directive(ctx, node, d?.AdReference);
        MetaLinks.Kit(ctx, node, d?.Material?.KitNumber);

        if (d is object)
        {
            foreach (var part in d.Material?.PartsRequired ?? [])
                ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), part.PartNumber, Edges.UsesPart, Edges.UsedByDocument);
            foreach (var part in d.Material?.PartsReturned ?? [])
                ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), part.PartNumber, Edges.RemovesPart, Edges.RemovedByMod);

            foreach (var revision in d.RevisionHistory)
            {
                var revNode = ctx.Graph.AddOrUpdate(new DocumentSchema.SbRevision
                {
                    RevisionKey = $"{doc.DocId}#{revision.Revision}",
                    SbId = doc.DocId,
                    Revision = revision.Revision,
                    Reason = revision.Reason ?? "",
                    Date = Date(revision.Date) ?? Stamp(doc),
                });
                ctx.CountNode(nameof(DocumentSchema.SbRevision));
                ctx.Graph.Link(node, revNode, Edges.HasRevision, Edges.RevisionOf);
                ctx.CountEdge(Edges.HasRevision);
            }
        }

        return node;
    }

    private static DocNode MapIo(ConnectorContext ctx, TechDocument doc, InternalOrderData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.InternalOrder
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            RequestText = Section(doc, "request"),
            BackgroundText = Section(doc, "background"),
            ExpectedOutcomeText = Section(doc, "expected_outcome"),
            RequestCategory = d?.RequestCategory.ToString() ?? "",
            Priority = d?.Priority.ToString() ?? "",
            IoStatus = d?.IoStatus.ToString() ?? "",
            RequesterType = d?.RaisedBy?.RequesterType.ToString() ?? "",
            Funding = d?.Commercial?.Funding.ToString() ?? "",
            BudgetKeur = d?.Commercial?.BudgetKeur ?? 0,
            AircraftCount = d?.FleetScope?.AircraftCount ?? 0,
            ClosureNote = d?.ClosureNote ?? "",
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.InternalOrder));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.RequestCategory, d?.RequestCategory.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.Priority, d?.Priority.ToString());
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.IoStatus, d?.IoStatus.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.Funding, d?.Commercial?.Funding.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.RequesterType, d?.RaisedBy?.RequesterType.ToString());

        if (d is object)
        {
            ctx.LinkKey(node, nameof(OperatorsIngest.Operator), d.RaisedBy?.OperatorCode,
                        Edges.AppliesToOperator, Edges.HasApplicableDocument);
            ctx.LinkKeys(node, nameof(MsnIngest.Msn), d.FleetScope?.Msns,
                         Edges.MentionsMsn, Edges.MentionedByDocument);
            ctx.LinkKey(node, nameof(DocumentSchema.Modification), d.CreatedMod,
                        Edges.CreatedMod, Edges.RaisedBy);
        }

        return node;
    }

    private static DocNode MapPawo(ConnectorContext ctx, TechDocument doc, PreAnalysisWorkOrderData? d,
                                   AssignmentPass.Assignment assignment)
    {
        var findings = d?.ExpectedFindings ?? [];
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.PreAnalysisWorkOrder
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            ObjectiveText = Section(doc, "objective"),
            ScopeNoteText = Section(doc, "scope_note"),
            MethodNoteText = Section(doc, "method_note"),
            FindingsSummaryText = Section(doc, "findings_summary"),
            NextStepsText = Section(doc, "next_steps"),
            SubjectMod = d?.SubjectMod ?? "",
            SourceIo = d?.SourceIo ?? "",
            PawoStatus = d?.PawoStatus.ToString() ?? "",
            Recommendation = d?.Recommendation.ToString() ?? "",
            Conclusion = d?.Conclusion ?? "",
            HeroScenario = d?.HeroScenario ?? false,
            IsHistorical = d?.IsHistorical ?? false,
            AnalysisRound = d?.AnalysisRound ?? 1,
            LeadDepartment = d?.Assigned?.LeadDepartment ?? "",
            LeadSite = d?.Assigned?.LeadSite ?? "",

            Outcome = d?.Outcome.ToString() ?? "",
            OpenedDate = Date(d?.RaisedDate) ?? Date(doc.Dates?.Created),
            CompletedDate = Date(d?.CompletedDate),
            DueDate = Date(d?.DueDate),
            EstimatedManhours = d?.Assigned?.EstimatedManhours ?? 0,

            // Decided by the assignment pass, written here so the node is created complete.
            WorkflowStatus = assignment.WorkflowStatus,
            IsUrgent = assignment.IsUrgent,
            HoursSpent = assignment.HoursSpent,

            FindingCount = findings.Count,
            BlockingFindingCount = findings.Count(f => f.Severity == Severity.BLOCKING),
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.PreAnalysisWorkOrder));

        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.PawoStatus, d?.PawoStatus.ToString());
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.Outcome, d?.Outcome.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.Recommendation, d?.Recommendation.ToString());
        MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.WorkflowStatus, assignment.WorkflowStatus);
        MetaLinks.Originator(ctx, node, d?.Assigned?.LeadDepartment, d?.Assigned?.LeadSite);

        if (d is object)
        {
            // The subject's modification number, so the dossier and every pre-analysis of
            // it gather on the one node rather than only pointing at each other.
            MetaLinks.ModNumber(ctx, node, ModNumberOf(d.SubjectMod));

            ctx.LinkKey(node, nameof(DocumentSchema.Modification), d.SubjectMod,
                        Edges.PreAnalysisOf, Edges.HasPreAnalysis);
            ctx.LinkKey(node, nameof(DocumentSchema.InternalOrder), d.SourceIo,
                        Edges.FromOrder, Edges.HasWorkOrder);

            foreach (var finding in findings)
            {
                var evidence = finding.Evidence;
                var findingNode = ctx.Graph.AddOrUpdate(new DocumentSchema.ExpectedFinding
                {
                    FindingKey = $"{doc.DocId}#{finding.FindingId}",
                    FindingId = finding.FindingId,
                    PawoId = doc.DocId,
                    TargetDocId = finding.DocId,
                    TargetDocType = finding.DocType.ToString(),
                    FindingType = finding.FindingType.ToString(),
                    Severity = finding.Severity.ToString(),
                    Discovery = finding.Discovery.ToString(),
                    Rationale = finding.Rationale ?? "",
                    Resolution = finding.Resolution ?? "",
                    SimilarityNote = evidence?.SimilarityNote ?? "",
                    IsSemanticOnly = evidence?.IsSemanticOnly ?? false,
                    ProgramLetter = Prog(doc),
                });
                ctx.CountNode(nameof(DocumentSchema.ExpectedFinding));

                ctx.Graph.Link(node, findingNode, Edges.HasFinding, Edges.FindingOf);
                ctx.CountEdge(Edges.HasFinding);

                var targetType = NodeTypeFor(finding.DocType);
                if (targetType is object)
                    ctx.LinkKey(findingNode, targetType, finding.DocId,
                                Edges.FindingTargets, Edges.TargetedByFinding);
            }
        }

        return node;
    }

    private static DocNode MapAmm(ConnectorContext ctx, TechDocument doc, AmmTaskData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.AmmTask
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            JobSetupNoteText = Section(doc, "job_setup_note"),
            ProcedureText = Section(doc, "procedure"),
            CloseOutText = Section(doc, "close_out"),
            TaskNumber = d?.TaskNumber ?? "",
            TaskType = d?.TaskType.ToString() ?? "",
            Manhours = d?.Manhours ?? 0,
            Personnel = d?.Personnel ?? 0,
            Steps = d?.Steps ?? 0,
            Warnings = d?.Warnings ?? 0,
            Cautions = d?.Cautions ?? 0,
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.AmmTask));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.AmmTaskType, d?.TaskType.ToString());

        if (d is object)
        {
            ctx.LinkKeys(node, nameof(ZonesIngest.Zone), d.Access?.Zones, Edges.InZone, Edges.HasZoneDocument);
            ctx.LinkKeys(node, nameof(ZonesIngest.AccessPanel), d.Access?.AccessPanels,
                         Edges.HasAccessPanel, Edges.AccessPanelOf);
            ctx.LinkKeys(node, nameof(DocumentSchema.AmmTask), d.ReferencedTasks,
                         Edges.ReferencesDoc, Edges.ReferencedByDoc);
            foreach (var expendable in d.JobSetup?.Expendables ?? [])
                ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), expendable.PartNumber,
                            Edges.UsesPart, Edges.UsedByDocument);
        }

        return node;
    }

    private static DocNode MapIpc(ConnectorContext ctx, TechDocument doc, IpcFigureData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.IpcFigure
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            FigureNoteText = Section(doc, "figure_note"),
            Figure = d?.Figure ?? "",
            FigureTitle = d?.FigureTitle ?? "",
            SheetCount = d?.SheetCount ?? 0,
            NextHigherAssembly = d?.NextHigherAssembly ?? "",
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.IpcFigure));

        // The next higher assembly is a part number in all but ten of the figures, so it is
        // linked as one — that is what makes "which figures sit under this assembly" answerable.
        ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), d?.NextHigherAssembly,
                    Edges.NextHigherAssembly, Edges.AssemblyOfFigure);

        foreach (var item in d?.Items ?? [])
        {
            var itemNode = ctx.Graph.AddOrUpdate(new DocumentSchema.IpcItem
            {
                ItemId = $"{d!.Figure}#{item.Item}",
                Figure = d.Figure,
                ItemName = item.Item,
                PartNumber = item.PartNumber,
                Nomenclature = item.Nomenclature ?? "",
                QtyPerAssy = item.QtyPerAssy,
                Effectivity = item.Effectivity ?? "",
                Vendor = item.Vendor ?? "",
                ProgramLetter = Prog(doc),
            });
            ctx.CountNode(nameof(DocumentSchema.IpcItem));

            ctx.Graph.Link(node, itemNode, Edges.HasItem, Edges.ItemOf);
            ctx.CountEdge(Edges.HasItem);

            ctx.LinkKey(itemNode, nameof(PartNumbersIngest.PartNumber), item.PartNumber,
                        Edges.ItemPart, Edges.ListedInIpcItem);
            ctx.LinkKey(itemNode, nameof(FinsIngest.Fin), item.Fin, Edges.UsesFin, Edges.UsedByDocument);

            // "PRE MOD-166204" / "POST MOD-166204" is the embodiment signal the whole
            // pre/post-mod story rests on, so it becomes a real edge rather than a string.
            var effectivity = item.Effectivity ?? "";
            var modId = ExtractModId(effectivity);
            if (modId is object)
            {
                if (effectivity.StartsWith("PRE", StringComparison.OrdinalIgnoreCase))
                    ctx.LinkKey(itemNode, nameof(DocumentSchema.Modification), modId,
                                Edges.EffectivityPreMod, Edges.PreModEffectivityOf);
                else if (effectivity.StartsWith("POST", StringComparison.OrdinalIgnoreCase))
                    ctx.LinkKey(itemNode, nameof(DocumentSchema.Modification), modId,
                                Edges.EffectivityPostMod, Edges.PostModEffectivityOf);
            }
        }

        return node;
    }

    /// <summary>The digits of a `MOD-166204` identifier, or 0 when it is not one.</summary>
    private static int ModNumberOf(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return 0;
        var digits = new string(modId!.Where(char.IsDigit).ToArray());

        return int.TryParse(digits, out var number) ? number : 0;
    }

    private static string? ExtractModId(string effectivity)
    {
        var index = effectivity.IndexOf("MOD-", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var tail = effectivity[index..];
        var end = 4;
        while (end < tail.Length && char.IsDigit(tail[end])) end++;
        return end > 4 ? tail[..end].ToUpperInvariant() : null;
    }

    private static DocNode MapSrm(ConnectorContext ctx, TechDocument doc, SrmRepairData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.SrmRepair
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            DamageAssessmentText = Section(doc, "damage_assessment"),
            RepairProcedureText = Section(doc, "repair_procedure"),
            InspectionRequirementsText = Section(doc, "inspection_requirements"),
            RepairNumber = d?.RepairNumber ?? "",
            RepairCategory = d?.RepairCategory.ToString() ?? "",
            DamageType = d?.DamageType.ToString() ?? "",
            NdtMethod = d?.NdtMethod.ToString() ?? "",
            WeightPenaltyKg = d?.WeightPenaltyKg ?? 0,
            StaticStrengthRestoredPct = d?.StaticStrengthRestoredPct ?? 0,
            RequiresOemApproval = d?.RequiresOemApproval ?? false,
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.SrmRepair));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.RepairCategory, d?.RepairCategory.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.DamageType, d?.DamageType.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.NdtMethod, d?.NdtMethod.ToString());

        foreach (var drawing in d?.RepairDrawings ?? [])
        {
            var drawingNode = ctx.Graph.AddOrUpdate(new DocumentSchema.RepairDrawing
            {
                Reference = drawing.Reference,
                Title = drawing.Title ?? "",
                Sheets = drawing.Sheets,
                ProgramLetter = Prog(doc),
            });
            ctx.CountNode(nameof(DocumentSchema.RepairDrawing));
            ctx.Graph.Link(node, drawingNode, Edges.HasRepairDrawing, Edges.RepairDrawingOf);
            ctx.CountEdge(Edges.HasRepairDrawing);
        }

        foreach (var material in d?.Materials ?? [])
            ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), material.PartNumber,
                        Edges.UsesPart, Edges.UsedByDocument);

        return node;
    }

    private static DocNode MapTsm(ConnectorContext ctx, TechDocument doc, TsmProcedureData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.TsmProcedure
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            FaultDescriptionText = Section(doc, "fault_description"),
            IsolationProcedureText = Section(doc, "isolation_procedure"),
            DispatchNoteText = Section(doc, "dispatch_note"),
            ProcedureNumber = d?.ProcedureNumber ?? "",
            FaultSymptom = d?.FaultSymptom ?? "",
            FaultCodes = string.Join(", ", d?.FaultCodes ?? []),
            IsolationSteps = d?.IsolationSteps ?? 0,
            MelItem = d?.Dispatch?.MelItem ?? "",
            Dispatchable = d?.Dispatch?.Dispatchable ?? false,
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.TsmProcedure));

        if (d is object)
        {
            ctx.LinkKeys(node, nameof(FinsIngest.Fin), d.AffectedFins, Edges.UsesFin, Edges.UsedByDocument);
            ctx.LinkKey(node, nameof(DocumentSchema.AmmTask), d.ConfirmationTask,
                        Edges.ReferencesDoc, Edges.ReferencedByDoc);

            // A fault code is what two procedures describing the same symptom in different
            // words have in common; the MEL item is what joins a dispatch note to the crew
            // entry carrying the same relief.
            MetaLinks.FaultCodes(ctx, node, d.FaultCodes);
            MetaLinks.Mel(ctx, node, d.Dispatch?.MelItem);
        }

        return node;
    }

    private static DocNode MapWdm(ConnectorContext ctx, TechDocument doc, WdmSheetData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.WdmSheet
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            SheetNoteText = Section(doc, "sheet_note"),
            RoutingNoteText = Section(doc, "routing_note"),
            SheetNumber = d?.SheetNumber ?? "",
            SheetTitle = d?.SheetTitle ?? "",
            DiagramType = d?.DiagramType.ToString() ?? "",
            WireCount = d?.WireCount ?? 0,
            WireGaugeRange = d?.WireGaugeRange ?? "",
            Harnesses = string.Join(", ", d?.Harnesses ?? []),
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.WdmSheet));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.DiagramType, d?.DiagramType.ToString());

        if (d is object)
        {
            foreach (var equipment in d.Equipment)
            {
                ctx.LinkKey(node, nameof(FinsIngest.Fin), equipment.Fin, Edges.UsesFin, Edges.UsedByDocument);
                ctx.LinkKey(node, nameof(ZonesIngest.Zone), equipment.Zone, Edges.InZone, Edges.HasZoneDocument);
            }

            foreach (var connector in d.Connectors)
            {
                ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), connector.PartNumber,
                            Edges.UsesPart, Edges.UsedByDocument);
                ctx.LinkKey(node, nameof(ZonesIngest.Zone), connector.LocationZone,
                            Edges.InZone, Edges.HasZoneDocument);
            }

            ctx.LinkKeys(node, nameof(DocumentSchema.WdmSheet), d.RelatedSheets,
                         Edges.ReferencesDoc, Edges.ReferencedByDoc);

            // Two sheets that draw the same bundle are related whether or not either says so.
            MetaLinks.Harnesses(ctx, node, d.Harnesses);
        }

        return node;
    }

    private static DocNode MapFcom(ConnectorContext ctx, TechDocument doc, FcomEntryData? d)
    {
        var node = ctx.Graph.AddOrUpdate(new DocumentSchema.FcomEntry
        {
            DocId = doc.DocId,
            Title = doc.Title,
            Abstract = doc.Abstract,
            ProgramLetter = Prog(doc),
            Status = doc.Status ?? "",
            Ata2d = doc.Ata?.Ata2d ?? "",
            Ata4d = doc.Ata?.Ata4d ?? "",
            Ata6d = doc.Ata?.Ata6d ?? "",
            Keywords = string.Join(", ", doc.Keywords),
            PurposeText = Section(doc, "purpose"),
            DescriptionText = Section(doc, "description"),
            CrewProcedureText = Section(doc, "crew_procedure"),
            LimitationNoteText = Section(doc, "limitation_note"),
            Section = d?.Section.ToString() ?? "",
            SectionTitle = d?.SectionTitle ?? "",
            EntryNumber = d?.EntryNumber ?? "",
            FlightPhase = d?.FlightPhase.ToString() ?? "",
            OperationalImpact = d?.OperationalImpact.ToString() ?? "",
            CrewAction = d?.CrewAction ?? "",
            AffectsQrh = d?.AffectsQrh ?? false,
            AffectsMel = d?.AffectsMel ?? false,
            Systems = string.Join(", ", d?.Systems ?? []),
            Timestamp = Stamp(doc),
        });
        ctx.CountNode(nameof(DocumentSchema.FcomEntry));

        MetaLinks.Classify(ctx, node, MetaLinks.Kind.FcomSection, d?.Section.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.FlightPhase, d?.FlightPhase.ToString());
        MetaLinks.Classify(ctx, node, MetaLinks.Kind.OperationalImpact, d?.OperationalImpact.ToString());

        if (d is object)
        {
            ctx.LinkKeys(node, nameof(DocumentSchema.FcomEntry), d.RelatedEntries,
                         Edges.ReferencesDoc, Edges.ReferencedByDoc);
            ctx.LinkKey(node, nameof(DocumentSchema.FcomEntry), d.SupersedesEntry,
                        Edges.SupersedesMod, Edges.SupersededByMod);

            // The systems a crew entry covers, on the same nodes the FINs and the loadable
            // software name — which is the only path from a flight-crew limitation to the
            // wiring of the system it limits.
            MetaLinks.Systems(ctx, node, d.Systems);
        }

        return node;
    }
}
