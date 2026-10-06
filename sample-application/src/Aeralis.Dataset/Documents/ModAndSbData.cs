using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeralis.Dataset.Documents;

/// <summary><c>type_data</c> for a MOD dossier.</summary>
public sealed class ModData
{
    [JsonPropertyName("mod_number")] public int ModNumber { get; set; }
    [JsonPropertyName("mod_class")] public ModClass ModClass { get; set; }
    [JsonPropertyName("change_category")] public ChangeCategory ChangeCategory { get; set; }
    [JsonPropertyName("trigger")] public ModTrigger Trigger { get; set; }
    [JsonPropertyName("customer_driven")] public bool CustomerDriven { get; set; }
    [JsonPropertyName("certification")] public CertificationInfo? Certification { get; set; }
    [JsonPropertyName("weight_impact_kg")] public double WeightImpactKg { get; set; }
    [JsonPropertyName("balance_impact")] public string? BalanceImpact { get; set; }
    [JsonPropertyName("electrical_load_impact_va")] public double ElectricalLoadImpactVa { get; set; }
    [JsonPropertyName("interchangeability")] public Interchangeability Interchangeability { get; set; }
    [JsonPropertyName("parts_added")] public List<string> PartsAdded { get; set; } = [];
    [JsonPropertyName("parts_removed")] public List<string> PartsRemoved { get; set; } = [];
    [JsonPropertyName("parts_modified")] public List<string> PartsModified { get; set; } = [];
    [JsonPropertyName("fins_affected")] public List<string> FinsAffected { get; set; } = [];
    [JsonPropertyName("software_affected")] public List<string> SoftwareAffected { get; set; } = [];
    [JsonPropertyName("estimated_manhours")] public double EstimatedManhours { get; set; }
    [JsonPropertyName("estimated_cost_keur")] public double EstimatedCostKeur { get; set; }
    [JsonPropertyName("lead_time_weeks")] public int LeadTimeWeeks { get; set; }
    [JsonPropertyName("production_impact")] public string? ProductionImpact { get; set; }
    [JsonPropertyName("risk_level")] public RiskLevel RiskLevel { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class CertificationInfo
{
    [JsonPropertyName("impact")] public CertificationImpact Impact { get; set; }
    [JsonPropertyName("authority")] public List<string> Authority { get; set; } = [];
    [JsonPropertyName("cs25_paragraphs")] public List<string> Cs25Paragraphs { get; set; } = [];
    [JsonPropertyName("requires_flight_test")] public bool RequiresFlightTest { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for a Service Bulletin.</summary>
public sealed class ServiceBulletinData
{
    [JsonPropertyName("sb_number")] public string SbNumber { get; set; } = "";
    [JsonPropertyName("sb_revision")] public string? SbRevision { get; set; }
    [JsonPropertyName("sb_type")] public SbType SbType { get; set; }
    [JsonPropertyName("compliance")] public Compliance Compliance { get; set; }
    [JsonPropertyName("compliance_time")] public string? ComplianceTime { get; set; }
    [JsonPropertyName("ad_linked")] public bool AdLinked { get; set; }
    [JsonPropertyName("ad_reference")] public string? AdReference { get; set; }
    [JsonPropertyName("reason_code")] public SbReasonCode ReasonCode { get; set; }
    [JsonPropertyName("embodied_mod")] public string? EmbodiedMod { get; set; }
    [JsonPropertyName("manpower")] public Manpower? Manpower { get; set; }
    [JsonPropertyName("material")] public SbMaterial? Material { get; set; }
    [JsonPropertyName("weight_change_kg")] public double WeightChangeKg { get; set; }
    [JsonPropertyName("accomplishment_steps")] public int AccomplishmentSteps { get; set; }
    [JsonPropertyName("interchangeability")] public Interchangeability Interchangeability { get; set; }
    [JsonPropertyName("warranty_applicable")] public bool WarrantyApplicable { get; set; }
    [JsonPropertyName("revision_history")] public List<SbRevisionEntry> RevisionHistory { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class Manpower
{
    [JsonPropertyName("elapsed_time_hours")] public double ElapsedTimeHours { get; set; }
    [JsonPropertyName("manhours")] public double Manhours { get; set; }
    [JsonPropertyName("personnel")] public int Personnel { get; set; }
    [JsonPropertyName("downtime_category")] public DowntimeCategory DowntimeCategory { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class SbMaterial
{
    [JsonPropertyName("kit_number")] public string? KitNumber { get; set; }
    [JsonPropertyName("kit_available_from")] public string? KitAvailableFrom { get; set; }
    [JsonPropertyName("parts_required")] public List<PartQuantity> PartsRequired { get; set; } = [];
    [JsonPropertyName("parts_returned")] public List<PartQuantity> PartsReturned { get; set; } = [];
    [JsonPropertyName("consumables")] public List<string> Consumables { get; set; } = [];
    [JsonPropertyName("tooling")] public List<string> Tooling { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class PartQuantity
{
    [JsonPropertyName("part_number")] public string PartNumber { get; set; } = "";
    [JsonPropertyName("qty")] public double Qty { get; set; }
    [JsonPropertyName("uom")] public string? Uom { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class SbRevisionEntry
{
    [JsonPropertyName("revision")] public string Revision { get; set; } = "";
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}
