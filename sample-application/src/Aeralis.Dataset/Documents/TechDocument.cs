using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeralis.Dataset.Documents;

/// <summary>
/// The common envelope every document in <c>data/documents/</c> carries. Mirrors
/// <c>.claude/skills/aeralis-dataset-conventions</c> §6.
/// </summary>
public sealed class TechDocument
{
    [JsonPropertyName("doc_id")] public string DocId { get; set; } = "";
    [JsonPropertyName("doc_type")] public DocType DocType { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("program")] public ProgramLetter Program { get; set; }
    [JsonPropertyName("program_name")] public string? ProgramName { get; set; }
    [JsonPropertyName("revision")] public string? Revision { get; set; }
    [JsonPropertyName("issue")] public IssueInfo? Issue { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("ata")] public AtaRef? Ata { get; set; }
    [JsonPropertyName("secondary_ata")] public List<string> SecondaryAta { get; set; } = [];
    [JsonPropertyName("applicability")] public Applicability? Applicability { get; set; }
    [JsonPropertyName("embodiment")] public Embodiment? Embodiment { get; set; }
    [JsonPropertyName("dates")] public DocumentDates? Dates { get; set; }
    [JsonPropertyName("abstract")] public string Abstract { get; set; } = "";
    [JsonPropertyName("sections")] public Dictionary<string, string> Sections { get; set; } = [];
    [JsonPropertyName("entities")] public EntityRefs? Entities { get; set; }
    [JsonPropertyName("references")] public List<DocumentReference> References { get; set; } = [];
    [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = [];
    [JsonPropertyName("originator")] public Originator? Originator { get; set; }

    /// <summary>Type-specific payload, re-deserialised by <see cref="DocType"/>.</summary>
    [JsonPropertyName("type_data")] public JsonElement TypeData { get; set; }

    [JsonPropertyName("_meta")] public DocumentMeta? Meta { get; set; }

    /// <summary>Anything the envelope does not declare. Non-empty means model drift.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary>An envelope together with its strongly-typed <c>type_data</c> payload.</summary>
public sealed record TechDocument<TPayload>(TechDocument Envelope, TPayload? Payload)
    where TPayload : class;

/// <summary>
/// Which issue of a document this is, and what caused it.
/// </summary>
/// <remarks>
/// A document file holds an array of issues, oldest first, and the last one is the document as
/// it stands. An issue after the first exists because a completed pre-analysis found something
/// the document had to say differently — <see cref="RaisedByPawo"/> and
/// <see cref="RaisedByFinding"/> name that finding, so the change is traceable to the work
/// order that required it.
/// </remarks>
public sealed class IssueInfo
{
    /// <summary>The issue letter, from A.</summary>
    [JsonPropertyName("letter")] public string Letter { get; set; } = "A";

    /// <summary>The issue this one replaces, or null for an original issue.</summary>
    [JsonPropertyName("supersedes")] public string? Supersedes { get; set; }

    /// <summary>When it was issued.</summary>
    [JsonPropertyName("date")] public string? Date { get; set; }

    /// <summary>Why it was raised, in the words a change record would use.</summary>
    [JsonPropertyName("reason")] public string? Reason { get; set; }

    /// <summary>The pre-analysis work order whose finding required it.</summary>
    [JsonPropertyName("raised_by_pawo")] public string? RaisedByPawo { get; set; }

    /// <summary>The finding within that work order.</summary>
    [JsonPropertyName("raised_by_finding")] public string? RaisedByFinding { get; set; }

    /// <summary>That finding's type, repeated here so the issue reads on its own.</summary>
    [JsonPropertyName("finding_type")] public FindingType FindingType { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class AtaRef
{
    [JsonPropertyName("ata_2d")] public string Ata2d { get; set; } = "";
    [JsonPropertyName("ata_4d")] public string Ata4d { get; set; } = "";
    [JsonPropertyName("ata_6d")] public string Ata6d { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class Applicability
{
    [JsonPropertyName("scope")] public ApplicabilityScope Scope { get; set; }
    [JsonPropertyName("aircraft_types")] public List<string> AircraftTypes { get; set; } = [];
    [JsonPropertyName("models")] public List<string> Models { get; set; } = [];
    [JsonPropertyName("msns")] public List<string> Msns { get; set; } = [];
    [JsonPropertyName("msn_ranges")] public List<MsnRange> MsnRanges { get; set; } = [];
    [JsonPropertyName("excluded_msns")] public List<string> ExcludedMsns { get; set; } = [];
    [JsonPropertyName("engine_types")] public List<string> EngineTypes { get; set; } = [];
    [JsonPropertyName("operators")] public List<string> Operators { get; set; } = [];
    [JsonPropertyName("embodiment_condition")] public EmbodimentCondition? EmbodimentCondition { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class MsnRange
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class EmbodimentCondition
{
    [JsonPropertyName("post_mod")] public List<string> PostMod { get; set; } = [];
    [JsonPropertyName("pre_mod")] public List<string> PreMod { get; set; } = [];
    [JsonPropertyName("post_sb")] public List<string> PostSb { get; set; } = [];
    [JsonPropertyName("pre_sb")] public List<string> PreSb { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class Embodiment
{
    [JsonPropertyName("status")] public EmbodimentStatus Status { get; set; }
    [JsonPropertyName("points")] public List<EmbodimentPoint> Points { get; set; } = [];
    [JsonPropertyName("production_effectivity")] public string? ProductionEffectivity { get; set; }
    [JsonPropertyName("production_cut_in_date")] public string? ProductionCutInDate { get; set; }
    [JsonPropertyName("retrofit_via")] public List<string> RetrofitVia { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class DocumentDates
{
    [JsonPropertyName("created")] public string? Created { get; set; }
    [JsonPropertyName("released")] public string? Released { get; set; }
    [JsonPropertyName("revised")] public string? Revised { get; set; }
    [JsonPropertyName("effective")] public string? Effective { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

/// <summary>The entity-linking surface — the fields the application indexes.</summary>
public sealed class EntityRefs
{
    [JsonPropertyName("part_numbers")] public List<string> PartNumbers { get; set; } = [];
    [JsonPropertyName("fins")] public List<string> Fins { get; set; } = [];
    [JsonPropertyName("drawings")] public List<string> Drawings { get; set; } = [];
    [JsonPropertyName("software")] public List<string> Software { get; set; } = [];
    [JsonPropertyName("zones")] public List<string> Zones { get; set; } = [];
    [JsonPropertyName("figures")] public List<string> Figures { get; set; } = [];
    [JsonPropertyName("repair_drawings")] public List<string> RepairDrawings { get; set; } = [];
    [JsonPropertyName("msn_mentions")] public List<string> MsnMentions { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }

    public int TotalCount => PartNumbers.Count + Fins.Count + Drawings.Count + Software.Count
                             + Zones.Count + Figures.Count + RepairDrawings.Count + MsnMentions.Count;
}

public sealed class DocumentReference
{
    [JsonPropertyName("doc_id")] public string DocId { get; set; } = "";
    [JsonPropertyName("doc_type")] public DocType DocType { get; set; }
    [JsonPropertyName("relation")] public RelationType Relation { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class Originator
{
    [JsonPropertyName("department")] public string? Department { get; set; }
    [JsonPropertyName("site")] public string? Site { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}

public sealed class DocumentMeta
{
    [JsonPropertyName("synthetic")] public bool Synthetic { get; set; }
    [JsonPropertyName("generator")] public string? Generator { get; set; }
    [JsonPropertyName("dataset_version")] public string? DatasetVersion { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unmapped { get; set; }
}
