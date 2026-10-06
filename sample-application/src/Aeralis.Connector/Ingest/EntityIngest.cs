using Curiosity.Library;
using CsvHelper.Configuration.Attributes;

namespace Aeralis.Connector.Ingest;

/// <summary>`part_numbers.csv` — the entity-linking backbone.</summary>
internal static class PartNumbersIngest
{
    public const int Version = 3;   // bumped: the status field links to Status now

    private sealed class Row
    {
        [Name("part_number")] public string PartNumber { get; set; } = "";
        [Name("description")] public string Description { get; set; } = "";
        [Name("pn_type")] public string PnType { get; set; } = "";
        [Name("ata_6d")] public string Ata6d { get; set; } = "";
        [Name("ata_2d")] public string Ata2d { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("supplier_code")] public string SupplierCode { get; set; } = "";
        [Name("uom")] public string Uom { get; set; } = "";
        [Name("mass_kg")] public double MassKg { get; set; }
        [Name("unit_price_eur")] public int UnitPriceEur { get; set; }
        [Name("lifecycle")] public string Lifecycle { get; set; } = "";
        [Name("interchangeable_with")] public string InterchangeableWith { get; set; } = "";
    }

    [Node]
    internal sealed class PartNumber
    {
        [Key] public string Pn { get; set; } = "";
        [Property] public string Description { get; set; } = "";
        [Property] public string PnType { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Uom { get; set; } = "";
        [Property] public double MassKg { get; set; }
        [Property] public int UnitPriceEur { get; set; }
        [Property] public string Lifecycle { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("part_numbers.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new PartNumber
                {
                    Pn = r.PartNumber,
                    Description = r.Description,
                    PnType = r.PnType,
                    Ata6d = r.Ata6d,
                    Ata2d = r.Ata2d,
                    Uom = r.Uom,
                    MassKg = r.MassKg,
                    UnitPriceEur = r.UnitPriceEur,
                    Lifecycle = r.Lifecycle,
                });
                ctx.CountNode(nameof(PartNumber));

                MetaLinks.AtaFrom6d(ctx, node, r.Ata6d);
                ctx.LinkKey(node, nameof(SuppliersIngest.Supplier), r.SupplierCode,
                            Edges.SuppliedBy, Edges.Supplies);
                ctx.LinkKey(node, nameof(PartNumber), r.InterchangeableWith,
                            Edges.InterchangeableWith, Edges.InterchangeableWith);
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.PartLifecycle, r.Lifecycle);
                MetaLinks.Classify(ctx, node, MetaLinks.Kind.PartType, r.PnType);

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`fins.csv` — a function in a location, the strongest cross-document signal.</summary>
internal static class FinsIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("fin")] public string Fin { get; set; } = "";
        [Name("description")] public string Description { get; set; } = "";
        [Name("ata_6d")] public string Ata6d { get; set; } = "";
        [Name("ata_2d")] public string Ata2d { get; set; } = "";
        [Name("system")] public string System { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("zone")] public string Zone { get; set; } = "";
        [Name("default_part_number")] public string DefaultPartNumber { get; set; } = "";
        [Name("lru")] public bool Lru { get; set; }
    }

    [Node]
    internal sealed class Fin
    {
        [Key] public string FinId { get; set; } = "";
        [Property] public string Description { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string SystemName { get; set; } = "";
        [Property] public bool Lru { get; set; }
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("fins.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new Fin
                {
                    FinId = r.Fin,
                    Description = r.Description,
                    Ata6d = r.Ata6d,
                    Ata2d = r.Ata2d,
                    SystemName = r.System,
                    Lru = r.Lru,
                });
                ctx.CountNode(nameof(Fin));

                MetaLinks.AtaFrom6d(ctx, node, r.Ata6d);
                MetaLinks.Systems(ctx, node, new[] { r.System });
                ctx.LinkKey(node, nameof(ZonesIngest.Zone), r.Zone, Edges.FinInZone, Edges.HasFin);
                ctx.LinkKey(node, nameof(PartNumbersIngest.PartNumber), r.DefaultPartNumber,
                            Edges.DefaultPart, Edges.DefaultPartOf);
                if (!string.IsNullOrWhiteSpace(r.Zone))
                    ctx.LinkKey(node, nameof(MetadataSchema.MajorZone), r.Zone.Substring(0, 1) + "00",
                                Edges.InMajorZone, Edges.HasZone);

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`drawings.csv`.</summary>
internal static class DrawingsIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("drawing_number")] public string DrawingNumber { get; set; } = "";
        [Name("title")] public string Title { get; set; } = "";
        [Name("ata_6d")] public string Ata6d { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("drawing_type")] public string DrawingType { get; set; } = "";
        [Name("issue")] public string Issue { get; set; } = "";
        [Name("issue_date")] public DateTimeOffset IssueDate { get; set; }
        [Name("cad_system")] public string CadSystem { get; set; } = "";
    }

