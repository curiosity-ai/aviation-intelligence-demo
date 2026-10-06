using Curiosity.Library;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// The metadata dimensions every document is classified by, as first-class nodes.
///
/// These were all bare string properties. A string can be searched but it cannot be
/// traversed, and two things follow from that:
///
/// <list type="bullet">
/// <item>the search UI can only offer a **related facet** on something that is a node
/// on the other end of an edge;</item>
/// <item><c>IQuery.HasCommonNeighborsWith</c> — the "shares a neighbour with the seed"
/// filter the similarity signals are built on — only sees neighbours, so a document
/// whose ATA 2D lives in a property is invisible to a same-chapter signal.</item>
/// </list>
///
/// So anything a user might filter by, or a similarity signal might key on, gets a node
/// and an edge in addition to the property. The property stays: it is what renders in a
/// details grid without a traversal.
/// </summary>
internal static class MetadataSchema
{
    /// <summary>An engine variant. Applicability often turns on this alone.</summary>
    [Node]
    internal sealed class EngineType
    {
        [Key] public string Name { get; set; } = "";
    }

    /// <summary>The authoring department — dropped entirely before this.</summary>
    [Node]
    internal sealed class Department
    {
        [Key] public string Code { get; set; } = "";
    }

    /// <summary>The authoring site.</summary>
    [Node]
    internal sealed class Site
    {
        [Key] public string Name { get; set; } = "";
    }

    /// <summary>STRUCTURE / SYSTEMS / AVIONICS / … — the ATA chapter's family.</summary>
    [Node]
    internal sealed class AtaDomain
    {
        [Key] public string Name { get; set; } = "";
    }

    /// <summary>100 Lower Fuselage, 800 Doors, … so zones roll up to something filterable.</summary>
    [Node]
    internal sealed class MajorZone
    {
        [Key] public string Code { get; set; } = "";
        [Property] public string Name { get; set; } = "";
    }

    /// <summary>An authored keyword. Cheap to link and it makes the keyword baseline visible.</summary>
    [Node]
    internal sealed class Keyword
    {
        [Key] public string Text { get; set; } = "";
    }

    /// <summary>
    /// An aircraft system by name — "Bleed Air Supply", "Autothrust", "Fuel".
    ///
    /// Three unrelated places named a system as a bare string: a FIN's <c>system</c>, a
    /// software load's <c>system</c>, and the list of systems an FCOM entry covers. They
    /// share a vocabulary, so as one node type they join a crew-facing entry to the wiring
    /// and the loadable software of the same system — a link nothing else in the data makes.
    /// </summary>
    [Node]
    internal sealed class AircraftSystem
    {
        [Key] public string Name { get; set; } = "";
    }

    /// <summary>
    /// A maintenance fault code. Multi-valued on a troubleshooting procedure and recurring
    /// across them, so it is the natural join between two procedures that describe the same
    /// symptom in different words.
    /// </summary>
    [Node]
    internal sealed class FaultCode
    {
        [Key] public string Code { get; set; } = "";
    }

    /// <summary>
    /// A Minimum Equipment List item. It is what connects a troubleshooting procedure's
    /// dispatch note to the flight-crew entry that carries the same relief.
    /// </summary>
    [Node]
    internal sealed class MelItem
    {
        [Key] public string ItemName { get; set; } = "";
    }

    /// <summary>
    /// A wiring harness. Multi-valued on a wiring sheet and shared between sheets, which is
    /// exactly the "the same bundle is drawn on both of these" relationship a string cannot
    /// express.
    /// </summary>
    [Node]
    internal sealed class Harness
    {
        [Key] public string Reference { get; set; } = "";
    }

    /// <summary>
    /// An airworthiness directive. The regulator's instrument, referenced by the service
    /// bulletins that answer it — and more than one bulletin answers some of them, which is
    /// the question "what are we doing about this AD?" that a string on each cannot answer.
    /// </summary>
    [Node]
    internal sealed class AirworthinessDirective
    {
        [Key] public string Reference { get; set; } = "";
    }

    /// <summary>
    /// A service kit. It is ordered, stocked and shipped as one thing, and several bulletins
    /// can call for the same one.
    /// </summary>
    [Node]
    internal sealed class ServiceKit
    {
        [Key] public string KitNumber { get; set; } = "";
    }

