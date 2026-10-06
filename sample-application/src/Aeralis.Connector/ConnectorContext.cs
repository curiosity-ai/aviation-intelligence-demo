using System.Globalization;
using Curiosity.Library;
using CsvHelper;
using CsvHelper.Configuration;

namespace Aeralis.Connector;

/// <summary>
/// Everything a handler needs: the graph, the hash cache, where the dataset lives, and
/// the counters the run summary is printed from. One instance per run.
/// </summary>
public sealed class ConnectorContext : IDisposable
{
    private readonly Dictionary<string, int> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Hits, int Misses)> _cacheStats = new(StringComparer.Ordinal);

    /// <summary>Metadata node keys already minted this run — see <see cref="Mint"/>.</summary>
    private readonly HashSet<string> _minted = new(StringComparer.Ordinal);

    public ConnectorContext(Graph graph, HashCache cache, string datasetRoot, bool dryRun)
    {
        Graph = graph;
        Cache = cache;
        DatasetRoot = datasetRoot;
        DryRun = dryRun;
    }

    public Graph Graph { get; }
    public HashCache Cache { get; }
    public string DatasetRoot { get; }
    public bool DryRun { get; }

    public string ReferenceDir => Path.Combine(DatasetRoot, "data", "reference");
    public string DocumentsDir => Path.Combine(DatasetRoot, "data", "documents");

    // -------------------------------------------------------------------------
    // reading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Streams a reference CSV into <typeparamref name="TRow"/>. Invariant culture is
    /// deliberate: the dataset writes ISO dates and '.' decimals regardless of host locale.
    /// </summary>
    public IEnumerable<TRow> ReadCsv<TRow>(string fileName)
    {
        var path = Path.Combine(ReferenceDir, fileName);
        if (!File.Exists(path)) yield break;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);
        foreach (var row in csv.GetRecords<TRow>()) yield return row;
    }

    /// <summary>
    /// Persists the hashes for work that has just been committed to the graph.
    ///
    /// In a dry run nothing reached the graph, so recording hashes would make the next
    /// real run skip every record it never actually ingested. The cache is therefore left
    /// untouched and its pending hashes are discarded when it is disposed.
    /// </summary>
    public async Task CommitCacheAsync()
    {
        if (DryRun) return;
        await Cache.CommitPendingHashesAsync();
    }

    /// <summary>Splits a pipe-separated reference cell, tolerating nulls and stray spaces.</summary>
    public static string[] Pipe(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // -------------------------------------------------------------------------
    // graph helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Links to a node by key without fetching it. The edge activates once both ends
    /// exist, which is what lets a handler link forward to a type ingested later.
    /// </summary>
    public void LinkKey(Node from, string toType, string? toKey, string forward, string reverse)
    {
        if (from is null || string.IsNullOrWhiteSpace(toKey)) return;
        Graph.Link(from, Node.FromKey(toType, toKey!), forward, reverse);
        CountEdge(forward);
    }

    public void LinkKeys(Node from, string toType, IEnumerable<string>? toKeys, string forward, string reverse)
    {
        if (toKeys is null) return;
        foreach (var key in toKeys) LinkKey(from, toType, key, forward, reverse);
    }

    // -------------------------------------------------------------------------
    // counters
    // -------------------------------------------------------------------------

    /// <summary>
    /// True the first time a given node key is seen this run.
    ///
    /// The metadata dimensions are a handful of distinct values shared by tens of
    /// thousands of rows — "Compliance:RECOMMENDED" is the same node for every one of a
    /// hundred bulletins. Calling <c>TryAdd</c> per row makes the pending buffer do work
    /// proportional to rows × values; minting once and linking by key afterwards makes it
    /// proportional to values. Links still happen every time, which is the point.
    /// </summary>
    public bool Mint(string nodeType, string key) => _minted.Add(nodeType + "\u0000" + key);

    public void CountNode(string type) => Bump(_nodes, type);

    public void CountEdge(string edge) => Bump(_edges, edge);

    public void CountCache(string handler, bool hit)
    {
        var current = _cacheStats.GetValueOrDefault(handler);
        _cacheStats[handler] = hit ? (current.Hits + 1, current.Misses) : (current.Hits, current.Misses + 1);
    }

    private static void Bump(Dictionary<string, int> counter, string key)
        => counter[key] = counter.GetValueOrDefault(key) + 1;

    public int TotalNodes => _nodes.Values.Sum();
    public int TotalEdges => _edges.Values.Sum();

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("NODES");
        foreach (var (type, count) in _nodes.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {type,-28} {count,8:N0}");
        Console.WriteLine($"  {"total",-28} {TotalNodes,8:N0}");

        Console.WriteLine();
        Console.WriteLine("EDGES");
        foreach (var (edge, count) in _edges.OrderByDescending(kv => kv.Value).Take(30))
            Console.WriteLine($"  {edge,-28} {count,8:N0}");
        if (_edges.Count > 30) Console.WriteLine($"  … {_edges.Count - 30} more edge types");
        Console.WriteLine($"  {"total",-28} {TotalEdges,8:N0}");

        if (_cacheStats.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("HASHCACHE");
            foreach (var (handler, (hits, misses)) in _cacheStats.OrderBy(kv => kv.Key))
                Console.WriteLine($"  {handler,-28} {hits,7:N0} hits  {misses,7:N0} misses");
        }
    }

    /// <summary>
    /// Nothing to release. The graph and the hash cache are owned by <c>Program</c>, which
    /// holds them in their own <c>using</c> declarations — disposing them here as well
    /// tears each of them down twice, and the second teardown of the graph blocks.
    /// </summary>
    public void Dispose()
    {
    }
}
