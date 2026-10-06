namespace Aeralis.FrontEnd.Schema
{
    /// <summary>The relationship a property is really a reference to.</summary>
    internal sealed class PropertyLink
    {
        public PropertyLink(string edgeType, params string[] nodeTypes)
        {
            EdgeType = edgeType;
            NodeTypes = nodeTypes;
        }

        public string EdgeType { get; }
        public string[] NodeTypes { get; }
    }

    /// <summary>
    /// Which properties are references, and to what.
    ///
    /// Almost every value in a details grid names something the graph already holds as a
    /// node: <c>Ata2d</c> is a chapter, <c>ProgramLetter</c> is a programme, <c>Status</c>
    /// and <c>RiskLevel</c> and <c>Trigger</c> are classifications, <c>Keywords</c> is a
    /// list of keywords. Rendered as text they are dead ends — you can read "53" and not
    /// ask what else is in chapter 53.
    ///
    /// So <see cref="AeralisRendererBase"/> asks this table for every property it is about
    /// to render, and draws the neighbours on the far side of the edge instead of the
    /// string. The property stays on the node — it is what the search index reads, and it
    /// is the fallback when the edge is missing.
    ///
    /// The mapping cannot be derived: that <c>Ata4d</c> is reached by <c>InAta4d</c> and
    /// <c>Ata2d</c> by <c>InAta2d</c>, and that <c>Status</c> means the <c>DocumentStatus</c>
    /// vocabulary rather than any of the other thirty-seven, is a decision the connector
    /// made. It is written out here, once, against the generated <see cref="N"/> and
    /// <see cref="E"/> so a rename on either side breaks the build.
    /// </summary>
    internal static class PropertyLinks
    {
        /// <summary>
        /// The link for one property of one node type, or null when the property is just a
        /// value.
        ///
        /// The per-type table wins, and the envelope is the fallback — not the other way
        /// round. Two types share an envelope property's *name* while meaning something
        /// else by it: a rule reaches its chapter over <c>RuleForAta</c> rather than
        /// <c>InAta2d</c>, and an aircraft's <c>Status</c> travels over <c>HasAircraftStatus</c>,
        /// not <c>HasDocumentStatus</c> — the same <see cref="N.Status"/> node at the far end,
        /// reached by the edge that says which field it came from. Matching the envelope first
        /// would have silently sent both down an edge that does not exist for them.
        /// </summary>
        public static PropertyLink For(string nodeType, string property)
            => PerType(nodeType, property) ?? Envelope(property);

        /// <summary>
        /// Properties every document carries, linked the same way on all of them. A type
        /// that has the property but not the edge — an IPC item carries
        /// <c>ProgramLetter</c> but is linked to its figure, not to the programme — simply
        /// finds no neighbours, and the renderer falls back to showing the value.
        /// </summary>
        private static PropertyLink Envelope(string property)
        {
            switch (property)
            {
                case N.Modification.Ata6d: return new PropertyLink(E.InAtaChapter, N.AtaChapter.Type);
                case N.Modification.Ata4d: return new PropertyLink(E.InAta4d, N.AtaChapter.Type);
                case N.Modification.Ata2d: return new PropertyLink(E.InAta2d, N.AtaChapter.Type);
                case N.Modification.ProgramLetter: return new PropertyLink(E.InProgram, N.Program.Type);
                case N.Modification.Keywords: return new PropertyLink(E.HasKeyword, N.Keyword.Type);
                case N.Modification.Status: return new PropertyLink(E.HasDocumentStatus, N.Status.Type);
                default: return null;
            }
        }

        private static PropertyLink PerType(string nodeType, string property)
        {
            // Every issue type carries the same three references, so they are answered once
            // rather than in ten near-identical switch arms. Checked before the per-type table
            // because an issue's `Status` is the envelope's, not its document type's.
            var issue = Issue(nodeType, property);
            if (issue is object) return issue;

            switch (nodeType)
            {
                case N.Modification.Type: return Modification(property);
                case N.ServiceBulletin.Type: return ServiceBulletin(property);
                case N.InternalOrder.Type: return InternalOrder(property);
                case N.PreAnalysisWorkOrder.Type: return PreAnalysisWorkOrder(property);
                case N.AmmTask.Type: return AmmTask(property);
                case N.IpcFigure.Type: return IpcFigure(property);
                case N.IpcItem.Type: return IpcItem(property);
                case N.SrmRepair.Type: return SrmRepair(property);
                case N.TsmProcedure.Type: return TsmProcedure(property);
                case N.WdmSheet.Type: return WdmSheet(property);
                case N.FcomEntry.Type: return FcomEntry(property);
                case N.ExpectedFinding.Type: return ExpectedFinding(property);
                case N.MsnRangeApplicability.Type: return MsnRange(property);
                case N.PartNumber.Type: return PartNumber(property);
                case N.Fin.Type: return Fin(property);
                case N.Drawing.Type: return Drawing(property);
                case N.SoftwareLoad.Type: return SoftwareLoad(property);
                case N.Msn.Type: return Msn(property);
                case N.Zone.Type: return Zone(property);
                case N.AtaChapter.Type: return AtaChapter(property);
                case N.Rule.Type: return Rule(property);
                case N.AircraftModel.Type: return AircraftModel(property);
                default: return null;
            }
        }

        /// <summary>
        /// The references an issue node carries, whatever document type it is an issue of.
        ///
        /// All ten issue types declare the same fields under the same names, so the property
        /// name is enough to answer this — but only for a node that *is* an issue, which is why
        /// the type is checked first: a document has a `Status` too, and it does not travel over
        /// `SupersedesIssue`.
        ///
        /// `Supersedes` and `RaisedByFinding` are the two that matter. They hold identifiers —
        /// an issue letter and a finding key — which as text are dead ends; as the edges the
        /// connector wrote, they are the chain back through the history and the pre-analysis
        /// finding that forced this revision. That second one is what makes a pre-analysis
        /// falsifiable, so it is worth a click.
        /// </summary>
        private static PropertyLink Issue(string nodeType, string property)
        {
            if (!IsIssueType(nodeType)) return null;

            switch (property)
            {
                case N.ModificationIssue.Supersedes:
                    return new PropertyLink(E.SupersedesIssue, nodeType);

                case N.ModificationIssue.RaisedByFinding:
                    return new PropertyLink(E.IssueRaisedBy, N.ExpectedFinding.Type);

                case N.ModificationIssue.DocId:
                    return new PropertyLink(E.IssueOfDocument, DocumentTypeOf(nodeType));

                default: return null;
            }
        }

        private static bool IsIssueType(string nodeType)
            => DocumentTypeOf(nodeType) is object;

        /// <summary>The document type an issue type is an issue of, or null when it is not one.</summary>
        private static string DocumentTypeOf(string nodeType)
        {
            switch (nodeType)
            {
                case N.ModificationIssue.Type: return N.Modification.Type;
                case N.ServiceBulletinIssue.Type: return N.ServiceBulletin.Type;
                case N.InternalOrderIssue.Type: return N.InternalOrder.Type;
                case N.PreAnalysisWorkOrderIssue.Type: return N.PreAnalysisWorkOrder.Type;
                case N.AmmTaskIssue.Type: return N.AmmTask.Type;
                case N.IpcFigureIssue.Type: return N.IpcFigure.Type;
                case N.SrmRepairIssue.Type: return N.SrmRepair.Type;
                case N.TsmProcedureIssue.Type: return N.TsmProcedure.Type;
                case N.WdmSheetIssue.Type: return N.WdmSheet.Type;
                case N.FcomEntryIssue.Type: return N.FcomEntry.Type;
                default: return null;
            }
        }

        // --- documents ---------------------------------------------------------

        private static PropertyLink Modification(string property)
        {
            switch (property)
            {
                case N.Modification.ModNumber: return new PropertyLink(E.HasModNumber, N.ModNumber.Type);
                case N.Modification.EmbodimentStatus: return new PropertyLink(E.HasEmbodimentStatus, N.Status.Type);
                case N.Modification.ChangeCategory: return new PropertyLink(E.HasChangeCategory, N.Classification.Type);
                case N.Modification.Trigger: return new PropertyLink(E.HasModTrigger, N.Classification.Type);
                case N.Modification.ModClass: return new PropertyLink(E.HasModClass, N.Classification.Type);
                case N.Modification.RiskLevel: return new PropertyLink(E.HasRiskLevel, N.Classification.Type);
                case N.Modification.CertificationImpact: return new PropertyLink(E.HasCertificationImpact, N.Classification.Type);
                case N.Modification.Interchangeability: return new PropertyLink(E.HasInterchangeability, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink ServiceBulletin(string property)
        {
            switch (property)
            {
                case N.ServiceBulletin.SbType: return new PropertyLink(E.HasSbType, N.Classification.Type);
                case N.ServiceBulletin.Compliance: return new PropertyLink(E.HasCompliance, N.Classification.Type);
                case N.ServiceBulletin.ReasonCode: return new PropertyLink(E.HasSbReasonCode, N.Classification.Type);
                case N.ServiceBulletin.DowntimeCategory: return new PropertyLink(E.HasDowntimeCategory, N.Classification.Type);
                case N.ServiceBulletin.AdReference: return new PropertyLink(E.UnderDirective, N.AirworthinessDirective.Type);
                case N.ServiceBulletin.KitNumber: return new PropertyLink(E.UsesKit, N.ServiceKit.Type);
                default: return null;
            }
        }

        private static PropertyLink InternalOrder(string property)
        {
            switch (property)
            {
                case N.InternalOrder.RequestCategory: return new PropertyLink(E.HasRequestCategory, N.Classification.Type);
                case N.InternalOrder.Priority: return new PropertyLink(E.HasPriority, N.Classification.Type);
                case N.InternalOrder.IoStatus: return new PropertyLink(E.HasIoStatus, N.Status.Type);
                case N.InternalOrder.RequesterType: return new PropertyLink(E.HasRequesterType, N.Classification.Type);
                case N.InternalOrder.Funding: return new PropertyLink(E.HasFunding, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink PreAnalysisWorkOrder(string property)
        {
            switch (property)
            {
                case N.PreAnalysisWorkOrder.SubjectMod: return new PropertyLink(E.PreAnalysisOf, N.Modification.Type);
                case N.PreAnalysisWorkOrder.SourceIo: return new PropertyLink(E.FromOrder, N.InternalOrder.Type);
                case N.PreAnalysisWorkOrder.PawoStatus: return new PropertyLink(E.HasPawoStatus, N.Status.Type);
                case N.PreAnalysisWorkOrder.WorkflowStatus: return new PropertyLink(E.HasWorkflowStatus, N.Status.Type);
                case N.PreAnalysisWorkOrder.Outcome: return new PropertyLink(E.HasOutcome, N.Status.Type);
                case N.PreAnalysisWorkOrder.Recommendation: return new PropertyLink(E.HasRecommendation, N.Classification.Type);
                case N.PreAnalysisWorkOrder.LeadDepartment: return new PropertyLink(E.FromDepartment, N.Department.Type);
                case N.PreAnalysisWorkOrder.LeadSite: return new PropertyLink(E.AtSite, N.Site.Type);
                default: return null;
            }
        }

        private static PropertyLink AmmTask(string property)
            => property == N.AmmTask.TaskType
                ? new PropertyLink(E.HasAmmTaskType, N.Classification.Type)
                : null;

        private static PropertyLink IpcFigure(string property)
            => property == N.IpcFigure.NextHigherAssembly
                ? new PropertyLink(E.NextHigherAssembly, N.PartNumber.Type)
                : null;

        private static PropertyLink IpcItem(string property)
        {
            switch (property)
            {
                case N.IpcItem.PartNumber: return new PropertyLink(E.ItemPart, N.PartNumber.Type);
                case N.IpcItem.Figure: return new PropertyLink(E.ItemOf, N.IpcFigure.Type);
                default: return null;
            }
        }

        private static PropertyLink SrmRepair(string property)
        {
            switch (property)
            {
                case N.SrmRepair.RepairCategory: return new PropertyLink(E.HasRepairCategory, N.Classification.Type);
                case N.SrmRepair.DamageType: return new PropertyLink(E.HasDamageType, N.Classification.Type);
                case N.SrmRepair.NdtMethod: return new PropertyLink(E.HasNdtMethod, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink TsmProcedure(string property)
        {
            switch (property)
            {
                case N.TsmProcedure.FaultCodes: return new PropertyLink(E.ReportsFaultCode, N.FaultCode.Type);
                case N.TsmProcedure.MelItem: return new PropertyLink(E.HasMelItem, N.MelItem.Type);
                default: return null;
            }
        }

        private static PropertyLink WdmSheet(string property)
        {
            switch (property)
            {
                case N.WdmSheet.Harnesses: return new PropertyLink(E.RoutesHarness, N.Harness.Type);
                case N.WdmSheet.DiagramType: return new PropertyLink(E.HasDiagramType, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink FcomEntry(string property)
        {
            switch (property)
            {
                case N.FcomEntry.Systems: return new PropertyLink(E.InSystem, N.AircraftSystem.Type);
                case N.FcomEntry.Section: return new PropertyLink(E.HasFcomSection, N.Classification.Type);
                case N.FcomEntry.FlightPhase: return new PropertyLink(E.HasFlightPhase, N.Classification.Type);
                case N.FcomEntry.OperationalImpact: return new PropertyLink(E.HasOperationalImpact, N.Classification.Type);
                default: return null;
            }
        }

        // --- child nodes -------------------------------------------------------

        private static PropertyLink ExpectedFinding(string property)
        {
            switch (property)
            {
                case N.ExpectedFinding.PawoId: return new PropertyLink(E.FindingOf, N.PreAnalysisWorkOrder.Type);
                // The target can be any document type, so the link names them all rather
                // than trying to resolve TargetDocType into one.
                case N.ExpectedFinding.TargetDocId: return new PropertyLink(E.FindingTargets, N.DocumentTypes);
                default: return null;
            }
        }

        private static PropertyLink MsnRange(string property)
        {
            switch (property)
            {
                case N.MsnRangeApplicability.FromMsn: return new PropertyLink(E.RangeFromMsn, N.Msn.Type);
                case N.MsnRangeApplicability.ToMsn: return new PropertyLink(E.RangeToMsn, N.Msn.Type);
                default: return null;
            }
        }

        // --- reference data ----------------------------------------------------

        private static PropertyLink PartNumber(string property)
        {
            switch (property)
            {
                case N.PartNumber.PnType: return new PropertyLink(E.HasPartType, N.Classification.Type);
                case N.PartNumber.Lifecycle: return new PropertyLink(E.HasPartLifecycle, N.Status.Type);
                default: return null;
            }
        }

        private static PropertyLink Fin(string property)
        {
            switch (property)
            {
                case N.Fin.SystemName: return new PropertyLink(E.InSystem, N.AircraftSystem.Type);
                default: return null;
            }
        }

        private static PropertyLink Drawing(string property)
        {
            switch (property)
            {
                case N.Drawing.DrawingType: return new PropertyLink(E.HasDrawingType, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink SoftwareLoad(string property)
        {
            switch (property)
            {
                case N.SoftwareLoad.SystemName: return new PropertyLink(E.InSystem, N.AircraftSystem.Type);
                case N.SoftwareLoad.LoadType: return new PropertyLink(E.HasSoftwareLoadType, N.Classification.Type);
                case N.SoftwareLoad.CertificationStatus: return new PropertyLink(E.HasCertificationStatus, N.Status.Type);
                default: return null;
            }
        }

        private static PropertyLink Msn(string property)
        {
            switch (property)
            {
                case N.Msn.AircraftType: return new PropertyLink(E.OfType, N.AircraftType.Type);
                case N.Msn.Model: return new PropertyLink(E.OfModel, N.AircraftModel.Type);
                case N.Msn.EngineType: return new PropertyLink(E.AppliesToEngine, N.EngineType.Type);
                case N.Msn.Status: return new PropertyLink(E.HasAircraftStatus, N.Status.Type);
                default: return null;
            }
        }

        private static PropertyLink Zone(string property)
            => property == N.Zone.MajorZone
                ? new PropertyLink(E.InMajorZone, N.MajorZone.Type)
                : null;

        private static PropertyLink AtaChapter(string property)
            => property == N.AtaChapter.Domain
                ? new PropertyLink(E.InAtaDomain, N.AtaDomain.Type)
                : null;

        private static PropertyLink Rule(string property)
        {
            switch (property)
            {
                case N.Rule.Ata2d: return new PropertyLink(E.RuleForAta, N.AtaChapter.Type);
                case N.Rule.Category: return new PropertyLink(E.HasRuleCategory, N.Classification.Type);
                case N.Rule.Severity: return new PropertyLink(E.HasRuleSeverity, N.Classification.Type);
                default: return null;
            }
        }

        private static PropertyLink AircraftModel(string property)
            => property == N.AircraftModel.AircraftType
                ? new PropertyLink(E.ModelOfType, N.AircraftType.Type)
                : null;
    }
}
