using Curiosity.Library;
using CsvHelper.Configuration.Attributes;

namespace Aeralis.Connector.Ingest;

// One handler class per input type. Each owns its CsvHelper DTO (`Row`) and its graph
// schema class as nested types, so neither can leak into another handler. The input
// schema deliberately does not have to match the node schema — zones split into a Zone
// plus its AccessPanel children, and the program table is derived, not read.

/// <summary>Programs, aircraft types and models. Derived from the dataset conventions, not from a file.</summary>
internal static class ProgramsIngest
{
    public const int Version = 2;

    [Node]
    internal sealed class Program
    {
        [Key] public string Letter { get; set; } = "";
        [Property] public string Name { get; set; } = "";
    }

    [Node]
    internal sealed class AircraftType
    {
        [Key] public string Name { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
    }

    [Node]
    internal sealed class AircraftModel
    {
        [Key] public string Name { get; set; } = "";
        [Property] public string AircraftType { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
    }

    /// <summary>Program letter → name, types, and the models under each type.</summary>
    public static readonly IReadOnlyDictionary<string, (string Name, (string Type, string[] Models)[] Types)> Catalogue =
        new Dictionary<string, (string, (string, string[])[])>
        {
            ["P"] = ("MERIDIAN Family",
                [("MERIDIAN-42", ["MRD-42K1"]), ("MERIDIAN-46", ["MRD-46K1"]), ("MERIDIAN-42F", ["MRD-42FK1"])]),
            ["C"] = ("CIRRANE",
                [("CIRRANE-11", ["CRN-11G1"]), ("CIRRANE-13", ["CRN-13G1"])]),
            ["R"] = ("TITAN",
                [("TITAN-88", ["TTN-88W1", "TTN-88W2", "TTN-88E1"])]),
            ["N"] = ("ZEPHYR Family",
                [("ZEPHYR-18", ["ZPH-18C1", "ZPH-18C2"]),
                 ("ZEPHYR-20", ["ZPH-20C2", "ZPH-20C5", "ZPH-20N3"]),
                 ("ZEPHYR-22", ["ZPH-22C4", "ZPH-22V2", "ZPH-22N1", "ZPH-22N7"]),
                 ("ZEPHYR-24", ["ZPH-24C1", "ZPH-24V1", "ZPH-24N3", "ZPH-24N7"])]),
            ["L"] = ("SOLANO Family",
                [("SOLANO-30", ["SLN-30R3", "SLN-30V3"]), ("SOLANO-34", ["SLN-34R3", "SLN-34V3"]),
                 ("SOLANO-38", ["SLN-38R1"]), ("SOLANO-39", ["SLN-39R1"]),
                 ("VANTOR-64", ["VTR-64C3"]), ("VANTOR-68", ["VTR-68R2"])]),
        };

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var (letter, (name, types)) in Catalogue)
        {
            var program = ctx.Graph.TryAdd(new Program { Letter = letter, Name = name });
            ctx.CountNode(nameof(Program));

            foreach (var (typeName, models) in types)
            {
                var type = ctx.Graph.TryAdd(new AircraftType { Name = typeName, ProgramLetter = letter });
                ctx.CountNode(nameof(AircraftType));
                ctx.Graph.Link(type, program, Edges.TypeOfProgram, Edges.HasAircraftType);
                ctx.CountEdge(Edges.TypeOfProgram);

                foreach (var modelName in models)
                {
                    var model = ctx.Graph.TryAdd(new AircraftModel
                    {
                        Name = modelName, AircraftType = typeName, ProgramLetter = letter,
                    });
                    ctx.CountNode(nameof(AircraftModel));
                    ctx.Graph.Link(model, type, Edges.ModelOfType, Edges.HasAircraftModel);
                    ctx.CountEdge(Edges.ModelOfType);
                }
            }
        }

        await ctx.Graph.CommitPendingAsync();
    }
}

