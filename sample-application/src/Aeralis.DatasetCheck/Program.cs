using System.Diagnostics;
using System.Text.Json;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

namespace Aeralis.DatasetCheck;

/// <summary>
/// Parses the whole dataset with the C# models and reports what it found: rows and
/// bytes per reference CSV, document counts per program and per type, schema fidelity,
/// and the total megabytes read.
/// </summary>
internal static class Program
{
    private static readonly string[] DocTypeOrder =
        ["IO", "MOD", "PAWO", "SB", "AMM", "IPC", "SRM", "TSM", "WDM", "FCOM"];

    private static readonly string[] ProgramOrder = ["P", "C", "R", "N", "L"];

    private static int Main(string[] args)
    {
        var root = ArgValue(args, "--root") ?? Directory.GetCurrentDirectory();
        var strict = args.Contains("--strict");
        var sample = int.TryParse(ArgValue(args, "--sample"), out var s) ? s : 0;
        var onlyProgram = ArgValue(args, "--program");
        root = Path.GetFullPath(root);

        var stopwatch = Stopwatch.StartNew();
        VocabularyLog.Clear();

        Console.WriteLine("Aeralis Technical Data — dataset check");
        Console.WriteLine($"Root: {root}");
        Console.WriteLine();

        var reference = DatasetLoader.LoadReference(root);
        PrintReference(reference);

        var documents = DatasetLoader.LoadDocuments(root, onlyProgram);
        PrintDocuments(documents);

        var schemaProblems = PrintSchema(documents, reference);

        if (sample > 0) PrintSamples(documents, sample);

        stopwatch.Stop();

        var totalFiles = reference.Files.Count + documents.Count;
        var totalBytes = reference.Bytes + documents.Bytes;
        Console.WriteLine("TOTAL");
        Console.WriteLine($"  files read ................ {totalFiles:N0}");
        Console.WriteLine($"  bytes read ................ {Mb(totalBytes)} ({totalBytes:N0} bytes)");
        Console.WriteLine($"  elapsed ................... {stopwatch.Elapsed.TotalSeconds:F2} s");
        Console.WriteLine();

        var hardFailures = reference.Problems.Count + documents.Problems.Count;
        if (hardFailures > 0)
        {
            Console.WriteLine($"FAILED — {hardFailures} load problem(s)");
            return 1;
        }
        if (strict && schemaProblems > 0)
        {
            Console.WriteLine($"FAILED — {schemaProblems} schema problem(s) (--strict)");
            return 1;
        }
        Console.WriteLine(schemaProblems == 0 ? "OK" : $"OK with {schemaProblems} schema warning(s)");
        return 0;
    }

    private static void PrintReference(Dataset.ReferenceData reference)
    {
        Console.WriteLine("REFERENCE DATA");
        foreach (var file in reference.Files)
            Console.WriteLine($"  {file.Name,-24} {file.Rows,8:N0} rows   {Mb(file.Bytes),10}");
        Console.WriteLine($"  {new string('-', 46)}");
        Console.WriteLine($"  {reference.Files.Count + " files",-24} {reference.Rows,8:N0} rows   {Mb(reference.Bytes),10}");
        foreach (var problem in reference.Problems.Take(20))
            Console.WriteLine($"  PROBLEM: {problem}");
        Console.WriteLine();
    }

    private static void PrintDocuments(DocumentSet documents)
    {
        Console.WriteLine("DOCUMENTS");
        if (documents.Count == 0)
        {
            Console.WriteLine("  (none found — run tools/emit.py first)");
            foreach (var problem in documents.Problems.Take(20))
                Console.WriteLine($"  PROBLEM: {problem}");
            Console.WriteLine();
            return;
        }

        var grid = new Dictionary<(string Program, string Type), int>();
        var bytesByType = new Dictionary<string, long>();
        var countByType = new Dictionary<string, int>();

        foreach (var doc in documents.Documents)
        {
            var program = doc.Envelope.Program.ToString();
            var type = doc.Envelope.DocType.ToString();
            grid[(program, type)] = grid.GetValueOrDefault((program, type)) + 1;
            bytesByType[type] = bytesByType.GetValueOrDefault(type) + doc.Bytes;
            countByType[type] = countByType.GetValueOrDefault(type) + 1;
        }

        var programs = ProgramOrder
            .Where(p => grid.Keys.Any(k => k.Program == p))
            .Concat(grid.Keys.Select(k => k.Program).Where(p => !ProgramOrder.Contains(p)).Distinct())
            .ToList();
        var types = DocTypeOrder
            .Where(t => countByType.ContainsKey(t))
            .Concat(countByType.Keys.Where(t => !DocTypeOrder.Contains(t)))
            .ToList();

        Console.Write("  Program");
        foreach (var type in types) Console.Write($"{type,6}");
        Console.WriteLine($"{"total",8}");

        foreach (var program in programs)
        {
            Console.Write($"  {program,-7}");
            var rowTotal = 0;
            foreach (var type in types)
            {
                var n = grid.GetValueOrDefault((program, type));
                rowTotal += n;
                Console.Write($"{n,6}");
            }
            Console.WriteLine($"{rowTotal,8}");
        }

        Console.WriteLine($"  {new string('-', 9 + types.Count * 6 + 8)}");
        Console.Write($"  {"total",-7}");
        foreach (var type in types) Console.Write($"{countByType.GetValueOrDefault(type),6}");
        Console.WriteLine($"{documents.Count,8}");
        Console.WriteLine();

        Console.WriteLine("  by type:");
        foreach (var type in types)
            Console.WriteLine($"    {type,-6} {countByType[type],5} files   {Mb(bytesByType[type]),10}");
        Console.WriteLine($"    {"all",-6} {documents.Count,5} files   {Mb(documents.Bytes),10}");

        foreach (var problem in documents.Problems.Take(20))
            Console.WriteLine($"  PROBLEM: {problem}");
        Console.WriteLine();
    }

