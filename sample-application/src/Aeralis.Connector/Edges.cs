namespace Aeralis.Connector;

/// <summary>
/// Every edge name in the graph, declared in both directions. Curiosity edges are
/// bi-directional and string-named: <c>graph.Link(a, b, AtoB, BtoA)</c> records the
/// relationship from both ends so traversal works either way.
///
/// Registered in one call via <c>CreateEdgeSchemaAsync(typeof(Edges))</c>.
/// </summary>
public static class Edges
{
    // --- program / applicability -------------------------------------------------
    public const string InProgram = nameof(InProgram);
    public const string HasDocument = nameof(HasDocument);

    public const string AppliesToType = nameof(AppliesToType);
    public const string AppliesToModel = nameof(AppliesToModel);
    public const string AppliesToMsn = nameof(AppliesToMsn);
    public const string AppliesToOperator = nameof(AppliesToOperator);
    public const string HasApplicableDocument = nameof(HasApplicableDocument);

    public const string ExcludesMsn = nameof(ExcludesMsn);
    public const string ExcludedByDocument = nameof(ExcludedByDocument);

    public const string MentionsMsn = nameof(MentionsMsn);
    public const string MentionedByDocument = nameof(MentionedByDocument);

    // --- ATA ---------------------------------------------------------------------
    // ATA is linked at all three levels, not just the 6D leaf. A document that only
    // reached its 2D chapter through two ChildOfAta hops is invisible to
    // HasCommonNeighborsWith and to a related-facet on chapter, which is exactly the
    // filtering the search UI and the similarity signals need.
    public const string InAtaChapter = nameof(InAtaChapter);      // the 6D leaf
    public const string HasAtaDocument = nameof(HasAtaDocument);

    public const string InAta4d = nameof(InAta4d);
    public const string HasAta4dDocument = nameof(HasAta4dDocument);

    public const string InAta2d = nameof(InAta2d);
    public const string HasAta2dDocument = nameof(HasAta2dDocument);

    public const string InAtaDomain = nameof(InAtaDomain);
    public const string HasAtaChapter = nameof(HasAtaChapter);

    public const string AlsoImpactsAta = nameof(AlsoImpactsAta);
    public const string AlsoImpactedBy = nameof(AlsoImpactedBy);

    public const string ChildOfAta = nameof(ChildOfAta);
    public const string HasAtaChild = nameof(HasAtaChild);

    // --- entities ----------------------------------------------------------------
    public const string UsesPart = nameof(UsesPart);
    public const string UsedByDocument = nameof(UsedByDocument);

    public const string AddsPart = nameof(AddsPart);
    public const string AddedByMod = nameof(AddedByMod);
    public const string RemovesPart = nameof(RemovesPart);
    public const string RemovedByMod = nameof(RemovedByMod);
    public const string ModifiesPart = nameof(ModifiesPart);
    public const string ModifiedByMod = nameof(ModifiedByMod);

    public const string UsesFin = nameof(UsesFin);
    public const string UsesDrawing = nameof(UsesDrawing);
    public const string UsesSoftware = nameof(UsesSoftware);

    public const string InZone = nameof(InZone);
    public const string HasZoneDocument = nameof(HasZoneDocument);

    // --- document to document ----------------------------------------------------
    public const string SupersedesMod = nameof(SupersedesMod);
    public const string SupersededByMod = nameof(SupersededByMod);

    public const string EmbodiedBy = nameof(EmbodiedBy);
    public const string Embodies = nameof(Embodies);

    public const string RequiresMod = nameof(RequiresMod);
    public const string PrerequisiteOf = nameof(PrerequisiteOf);

    public const string ConcurrentWith = nameof(ConcurrentWith);

    public const string Impacts = nameof(Impacts);
    public const string ImpactedBy = nameof(ImpactedBy);

    public const string DocumentedIn = nameof(DocumentedIn);
    public const string DocumentsItem = nameof(DocumentsItem);

    public const string ReferencesDoc = nameof(ReferencesDoc);
    public const string ReferencedByDoc = nameof(ReferencedByDoc);

    public const string RaisedBy = nameof(RaisedBy);
    public const string CreatedMod = nameof(CreatedMod);

    public const string PreAnalysisOf = nameof(PreAnalysisOf);
    public const string HasPreAnalysis = nameof(HasPreAnalysis);

    public const string FromOrder = nameof(FromOrder);
    public const string HasWorkOrder = nameof(HasWorkOrder);

    public const string PostModCondition = nameof(PostModCondition);
    public const string PostModConditionOf = nameof(PostModConditionOf);
    public const string PreModCondition = nameof(PreModCondition);
    public const string PreModConditionOf = nameof(PreModConditionOf);

    // --- child nodes -------------------------------------------------------------
    public const string HasFinding = nameof(HasFinding);
    public const string FindingOf = nameof(FindingOf);

