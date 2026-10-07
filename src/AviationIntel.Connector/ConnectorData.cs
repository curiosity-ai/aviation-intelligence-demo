using Curiosity.Library;

namespace AviationIntel.Connector;

/// <summary>
/// The curated inputs the connectors read - the feed directory and the five reference vocabularies -
/// stored in the workspace as files, so the in-workspace data connectors
/// (<c>src/AviationIntel.Definitions/code/data-connector-tasks/</c>) can read them without access to
/// this repository.
/// </summary>
/// <remarks>
/// Each file is stored under a fixed identifier, and a file's UID is a hash of its identifier
/// (<c>_FileEntry.For(identifier)</c>). That is what lets the in-workspace connectors name the files
/// they read without a UID copied out of one workspace: the same identifier is the same UID in every
/// workspace. The identifiers here and in the connector scripts must stay identical.
/// </remarks>
internal static class ConnectorData
{
    public const string Source = "Aviation Intelligence connector data";

    public static readonly (string Identifier, string RelativePath)[] Files =
    {
        ("aviation-intel/data/feeds/aviation-aerospace-rss-directory.csv", "data/feeds/aviation-aerospace-rss-directory.csv"),
        ("aviation-intel/data/reference/organisations.csv",                "data/reference/organisations.csv"),
        ("aviation-intel/data/reference/aircraft.csv",                     "data/reference/aircraft.csv"),
        ("aviation-intel/data/reference/engines.csv",                      "data/reference/engines.csv"),
        ("aviation-intel/data/reference/launch_vehicles.csv",              "data/reference/launch_vehicles.csv"),
        ("aviation-intel/data/reference/airports.csv",                     "data/reference/airports.csv"),
    };

    /// <summary>
    /// Uploads every file, private so it never shows up in a user's search. An unchanged file is left
    /// alone (the upload compares hashes), so this is safe to run after every change to <c>data/</c>.
    /// </summary>
    public static async Task UploadAsync(Graph graph, string root)
    {
        foreach (var (identifier, relativePath) in Files)
        {
            var path = Path.Combine(root, relativePath);

            await using var stream = File.OpenRead(path);

            var node = await graph.UploadFileWithIdentifierAsync(stream, identifier, Path.GetFileName(path), Source, checkHash: true, initiallyPrivate: true);

            Console.WriteLine($"  {relativePath,-52} → {node.UID}");
        }
    }
}
