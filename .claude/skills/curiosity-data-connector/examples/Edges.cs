// Reference template — see ../SKILL.md §5.
//
// Every edge as a const string, in BOTH directions, in one place, registered in one call
// (Program.cs: CreateEdgeSchemaAsync(typeof(Edges))).
//
// Two rules this file exists to enforce:
//   - an edge is always a pair, named from each end's point of view;
//   - one edge name means exactly one relationship. Reusing a name for two different
//     relationships makes every traversal of it return a mixture nobody can narrow.

namespace App.Connector;

internal static class Edges
{
    // --- partitioning & classification -------------------------------------------------
    public const string InPartition          = nameof(InPartition);
    public const string HasDocument          = nameof(HasDocument);

    public const string InCategory           = nameof(InCategory);
    public const string HasCategoryDocument  = nameof(HasCategoryDocument);

    // A hierarchy is linked at EVERY level, not just the leaf: a parent chain makes the top
    // reachable, but a three-hop walk is not a common neighbour, so a same-top-level
    // similarity signal would find nothing.
    public const string InCategoryL2         = nameof(InCategoryL2);
    public const string HasCategoryL2Document= nameof(HasCategoryL2Document);

    public const string ChildOfCategory      = nameof(ChildOfCategory);
    public const string HasCategoryChild     = nameof(HasCategoryChild);

    public const string HasKeyword           = nameof(HasKeyword);
    public const string KeywordOf            = nameof(KeywordOf);

    // --- status & classification vocabularies ------------------------------------------
    // One Status node type keyed by the VALUE alone, so everything in a state gathers on one
    // node — and one edge pair per field, so "this record's own status" is still one
    // traversal with one answer. MetaLinks derives these names; a missing constant throws at
    // startup rather than writing links over an unregistered edge type.
    public const string HasDocumentStatus    = nameof(HasDocumentStatus);
    public const string DocumentStatusOf     = nameof(DocumentStatusOf);

    public const string HasWorkflowStatus    = nameof(HasWorkflowStatus);
    public const string WorkflowStatusOf     = nameof(WorkflowStatusOf);

    public const string HasPriority          = nameof(HasPriority);
    public const string PriorityOf           = nameof(PriorityOf);

    public const string HasRiskLevel         = nameof(HasRiskLevel);
    public const string RiskLevelOf          = nameof(RiskLevelOf);

    public const string HasUserRole          = nameof(HasUserRole);
    public const string UserRoleOf           = nameof(UserRoleOf);

    // --- entities -----------------------------------------------------------------------
    public const string UsesPart             = nameof(UsesPart);
    public const string UsedByDocument       = nameof(UsedByDocument);

    // --- document to document -------------------------------------------------------------
    public const string CaseOf               = nameof(CaseOf);          // Case → the Change it analyses
    public const string HasCase              = nameof(HasCase);

    public const string ReferencesDoc        = nameof(ReferencesDoc);   // generic, stated in the source
    public const string ReferencedByDoc      = nameof(ReferencedByDoc);

    // Ground truth for evaluation — NOT a claim that the impact is confirmed.
    public const string HasFinding           = nameof(HasFinding);
    public const string FindingOf            = nameof(FindingOf);

    public const string FindingTargets       = nameof(FindingTargets);
    public const string TargetedByFinding    = nameof(TargetedByFinding);

    // --- decisions: written ONLY by the endpoints, never by the connector ------------------
    public const string ConfirmedImpact      = nameof(ConfirmedImpact);
    public const string ConfirmedImpactOf    = nameof(ConfirmedImpactOf);

    public const string RejectedImpact       = nameof(RejectedImpact);
    public const string RejectedImpactOf     = nameof(RejectedImpactOf);

    // --- people ---------------------------------------------------------------------------
    // Ownership is an edge and ONLY an edge. There is deliberately no AssignedToUserName
    // property: a name beside the edge is a second copy of one fact, and the two drift.
    public const string AssignedTo           = nameof(AssignedTo);
    public const string HasAssignment        = nameof(HasAssignment);

    public const string DelegatedTo          = nameof(DelegatedTo);
    public const string HasDelegation        = nameof(HasDelegation);

    public const string SpecialisesIn        = nameof(SpecialisesIn);
    public const string SpecialistIs         = nameof(SpecialistIs);

    // --- files ------------------------------------------------------------------------------
    // The workspace's OWN attachment edge names (hence the underscore). The product already
    // renders this pair as the attachment list and already has a viewer for the file; anything
    // else gives you edges nothing in the product knows how to show.
    public const string AttachmentOf         = "_AttachmentOf";
    public const string HasAttachment        = "_HasAttachment";
}
