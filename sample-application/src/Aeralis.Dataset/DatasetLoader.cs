using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Aeralis.Dataset.Documents;
using Aeralis.Dataset.Reference;

namespace Aeralis.Dataset;

/// <summary>Per-file accounting so the checker can report counts and bytes read.</summary>
public sealed record FileStat(string Name, int Rows, long Bytes);

public sealed class ReferenceData
{
    public List<AtaChapter> AtaChapters { get; init; } = [];
    public List<PartNumberRow> PartNumbers { get; init; } = [];
    public List<FinRow> Fins { get; init; } = [];
    public List<MsnRow> Msns { get; init; } = [];
    public List<DrawingRow> Drawings { get; init; } = [];
    public List<SoftwareRow> Software { get; init; } = [];
    public List<ZoneRow> Zones { get; init; } = [];
    public List<OperatorRow> Operators { get; init; } = [];
    public List<SupplierRow> Suppliers { get; init; } = [];
    public List<FileStat> Files { get; init; } = [];
    public List<string> Problems { get; init; } = [];

    public long Bytes => Files.Sum(f => f.Bytes);
    public int Rows => Files.Sum(f => f.Rows);
}

/// <summary>One parsed issue of a document, plus where it came from and how big it was.</summary>
/// <param name="Path">The file, relative to the dataset root. Shared by every issue of it.</param>
/// <param name="Bytes">The size of that file.</param>
/// <param name="Envelope">This issue.</param>
/// <param name="Payload">Its <c>type_data</c>, deserialised by document type.</param>
/// <param name="PayloadError">Why the payload could not be read, if it could not.</param>
/// <param name="IsLatest">
/// Whether this is the document as it stands. A file holds an array of issues, oldest first,
/// and the last one is current.
/// </param>
public sealed record LoadedDocument(
    string Path, long Bytes, TechDocument Envelope, object? Payload, string? PayloadError,
    bool IsLatest = true)
{
    /// <summary>The issue letter, or "A" for a document that declares none.</summary>
    public string Issue => Envelope.Issue?.Letter ?? "A";
}

public sealed class DocumentSet
{
    /// <summary>Every issue of every document, oldest first within a document.</summary>
    public List<LoadedDocument> Issues { get; init; } = [];

    public List<string> Problems { get; init; } = [];

    /// <summary>
    /// The documents as they stand — the latest issue of each. This is what most callers mean
    /// by "the dataset"; <see cref="Issues"/> is the history behind it.
    /// </summary>
    public List<LoadedDocument> Documents => Issues.Where(i => i.IsLatest).ToList();

    /// <summary>Every issue of one document, oldest first.</summary>
    public IEnumerable<LoadedDocument> IssuesOf(string docId) =>
        Issues.Where(i => i.Envelope.DocId == docId);

    // A file is counted once, not once per issue.
    public long Bytes => Issues.Where(i => i.IsLatest).Sum(d => d.Bytes);
    public int Count => Issues.Count(i => i.IsLatest);
}

