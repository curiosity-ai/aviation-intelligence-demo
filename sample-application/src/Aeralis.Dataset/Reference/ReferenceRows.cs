using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using CsvHelper.TypeConversion;

namespace Aeralis.Dataset.Reference;

/// <summary>Reads a pipe-separated cell such as <c>P|C|R</c> as a list.</summary>
public sealed class PipeListConverter : DefaultTypeConverter
{
    public override object ConvertFromString(string? text, CsvHelper.IReaderRow row, MemberMapData memberMapData)
        => string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();

    public override string? ConvertToString(object? value, CsvHelper.IWriterRow row, MemberMapData memberMapData)
        => value is List<string> list ? string.Join('|', list) : base.ConvertToString(value, row, memberMapData);
}

/// <summary>Common shape: a reference row that declares which programs it applies to.</summary>
public interface IProgramScoped
{
    List<string> Programs { get; }
}

public sealed class AtaChapter
{
    [Name("code")] public string Code { get; set; } = "";
    [Name("level")] public int Level { get; set; }
    [Name("parent")] public string Parent { get; set; } = "";
    [Name("title")] public string Title { get; set; } = "";
    [Name("domain")] public string Domain { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];

    /// <summary>
    /// Fallback illustration placement for a document that carries no zone: an
    /// <c>AircraftSection</c> enum name from <c>Aeralis.Illustrations</c>.
    /// </summary>
    [Name("airframe_section")] public string AirframeSection { get; set; } = "";

    /// <summary>The <c>ViewKind</c> the chapter reads best on: Plan, Side or Front.</summary>
    [Name("view_hint")] public string ViewHint { get; set; } = "";
}

public sealed class PartNumberRow : IProgramScoped
{
    [Name("part_number")] public string PartNumber { get; set; } = "";
    [Name("description")] public string Description { get; set; } = "";
    [Name("pn_type")] public string PnType { get; set; } = "";
    [Name("ata_6d")] public string Ata6d { get; set; } = "";
    [Name("ata_2d")] public string Ata2d { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];
    [Name("supplier_code")] public string SupplierCode { get; set; } = "";
    [Name("uom")] public string Uom { get; set; } = "";
    [Name("mass_kg")] public double MassKg { get; set; }
    [Name("unit_price_eur")] public int UnitPriceEur { get; set; }
    [Name("lifecycle")] public string Lifecycle { get; set; } = "";
    [Name("interchangeable_with")] public string InterchangeableWith { get; set; } = "";
}

public sealed class FinRow : IProgramScoped
{
    [Name("fin")] public string Fin { get; set; } = "";
    [Name("description")] public string Description { get; set; } = "";
    [Name("ata_6d")] public string Ata6d { get; set; } = "";
    [Name("ata_2d")] public string Ata2d { get; set; } = "";
    [Name("system")] public string System { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];
    [Name("zone")] public string Zone { get; set; } = "";
    [Name("default_part_number")] public string DefaultPartNumber { get; set; } = "";
    [Name("lru")] public bool Lru { get; set; }
}

public sealed class MsnRow
{
    [Name("msn")] public string Msn { get; set; } = "";
    [Name("program")] public string Program { get; set; } = "";
    [Name("aircraft_type")] public string AircraftType { get; set; } = "";
    [Name("model")] public string Model { get; set; } = "";
    [Name("line_number")] public int LineNumber { get; set; }
    [Name("operator_code")] public string OperatorCode { get; set; } = "";
    [Name("engine_type")] public string EngineType { get; set; } = "";
    [Name("delivery_date")] public string DeliveryDate { get; set; } = "";
    [Name("registration")] public string Registration { get; set; } = "";
    [Name("status")] public string Status { get; set; } = "";
}

public sealed class DrawingRow : IProgramScoped
{
    [Name("drawing_number")] public string DrawingNumber { get; set; } = "";
    [Name("title")] public string Title { get; set; } = "";
    [Name("ata_6d")] public string Ata6d { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];
    [Name("drawing_type")] public string DrawingType { get; set; } = "";
    [Name("issue")] public string Issue { get; set; } = "";
    [Name("issue_date")] public string IssueDate { get; set; } = "";
    [Name("cad_system")] public string CadSystem { get; set; } = "";
}

public sealed class SoftwareRow : IProgramScoped
{
    [Name("software_pn")] public string SoftwarePn { get; set; } = "";
    [Name("software_name")] public string SoftwareName { get; set; } = "";
    [Name("system")] public string System { get; set; } = "";
    [Name("ata_6d")] public string Ata6d { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];
    [Name("version")] public string Version { get; set; } = "";
    [Name("load_type")] public string LoadType { get; set; } = "";
    [Name("host_fin")] public string HostFin { get; set; } = "";
    [Name("certification_status")] public string CertificationStatus { get; set; } = "";
    [Name("supersedes")] public string Supersedes { get; set; } = "";
}

public sealed class ZoneRow : IProgramScoped
{
    [Name("zone")] public string Zone { get; set; } = "";
    [Name("zone_name")] public string ZoneName { get; set; } = "";
    [Name("major_zone")] public string MajorZone { get; set; } = "";
    [Name("major_zone_name")] public string MajorZoneName { get; set; } = "";
    [Name("programs")][TypeConverter(typeof(PipeListConverter))] public List<string> Programs { get; set; } = [];
    [Name("access_panels")][TypeConverter(typeof(PipeListConverter))] public List<string> AccessPanels { get; set; } = [];

    /// <summary>
    /// Where an illustration callout for this zone points: an <c>AircraftSection</c>
    /// enum name from <c>Aeralis.Illustrations</c>.
    /// </summary>
    [Name("airframe_section")] public string AirframeSection { get; set; } = "";

    /// <summary>The <c>ViewKind</c> the zone reads best on: Plan, Side or Front.</summary>
    [Name("view_hint")] public string ViewHint { get; set; } = "";

    /// <summary>
    /// Position along the section, 0.05–0.95, where 0 is its forward or inboard end
    /// (nose for the fuselage, root for a wing) and 1 the aft or outboard end.
    /// </summary>
    [Name("section_position")] public double SectionPosition { get; set; }
}

public sealed class OperatorRow
{
    [Name("operator_code")] public string OperatorCode { get; set; } = "";
    [Name("operator_name")] public string OperatorName { get; set; } = "";
    [Name("iata")] public string Iata { get; set; } = "";
    [Name("icao")] public string Icao { get; set; } = "";
    [Name("country")] public string Country { get; set; } = "";
    [Name("region")] public string Region { get; set; } = "";
    [Name("fleet_programs")][TypeConverter(typeof(PipeListConverter))] public List<string> FleetPrograms { get; set; } = [];
    [Name("registration_prefix")] public string RegistrationPrefix { get; set; } = "";
}

public sealed class SupplierRow
{
    [Name("supplier_code")] public string SupplierCode { get; set; } = "";
    [Name("supplier_name")] public string SupplierName { get; set; } = "";
    [Name("country")] public string Country { get; set; } = "";
    [Name("domains")][TypeConverter(typeof(PipeListConverter))] public List<string> Domains { get; set; } = [];
    [Name("cage_code")] public string CageCode { get; set; } = "";
}