    public const string FindingTargets = nameof(FindingTargets);
    public const string TargetedByFinding = nameof(TargetedByFinding);

    /// <summary>Written by the front-end's confirm flow, never by the connector.</summary>
    public const string ConfirmedImpact = nameof(ConfirmedImpact);
    public const string ConfirmedImpactOf = nameof(ConfirmedImpactOf);

    /// <summary>
    /// The other half of the same judgement, and just as much worth recording: a candidate
    /// the engineer looked at and ruled out. Without it a dismissed suggestion comes back
    /// looking untouched on the next pass, and there is no way to tell "not yet reviewed"
    /// from "reviewed and rejected" — which is exactly what a pre-analysis has to be able
    /// to say. Written by the front-end, never by the connector.
    /// </summary>
    public const string RejectedImpact = nameof(RejectedImpact);
    public const string RejectedImpactOf = nameof(RejectedImpactOf);

    public const string HasItem = nameof(HasItem);
    public const string ItemOf = nameof(ItemOf);

    public const string ItemPart = nameof(ItemPart);
    public const string ListedInIpcItem = nameof(ListedInIpcItem);

    public const string EffectivityPreMod = nameof(EffectivityPreMod);
    public const string PreModEffectivityOf = nameof(PreModEffectivityOf);
    public const string EffectivityPostMod = nameof(EffectivityPostMod);
    public const string PostModEffectivityOf = nameof(PostModEffectivityOf);

    public const string HasRepairDrawing = nameof(HasRepairDrawing);
    public const string RepairDrawingOf = nameof(RepairDrawingOf);

    public const string HasRevision = nameof(HasRevision);
    public const string RevisionOf = nameof(RevisionOf);

    // --- reference data ----------------------------------------------------------
    public const string SuppliedBy = nameof(SuppliedBy);
    public const string Supplies = nameof(Supplies);

    public const string DefaultPart = nameof(DefaultPart);
    public const string DefaultPartOf = nameof(DefaultPartOf);

    public const string HostFin = nameof(HostFin);
    public const string HostsSoftware = nameof(HostsSoftware);

    public const string SupersedesSoftware = nameof(SupersedesSoftware);
    public const string SupersededBySoftware = nameof(SupersededBySoftware);

    public const string InterchangeableWith = nameof(InterchangeableWith);

    public const string FinInZone = nameof(FinInZone);
    public const string HasFin = nameof(HasFin);

    public const string HasAccessPanel = nameof(HasAccessPanel);
    public const string AccessPanelOf = nameof(AccessPanelOf);

    public const string OperatedBy = nameof(OperatedBy);
    public const string Operates = nameof(Operates);

    public const string OfType = nameof(OfType);
    public const string HasMsn = nameof(HasMsn);

    public const string OfModel = nameof(OfModel);
    public const string HasModelMsn = nameof(HasModelMsn);

    public const string TypeOfProgram = nameof(TypeOfProgram);
    public const string HasAircraftType = nameof(HasAircraftType);

    public const string ModelOfType = nameof(ModelOfType);
    public const string HasAircraftModel = nameof(HasAircraftModel);

    // --- metadata dimensions -----------------------------------------------------
    // Every one of these was a bare string property before. As nodes they become
    // related-facets in the search UI and usable neighbours for
    // IQuery.HasCommonNeighborsWith, which is what the similarity signals traverse.

    public const string AppliesToEngine = nameof(AppliesToEngine);
    public const string HasEngineDocument = nameof(HasEngineDocument);

    public const string FromDepartment = nameof(FromDepartment);
    public const string OriginatedDocument = nameof(OriginatedDocument);

    public const string AtSite = nameof(AtSite);
    public const string SiteOfDocument = nameof(SiteOfDocument);

    public const string InMajorZone = nameof(InMajorZone);
    public const string HasZone = nameof(HasZone);

    public const string HasKeyword = nameof(HasKeyword);
    public const string KeywordOf = nameof(KeywordOf);

