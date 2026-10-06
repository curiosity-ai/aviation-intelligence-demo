using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeralis.Dataset;

/// <summary>
/// Controlled vocabularies from the authoring skills. Members are named exactly as
/// they appear in the JSON, so a value that fails to parse is a real data defect
/// rather than a naming mismatch — it lands on <c>Unknown</c> and is recorded in
/// <see cref="VocabularyLog"/>.
/// </summary>
public enum DocType { Unknown = 0, IO, MOD, PAWO, SB, AMM, IPC, SRM, TSM, WDM, FCOM }

public enum ProgramLetter { Unknown = 0, M, C, T, Z, S }

public enum ApplicabilityScope
{
    Unknown = 0, ALL_PROGRAM, TYPE, MODEL, MSN_RANGE, MSN_LIST, OPERATOR,
}

public enum EmbodimentStatus
{
    Unknown = 0, PROPOSAL, PRE_ANALYSIS, LAUNCHED, FROZEN, IN_PRODUCTION, IN_SERVICE,
    SUPERSEDED, CANCELLED,
}

public enum EmbodimentPoint { Unknown = 0, PRODUCTION, RETROFIT, SPARES, NONE }

public enum FcomSection { Unknown = 0, DSC, PRO, LIM, PER }

public enum OperationalImpact
{
    Unknown = 0, NONE, PROCEDURE_CHANGE, LIMITATION_CHANGE, PERFORMANCE_CHANGE,
    TRAINING_REQUIRED,
}

public enum FlightPhase
{
    Unknown = 0, PREFLIGHT, TAXI, TAKEOFF, CLIMB, CRUISE, DESCENT, APPROACH, LANDING,
    ALL_PHASES,
}

public enum RelationType
{
    Unknown = 0, SUPERSEDES, SUPERSEDED_BY, EMBODIED_BY, EMBODIES, PREREQUISITE_OF,
    REQUIRES, CONCURRENT_WITH, IMPACTS, DOCUMENTED_IN, REFERENCES, RAISED_BY, CREATED,
    PRE_ANALYSIS_OF, CONFLICTS_WITH, REPLACES_PART_IN,
}

public enum ModClass { Unknown = 0, MAJOR, MINOR }

public enum ChangeCategory
{
    Unknown = 0, STRUCTURE, SYSTEM, CABIN, SOFTWARE, POWERPLANT, ELECTRICAL, AVIONICS,
    DOCUMENTATION, REPAIR,
}

public enum ModTrigger
{
    Unknown = 0, IN_SERVICE_FINDING, CUSTOMER_REQUEST, OBSOLESCENCE, WEIGHT_SAVING,
    RELIABILITY, AIRWORTHINESS_DIRECTIVE, CERTIFICATION, SUPPLIER_CHANGE, COST_REDUCTION,
    FUEL_BURN, PRODUCTION_IMPROVEMENT, SAFETY,
}

public enum CertificationImpact { Unknown = 0, NO_IMPACT, MINOR_CHANGE, MAJOR_CHANGE, STC }

public enum Interchangeability
{
    Unknown = 0, INTERCHANGEABLE, ONE_WAY_INTERCHANGEABLE, NOT_INTERCHANGEABLE,
}

public enum RiskLevel { Unknown = 0, LOW, MEDIUM, HIGH }

public enum SbType
{
    Unknown = 0, MODIFICATION, INSPECTION, REPAIR, SOFTWARE_LOAD, CONVERSION, INFORMATION,
    SUPPLIER,
}

public enum Compliance { Unknown = 0, MANDATORY, RECOMMENDED, DESIRABLE, OPTIONAL }

public enum DowntimeCategory { Unknown = 0, LINE, A_CHECK, C_CHECK, D_CHECK, AOG }

public enum SbReasonCode
{
    Unknown = 0, FATIGUE, CORROSION, OBSOLESCENCE, RELIABILITY, SAFETY, PERFORMANCE,
    CUSTOMER, PRODUCTION, SOFTWARE_DEFECT, SUPPLIER_QUALITY,
}

public enum RequesterType
{
    Unknown = 0, OPERATOR, LESSOR, OEM_INTERNAL, MRO, AUTHORITY, SUPPLIER,
}

public enum RequestCategory
{
    Unknown = 0, STRUCTURE, SYSTEM, CABIN, SOFTWARE, REPAIR, POWERPLANT, AVIONICS, WEIGHT,
    OBSOLESCENCE,
}

