using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

namespace Aeralis.Attachments.Tests;

/// <summary>
/// The repository's own dataset, loaded once for the whole test run.
/// </summary>
/// <remarks>
/// These tests read <c>data/</c> rather than carrying fixtures of their own. The dataset is
/// the input the attachment builder exists to render, it is committed, and it is already
/// gated by <c>tools/validate.py</c> and <c>Aeralis.DatasetCheck</c> — a copy here would be a
/// second thing to keep in step for no gain.
/// </remarks>
public static class Dataset
{
    /// <summary>The repository root, found by walking up from the test binary.</summary>
    public static string Root { get; } = FindRoot();

    public static ReferenceData Reference { get; } = DatasetLoader.LoadReference(Root);

    private static readonly DocumentSet Set = DatasetLoader.LoadDocuments(Root);

    /// <summary>Every issue of every document, oldest first within a document.</summary>
    public static IReadOnlyList<LoadedDocument> Issues { get; } = Set.Issues;

    /// <summary>The documents as they stand — the latest issue of each.</summary>
    public static IReadOnlyList<LoadedDocument> Documents { get; } = Set.Documents;

    /// <summary>
    /// One document of each type, chosen as the first by identifier so the choice does not
    /// move when the dataset grows.
    /// </summary>
    public static IReadOnlyList<LoadedDocument> OnePerType { get; } = Documents
        .GroupBy(d => d.Envelope.DocType)
        .OrderBy(g => g.Key)
        .Select(g => g.OrderBy(d => d.Envelope.DocId, StringComparer.Ordinal).First())
        .ToList();

    /// <summary>xUnit member data: every document type, with a document of that type.</summary>
    public static TheoryData<string> OnePerTypeIds()
    {
        var data = new TheoryData<string>();
        foreach (var document in OnePerType) data.Add(document.Envelope.DocId);
        return data;
    }

    public static LoadedDocument ById(string docId) =>
        Documents.First(d => d.Envelope.DocId == docId);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "reference")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No dataset found above {AppContext.BaseDirectory}. Run the tests from inside the repository.");
    }
}