    /// <summary>
    /// A modification number — the change itself, as opposed to the dossier describing it.
    ///
    /// <c>MOD-166204</c> and every <c>PAWO-166204-nn</c> analysing it share the number, so
    /// the node is where the whole change gathers: open it and the dossier, its
    /// pre-analyses and anything else carrying the number are all one hop away. As an
    /// <c>int</c> property on each document it was a fact you could read and not follow.
    /// </summary>
    [Node]
    internal sealed class ModNumber
    {
        [Key] public string Number { get; set; } = "";
    }

    /// <summary>
    /// Where something stands right now — one node per status <em>value</em>, shared by
    /// everything that stands there.
    ///
    /// Nine fields across the dataset are the same idea under different names: a document's
    /// <c>status</c>, a modification's embodiment status, an internal order's
    /// <c>io_status</c>, a work order's <c>pawo_status</c> and the workflow status the
    /// assignment pass derives, how a closed pre-analysis came out, a part's
    /// <c>lifecycle</c>, a software load's <c>certification_status</c> and an aircraft's
    /// <c>status</c>. They are all "what state is this in", and they draw on one overlapping
    /// vocabulary — <c>CANCELLED</c>, <c>ON_HOLD</c>, <c>IN_PROGRESS</c>, <c>SUPERSEDED</c>,
    /// <c>WITHDRAWN</c>, <c>REJECTED</c> each occur in two or three of them.
    ///
    /// So the key is the <strong>value alone</strong>, not <c>{Kind}:{Value}</c> the way
    /// <see cref="Classification"/> is keyed. That is the whole point of the type: one
    /// <c>ON_HOLD</c> node that an internal order and a pre-analysis both hang off, so
    /// "everything on hold" is one node and not a query per document type. Which field a
    /// link came from is carried by the <em>edge</em> — <c>HasIoStatus</c>,
    /// <c>HasPawoStatus</c>, … — so a renderer can still ask for this document's own status
    /// and get one answer.
    ///
    /// The one place the shared key conflates rather than joins is <c>IN_SERVICE</c> and
    /// <c>IN_PRODUCTION</c>: a modification is embodied in service, an aircraft is flying.
    /// Same word, different subject. They stay one node deliberately — in this domain a
    /// change in service and the fleet it is in service on are the same fact seen from two
    /// ends — and the edge tells them apart when it matters.
    /// </summary>
    [Node]
    internal sealed class Status
    {
        [Key] public string Value { get; set; } = "";

        /// <summary>The value as prose — <c>IN_PROGRESS</c> reads "In progress" in a facet.</summary>
        [Property] public string Label { get; set; } = "";
    }

    /// <summary>
    /// The controlled-vocabulary long tail — compliance, risk level, damage type and two
    /// dozen others. Status vocabularies are <em>not</em> here; they are
    /// <see cref="Status"/>, keyed by value so they can be shared.
    ///
    /// One node type keyed <c>{Kind}:{Value}</c> rather than twenty-odd near-empty types:
    /// each is a single enum with a handful of values, they are all "how this document is
    /// classified", and the <c>Kind</c> property keeps them apart in a facet. Adding a new
    /// vocabulary then costs a call, not a schema change.
    ///
    /// The key carries the kind because these vocabularies do <em>not</em> share a value
    /// space and would be wrong to join: a <c>MAJOR</c> modification class and a
    /// <c>MAJOR</c> finding severity have nothing to do with one another.
    /// </summary>
    [Node]
    internal sealed class Classification
    {
        [Key] public string Id { get; set; } = "";
        [Property] public string Kind { get; set; } = "";
        [Property] public string Value { get; set; } = "";
    }
}

/// <summary>Mints and links the metadata nodes. Called from the document and reference handlers.</summary>
internal static class MetaLinks
{
    /// <summary>
    /// The fields that carry a <see cref="MetadataSchema.Status"/> — every "what state is
    /// this in" field in the dataset, under whatever name its own type gives it.
    ///
    /// A field belongs here when its value is a <em>state the thing is currently in and can
    /// move out of</em>. That is why <c>Recommendation</c>, <c>Priority</c>,
    /// <c>Severity</c>, <c>Compliance</c> and <c>RiskLevel</c> are not here and stay
    /// <see cref="Kind">classifications</see>: they are judgements about the thing, not
    /// where it stands. <c>Outcome</c> is here because it is the state a completed
    /// pre-analysis ended in, and it shares <c>REJECTED</c> with <c>IoStatus</c>.
    ///
    /// The name doubles as the edge pair, exactly as <see cref="Kind"/> does:
    /// <c>Has{Field}</c> / <c>{Field}Of</c>.
    /// </summary>
    internal static class StatusField
    {
        /// <summary>The envelope <c>status</c> every document and every issue of one carries.</summary>
        public const string DocumentStatus = "DocumentStatus";