    // --- classifications, one edge pair per vocabulary ---------------------------
    //
    // Every one of these points at the same `Classification` node type, keyed
    // `{Kind}:{Value}`. The edge is what carries the kind, and that is the whole point:
    // a single `HasClassification` made "risk level" and "compliance" one undifferentiated
    // facet, and gave a renderer no way to show *this document's risk level* — traversing
    // it returns every classification the document has. `MetaLinks.Classify` derives the
    // pair from the kind (`Has{Kind}` / `{Kind}Of`) and fails at startup if either name is
    // missing here, so a new vocabulary cannot be linked over an unregistered edge.
    public const string HasEmbodimentStatus = nameof(HasEmbodimentStatus);
    public const string EmbodimentStatusOf = nameof(EmbodimentStatusOf);
    public const string HasChangeCategory = nameof(HasChangeCategory);
    public const string ChangeCategoryOf = nameof(ChangeCategoryOf);
    public const string HasModTrigger = nameof(HasModTrigger);
    public const string ModTriggerOf = nameof(ModTriggerOf);
    public const string HasModClass = nameof(HasModClass);
    public const string ModClassOf = nameof(ModClassOf);
    public const string HasRiskLevel = nameof(HasRiskLevel);
    public const string RiskLevelOf = nameof(RiskLevelOf);
    public const string HasCertificationImpact = nameof(HasCertificationImpact);
    public const string CertificationImpactOf = nameof(CertificationImpactOf);
    public const string HasInterchangeability = nameof(HasInterchangeability);
    public const string InterchangeabilityOf = nameof(InterchangeabilityOf);
    public const string HasSbType = nameof(HasSbType);
    public const string SbTypeOf = nameof(SbTypeOf);
    public const string HasCompliance = nameof(HasCompliance);
    public const string ComplianceOf = nameof(ComplianceOf);
    public const string HasSbReasonCode = nameof(HasSbReasonCode);
    public const string SbReasonCodeOf = nameof(SbReasonCodeOf);
    public const string HasDowntimeCategory = nameof(HasDowntimeCategory);
    public const string DowntimeCategoryOf = nameof(DowntimeCategoryOf);
    public const string HasRequestCategory = nameof(HasRequestCategory);
    public const string RequestCategoryOf = nameof(RequestCategoryOf);
    public const string HasPriority = nameof(HasPriority);
    public const string PriorityOf = nameof(PriorityOf);
    public const string HasIoStatus = nameof(HasIoStatus);
    public const string IoStatusOf = nameof(IoStatusOf);
    public const string HasFunding = nameof(HasFunding);
    public const string FundingOf = nameof(FundingOf);
    public const string HasRequesterType = nameof(HasRequesterType);
    public const string RequesterTypeOf = nameof(RequesterTypeOf);
    public const string HasPawoStatus = nameof(HasPawoStatus);
    public const string PawoStatusOf = nameof(PawoStatusOf);
    public const string HasWorkflowStatus = nameof(HasWorkflowStatus);
    public const string WorkflowStatusOf = nameof(WorkflowStatusOf);
    public const string HasOutcome = nameof(HasOutcome);
    public const string OutcomeOf = nameof(OutcomeOf);
    public const string HasRecommendation = nameof(HasRecommendation);
    public const string RecommendationOf = nameof(RecommendationOf);
    public const string HasAmmTaskType = nameof(HasAmmTaskType);
    public const string AmmTaskTypeOf = nameof(AmmTaskTypeOf);
    public const string HasRepairCategory = nameof(HasRepairCategory);
    public const string RepairCategoryOf = nameof(RepairCategoryOf);
    public const string HasDamageType = nameof(HasDamageType);
    public const string DamageTypeOf = nameof(DamageTypeOf);
    public const string HasNdtMethod = nameof(HasNdtMethod);
    public const string NdtMethodOf = nameof(NdtMethodOf);
    public const string HasDiagramType = nameof(HasDiagramType);
    public const string DiagramTypeOf = nameof(DiagramTypeOf);
    public const string HasFcomSection = nameof(HasFcomSection);
    public const string FcomSectionOf = nameof(FcomSectionOf);
    public const string HasOperationalImpact = nameof(HasOperationalImpact);
    public const string OperationalImpactOf = nameof(OperationalImpactOf);
    public const string HasFlightPhase = nameof(HasFlightPhase);
    public const string FlightPhaseOf = nameof(FlightPhaseOf);
    public const string HasDocumentStatus = nameof(HasDocumentStatus);
    public const string DocumentStatusOf = nameof(DocumentStatusOf);
    public const string HasPartLifecycle = nameof(HasPartLifecycle);
    public const string PartLifecycleOf = nameof(PartLifecycleOf);
    public const string HasPartType = nameof(HasPartType);
    public const string PartTypeOf = nameof(PartTypeOf);
    public const string HasDrawingType = nameof(HasDrawingType);
    public const string DrawingTypeOf = nameof(DrawingTypeOf);
    public const string HasSoftwareLoadType = nameof(HasSoftwareLoadType);
    public const string SoftwareLoadTypeOf = nameof(SoftwareLoadTypeOf);
    public const string HasCertificationStatus = nameof(HasCertificationStatus);
    public const string CertificationStatusOf = nameof(CertificationStatusOf);
    public const string HasAircraftStatus = nameof(HasAircraftStatus);
    public const string AircraftStatusOf = nameof(AircraftStatusOf);
    public const string HasRuleCategory = nameof(HasRuleCategory);
    public const string RuleCategoryOf = nameof(RuleCategoryOf);
    public const string HasRuleSeverity = nameof(HasRuleSeverity);
    public const string RuleSeverityOf = nameof(RuleSeverityOf);
    public const string HasUserRole = nameof(HasUserRole);
    public const string UserRoleOf = nameof(UserRoleOf);