    private static int PrintSchema(DocumentSet documents, Dataset.ReferenceData reference)
    {
        var unmapped = new List<string>();
        var payloadErrors = new List<string>();
        var missingPayload = new List<string>();

        foreach (var doc in documents.Documents)
        {
            foreach (var key in DatasetLoader.UnmappedMembers(doc.Envelope))
                unmapped.Add($"{doc.Path}  {key}");
            if (doc.Payload is not null)
                foreach (var key in DatasetLoader.UnmappedMembers(doc.Payload))
                    unmapped.Add($"{doc.Path}  type_data.{key}");
            if (doc.PayloadError is not null)
                payloadErrors.Add($"{doc.Path}  {doc.PayloadError}");
            else if (doc.Payload is null && doc.Envelope.DocType != DocType.Unknown)
                missingPayload.Add(doc.Path);
        }

        var vocab = VocabularyLog.All
            .GroupBy(e => $"{e.EnumType}='{e.Value}'")
            .Select(g => $"{g.Key} ×{g.Count()}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine("SCHEMA");
        Console.WriteLine($"  documents parsed .......... {documents.Count - payloadErrors.Count} / {documents.Count}");
        Console.WriteLine($"  reference rows parsed ..... {reference.Rows:N0}");
        Console.WriteLine($"  unmapped JSON members ..... {unmapped.Count}");
        Console.WriteLine($"  vocabulary violations ..... {VocabularyLog.All.Count}");
        Console.WriteLine($"  type_data parse errors .... {payloadErrors.Count}");
        Console.WriteLine($"  type_data absent .......... {missingPayload.Count}");

        foreach (var line in unmapped.Take(15)) Console.WriteLine($"    unmapped: {line}");
        if (unmapped.Count > 15) Console.WriteLine($"    ... {unmapped.Count - 15} more");
        foreach (var line in vocab.Take(15)) Console.WriteLine($"    vocabulary: {line}");
        if (vocab.Count > 15) Console.WriteLine($"    ... {vocab.Count - 15} more");
        foreach (var line in payloadErrors.Take(10)) Console.WriteLine($"    type_data: {line}");
        foreach (var line in missingPayload.Take(10)) Console.WriteLine($"    no type_data: {line}");
        Console.WriteLine();

        return unmapped.Count + VocabularyLog.All.Count + payloadErrors.Count + missingPayload.Count;
    }

    private static void PrintSamples(DocumentSet documents, int perType)
    {
        Console.WriteLine("SAMPLES");
        foreach (var group in documents.Documents.GroupBy(d => d.Envelope.DocType).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {group.Key}");
            foreach (var doc in group.Take(perType))
            {
                var e = doc.Envelope;
                Console.WriteLine($"    {e.DocId,-30} {e.Program} {e.Ata?.Ata6d,-10} "
                                  + $"entities:{e.Entities?.TotalCount ?? 0,-3} refs:{e.References.Count,-2} "
                                  + $"| {Truncate(e.Title, 60)}");
                if (doc.Payload is PreAnalysisWorkOrderData pawo)
                    Console.WriteLine($"      subject {pawo.SubjectMod}, {pawo.ExpectedFindings.Count} findings, "
                                      + $"{pawo.ExpectedFindings.Count(f => f.Severity == Severity.BLOCKING)} blocking"
                                      + (pawo.HeroScenario ? ", HERO" : ""));
            }
        }
        Console.WriteLine();
    }

    private static string Mb(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024.0 / 1024.0:F2} MB"
        : $"{bytes / 1024.0:F1} KB";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