        /// <summary>Where a modification stands in its embodiment lifecycle.</summary>
        public const string EmbodimentStatus = "EmbodimentStatus";

        public const string IoStatus = "IoStatus";
        public const string PawoStatus = "PawoStatus";

        /// <summary>Derived by the assignment pass rather than authored — a state all the same.</summary>
        public const string WorkflowStatus = "WorkflowStatus";

        /// <summary>How a closed pre-analysis ended: validated, rejected, delegated.</summary>
        public const string Outcome = "Outcome";

        /// <summary>A part number's <c>lifecycle</c>: active, new, superseded, obsolete.</summary>
        public const string PartLifecycle = "PartLifecycle";

        /// <summary>A software load's certification state.</summary>
        public const string CertificationStatus = "CertificationStatus";

        /// <summary>An aircraft's <c>status</c>: in service, stored, withdrawn, in production.</summary>
        public const string AircraftStatus = "AircraftStatus";
    }

    /// <summary>Kinds used for <see cref="MetadataSchema.Classification"/>, so the strings live in one place.</summary>
    internal static class Kind
    {
        public const string ChangeCategory = "ChangeCategory";
        public const string ModTrigger = "ModTrigger";
        public const string ModClass = "ModClass";
        public const string RiskLevel = "RiskLevel";
        public const string CertificationImpact = "CertificationImpact";
        public const string Interchangeability = "Interchangeability";
        public const string SbType = "SbType";
        public const string Compliance = "Compliance";
        public const string SbReasonCode = "SbReasonCode";
        public const string DowntimeCategory = "DowntimeCategory";
        public const string RequestCategory = "RequestCategory";
        public const string Priority = "Priority";
        public const string Funding = "Funding";
        public const string RequesterType = "RequesterType";
        public const string Recommendation = "Recommendation";
        public const string AmmTaskType = "AmmTaskType";
        public const string RepairCategory = "RepairCategory";
        public const string DamageType = "DamageType";
        public const string NdtMethod = "NdtMethod";
        public const string DiagramType = "DiagramType";
        public const string FcomSection = "FcomSection";
        public const string OperationalImpact = "OperationalImpact";
        public const string FlightPhase = "FlightPhase";
        public const string PartType = "PartType";
        public const string DrawingType = "DrawingType";
        public const string SoftwareLoadType = "SoftwareLoadType";
        public const string RuleCategory = "RuleCategory";
        public const string RuleSeverity = "RuleSeverity";
        public const string UserRole = "UserRole";
    }

    /// <summary>
    /// The edge pair each vocabulary is linked over, derived from its kind as
    /// <c>Has{Kind}</c> / <c>{Kind}Of</c> and checked against <see cref="Edges"/> once.
    ///
    /// Deriving rather than listing keeps the two in step; validating turns a vocabulary
    /// added without its edges into a startup failure instead of thousands of links over an
    /// edge type the schema never registered.
    /// </summary>
    private static readonly Dictionary<string, (string Forward, string Reverse)> ClassificationEdges =
        EdgePairsFor(typeof(Kind), "Classification kinds");

    /// <summary>
    /// The edge pair each status field is linked over, derived and validated the same way.
    ///
    /// A field appearing in both <see cref="Kind"/> and <see cref="StatusField"/> would send
    /// two node types down one edge — traversing it would return a <c>Status</c> sometimes
    /// and a <c>Classification</c> other times — so the two sets are checked disjoint at
    /// startup rather than discovered in a facet.
    /// </summary>
    private static readonly Dictionary<string, (string Forward, string Reverse)> StatusEdges =
        BuildStatusEdges();