/// <summary>`ata_chapters.csv` → the ATA 2D/4D/6D tree.</summary>
internal static class AtaChaptersIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("code")] public string Code { get; set; } = "";
        [Name("level")] public int Level { get; set; }
        [Name("parent")] public string Parent { get; set; } = "";
        [Name("title")] public string Title { get; set; } = "";
        [Name("domain")] public string Domain { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
    }

    [Node]
    internal sealed class AtaChapter
    {
        [Key] public string Code { get; set; } = "";
        [Property] public int Level { get; set; }
        [Property] public string Title { get; set; } = "";
        [Property] public string Domain { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("ata_chapters.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new AtaChapter
                {
                    Code = r.Code,
                    Level = r.Level,
                    Title = r.Title,
                    Domain = r.Domain,
                    Ata2d = r.Code.Split('-')[0],
                });
                ctx.CountNode(nameof(AtaChapter));

                ctx.LinkKey(node, nameof(AtaChapter), r.Parent, Edges.ChildOfAta, Edges.HasAtaChild);

                if (!string.IsNullOrWhiteSpace(r.Domain))
                {
                    if (ctx.Mint(nameof(MetadataSchema.AtaDomain), r.Domain))
                    {
                        ctx.Graph.TryAdd(new MetadataSchema.AtaDomain { Name = r.Domain });
                        ctx.CountNode(nameof(MetadataSchema.AtaDomain));
                    }
                    ctx.LinkKey(node, nameof(MetadataSchema.AtaDomain), r.Domain,
                                Edges.InAtaDomain, Edges.HasAtaChapter);
                }

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`zones.csv` → a Zone plus one AccessPanel child per listed panel.</summary>
internal static class ZonesIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("zone")] public string Zone { get; set; } = "";
        [Name("zone_name")] public string ZoneName { get; set; } = "";
        [Name("major_zone")] public string MajorZone { get; set; } = "";
        [Name("major_zone_name")] public string MajorZoneName { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("access_panels")] public string AccessPanels { get; set; } = "";
    }

    [Node]
    internal sealed class Zone
    {
        [Key] public string ZoneId { get; set; } = "";
        [Property] public string ZoneName { get; set; } = "";
        [Property] public string MajorZone { get; set; } = "";
        [Property] public string MajorZoneName { get; set; } = "";
    }

    [Node]
    internal sealed class AccessPanel
    {
        [Key] public string Panel { get; set; } = "";
        [Property] public string ZoneId { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("zones.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new Zone
                {
                    ZoneId = r.Zone,
                    ZoneName = r.ZoneName,
                    MajorZone = r.MajorZone,
                    MajorZoneName = r.MajorZoneName,
                });
                ctx.CountNode(nameof(Zone));

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);

                if (ctx.Mint(nameof(MetadataSchema.MajorZone), r.MajorZone))
                {
                    ctx.Graph.TryAdd(new MetadataSchema.MajorZone
                    {
                        Code = r.MajorZone, Name = r.MajorZoneName,
                    });
                    ctx.CountNode(nameof(MetadataSchema.MajorZone));
                }
                ctx.LinkKey(node, nameof(MetadataSchema.MajorZone), r.MajorZone,
                            Edges.InMajorZone, Edges.HasZone);

                foreach (var panel in ConnectorContext.Pipe(r.AccessPanels))
                {
                    var panelNode = ctx.Graph.TryAdd(new AccessPanel { Panel = panel, ZoneId = r.Zone });
                    ctx.CountNode(nameof(AccessPanel));
                    ctx.Graph.Link(node, panelNode, Edges.HasAccessPanel, Edges.AccessPanelOf);
                    ctx.CountEdge(Edges.HasAccessPanel);
                }
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`suppliers.csv`.</summary>
internal static class SuppliersIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("supplier_code")] public string SupplierCode { get; set; } = "";
        [Name("supplier_name")] public string SupplierName { get; set; } = "";
        [Name("country")] public string Country { get; set; } = "";
        [Name("domains")] public string Domains { get; set; } = "";
        [Name("cage_code")] public string CageCode { get; set; } = "";
    }

    [Node]
    internal sealed class Supplier
    {
        [Key] public string SupplierCode { get; set; } = "";
        [Property] public string SupplierName { get; set; } = "";
        [Property] public string Country { get; set; } = "";
        [Property] public string Domains { get; set; } = "";
        [Property] public string CageCode { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("suppliers.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                ctx.Graph.AddOrUpdate(new Supplier
                {
                    SupplierCode = r.SupplierCode,
                    SupplierName = r.SupplierName,
                    Country = r.Country,
                    Domains = string.Join(", ", ConnectorContext.Pipe(r.Domains)),
                    CageCode = r.CageCode,
                });
                ctx.CountNode(nameof(Supplier));
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`operators.csv`.</summary>
internal static class OperatorsIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("operator_code")] public string OperatorCode { get; set; } = "";
        [Name("operator_name")] public string OperatorName { get; set; } = "";
        [Name("iata")] public string Iata { get; set; } = "";
        [Name("icao")] public string Icao { get; set; } = "";
        [Name("country")] public string Country { get; set; } = "";
        [Name("region")] public string Region { get; set; } = "";
        [Name("fleet_programs")] public string FleetPrograms { get; set; } = "";
        [Name("registration_prefix")] public string RegistrationPrefix { get; set; } = "";
    }

    [Node]
    internal sealed class Operator
    {
        [Key] public string OperatorCode { get; set; } = "";
        [Property] public string OperatorName { get; set; } = "";
        [Property] public string Iata { get; set; } = "";
        [Property] public string Icao { get; set; } = "";
        [Property] public string Country { get; set; } = "";
        [Property] public string Region { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("operators.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new Operator
                {
                    OperatorCode = r.OperatorCode,
                    OperatorName = r.OperatorName,
                    Iata = r.Iata,
                    Icao = r.Icao,
                    Country = r.Country,
                    Region = r.Region,
                });
                ctx.CountNode(nameof(Operator));

                foreach (var letter in ConnectorContext.Pipe(r.FleetPrograms))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}