public enum Priority { Unknown = 0, AOG, HIGH, MEDIUM, LOW }

public enum IoStatus
{
    Unknown = 0, RAISED, UNDER_REVIEW, CONVERTED_TO_MOD, REJECTED, ON_HOLD, CLOSED,
}

public enum Funding { Unknown = 0, CUSTOMER_FUNDED, OEM_FUNDED, WARRANTY, SHARED, CONTRACTUAL }

public enum PawoStatus { Unknown = 0, RAISED, IN_PROGRESS, ON_HOLD, COMPLETED, CANCELLED }

/// <summary>How a closed pre-analysis ended. <c>Unknown</c> is the correct value while it is open.</summary>
public enum PawoOutcome { Unknown = 0, VALIDATED, REJECTED, DELEGATED }

public enum AnalysisMethod
{
    Unknown = 0, ENTITY, SEMANTIC, ENTITY_AND_SEMANTIC, MANUAL_REVIEW, SME_INTERVIEW,
}

public enum TaskStatus { Unknown = 0, OPEN, IN_PROGRESS, DONE, BLOCKED }

public enum Recommendation
{
    Unknown = 0, PROCEED, PROCEED_WITH_CONDITIONS, REWORK_SCOPE, MERGE_WITH_EXISTING_MOD,
    REJECT, PENDING,
}

public enum FindingType
{
    Unknown = 0, DUPLICATE, OVERLAPPING_SCOPE, PREREQUISITE, SUPERSESSION,
    EFFECTIVITY_INVALIDATION, PART_CONFLICT, FIN_CONFLICT, SOFTWARE_CONFLICT,
    ZONE_CONFLICT, REPAIR_CONFLICT, MANUAL_IMPACT, CERTIFICATION_IMPACT,
    CONCURRENCY_REQUIRED,
}

public enum Severity { Unknown = 0, BLOCKING, MAJOR, MINOR, INFORMATIONAL }

public enum DiscoveryMode { Unknown = 0, ENTITY, SEMANTIC, BOTH, APPLICABILITY, GRAPH_TRAVERSAL }

public enum AmmTaskType
{
    Unknown = 0, REMOVAL, INSTALLATION, INSPECTION, REPAIR, CLEANING, TEST, SERVICING,
    DEACTIVATION, ADJUSTMENT,
}

public enum RepairCategory { Unknown = 0, TEMPORARY, INTERIM, PERMANENT }

public enum DamageType
{
    Unknown = 0, CRACK, CORROSION, DENT, SCRATCH, PUNCTURE, DELAMINATION, LIGHTNING_STRIKE,
    IMPACT, HOLE,
}

public enum NdtMethod
{
    Unknown = 0, VISUAL, EDDY_CURRENT, ULTRASONIC, X_RAY, DYE_PENETRANT, THERMOGRAPHY,
    TAP_TEST,
}

public enum DiagramType
{
    Unknown = 0, WIRING_DIAGRAM, SCHEMATIC, EQUIPMENT_LIST, CONNECTOR_LIST, GROUND_POINT_LIST,
}

/// <summary>Records every enum value the dataset used that the vocabulary does not contain.</summary>
public static class VocabularyLog
{
    private static readonly ConcurrentBag<(string EnumType, string Value)> Entries = [];

    public static void Record(string enumType, string value) => Entries.Add((enumType, value));

    public static IReadOnlyList<(string EnumType, string Value)> All => [.. Entries];

    public static void Clear() => Entries.Clear();
}

/// <summary>
/// Maps a JSON string onto <typeparamref name="T"/>, falling back to <c>Unknown</c> and
/// logging the offending value instead of throwing — an unknown value is a finding to
/// report, not a reason to abandon the file.
/// </summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return default;
        var raw = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : reader.GetInt32().ToString();
        if (string.IsNullOrEmpty(raw)) return default;
        if (Enum.TryParse<T>(raw, ignoreCase: false, out var value)) return value;
        if (Enum.TryParse(raw, ignoreCase: true, out value)) return value;
        VocabularyLog.Record(typeof(T).Name, raw);
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>Supplies <see cref="TolerantEnumConverter{T}"/> for every enum in this assembly.</summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsEnum && typeToConvert.Assembly == typeof(DocType).Assembly;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}
