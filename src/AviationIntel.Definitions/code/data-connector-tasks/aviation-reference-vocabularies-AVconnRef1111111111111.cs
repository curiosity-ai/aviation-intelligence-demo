[task: Curiosity.Tasks.Name("Aviation reference vocabularies")]
[task: Curiosity.Tasks.UID("AVconnRef1111111111111")]
[task: Curiosity.Tasks.Schedule("41 4 * * 1")]
[task: Curiosity.Tasks.Connector]

//The in-workspace port of src/AviationIntel.Connector/Ingest/ReferenceIngest.cs: the five vocabularies the
//workspace spots inside articles (organisations, aircraft types, engine types, launch vehicles, airports),
//generated from Wikidata and OurAirports by tools/build_reference_vocabularies.py.
//
//The CSVs are files in the workspace, stored by `AviationIntel.Connector --upload-data` under fixed
//identifiers; a file's UID is a hash of its identifier, so the same lines find them in every workspace.
//
//Only what the source states is written: each entry, who builds an aircraft type or engine, which engines
//power an aircraft, and every spottable term as a case-sensitive alias. No article is linked to anything
//here - that is what the spotters and linking rules discover, as _Mentions / _AppearsIn.
//
//Aliases are cleared and re-added on every run, so a term the generator stops producing leaves the
//spotter (the stand-alone connector needed a ledger file for this). After a run that changed terms,
//refresh the spotter models - see docs/running-locally.md.

const string FOLDER = "aviation-intel/data/reference/";

var counts = new Dictionary<string, int>();

foreach (var row in await ReadAsync("organisations.csv"))
{
    var node = Graph.AddOrUpdate(Key(N.Organisation.Type, row["Id"]), new
    {
        WikidataId = row["Id"],
        Name = row["Name"],
        Kind = row["Kind"],
        Country = row["Country"],
        IcaoCode = row["IcaoCode"],
    });

    Terms(node, row["Terms"]);
    Count(N.Organisation.Type);
}

foreach (var row in await ReadAsync("engines.csv"))
{
    var node = Graph.AddOrUpdate(Key(N.EngineType.Type, row["Id"]), new
    {
        WikidataId = row["Id"],
        Name = row["Name"],
    });

    Terms(node, row["Terms"]);
    Count(N.EngineType.Type);

    foreach (var id in Split(row["ManufacturerIds"])) Graph.Link(node, Key(N.Organisation.Type, id), E.EngineBuiltBy, E.BuildsEngine);
}

foreach (var row in await ReadAsync("aircraft.csv"))
{
    var node = Graph.AddOrUpdate(Key(N.AircraftType.Type, row["Id"]), new
    {
        WikidataId = row["Id"],
        Name = row["Name"],
        FirstFlight = row["FirstFlight"],
    });

    Terms(node, row["Terms"]);
    Count(N.AircraftType.Type);

    foreach (var id in Split(row["ManufacturerIds"])) Graph.Link(node, Key(N.Organisation.Type, id), E.AircraftBuiltBy, E.BuildsAircraft);
    foreach (var id in Split(row["EngineIds"])) Graph.Link(node, Key(N.EngineType.Type, id), E.PoweredBy, E.Powers);
}

foreach (var row in await ReadAsync("launch_vehicles.csv"))
{
    var node = Graph.AddOrUpdate(Key(N.LaunchVehicle.Type, row["Id"]), new
    {
        WikidataId = row["Id"],
        Name = row["Name"],
    });

    Terms(node, row["Terms"]);
    Count(N.LaunchVehicle.Type);

    foreach (var id in Split(row["ManufacturerIds"])) Graph.Link(node, Key(N.Organisation.Type, id), E.LaunchVehicleBuiltBy, E.BuildsLaunchVehicle);
}

foreach (var row in await ReadAsync("airports.csv"))
{
    var node = Graph.AddOrUpdate(Key(N.Airport.Type, row["IcaoCode"]), new
    {
        IcaoCode = row["IcaoCode"],
        Name = row["Name"],
        IataCode = row["IataCode"],
        Municipality = row["Municipality"],
        Country = row["Country"],
    });

    Terms(node, row["Terms"]);
    Count(N.Airport.Type);
}

var summary = "Written: " + string.Join(", ", counts.Select(kv => $"{kv.Value:n0} {kv.Key}"));

await Graph.LogAsync(LogLevel.Information, summary);

return summary;

//Case-sensitive, like the spotters: "LEAP" the engine is not "leap" the verb. Operations on one node are
//applied in the order they were queued, so the clear always lands before the new terms.
void Terms(Curiosity.Library.Node node, string terms)
{
    Graph.ClearAliases(node);

    foreach (var term in Split(terms)) Graph.AddAlias(node, Language.Any, term, ignoreCase: false);
}

void Count(string type) => counts[type] = counts.GetValueOrDefault(type) + 1;

async Task<List<Dictionary<string, string>>> ReadAsync(string fileName)
{
    var file = Graph.GetFile(UID(GraphDB.Schema._FileEntry.For(FOLDER + fileName)));
    var rows = new List<Dictionary<string, string>>();

    var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
    {
        TrimOptions = CsvHelper.Configuration.TrimOptions.Trim,
        MissingFieldFound = null,
    };

    using (var stream = await Graph.OpenFileAsync(file.Node))
    using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
    using (var csv = new CsvHelper.CsvReader(reader, config))
    {
        await csv.ReadAsync();
        csv.ReadHeader();

        var header = csv.HeaderRecord;

        while (await csv.ReadAsync())
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var column in header) row[column] = csv.GetField(column) ?? "";

            rows.Add(row);
        }
    }

    await Graph.LogAsync(LogLevel.Information, $"{file.Name}: {rows.Count:n0} rows");

    return rows;
}

static IEnumerable<string> Split(string value) => (value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

