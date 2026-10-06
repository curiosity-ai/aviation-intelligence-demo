using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeralis.Dataset.Documents;

/// <summary><c>type_data</c> for an Aircraft Maintenance Manual task.</summary>
public sealed class AmmTaskData
{
    [JsonPropertyName("task_number")] public string TaskNumber { get; set; } = "";
    [JsonPropertyName("task_type")] public AmmTaskType TaskType { get; set; }
    [JsonPropertyName("effectivity_note")] public string? EffectivityNote { get; set; }
    [JsonPropertyName("manhours")] public double Manhours { get; set; }
    [JsonPropertyName("personnel")] public int Personnel { get; set; }
    [JsonPropertyName("elapsed_time_hours")] public double ElapsedTimeHours { get; set; }
    [JsonPropertyName("access")] public AmmAccess? Access { get; set; }
    [JsonPropertyName("job_setup")] public AmmJobSetup? JobSetup { get; set; }
    [JsonPropertyName("referenced_tasks")] public List<string> ReferencedTasks { get; set; } = [];
    [JsonPropertyName("steps")] public int Steps { get; set; }
    [JsonPropertyName("warnings")] public int Warnings { get; set; }
    [JsonPropertyName("cautions")] public int Cautions { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class AmmAccess
{
    [JsonPropertyName("zones")] public List<string> Zones { get; set; } = [];
    [JsonPropertyName("access_panels")] public List<string> AccessPanels { get; set; } = [];
    [JsonPropertyName("doors")] public List<string> Doors { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class AmmJobSetup
{
    [JsonPropertyName("safety_precautions")] public List<string> SafetyPrecautions { get; set; } = [];
    [JsonPropertyName("tooling")] public List<ToolingItem> Tooling { get; set; } = [];
    [JsonPropertyName("consumables")] public List<string> Consumables { get; set; } = [];
    [JsonPropertyName("expendables")] public List<PartQuantity> Expendables { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class ToolingItem
{
    [JsonPropertyName("reference")] public string Reference { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for an Illustrated Parts Catalogue figure.</summary>
public sealed class IpcFigureData
{
    [JsonPropertyName("figure")] public string Figure { get; set; } = "";
    [JsonPropertyName("figure_title")] public string? FigureTitle { get; set; }
    [JsonPropertyName("sheet_count")] public int SheetCount { get; set; }
    [JsonPropertyName("items")] public List<IpcItem> Items { get; set; } = [];
    [JsonPropertyName("next_higher_assembly")] public string? NextHigherAssembly { get; set; }
    [JsonPropertyName("related_figures")] public List<string> RelatedFigures { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class IpcItem
{
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    [JsonPropertyName("part_number")] public string PartNumber { get; set; } = "";
    [JsonPropertyName("nomenclature")] public string? Nomenclature { get; set; }
    [JsonPropertyName("qty_per_assy")] public double QtyPerAssy { get; set; }
    [JsonPropertyName("units_per_assy")] public string? UnitsPerAssy { get; set; }

    /// <summary>Free text, but "PRE MOD-nnnnnn" / "POST MOD-nnnnnn" carries embodiment state.</summary>
    [JsonPropertyName("effectivity")] public string? Effectivity { get; set; }

    [JsonPropertyName("fin")] public string? Fin { get; set; }
    [JsonPropertyName("vendor")] public string? Vendor { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for a Structure Repair Manual repair.</summary>
public sealed class SrmRepairData
{
    [JsonPropertyName("repair_number")] public string RepairNumber { get; set; } = "";
    [JsonPropertyName("repair_category")] public RepairCategory RepairCategory { get; set; }
    [JsonPropertyName("damage_type")] public DamageType DamageType { get; set; }
    [JsonPropertyName("allowable_damage_limits")] public string? AllowableDamageLimits { get; set; }
    [JsonPropertyName("repair_drawings")] public List<RepairDrawing> RepairDrawings { get; set; } = [];
    [JsonPropertyName("materials")] public List<RepairMaterial> Materials { get; set; } = [];
    [JsonPropertyName("fastener_pattern")] public string? FastenerPattern { get; set; }
    [JsonPropertyName("ndt_method")] public NdtMethod NdtMethod { get; set; }
    [JsonPropertyName("static_strength_restored_pct")] public double StaticStrengthRestoredPct { get; set; }
    [JsonPropertyName("fatigue_life_note")] public string? FatigueLifeNote { get; set; }
    [JsonPropertyName("weight_penalty_kg")] public double WeightPenaltyKg { get; set; }
    [JsonPropertyName("aerodynamic_smoothness")] public string? AerodynamicSmoothness { get; set; }
    [JsonPropertyName("requires_oem_approval")] public bool RequiresOemApproval { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class RepairDrawing
{
    [JsonPropertyName("reference")] public string Reference { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("sheets")] public int Sheets { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class RepairMaterial
{
    [JsonPropertyName("part_number")] public string PartNumber { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("qty")] public double Qty { get; set; }
    [JsonPropertyName("uom")] public string? Uom { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for a Trouble Shooting Manual procedure.</summary>
public sealed class TsmProcedureData
{
    [JsonPropertyName("procedure_number")] public string ProcedureNumber { get; set; } = "";
    [JsonPropertyName("fault_symptom")] public string? FaultSymptom { get; set; }
    [JsonPropertyName("fault_codes")] public List<string> FaultCodes { get; set; } = [];
    [JsonPropertyName("warning_messages")] public List<string> WarningMessages { get; set; } = [];
    [JsonPropertyName("affected_fins")] public List<string> AffectedFins { get; set; } = [];
    [JsonPropertyName("possible_causes")] public List<PossibleCause> PossibleCauses { get; set; } = [];
    [JsonPropertyName("isolation_steps")] public int IsolationSteps { get; set; }
    [JsonPropertyName("confirmation_task")] public string? ConfirmationTask { get; set; }
    [JsonPropertyName("dispatch")] public DispatchInfo? Dispatch { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class PossibleCause
{
    [JsonPropertyName("cause")] public string Cause { get; set; } = "";
    [JsonPropertyName("probability_pct")] public double ProbabilityPct { get; set; }
    [JsonPropertyName("fin")] public string? Fin { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class DispatchInfo
{
    [JsonPropertyName("mel_item")] public string? MelItem { get; set; }
    [JsonPropertyName("dispatchable")] public bool Dispatchable { get; set; }
    [JsonPropertyName("dispatch_condition")] public string? DispatchCondition { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for a Wiring Diagram Manual sheet.</summary>
public sealed class WdmSheetData
{
    [JsonPropertyName("sheet_number")] public string SheetNumber { get; set; } = "";
    [JsonPropertyName("sheet_title")] public string? SheetTitle { get; set; }
    [JsonPropertyName("diagram_type")] public DiagramType DiagramType { get; set; }
    [JsonPropertyName("harnesses")] public List<string> Harnesses { get; set; } = [];
    [JsonPropertyName("connectors")] public List<WdmConnector> Connectors { get; set; } = [];
    [JsonPropertyName("equipment")] public List<WdmEquipment> Equipment { get; set; } = [];
    [JsonPropertyName("wire_count")] public int WireCount { get; set; }
    [JsonPropertyName("wire_gauge_range")] public string? WireGaugeRange { get; set; }
    [JsonPropertyName("routes")] public List<string> Routes { get; set; } = [];
    [JsonPropertyName("related_sheets")] public List<string> RelatedSheets { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class WdmConnector
{
    [JsonPropertyName("reference")] public string Reference { get; set; } = "";
    [JsonPropertyName("part_number")] public string? PartNumber { get; set; }
    [JsonPropertyName("location_zone")] public string? LocationZone { get; set; }
    [JsonPropertyName("contacts")] public int Contacts { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class WdmEquipment
{
    [JsonPropertyName("fin")] public string Fin { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("zone")] public string? Zone { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary><c>type_data</c> for a Flight Crew Operating Manual entry.</summary>
public sealed class FcomEntryData
{
    [JsonPropertyName("section")] public FcomSection Section { get; set; }
    [JsonPropertyName("section_title")] public string? SectionTitle { get; set; }
    [JsonPropertyName("entry_number")] public string EntryNumber { get; set; } = "";
    [JsonPropertyName("flight_phase")] public FlightPhase FlightPhase { get; set; }
    [JsonPropertyName("operational_impact")] public OperationalImpact OperationalImpact { get; set; }
    [JsonPropertyName("crew_action")] public string? CrewAction { get; set; }
    [JsonPropertyName("affects_qrh")] public bool AffectsQrh { get; set; }
    [JsonPropertyName("affects_mel")] public bool AffectsMel { get; set; }
    [JsonPropertyName("mel_items")] public List<string> MelItems { get; set; } = [];
    [JsonPropertyName("systems")] public List<string> Systems { get; set; } = [];
    [JsonPropertyName("related_entries")] public List<string> RelatedEntries { get; set; } = [];
    [JsonPropertyName("supersedes_entry")] public string? SupersedesEntry { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}