    private static Dictionary<string, (string Forward, string Reverse)> BuildStatusEdges()
    {
        var map = EdgePairsFor(typeof(StatusField), "Status fields");

        var overlap = map.Keys.Where(ClassificationEdges.ContainsKey).ToList();

        if (overlap.Count > 0)
        {
            throw new InvalidOperationException(
                "Names declared as both a Kind and a StatusField, which would put two node types "
                + "on one edge: " + string.Join(", ", overlap));
        }

        return map;
    }

    /// <summary>
    /// Reads the constants off a vocabulary class and pairs each with its
    /// <c>Has{Name}</c> / <c>{Name}Of</c> edges.
    ///
    /// Deriving rather than listing keeps the two in step; validating turns a vocabulary
    /// added without its edges into a startup failure instead of thousands of links over an
    /// edge type the schema never registered.
    /// </summary>
    private static Dictionary<string, (string Forward, string Reverse)> EdgePairsFor(Type vocabulary, string what)
    {
        var declared = typeof(Edges)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var names = vocabulary
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var name in names)
        {
            var forward = "Has" + name;
            var reverse = name + "Of";

            if (declared.Contains(forward) && declared.Contains(reverse)) map[name] = (forward, reverse);
            else missing.Add($"{name} (expects Edges.{forward} and Edges.{reverse})");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what} with no edge pair declared in Edges.cs: " + string.Join(", ", missing));
        }

        return map;
    }

    /// <summary>
    /// Links a document to one classification value. No-ops on blank or <c>Unknown</c>,
    /// so an absent enum does not mint a node nobody wants to filter by.
    ///
    /// Each vocabulary travels on its own edge pair, so "this document's risk level" is a
    /// traversal that returns the risk level — not every classification it carries.
    /// </summary>
    public static void Classify(ConnectorContext ctx, Node from, string kind, string? value)
    {
        if (from is null || string.IsNullOrWhiteSpace(value) || value == "Unknown") return;

        var id = kind + ":" + value;

        if (ctx.Mint(nameof(MetadataSchema.Classification), id))
        {
            ctx.Graph.TryAdd(new MetadataSchema.Classification { Id = id, Kind = kind, Value = value! });
            ctx.CountNode(nameof(MetadataSchema.Classification));
        }

        var (forward, reverse) = ClassificationEdges[kind];
        ctx.LinkKey(from, nameof(MetadataSchema.Classification), id, forward, reverse);
    }

    /// <summary>
    /// Links anything that has a state to the <see cref="MetadataSchema.Status"/> node for
    /// that value. No-ops on blank or <c>Unknown</c>, so an absent enum — a pre-analysis
    /// that is still open and therefore has no outcome — does not mint a node.
    ///
    /// The node is keyed by the value alone and so is shared across every field and every
    /// type that reaches the same state; <paramref name="field"/> only decides which edge
    /// the link travels on, which is what keeps "this document's own status" answerable.
    /// </summary>
    public static void SetStatus(ConnectorContext ctx, Node from, string field, string? value)
    {
        if (from is null || string.IsNullOrWhiteSpace(value) || value == "Unknown") return;

        var status = CanonicalStatus(value!);

        if (ctx.Mint(nameof(MetadataSchema.Status), status))
        {
            ctx.Graph.TryAdd(new MetadataSchema.Status { Value = status, Label = StatusLabel(status) });
            ctx.CountNode(nameof(MetadataSchema.Status));
        }

        var (forward, reverse) = StatusEdges[field];
        ctx.LinkKey(from, nameof(MetadataSchema.Status), status, forward, reverse);
    }

    /// <summary>
    /// The one spelling a status value is keyed by: <c>SCREAMING_SNAKE</c>, which is what
    /// every authored vocabulary in the dataset already uses.
    ///
    /// A shared node is only shared if everyone spells the state the same way, and the whole
    /// point of <see cref="MetadataSchema.Status"/> is that they do. A caller handing in
    /// <c>OnHold</c> where the documents say <c>ON_HOLD</c> would otherwise split "on hold"
    /// across two nodes and two facet entries — which is exactly what the derived workflow
    /// vocabulary used to do before it was spelled like the rest.
    /// </summary>
    private static string CanonicalStatus(string value)
    {
        var text = value.Trim();
        var sb = new System.Text.StringBuilder(text.Length + 4);

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == ' ' || c == '-') { sb.Append('_'); continue; }

            // A lower-to-upper transition is a word break in PascalCase. SCREAMING_SNAKE has
            // none, so a value already in canonical form passes through untouched.
            if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1])) sb.Append('_');

            sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>CONVERTED_TO_MOD</c> → "Converted to mod". The values are screaming-snake enum
    /// members and read badly in a facet list; the node keeps the canonical value as its key
    /// so nothing downstream has to un-prettify it.
    /// </summary>
    private static string StatusLabel(string value)
    {
        var words = value.Replace('_', ' ').ToLowerInvariant();

        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    public static void EngineTypes(ConnectorContext ctx, Node from, IEnumerable<string>? engines)
    {
        if (engines is null) return;
        foreach (var engine in engines)
        {
            if (string.IsNullOrWhiteSpace(engine)) continue;
            if (ctx.Mint(nameof(MetadataSchema.EngineType), engine))
            {
                ctx.Graph.TryAdd(new MetadataSchema.EngineType { Name = engine });
                ctx.CountNode(nameof(MetadataSchema.EngineType));
            }
            ctx.LinkKey(from, nameof(MetadataSchema.EngineType), engine,
                        Edges.AppliesToEngine, Edges.HasEngineDocument);
        }
    }

    public static void Originator(ConnectorContext ctx, Node from, string? department, string? site)
    {
        if (!string.IsNullOrWhiteSpace(department))
        {
            if (ctx.Mint(nameof(MetadataSchema.Department), department!))
            {
                ctx.Graph.TryAdd(new MetadataSchema.Department { Code = department! });
                ctx.CountNode(nameof(MetadataSchema.Department));
            }
            ctx.LinkKey(from, nameof(MetadataSchema.Department), department,
                        Edges.FromDepartment, Edges.OriginatedDocument);
        }

        if (!string.IsNullOrWhiteSpace(site))
        {
            if (ctx.Mint(nameof(MetadataSchema.Site), site!))
            {
                ctx.Graph.TryAdd(new MetadataSchema.Site { Name = site! });
                ctx.CountNode(nameof(MetadataSchema.Site));
            }
            ctx.LinkKey(from, nameof(MetadataSchema.Site), site, Edges.AtSite, Edges.SiteOfDocument);
        }
    }

    /// <summary>
    /// Mints a keyed metadata node once per run and links to it.
    ///
    /// Every dimension below is a small vocabulary shared by thousands of rows, so the node
    /// is created on first sight and everything after that is a link by key. <c>Mint</c> is
    /// what keeps the pending buffer proportional to distinct values rather than to rows.
    /// </summary>
    private static void LinkDimension<TNode>(ConnectorContext ctx, Node from, string? value,
                                             Func<string, TNode> create,
                                             string forward, string reverse)
        where TNode : class
    {
        if (from is null || string.IsNullOrWhiteSpace(value)) return;

        var key = value!.Trim();

        if (ctx.Mint(typeof(TNode).Name, key))
        {
            ctx.Graph.TryAdd(create(key));
            ctx.CountNode(typeof(TNode).Name);
        }

        ctx.LinkKey(from, typeof(TNode).Name, key, forward, reverse);
    }

    /// <summary>The system(s) a document, FIN or software load belongs to.</summary>
    public static void Systems(ConnectorContext ctx, Node from, IEnumerable<string?>? systems)
    {
        if (systems is null) return;
        foreach (var system in systems)
            LinkDimension(ctx, from, system, name => new MetadataSchema.AircraftSystem { Name = name },
                          Edges.InSystem, Edges.HasSystemDocument);
    }

    /// <summary>The fault codes a troubleshooting procedure isolates.</summary>
    public static void FaultCodes(ConnectorContext ctx, Node from, IEnumerable<string?>? codes)
    {
        if (codes is null) return;
        foreach (var code in codes)
            LinkDimension(ctx, from, code, value => new MetadataSchema.FaultCode { Code = value },
                          Edges.ReportsFaultCode, Edges.DiagnosedBy);
    }

    /// <summary>The MEL item a procedure or a crew entry carries relief under.</summary>
    public static void Mel(ConnectorContext ctx, Node from, string? item)
        => LinkDimension(ctx, from, item, value => new MetadataSchema.MelItem { ItemName = value },
                         Edges.HasMelItem, Edges.MelItemOf);

    /// <summary>The harnesses a wiring sheet routes.</summary>
    public static void Harnesses(ConnectorContext ctx, Node from, IEnumerable<string?>? harnesses)
    {
        if (harnesses is null) return;
        foreach (var harness in harnesses)
            LinkDimension(ctx, from, harness, value => new MetadataSchema.Harness { Reference = value },
                          Edges.RoutesHarness, Edges.RoutedOnSheet);
    }

    /// <summary>Where an engineer sits and what they are. Same node types the documents use.</summary>
    public static void UserOrganisation(ConnectorContext ctx, Node user, string? site,
                                        string? department, string? role)
    {
        LinkDimension(ctx, user, site, name => new MetadataSchema.Site { Name = name },
                      Edges.WorksAtSite, Edges.SiteOfUser);
        LinkDimension(ctx, user, department, code => new MetadataSchema.Department { Code = code },
                      Edges.InDepartment, Edges.DepartmentOfUser);

        Classify(ctx, user, Kind.UserRole, role);
    }

    /// <summary>The airworthiness directive a bulletin answers.</summary>
    public static void Directive(ConnectorContext ctx, Node from, string? reference)
        => LinkDimension(ctx, from, reference,
                         value => new MetadataSchema.AirworthinessDirective { Reference = value },
                         Edges.UnderDirective, Edges.MandatedBulletin);

    /// <summary>The kit a bulletin's material is ordered as.</summary>
    public static void Kit(ConnectorContext ctx, Node from, string? kitNumber)
        => LinkDimension(ctx, from, kitNumber,
                         value => new MetadataSchema.ServiceKit { KitNumber = value },
                         Edges.UsesKit, Edges.KitFor);

    /// <summary>
    /// The modification number a document belongs to. Passed as the number rather than
    /// parsed out of the identifier, so the caller decides what counts as belonging.
    /// </summary>
    public static void ModNumber(ConnectorContext ctx, Node from, int number)
    {
        if (number <= 0) return;
        LinkDimension(ctx, from, number.ToString(),
                      value => new MetadataSchema.ModNumber { Number = value },
                      Edges.HasModNumber, Edges.ModNumberOf);
    }

    public static void Keywords(ConnectorContext ctx, Node from, IEnumerable<string>? keywords)
    {
        if (keywords is null) return;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            var text = keyword.Trim().ToLowerInvariant();
            if (ctx.Mint(nameof(MetadataSchema.Keyword), text))
            {
                ctx.Graph.TryAdd(new MetadataSchema.Keyword { Text = text });
                ctx.CountNode(nameof(MetadataSchema.Keyword));
            }
            ctx.LinkKey(from, nameof(MetadataSchema.Keyword), text, Edges.HasKeyword, Edges.KeywordOf);
        }
    }

    /// <summary>
    /// Links all three ATA levels, not just the 6D leaf. The 4D and 2D chapters already
    /// exist as nodes from the catalogue, so this is two extra edges that turn "same
    /// chapter" into a one-hop common neighbour instead of a three-hop walk.
    /// </summary>
    public static void Ata(ConnectorContext ctx, Node from, string? ata6d, string? ata4d, string? ata2d)
    {
        ctx.LinkKey(from, nameof(AtaChaptersIngest.AtaChapter), ata6d,
                    Edges.InAtaChapter, Edges.HasAtaDocument);
        ctx.LinkKey(from, nameof(AtaChaptersIngest.AtaChapter), ata4d,
                    Edges.InAta4d, Edges.HasAta4dDocument);
        ctx.LinkKey(from, nameof(AtaChaptersIngest.AtaChapter), ata2d,
                    Edges.InAta2d, Edges.HasAta2dDocument);
    }

    /// <summary>Derives 4D and 2D from a 6D code when the source only carries the leaf.</summary>
    public static void AtaFrom6d(ConnectorContext ctx, Node from, string? ata6d)
    {
        if (string.IsNullOrWhiteSpace(ata6d)) return;
        var parts = ata6d!.Split('-');
        var ata4d = parts.Length >= 2 ? parts[0] + "-" + parts[1] : null;
        var ata2d = parts.Length >= 1 ? parts[0] : null;
        Ata(ctx, from, ata6d, ata4d, ata2d);
    }
}
