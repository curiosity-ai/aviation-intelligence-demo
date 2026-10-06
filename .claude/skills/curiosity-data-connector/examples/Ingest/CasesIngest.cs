// Reference template: a JSON document handler with child nodes, metadata links, and derived
// workflow state folded into the single AddOrUpdate. See ../../SKILL.md §4 and §7.

using Curiosity.Library;

namespace App.Connector.Ingest;

internal static class CasesIngest
{
    public const int Version = 1;

    // -------------------------------------------------------------------------------------
    // the source shape
    // -------------------------------------------------------------------------------------

    private sealed class CaseFile
    {
        public string   CaseId      { get; set; } = "";
        public string   Title       { get; set; } = "";
        public string   Abstract    { get; set; } = "";
        public string   Partition   { get; set; } = "";
        public string   SubjectId   { get; set; } = "";
        public string   Status      { get; set; } = "";
        public string   Priority    { get; set; } = "";
        public string   CategoryL4  { get; set; } = "";
        public string   CategoryL2  { get; set; } = "";
        public string[] Keywords    { get; set; } = [];
        public string[] PartNumbers { get; set; } = [];
        public string[] References  { get; set; } = [];
        public Section  Sections    { get; set; } = new();
        public Finding[] ExpectedFindings { get; set; } = [];
        public DateTimeOffset Created  { get; set; }
        public DateTimeOffset? DueDate { get; set; }
    }

    private sealed class Section
    {
        public string Objective { get; set; } = "";
        public string Method    { get; set; } = "";
        public string Findings  { get; set; } = "";
    }

    private sealed class Finding
    {
        public string FindingId { get; set; } = "";
        public string TargetId  { get; set; } = "";
        public string Severity  { get; set; } = "";
        public string Discovery { get; set; } = "";   // SEMANTIC | ENTITY — the ground truth
        public string Rationale { get; set; } = "";
    }

    // -------------------------------------------------------------------------------------
    // the graph nodes
    // -------------------------------------------------------------------------------------

    [Node]
    internal sealed class Case
    {
        [Key]       public string DocId    { get; set; } = "";
        [Timestamp] public DateTimeOffset Created { get; set; }

        [Property] public string Title      { get; set; } = "";
        [Property] public string Abstract   { get; set; } = "";
        [Property] public string Partition  { get; set; } = "";
        [Property] public string SubjectId  { get; set; } = "";
        [Property] public string Status     { get; set; } = "";
        [Property] public string Priority   { get; set; } = "";
        [Property] public string CategoryL4 { get; set; } = "";
        [Property] public string CategoryL2 { get; set; } = "";
        [Property] public string Keywords   { get; set; } = "";

        // Source sections are flattened into ONE PROPERTY EACH rather than concatenated, so
        // the search index and the similarity engine can weight them separately.
        [Property] public string ObjectiveText { get; set; } = "";
        [Property] public string MethodText    { get; set; } = "";
        [Property] public string FindingsText  { get; set; } = "";

        // Derived workflow state — computed by AssignmentPass.Plan BEFORE this pass runs, and
        // written here, in the node's single AddOrUpdate. Ownership itself is NOT here: it is
        // an edge, written afterwards by AssignmentPass.LinkAsync.
        [Property] public string WorkflowStatus { get; set; } = "";
        [Property] public bool   IsUrgent       { get; set; }
        [Property] public double HoursSpent     { get; set; }
        [Property] public DateTimeOffset? DueDate       { get; set; }
        [Property] public DateTimeOffset? CompletedDate { get; set; }

        // Denormalised for cheap dashboard counts.
        [Property] public int FindingCount         { get; set; }
        [Property] public int BlockingFindingCount { get; set; }
    }

    /// <summary>
    /// A child node, because a finding is individually linkable and individually decidable —
    /// the two tests in curiosity-graph-modelling §7. Keyed from its parent so it is
    /// reconstructible.
    /// </summary>
    [Node]
    internal sealed class CaseFinding
    {
        [Key]      public string FindingKey { get; set; } = "";   // "{caseId}#{findingId}"
        [Property] public string CaseId     { get; set; } = "";
        [Property] public string Severity   { get; set; } = "";
        [Property] public string Discovery  { get; set; } = "";
        [Property] public string Rationale  { get; set; } = "";
    }