    /// <summary>
    /// The aircraft system, by name. Reached from three directions that never used to meet:
    /// a FIN's system, a software load's system, and the systems an FCOM entry covers.
    /// </summary>
    public const string InSystem = nameof(InSystem);
    public const string HasSystemDocument = nameof(HasSystemDocument);

    public const string ReportsFaultCode = nameof(ReportsFaultCode);
    public const string DiagnosedBy = nameof(DiagnosedBy);

    public const string HasMelItem = nameof(HasMelItem);
    public const string MelItemOf = nameof(MelItemOf);

    public const string RoutesHarness = nameof(RoutesHarness);
    public const string RoutedOnSheet = nameof(RoutedOnSheet);

    /// <summary>The assembly an IPC figure sits under — a part number, not a free-text label.</summary>
    public const string NextHigherAssembly = nameof(NextHigherAssembly);
    public const string AssemblyOfFigure = nameof(AssemblyOfFigure);

    /// <summary>The airworthiness directive a service bulletin answers.</summary>
    public const string UnderDirective = nameof(UnderDirective);
    public const string MandatedBulletin = nameof(MandatedBulletin);

    /// <summary>The kit a service bulletin's material is ordered as.</summary>
    public const string UsesKit = nameof(UsesKit);
    public const string KitFor = nameof(KitFor);

    /// <summary>
    /// The modification number a document belongs to. A dossier and every pre-analysis of
    /// it carry the same number in their identifiers, so as a node it is the one place that
    /// gathers the whole change — the number itself, rather than an integer on each.
    /// </summary>
    public const string HasModNumber = nameof(HasModNumber);
    public const string ModNumberOf = nameof(ModNumberOf);

    /// <summary>The two aircraft an applicability range is bounded by.</summary>
    public const string RangeFromMsn = nameof(RangeFromMsn);
    public const string RangeToMsn = nameof(RangeToMsn);
    public const string StartsRange = nameof(StartsRange);
    public const string EndsRange = nameof(EndsRange);

    // --- users, assignment, rules ------------------------------------------------
    public const string AssignedTo = nameof(AssignedTo);
    public const string HasAssignment = nameof(HasAssignment);

    public const string DelegatedTo = nameof(DelegatedTo);
    public const string HasDelegation = nameof(HasDelegation);

    public const string SpecialisesIn = nameof(SpecialisesIn);
    public const string SpecialistIs = nameof(SpecialistIs);

    /// <summary>
    /// Where an engineer works and what they do. `_User` is a graph-owned node type this
    /// connector cannot add properties to, so these were simply dropped before — as edges
    /// to the same Site / Department / Classification nodes the documents use, an engineer
    /// and the documents from their office are one hop apart.
    /// </summary>
    public const string WorksAtSite = nameof(WorksAtSite);
    public const string SiteOfUser = nameof(SiteOfUser);

    public const string InDepartment = nameof(InDepartment);
    public const string DepartmentOfUser = nameof(DepartmentOfUser);

    // A user's role is a `Classification` like any other, so it travels on the
    // vocabulary's own edge pair (`HasUserRole` / `UserRoleOf`) above.

    public const string AppliesRule = nameof(AppliesRule);
    public const string RuleAppliedBy = nameof(RuleAppliedBy);

    public const string RuleForAta = nameof(RuleForAta);
    public const string HasRule = nameof(HasRule);

    // --- attachments ---------------------------------------------------------------
    //
    // The underscore prefix marks these as the workspace's own edge names rather than
    // ours: an uploaded file is a `_FileEntry`, a node type the graph owns, and the
    // application already renders `_HasAttachment` as the attachment list of whatever
    // the file hangs off. Naming them anything else would give us edges nothing in the
    // product knows how to show.
    public const string HasAttachment = "_HasAttachment";
    public const string AttachmentOf = "_AttachmentOf";

    // The CSDB object a publication was rendered from, and back to the document it projects.
    public const string HasSource = nameof(HasSource);
    public const string SourceOf = nameof(SourceOf);

    // --- issues ----------------------------------------------------------------------
    public const string HasIssue = nameof(HasIssue);
    public const string IssueOfDocument = nameof(IssueOfDocument);

    public const string SupersedesIssue = nameof(SupersedesIssue);
    public const string SupersededByIssue = nameof(SupersededByIssue);

    // The finding that required an issue, and the work order it belongs to.
    public const string IssueRaisedBy = nameof(IssueRaisedBy);
    public const string RequiredIssue = nameof(RequiredIssue);
}
