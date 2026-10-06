// Reference template: the shared metadata vocabularies. See curiosity-graph-modelling §3.
//
// Every dimension a user might filter across MORE THAN ONE node type, or a similarity signal
// might key on, gets a node and an edge — and keeps its property. The property is what renders
// in a details grid without a traversal and what the text index reads; the node is what makes
// the value traversable, faceted across types, and reachable by HasCommonNeighborsWith.

using Curiosity.Library;

namespace App.Connector.Ingest;

internal static class MetaLinks
{
    // ---------------------------------------------------------------------------------------
    // node types
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A state something is in and can move out of. Keyed by the VALUE alone, so one node
    /// gathers everything in that state across every record type; which FIELD the link came
    /// from is carried by the edge, so "this record's own status" is still one traversal with
    /// one answer.
    /// </summary>
    [Node]
    internal sealed class Status
    {
        [Key]      public string Value   { get; set; } = "";
        /// <summary>What the UI shows. See the note on canonical keys below.</summary>
        [Property] public string Display { get; set; } = "";
    }

    /// <summary>
    /// The rest of the controlled-vocabulary long tail — priority, risk level, severity,
    /// compliance, role. One node TYPE keyed "{Kind}:{Value}", but one edge PAIR per
    /// vocabulary: a single shared edge would make every vocabulary one undifferentiated facet
    /// and leave a renderer no way to ask for this record's risk level alone.
    /// </summary>
    [Node]
    internal sealed class Classification
    {
        [Key]      public string ClassificationKey { get; set; } = "";   // "{Kind}:{Value}"
        [Property] public string Kind              { get; set; } = "";
        [Property] public string Value             { get; set; } = "";
    }

    [Node]
    internal sealed class Keyword
    {
        [Key] public string Text { get; set; } = "";
    }

    // ---------------------------------------------------------------------------------------
    // the vocabularies
    // ---------------------------------------------------------------------------------------

    internal static class StatusField
    {
        public const string DocumentStatus = nameof(DocumentStatus);
        public const string WorkflowStatus = nameof(WorkflowStatus);
        public const string Lifecycle      = nameof(Lifecycle);

        public static readonly string[] All = [DocumentStatus, WorkflowStatus, Lifecycle];
    }

    internal static class Kind
    {
        public const string Priority  = nameof(Priority);
        public const string RiskLevel = nameof(RiskLevel);
        public const string UserRole  = nameof(UserRole);

        public static readonly string[] All = [Priority, RiskLevel, UserRole];
    }

    /// <summary>
    /// A name in both lists would put two node types on one edge, so traversing it would return
    /// a Status sometimes and a Classification other times. Fail the run at startup instead.
    /// </summary>
    public static void VerifyDisjoint()
    {
        var overlap = StatusField.All.Intersect(Kind.All, StringComparer.Ordinal).ToArray();
        if (overlap.Length > 0)
            throw new InvalidOperationException($"Vocabulary name(s) in both lists: {string.Join(", ", overlap)}");
    }

    // ---------------------------------------------------------------------------------------
    // writing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The canonical form of a state.
    ///
    /// A shared node is only shared if every producer spells the state the same way, so every
    /// value goes through here. PREFER A USER-FRIENDLY CANONICAL FORM: this key is the facet
    /// label, the chip on a card and the title of the node's own page. Where the source
    /// vocabulary must be preserved machine-shaped, keep the machine form as the key and put
    /// the friendly one in Display — then point the renderer's LabelField at Display.
    /// Decide which of the two BEFORE ingesting: changing the key later re-mints every node
    /// and orphans every edge.
    /// </summary>
    private static string Canonical(string value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : char.ToUpperInvariant(value.Trim()[0]) + value.Trim()[1..].Replace('_', ' ').ToLowerInvariant();

    public static void SetStatus(ConnectorContext ctx, Node from, string field, string? value)
    {
        var canonical = Canonical(value ?? "");
        if (canonical.Length == 0) return;

        // Mint once per run, link by key every time — see ConnectorContext.Mint.
        if (ctx.Mint(nameof(Status), canonical))
        {
            ctx.Graph.TryAdd(new Status { Value = canonical, Display = canonical });
            ctx.CountNode(nameof(Status));
        }

        var (forward, reverse) = EdgePair("Has" + field, field + "Of");
        ctx.LinkKey(from, nameof(Status), canonical, forward, reverse);
    }

    public static void Classify(ConnectorContext ctx, Node from, string kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var key = kind + ":" + value.Trim();

        if (ctx.Mint(nameof(Classification), key))
        {
            ctx.Graph.TryAdd(new Classification { ClassificationKey = key, Kind = kind, Value = value.Trim() });
            ctx.CountNode(nameof(Classification));
        }

        var (forward, reverse) = EdgePair("Has" + kind, kind + "Of");
        ctx.LinkKey(from, nameof(Classification), key, forward, reverse);
    }

    public static void Keywords(ConnectorContext ctx, Node from, IEnumerable<string> keywords)
    {
        foreach (var raw in keywords)
        {
            var text = raw.Trim().ToLowerInvariant();
            if (text.Length == 0) continue;

            if (ctx.Mint(nameof(Keyword), text))
            {
                ctx.Graph.TryAdd(new Keyword { Text = text });
                ctx.CountNode(nameof(Keyword));
            }

            ctx.LinkKey(from, nameof(Keyword), text, Edges.HasKeyword, Edges.KeywordOf);
        }
    }

    /// <summary>
    /// Derives the edge pair from the vocabulary name and checks both constants exist.
    ///
    /// Forgetting to declare them would otherwise write thousands of links over an edge type
    /// the schema never registered — silently, and only discovered when a traversal returns
    /// nothing. Adding a vocabulary is therefore: a constant here, two constants in Edges, and
    /// a call. Miss either and the run fails at startup.
    /// </summary>
    private static (string Forward, string Reverse) EdgePair(string forward, string reverse)
    {
        var declared = typeof(Edges)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        if (!declared.Contains(forward) || !declared.Contains(reverse))
            throw new InvalidOperationException($"Edges is missing '{forward}' and/or '{reverse}'.");

        return (forward, reverse);
    }
}
