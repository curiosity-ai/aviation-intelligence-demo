using System.Globalization;
using Curiosity.Library;
using CsvHelper;
using CsvHelper.Configuration;

namespace AviationIntel.Connector;

/// <summary>
/// Everything a handler needs: the graph, the hash cache, the run options and the counters the
/// summary is printed from. One instance per run.
/// </summary>
public sealed class ConnectorContext : IDisposable
{
    private readonly Dictionary<string, int> _nodes   = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _edges   = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _outcome = new(StringComparer.Ordinal);
    private readonly HashSet<string>         _minted  = new(StringComparer.Ordinal);
    private readonly object                  _lock    = new();

    public ConnectorContext(Graph graph, HashCache cache, ConnectorOptions options)
    {
        Graph   = graph;
        Cache   = cache;
        Options = options;
    }

    public Graph            Graph   { get; }
    public HashCache        Cache   { get; }
    public ConnectorOptions Options { get; }
    public bool             DryRun  => Options.DryRun;

    public IEnumerable<TRow> ReadCsv<TRow>(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Feed directory not found", path);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord   = true,
            MissingFieldFound = null,
            TrimOptions       = TrimOptions.Trim,
        };

        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        using var csv    = new CsvReader(reader, config);

        foreach (var row in csv.GetRecords<TRow>()) yield return row;
    }

    /// <summary>
    /// Persists the hashes for work that has just been committed. Never in a dry run: nothing
    /// reached the graph, and a recorded hash would make the next real run skip it forever.
    /// </summary>
    public async Task CommitCacheAsync()
    {
        if (DryRun) return;
        await Cache.CommitPendingHashesAsync();
    }

    /// <summary>Links to a node by key without fetching it; the edge activates once both ends exist.</summary>
    public void LinkKey(Node from, string toType, string? toKey, string forward, string reverse)
    {
        if (string.IsNullOrWhiteSpace(toKey)) return;
        Graph.Link(from, Node.FromKey(toType, toKey!), forward, reverse);
        CountEdge(forward);
    }

    /// <summary>True the first time a metadata key is seen this run - mint once, link always.</summary>
    public bool Mint(string nodeType, string key)
    {
        lock (_lock) return _minted.Add(nodeType + "\u0000" + key);
    }

    public void CountNode(string type)    { lock (_lock) Bump(_nodes, type); }
    public void CountEdge(string edge)    { lock (_lock) Bump(_edges, edge); }
    public void CountOutcome(string what) { lock (_lock) Bump(_outcome, what); }

    private static void Bump(Dictionary<string, int> counter, string key) => counter[key] = counter.GetValueOrDefault(key) + 1;

    public int TotalNodes { get { lock (_lock) return _nodes.Values.Sum(); } }
    public int TotalEdges { get { lock (_lock) return _edges.Values.Sum(); } }

    public void PrintSummary()
    {
        Print("NODES",    _nodes);
        Print("EDGES",    _edges);
        Print("OUTCOMES", _outcome);
    }

    private static void Print(string title, Dictionary<string, int> counter)
    {
        if (counter.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine(title);

        foreach (var (key, count) in counter.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  {key,-36} {count,8:N0}");
    }

    public void Dispose()
    {
    }
}

/// <summary>The command line, parsed once.</summary>
public sealed record ConnectorOptions
{
    public required string  Root               { get; init; }
    public required string  DirectoryCsv       { get; init; }
    public bool             DryRun             { get; init; }
    public bool             NoBrowser          { get; init; }
    public string?          ChromiumPath       { get; init; }
    public int              BrowserConcurrency { get; init; } = 4;
    public int              MaxItemsPerFeed    { get; init; } = 50;
    public int              SinceDays          { get; init; } = 14;
    public string?          FeedFilter         { get; init; }
    public string?          PriorityFilter     { get; init; }
    public int              FeedLimit          { get; init; }
    public TimeSpan         PageTimeout        { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan         PerHostDelay       { get; init; } = TimeSpan.FromSeconds(1);
    public string           UserAgent          { get; init; } = DefaultUserAgent;

    /// <summary>Where the reference vocabularies' last-written terms are kept; beside the hash cache, per endpoint.</summary>
    public string?          TermsLedgerPath    { get; init; }

    //A current desktop Chrome: several newsrooms answer a library user agent with 403.
    public const string DefaultUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
}