    [Node]
    internal sealed class Drawing
    {
        [Key] public string DrawingNumber { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string DrawingType { get; set; } = "";
        [Property] public string Issue { get; set; } = "";
        [Property] public string CadSystem { get; set; } = "";
        [Timestamp] public DateTimeOffset IssueDate { get; set; }
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("drawings.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new Drawing
                {
                    DrawingNumber = r.DrawingNumber,
                    Title = r.Title,
                    Ata6d = r.Ata6d,
                    DrawingType = r.DrawingType,
                    Issue = r.Issue,
                    CadSystem = r.CadSystem,
                    IssueDate = r.IssueDate,
                });
                ctx.CountNode(nameof(Drawing));
                MetaLinks.Classify(ctx, node, MetaLinks.Kind.DrawingType, r.DrawingType);

                MetaLinks.AtaFrom6d(ctx, node, r.Ata6d);

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`software.csv` — LSAP loads and their supersession chains.</summary>
internal static class SoftwareIngest
{
    public const int Version = 3;   // bumped: the status field links to Status now

    private sealed class Row
    {
        [Name("software_pn")] public string SoftwarePn { get; set; } = "";
        [Name("software_name")] public string SoftwareName { get; set; } = "";
        [Name("system")] public string System { get; set; } = "";
        [Name("ata_6d")] public string Ata6d { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("version")] public string Version { get; set; } = "";
        [Name("load_type")] public string LoadType { get; set; } = "";
        [Name("host_fin")] public string HostFin { get; set; } = "";
        [Name("certification_status")] public string CertificationStatus { get; set; } = "";
        [Name("supersedes")] public string Supersedes { get; set; } = "";
    }

    [Node]
    internal sealed class SoftwareLoad
    {
        [Key] public string SoftwarePn { get; set; } = "";
        [Property] public string SoftwareName { get; set; } = "";
        [Property] public string SystemName { get; set; } = "";
        [Property] public string Ata6d { get; set; } = "";
        [Property] public string SoftwareVersion { get; set; } = "";
        [Property] public string LoadType { get; set; } = "";
        [Property] public string CertificationStatus { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("software.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new SoftwareLoad
                {
                    SoftwarePn = r.SoftwarePn,
                    SoftwareName = r.SoftwareName,
                    SystemName = r.System,
                    Ata6d = r.Ata6d,
                    SoftwareVersion = r.Version,
                    LoadType = r.LoadType,
                    CertificationStatus = r.CertificationStatus,
                });
                ctx.CountNode(nameof(SoftwareLoad));

                MetaLinks.AtaFrom6d(ctx, node, r.Ata6d);
                MetaLinks.Systems(ctx, node, new[] { r.System });
                ctx.LinkKey(node, nameof(FinsIngest.Fin), r.HostFin, Edges.HostFin, Edges.HostsSoftware);
                ctx.LinkKey(node, nameof(SoftwareLoad), r.Supersedes,
                            Edges.SupersedesSoftware, Edges.SupersededBySoftware);
                MetaLinks.Classify(ctx, node, MetaLinks.Kind.SoftwareLoadType, r.LoadType);
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.CertificationStatus, r.CertificationStatus);

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`msn_registry.csv` — the aircraft themselves.</summary>
internal static class MsnIngest
{
    public const int Version = 3;   // bumped: the status field links to Status now

    private sealed class Row
    {
        [Name("msn")] public string Msn { get; set; } = "";
        [Name("program")] public string Program { get; set; } = "";
        [Name("aircraft_type")] public string AircraftType { get; set; } = "";
        [Name("model")] public string Model { get; set; } = "";
        [Name("line_number")] public int LineNumber { get; set; }
        [Name("operator_code")] public string OperatorCode { get; set; } = "";
        [Name("engine_type")] public string EngineType { get; set; } = "";
        [Name("delivery_date")] public DateTimeOffset DeliveryDate { get; set; }
        [Name("registration")] public string Registration { get; set; } = "";
        [Name("status")] public string Status { get; set; } = "";
    }

    [Node]
    internal sealed class Msn
    {
        [Key] public string MsnId { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";
        [Property] public string AircraftType { get; set; } = "";
        [Property] public string Model { get; set; } = "";
        [Property] public int LineNumber { get; set; }
        [Property] public string EngineType { get; set; } = "";
        [Property] public string Registration { get; set; } = "";
        [Property] public string Status { get; set; } = "";
        [Timestamp] public DateTimeOffset DeliveryDate { get; set; }
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("msn_registry.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var node = ctx.Graph.AddOrUpdate(new Msn
                {
                    MsnId = r.Msn,
                    ProgramLetter = r.Program,
                    AircraftType = r.AircraftType,
                    Model = r.Model,
                    LineNumber = r.LineNumber,
                    EngineType = r.EngineType,
                    Registration = r.Registration,
                    Status = r.Status,
                    DeliveryDate = r.DeliveryDate,
                });
                ctx.CountNode(nameof(Msn));

                ctx.LinkKey(node, nameof(ProgramsIngest.Program), r.Program, Edges.InProgram, Edges.HasDocument);
                ctx.LinkKey(node, nameof(ProgramsIngest.AircraftType), r.AircraftType, Edges.OfType, Edges.HasMsn);
                ctx.LinkKey(node, nameof(ProgramsIngest.AircraftModel), r.Model, Edges.OfModel, Edges.HasModelMsn);
                ctx.LinkKey(node, nameof(OperatorsIngest.Operator), r.OperatorCode, Edges.OperatedBy, Edges.Operates);
                MetaLinks.EngineTypes(ctx, node, new[] { r.EngineType });
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.AircraftStatus, r.Status);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}

/// <summary>`rules.csv` — the engineering rules surfaced in the pre-analysis Rules tab.</summary>
internal static class RulesIngest
{
    public const int Version = 2;

    private sealed class Row
    {
        [Name("rule_id")] public string RuleId { get; set; } = "";
        [Name("title")] public string Title { get; set; } = "";
        [Name("text")] public string Text { get; set; } = "";
        [Name("category")] public string Category { get; set; } = "";
        [Name("severity")] public string Severity { get; set; } = "";
        [Name("ata_2d")] public string Ata2d { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
        [Name("source")] public string Source { get; set; } = "";
    }

    [Node]
    internal sealed class Rule
    {
        [Key] public string RuleId { get; set; } = "";
        [Property] public string Title { get; set; } = "";
        [Property] public string Text { get; set; } = "";
        [Property] public string Category { get; set; } = "";
        [Property] public string Severity { get; set; } = "";
        [Property] public string Ata2d { get; set; } = "";
        [Property] public string Source { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var row in ctx.ReadCsv<Row>("rules.csv"))
        {
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                var atas = ConnectorContext.Pipe(r.Ata2d);
                var node = ctx.Graph.AddOrUpdate(new Rule
                {
                    RuleId = r.RuleId,
                    Title = r.Title,
                    Text = r.Text,
                    Category = r.Category,
                    Severity = r.Severity,
                    Ata2d = string.Join(", ", atas),
                    Source = r.Source,
                });
                ctx.CountNode(nameof(Rule));

                foreach (var ata in atas)
                    ctx.LinkKey(node, nameof(AtaChaptersIngest.AtaChapter), ata, Edges.RuleForAta, Edges.HasRule);

                MetaLinks.Classify(ctx, node, MetaLinks.Kind.RuleCategory, r.Category);
                MetaLinks.Classify(ctx, node, MetaLinks.Kind.RuleSeverity, r.Severity);

                foreach (var letter in ConnectorContext.Pipe(r.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter, Edges.InProgram, Edges.HasDocument);
            });
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }
}
