using System.Diagnostics;
using Aeralis.Attachments;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

// Generates the attachments for the dataset and writes them to a directory, which is what the
// connector uploads. Useful on its own for looking at what a document renders to.
//
//   dotnet run --project src/Aeralis.Attachments -- --root . --out build/attachments
//   dotnet run --project src/Aeralis.Attachments -- --root . --out /tmp/x --doc MOD-160142
//   dotnet run --project src/Aeralis.Attachments -- --root . --out /tmp/x --type AMM --limit 5
//
// Every issue of a document is rendered; --latest renders only the current one.

var root = Path.GetFullPath(Arg("--root") ?? Directory.GetCurrentDirectory());
var output = Path.GetFullPath(Arg("--out") ?? Path.Combine(root, "build", "attachments"));
var docFilter = Args("--doc");
var typeFilter = Args("--type").Select(t => Enum.Parse<DocType>(t, ignoreCase: true)).ToHashSet();
var programFilter = Args("--program").Select(p => Enum.Parse<ProgramLetter>(p, ignoreCase: true)).ToHashSet();
var limit = int.TryParse(Arg("--limit"), out var parsed) ? parsed : int.MaxValue;
var latestOnly = Environment.GetCommandLineArgs().Contains("--latest");
var parallelism = int.TryParse(Arg("--parallel"), out var threads)
    ? threads
    : Math.Max(1, Environment.ProcessorCount / 2);

if (!Directory.Exists(Path.Combine(root, "data", "reference")))
{
    Console.Error.WriteLine($"No dataset found under {root}. Pass --root pointing at the repository.");
    return 2;
}

Console.WriteLine("Aeralis Technical Data — document attachments");
Console.WriteLine($"Root:   {root}");
Console.WriteLine($"Output: {output}");

var reference = DatasetLoader.LoadReference(root);

// Every issue, not only the current one — each was published in its own right and has its own
// markup, artwork and PDF. --latest renders just the current state, which is the quicker loop
// when you only want to look at what a document is now.
var loadedSet = DatasetLoader.LoadDocuments(root);
var documents = (latestOnly ? loadedSet.Documents : (IEnumerable<LoadedDocument>)loadedSet.Issues)
    .Where(d => docFilter.Count == 0 || docFilter.Contains(d.Envelope.DocId))
    .Where(d => typeFilter.Count == 0 || typeFilter.Contains(d.Envelope.DocType))
    .Where(d => programFilter.Count == 0 || programFilter.Contains(d.Envelope.Program))
    .OrderBy(d => d.Envelope.DocId, StringComparer.Ordinal)
    .ThenBy(d => d.Issue, StringComparer.Ordinal)
    .Take(limit)
    .ToList();

Console.WriteLine($"Issues: {documents.Count:N0}   (parallelism {parallelism})");
Console.WriteLine();

Directory.CreateDirectory(output);
var builder = new AttachmentBuilder(reference, Path.Combine(output, ".work"));

var stopwatch = Stopwatch.StartNew();
var counts = new Dictionary<AttachmentKind, int>();
var bytes = 0L;
var failures = new List<string>();
var done = 0;
var gate = new Lock();

// The published files sit at the top; what they were rendered from sits beside them, so a
// directory can be read as "here is the PDF, here is the markup and the artwork it came from".
var sourceDirectory = Path.Combine(output, "source");
var imageDirectory = Path.Combine(output, "images");
var sources = 0;

Parallel.ForEach(documents, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, document =>
{
    DocumentRendering rendering;
    try
    {
        rendering = builder.Build(document);
    }
    catch (Exception ex)
    {
        lock (gate) failures.Add($"{document.Envelope.DocId}: {ex.GetType().Name} — {ex.Message}");
        return;
    }

    foreach (var attachment in rendering.Files)
    {
        File.WriteAllBytes(Path.Combine(output, attachment.FileName), attachment.Bytes);
    }

    if (rendering.Source is { } source)
    {
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, source.FileName), source.Xml);

        if (source.Images.Count > 0) Directory.CreateDirectory(imageDirectory);
        foreach (var image in source.Images)
        {
            File.WriteAllBytes(Path.Combine(imageDirectory, image.FileName), image.Bytes);
        }
    }

    lock (gate)
    {
        foreach (var attachment in rendering.Files.Concat(rendering.Source?.Images ?? []))
        {
            counts[attachment.Kind] = counts.GetValueOrDefault(attachment.Kind) + 1;
            bytes += attachment.Bytes.Length;
        }

        if (rendering.Source is { } written)
        {
            sources++;
            bytes += written.Xml.Length;
        }

        if (++done % 25 == 0 || done == documents.Count)
        {
            Console.WriteLine($"  {done,5:N0}/{documents.Count:N0}  {stopwatch.Elapsed.TotalSeconds,6:F1} s");
        }
    }
});

stopwatch.Stop();
Console.WriteLine();
foreach (var (kind, count) in counts.OrderByDescending(kv => kv.Value))
{
    Console.WriteLine($"  {kind,-22} {count,6:N0}");
}
Console.WriteLine($"  {"CsdbSource",-22} {sources,6:N0}");
Console.WriteLine($"  {"total bytes",-22} {bytes / 1024.0 / 1024.0,6:F1} MB");
Console.WriteLine($"  {"elapsed",-22} {stopwatch.Elapsed.TotalSeconds,6:F1} s");

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} document(s) failed:");
    foreach (var failure in failures.Take(20)) Console.Error.WriteLine($"  {failure}");
    return 1;
}

return 0;

string? Arg(string name)
{
    var argv = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(argv, name);
    return index >= 0 && index + 1 < argv.Length ? argv[index + 1] : null;
}

// Repeatable options: --type MOD --type SB
List<string> Args(string name)
{
    var argv = Environment.GetCommandLineArgs();
    var values = new List<string>();
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == name) values.Add(argv[i + 1]);
    }
    return values;
}