/// <summary>
/// Loads the whole dataset from disk: the reference CSVs through CsvHelper and the
/// documents through System.Text.Json. Reports drift rather than hiding it — anything
/// that lands in a <c>[JsonExtensionData]</c> bag or fails an enum parse is surfaced.
/// </summary>
public static class DatasetLoader
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new TolerantEnumConverterFactory());
        return options;
    }

    public static readonly IReadOnlyDictionary<DocType, Type> PayloadTypes =
        new Dictionary<DocType, Type>
        {
            [DocType.MOD] = typeof(ModData),
            [DocType.SB] = typeof(ServiceBulletinData),
            [DocType.IO] = typeof(InternalOrderData),
            [DocType.PAWO] = typeof(PreAnalysisWorkOrderData),
            [DocType.AMM] = typeof(AmmTaskData),
            [DocType.IPC] = typeof(IpcFigureData),
            [DocType.SRM] = typeof(SrmRepairData),
            [DocType.TSM] = typeof(TsmProcedureData),
            [DocType.WDM] = typeof(WdmSheetData),
            [DocType.FCOM] = typeof(FcomEntryData),
        };

    // -----------------------------------------------------------------------
    // reference CSVs
    // -----------------------------------------------------------------------

    public static ReferenceData LoadReference(string root)
    {
        var dir = Path.Combine(root, "data", "reference");
        var data = new ReferenceData();
        if (!Directory.Exists(dir))
        {
            data.Problems.Add($"reference directory not found: {dir}");
            return data;
        }

        data.AtaChapters.AddRange(ReadCsv<AtaChapter>(dir, "ata_chapters.csv", data));
        data.PartNumbers.AddRange(ReadCsv<PartNumberRow>(dir, "part_numbers.csv", data));
        data.Fins.AddRange(ReadCsv<FinRow>(dir, "fins.csv", data));
        data.Msns.AddRange(ReadCsv<MsnRow>(dir, "msn_registry.csv", data));
        data.Drawings.AddRange(ReadCsv<DrawingRow>(dir, "drawings.csv", data));
        data.Software.AddRange(ReadCsv<SoftwareRow>(dir, "software.csv", data));
        data.Zones.AddRange(ReadCsv<ZoneRow>(dir, "zones.csv", data));
        data.Operators.AddRange(ReadCsv<OperatorRow>(dir, "operators.csv", data));
        data.Suppliers.AddRange(ReadCsv<SupplierRow>(dir, "suppliers.csv", data));
        return data;
    }

    private static List<T> ReadCsv<T>(string dir, string fileName, ReferenceData data)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            data.Problems.Add($"{fileName}: file not found");
            data.Files.Add(new FileStat(fileName, 0, 0));
            return [];
        }

        var bytes = new FileInfo(path).Length;
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Report a renamed or missing column instead of silently yielding defaults.
            HeaderValidated = ctx =>
            {
                foreach (var header in ctx.InvalidHeaders)
                    data.Problems.Add($"{fileName}: missing column '{string.Join('/', header.Names)}'");
            },
            MissingFieldFound = ctx =>
                data.Problems.Add($"{fileName}: missing field at row {ctx.Context?.Parser?.Row}"),
        };

        var rows = new List<T>();
        try
        {
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, config);
            rows.AddRange(csv.GetRecords<T>());
        }
        catch (Exception ex)
        {
            data.Problems.Add($"{fileName}: {ex.GetType().Name} — {ex.Message}");
        }

        data.Files.Add(new FileStat(fileName, rows.Count, bytes));
        return rows;
    }

    // -----------------------------------------------------------------------
    // documents
    // -----------------------------------------------------------------------

    public static DocumentSet LoadDocuments(string root, string? program = null)
    {
        var dir = Path.Combine(root, "data", "documents");
        var set = new DocumentSet();
        if (!Directory.Exists(dir))
        {
            set.Problems.Add($"documents directory not found: {dir}");
            return set;
        }

        var search = program is null ? dir : Path.Combine(dir, program);
        if (!Directory.Exists(search))
        {
            set.Problems.Add($"program directory not found: {search}");
            return set;
        }

        foreach (var path in Directory.EnumerateFiles(search, "*.json", SearchOption.AllDirectories)
                                      .OrderBy(p => p, StringComparer.Ordinal))
        {
            var bytes = new FileInfo(path).Length;

            // A document file is an array of issues, oldest first, and the last one is the
            // document as it stands.
            List<TechDocument>? issues;
            try
            {
                using var stream = File.OpenRead(path);
                issues = JsonSerializer.Deserialize<List<TechDocument>>(stream, JsonOptions);
            }
            catch (JsonException ex)
            {
                set.Problems.Add($"{Rel(root, path)}: invalid JSON — {ex.Message}");
                continue;
            }

            if (issues is null || issues.Count == 0)
            {
                set.Problems.Add($"{Rel(root, path)}: no issues — a document file is a non-empty array");
                continue;
            }

            for (var index = 0; index < issues.Count; index++)
            {
                var envelope = issues[index];

                object? payload = null;
                string? payloadError = null;
                if (PayloadTypes.TryGetValue(envelope.DocType, out var payloadType)
                    && envelope.TypeData.ValueKind is JsonValueKind.Object)
                {
                    try
                    {
                        payload = envelope.TypeData.Deserialize(payloadType, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        payloadError = ex.Message;
                    }
                }
                else if (envelope.DocType == DocType.Unknown)
                {
                    payloadError = "unknown doc_type";
                }

                set.Issues.Add(new LoadedDocument(Rel(root, path), bytes, envelope, payload,
                    payloadError, IsLatest: index == issues.Count - 1));
            }
        }

        return set;
    }

    /// <summary>Strongly-typed view over one document type.</summary>
    public static List<TechDocument<TPayload>> Typed<TPayload>(this DocumentSet set, DocType docType)
        where TPayload : class
        => [.. set.Documents
                  .Where(d => d.Envelope.DocType == docType)
                  .Select(d => new TechDocument<TPayload>(d.Envelope, d.Payload as TPayload))];

    // -----------------------------------------------------------------------
    // drift detection
    // -----------------------------------------------------------------------

    /// <summary>
    /// Walks a parsed object graph and yields every <c>[JsonExtensionData]</c> key found.
    /// A non-empty result means the JSON carries members the C# model does not declare.
    /// </summary>
    public static IEnumerable<string> UnmappedMembers(object? node, string path = "")
    {
        if (node is null) yield break;

        var type = node.GetType();
        if (type.IsPrimitive || node is string || node is JsonElement) yield break;

        if (node is System.Collections.IEnumerable sequence and not System.Collections.IDictionary)
        {
            var index = 0;
            foreach (var item in sequence)
            {
                foreach (var found in UnmappedMembers(item, $"{path}[{index}]")) yield return found;
                index++;
            }
            yield break;
        }

        foreach (var property in type.GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            object? value;
            try { value = property.GetValue(node); }
            catch (Exception) { continue; }
            if (value is null) continue;

            var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";

            if (property.Name == "Unmapped" && value is Dictionary<string, JsonElement> extra)
            {
                foreach (var key in extra.Keys) yield return $"{path}:{key}";
                continue;
            }

            if (value is string || value.GetType().IsPrimitive || value is JsonElement) continue;
            if (value.GetType().Assembly != typeof(TechDocument).Assembly
                && value is not System.Collections.IEnumerable) continue;

            foreach (var found in UnmappedMembers(value, childPath)) yield return found;
        }
    }

    private static string Rel(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    public static (T Result, TimeSpan Elapsed) Timed<T>(Func<T> action)
    {
        var sw = Stopwatch.StartNew();
        var result = action();
        sw.Stop();
        return (result, sw.Elapsed);
    }
}
