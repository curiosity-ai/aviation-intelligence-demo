using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeralis.Dataset.Documents;

/// <summary><c>type_data</c> for an Internal Order — the request that starts the chain.</summary>
public sealed class InternalOrderData
{
    [JsonPropertyName("io_number")] public string IoNumber { get; set; } = "";
    [JsonPropertyName("raised_by")] public RaisedBy? RaisedBy { get; set; }
    [JsonPropertyName("request_category")] public RequestCategory RequestCategory { get; set; }
    [JsonPropertyName("priority")] public Priority Priority { get; set; }
    [JsonPropertyName("io_status")] public IoStatus IoStatus { get; set; }
    [JsonPropertyName("fleet_scope")] public FleetScope? FleetScope { get; set; }
    [JsonPropertyName("business_justification")] public string? BusinessJustification { get; set; }
    [JsonPropertyName("commercial")] public CommercialInfo? Commercial { get; set; }
    [JsonPropertyName("target_dates")] public TargetDates? TargetDates { get; set; }
    [JsonPropertyName("created_mod")] public string? CreatedMod { get; set; }

    /// <summary>
    /// Why the order was closed without a MOD. Set when <see cref="IoStatus"/> is
    /// REJECTED, ON_HOLD or CLOSED; null when the order converted. Mutually exclusive
    /// with <see cref="CreatedMod"/>.
    /// </summary>
    [JsonPropertyName("closure_note")] public string? ClosureNote { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class RaisedBy
{
    [JsonPropertyName("requester_type")] public RequesterType RequesterType { get; set; }
    [JsonPropertyName("operator_code")] public string? OperatorCode { get; set; }
    [JsonPropertyName("contact_role")] public string? ContactRole { get; set; }
    [JsonPropertyName("oem_focal_department")] public string? OemFocalDepartment { get; set; }
    [JsonPropertyName("oem_focal_site")] public string? OemFocalSite { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class FleetScope
{
    [JsonPropertyName("aircraft_count")] public int AircraftCount { get; set; }
    [JsonPropertyName("msns")] public List<string> Msns { get; set; } = [];
    [JsonPropertyName("aircraft_types")] public List<string> AircraftTypes { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class CommercialInfo
{
    [JsonPropertyName("funding")] public Funding Funding { get; set; }
    [JsonPropertyName("budget_keur")] public double BudgetKeur { get; set; }
    [JsonPropertyName("contract_reference")] public string? ContractReference { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class TargetDates
{
    [JsonPropertyName("requested_response")] public string? RequestedResponse { get; set; }
    [JsonPropertyName("requested_embodiment_start")] public string? RequestedEmbodimentStart { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary>
/// <c>type_data</c> for a Pre-Analysis Work Order. Carries the ground truth the
/// application's conflict search is scored against.
/// </summary>
public sealed class PreAnalysisWorkOrderData
{
    [JsonPropertyName("pawo_number")] public string PawoNumber { get; set; } = "";
    [JsonPropertyName("subject_mod")] public string SubjectMod { get; set; } = "";
    [JsonPropertyName("source_io")] public string? SourceIo { get; set; }
    [JsonPropertyName("pawo_status")] public PawoStatus PawoStatus { get; set; }
    [JsonPropertyName("hero_scenario")] public bool HeroScenario { get; set; }

    /// <summary>
    /// True for a closed pre-analysis against an already-released modification. These are what
    /// the dashboard's throughput, hours-per-order and outcome-split metrics are computed from,
    /// and what the "similar previous analysis" search reads.
    /// </summary>
    [JsonPropertyName("is_historical")] public bool IsHistorical { get; set; }

    /// <summary>Set when <see cref="IsHistorical"/>; null while the work order is open.</summary>
    [JsonPropertyName("completed_date")] public string? CompletedDate { get; set; }

    /// <summary>VALIDATED / REJECTED / DELEGATED on a closed order; null while open.</summary>
    [JsonPropertyName("outcome")] public PawoOutcome Outcome { get; set; }
    [JsonPropertyName("raised_date")] public string? RaisedDate { get; set; }
    [JsonPropertyName("due_date")] public string? DueDate { get; set; }
    [JsonPropertyName("analysis_round")] public int AnalysisRound { get; set; }
    [JsonPropertyName("assigned")] public PawoAssignment? Assigned { get; set; }
    [JsonPropertyName("search_scope")] public SearchScope? SearchScope { get; set; }
    [JsonPropertyName("tasks")] public List<PawoTask> Tasks { get; set; } = [];
    [JsonPropertyName("expected_findings")] public List<ExpectedFinding> ExpectedFindings { get; set; } = [];
    [JsonPropertyName("expected_non_findings")] public List<ExpectedNonFinding> ExpectedNonFindings { get; set; } = [];
    [JsonPropertyName("conclusion")] public string? Conclusion { get; set; }
    [JsonPropertyName("recommendation")] public Recommendation Recommendation { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class PawoAssignment
{
    [JsonPropertyName("lead_department")] public string? LeadDepartment { get; set; }
    [JsonPropertyName("lead_site")] public string? LeadSite { get; set; }
    [JsonPropertyName("contributing_departments")] public List<string> ContributingDepartments { get; set; } = [];
    [JsonPropertyName("estimated_manhours")] public double EstimatedManhours { get; set; }
    [JsonPropertyName("spent_manhours")] public double SpentManhours { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class SearchScope
{
    [JsonPropertyName("programs")] public List<string> Programs { get; set; } = [];
    [JsonPropertyName("aircraft_types")] public List<string> AircraftTypes { get; set; } = [];
    [JsonPropertyName("ata_2d")] public List<string> Ata2d { get; set; } = [];
    [JsonPropertyName("ata_4d")] public List<string> Ata4d { get; set; } = [];
    [JsonPropertyName("doc_types")] public List<DocType> DocTypes { get; set; } = [];
    [JsonPropertyName("msn_range")] public MsnRange? MsnRange { get; set; }
    [JsonPropertyName("entity_seeds")] public EntityRefs? EntitySeeds { get; set; }
    [JsonPropertyName("similarity_seeds")] public List<string> SimilaritySeeds { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class PawoTask
{
    [JsonPropertyName("task_id")] public string TaskId { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("method")] public AnalysisMethod Method { get; set; }
    [JsonPropertyName("owner")] public string? Owner { get; set; }
    [JsonPropertyName("status")] public TaskStatus Status { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary>One known conflict the application is expected to surface.</summary>
public sealed class ExpectedFinding
{
    [JsonPropertyName("finding_id")] public string FindingId { get; set; } = "";
    [JsonPropertyName("doc_id")] public string DocId { get; set; } = "";
    [JsonPropertyName("doc_type")] public DocType DocType { get; set; }
    [JsonPropertyName("finding_type")] public FindingType FindingType { get; set; }
    [JsonPropertyName("severity")] public Severity Severity { get; set; }
    [JsonPropertyName("discovery")] public DiscoveryMode Discovery { get; set; }
    [JsonPropertyName("evidence")] public FindingEvidence? Evidence { get; set; }
    [JsonPropertyName("rationale")] public string? Rationale { get; set; }
    [JsonPropertyName("resolution")] public string? Resolution { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class FindingEvidence
{
    [JsonPropertyName("shared_part_numbers")] public List<string> SharedPartNumbers { get; set; } = [];
    [JsonPropertyName("shared_fins")] public List<string> SharedFins { get; set; } = [];
    [JsonPropertyName("shared_zones")] public List<string> SharedZones { get; set; } = [];
    [JsonPropertyName("shared_ata_6d")] public List<string> SharedAta6d { get; set; } = [];
    [JsonPropertyName("shared_software")] public List<string> SharedSoftware { get; set; } = [];
    [JsonPropertyName("shared_drawings")] public List<string> SharedDrawings { get; set; } = [];
    [JsonPropertyName("similarity_note")] public string? SimilarityNote { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }

    /// <summary>True when the finding rests on similarity alone — no shared identifiers.</summary>
    public bool IsSemanticOnly => SharedPartNumbers.Count == 0 && SharedFins.Count == 0
                                  && SharedZones.Count == 0 && SharedAta6d.Count == 0
                                  && SharedSoftware.Count == 0 && SharedDrawings.Count == 0;
}

public sealed class ExpectedNonFinding
{
    [JsonPropertyName("doc_id")] public string DocId { get; set; } = "";
    [JsonPropertyName("doc_type")] public DocType DocType { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}