    // -------------------------------------------------------------------------------------

    public static async Task RunAsync(ConnectorContext ctx, AssignmentPass.Plan plan)
    {
        foreach (var path in ctx.JsonFiles("cases"))
        {
            var doc = ctx.ReadJson<CaseFile>(path);
            if (doc is null) continue;

            var state = plan.For(doc.CaseId);

            // The plan's fingerprint is part of the cache key, so re-tuning the staffing model
            // rewrites the records it moved even though nothing in the corpus changed.
            ctx.Cache.IfNotCached(Version, (doc, state.Fingerprint), _ =>
            {
                var node = ctx.Graph.AddOrUpdate(new Case
                {
                    DocId      = doc.CaseId,
                    Created    = doc.Created,
                    Title      = doc.Title,
                    Abstract   = doc.Abstract,
                    Partition  = doc.Partition,
                    SubjectId  = doc.SubjectId,
                    Status     = doc.Status,
                    Priority   = doc.Priority,
                    CategoryL4 = doc.CategoryL4,
                    CategoryL2 = doc.CategoryL2,
                    Keywords   = string.Join(", ", doc.Keywords),

                    ObjectiveText = doc.Sections.Objective,
                    MethodText    = doc.Sections.Method,
                    FindingsText  = doc.Sections.Findings,

                    WorkflowStatus = state.WorkflowStatus,
                    IsUrgent       = state.IsUrgent,
                    HoursSpent     = state.HoursSpent,
                    DueDate        = doc.DueDate,
                    CompletedDate  = state.CompletedDate,

                    FindingCount         = doc.ExpectedFindings.Length,
                    BlockingFindingCount = doc.ExpectedFindings.Count(f => f.Severity == "BLOCKING"),
                });

                ctx.CountNode(nameof(Case));

                // --- structured relationships, stated by the source ---------------------
                ctx.LinkKey (node, nameof(PartitionsIngest.Partition), doc.Partition,  Edges.InPartition,   Edges.HasDocument);
                ctx.LinkKey (node, nameof(CategoriesIngest.Category),  doc.CategoryL4, Edges.InCategory,    Edges.HasCategoryDocument);
                ctx.LinkKey (node, nameof(CategoriesIngest.Category),  doc.CategoryL2, Edges.InCategoryL2,  Edges.HasCategoryL2Document);
                ctx.LinkKeys(node, nameof(PartNumbersIngest.PartNumber), doc.PartNumbers, Edges.UsesPart,   Edges.UsedByDocument);
                ctx.LinkKey (node, nameof(Case),                       doc.SubjectId,  Edges.CaseOf,        Edges.HasCase);

                foreach (var reference in doc.References)
                    ctx.LinkKey(node, nameof(Case), reference, Edges.ReferencesDoc, Edges.ReferencedByDoc);

                // --- metadata dimensions: node + edge, and the property stays -----------
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.DocumentStatus, doc.Status);
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.WorkflowStatus, state.WorkflowStatus);
                MetaLinks.Classify (ctx, node, MetaLinks.Kind.Priority, doc.Priority);
                MetaLinks.Keywords (ctx, node, doc.Keywords);

                // --- child nodes ---------------------------------------------------------
                // Ground truth for evaluation, linked as FindingTargets — NOT as a claim that
                // the impact is confirmed. A confirmed impact is a distinct edge written only
                // by the endpoint behind the user's own decision.
                foreach (var finding in doc.ExpectedFindings)
                {
                    var child = ctx.Graph.AddOrUpdate(new CaseFinding
                    {
                        FindingKey = doc.CaseId + "#" + finding.FindingId,
                        CaseId     = doc.CaseId,
                        Severity   = finding.Severity,
                        Discovery  = finding.Discovery,
                        Rationale  = finding.Rationale,
                    });

                    ctx.CountNode(nameof(CaseFinding));

                    ctx.Graph.Link(node, child, Edges.HasFinding, Edges.FindingOf);
                    ctx.LinkKey(child, nameof(Case), finding.TargetId, Edges.FindingTargets, Edges.TargetedByFinding);
                }

                // NOTE what is deliberately NOT linked here: nothing that the application is
                // meant to discover. No "these two read alike", no part number that appears
                // only in prose. Those arrive as _Mentions / _AppearsIn from the workspace
                // pipeline, as a separate additive layer.
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}
