using Curiosity.Library;
using CsvHelper.Configuration.Attributes;
using Mosaik.Core;

namespace AviationIntel.Connector.Ingest;

/// <summary>
/// <c>data/reference/*.csv</c> → the four vocabularies the workspace spots inside articles:
/// <see cref="Organisation"/>, <see cref="AircraftType"/>, <see cref="EngineType"/> and <see cref="Airport"/>.
/// </summary>
/// <remarks>
/// <para>
/// These are master lists, generated from Wikidata and OurAirports by
/// <c>tools/build_reference_vocabularies.py</c> - never hand-authored. The connector writes each
/// entry, the relationships its source states (who builds an aircraft type, which engines power it),
/// and every spottable term as an alias. It does <b>not</b> link any article to any entity: that is
/// what the workspace's spotters and linking rules discover, as <c>_Mentions</c> / <c>_AppearsIn</c>.
/// </para>
/// <para>
/// The spotters are built from the key field (a Wikidata or ICAO identifier, which never occurs in
/// prose) with aliases included, so what is found in text is exactly the <c>Terms</c> column. That is
/// how an entity whose name is a common word ("Emirates") stays in the graph without every mention
/// of the word linking to it.
/// </para>
/// <para>
/// Aliases are only ever added. A term the generator stops producing stays on the node until the
/// workspace is rebuilt; the vocabularies change rarely enough for that to be the right trade.
/// </para>
/// </remarks>
internal static class ReferenceIngest
{
    public const int Version = 1;

    [Node]
    internal sealed class Organisation
    {
        [Key]      public string WikidataId { get; set; } = "";
        [Property] public string Name       { get; set; } = "";
        [Property] public string Kind       { get; set; } = "";
        [Property] public string Country    { get; set; } = "";
        [Property] public string IcaoCode   { get; set; } = "";
    }

    [Node]
    internal sealed class AircraftType
    {
        [Key]      public string WikidataId  { get; set; } = "";
        [Property] public string Name        { get; set; } = "";
        [Property] public string FirstFlight { get; set; } = "";
    }

    [Node]
    internal sealed class EngineType
    {
        [Key]      public string WikidataId { get; set; } = "";
        [Property] public string Name       { get; set; } = "";
    }

    [Node]
    internal sealed class Airport
    {
        [Key]      public string IcaoCode     { get; set; } = "";
        [Property] public string Name         { get; set; } = "";
        [Property] public string IataCode     { get; set; } = "";
        [Property] public string Municipality { get; set; } = "";
        [Property] public string Country      { get; set; } = "";
    }

    private sealed class OrganisationRow
    {
        [Name("Id")]       public string Id       { get; set; } = "";
        [Name("Name")]     public string Name     { get; set; } = "";
        [Name("Kind")]     public string Kind     { get; set; } = "";
        [Name("Country")]  public string Country  { get; set; } = "";
        [Name("IcaoCode")] public string IcaoCode { get; set; } = "";
        [Name("Terms")]    public string Terms    { get; set; } = "";
    }

    private sealed class AircraftRow
    {
        [Name("Id")]              public string Id              { get; set; } = "";
        [Name("Name")]            public string Name            { get; set; } = "";
        [Name("FirstFlight")]     public string FirstFlight     { get; set; } = "";
        [Name("ManufacturerIds")] public string ManufacturerIds { get; set; } = "";
        [Name("EngineIds")]       public string EngineIds       { get; set; } = "";
        [Name("Terms")]           public string Terms           { get; set; } = "";
    }

    private sealed class EngineRow
    {
        [Name("Id")]              public string Id              { get; set; } = "";
        [Name("Name")]            public string Name            { get; set; } = "";
        [Name("ManufacturerIds")] public string ManufacturerIds { get; set; } = "";
        [Name("Terms")]           public string Terms           { get; set; } = "";
    }

    private sealed class AirportRow
    {
        [Name("IcaoCode")]     public string IcaoCode     { get; set; } = "";
        [Name("Name")]         public string Name         { get; set; } = "";
        [Name("IataCode")]     public string IataCode     { get; set; } = "";
        [Name("Municipality")] public string Municipality { get; set; } = "";
        [Name("Country")]      public string Country      { get; set; } = "";
        [Name("Terms")]        public string Terms        { get; set; } = "";
    }

    public static async Task RunAsync(ConnectorContext ctx)
    {
        var folder = Path.Combine(ctx.Options.Root, "data", "reference");

        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"    no reference vocabularies at {folder} - skipped");
            return;
        }

        var hashes = new List<UID.UID128>();

        foreach (var row in ctx.ReadCsv<OrganisationRow>(Path.Combine(folder, "organisations.csv")))
        {
            if (Unchanged(ctx, row, hashes)) continue;

            var node = ctx.Graph.AddOrUpdate(new Organisation { WikidataId = row.Id, Name = row.Name, Kind = row.Kind, Country = row.Country, IcaoCode = row.IcaoCode });
            ctx.CountNode(nameof(Organisation));
            AddTerms(ctx, node, row.Terms);
        }

        foreach (var row in ctx.ReadCsv<EngineRow>(Path.Combine(folder, "engines.csv")))
        {
            if (Unchanged(ctx, row, hashes)) continue;

            var node = ctx.Graph.AddOrUpdate(new EngineType { WikidataId = row.Id, Name = row.Name });
            ctx.CountNode(nameof(EngineType));
            AddTerms(ctx, node, row.Terms);

            foreach (var id in Split(row.ManufacturerIds)) ctx.LinkKey(node, nameof(Organisation), id, Edges.EngineBuiltBy, Edges.BuildsEngine);
        }

        foreach (var row in ctx.ReadCsv<AircraftRow>(Path.Combine(folder, "aircraft.csv")))
        {
            if (Unchanged(ctx, row, hashes)) continue;

            var node = ctx.Graph.AddOrUpdate(new AircraftType { WikidataId = row.Id, Name = row.Name, FirstFlight = row.FirstFlight });
            ctx.CountNode(nameof(AircraftType));
            AddTerms(ctx, node, row.Terms);

            foreach (var id in Split(row.ManufacturerIds)) ctx.LinkKey(node, nameof(Organisation), id, Edges.AircraftBuiltBy, Edges.BuildsAircraft);
            foreach (var id in Split(row.EngineIds))       ctx.LinkKey(node, nameof(EngineType),   id, Edges.PoweredBy,       Edges.Powers);
        }

        foreach (var row in ctx.ReadCsv<AirportRow>(Path.Combine(folder, "airports.csv")))
        {
            if (Unchanged(ctx, row, hashes)) continue;

            var node = ctx.Graph.AddOrUpdate(new Airport { IcaoCode = row.IcaoCode, Name = row.Name, IataCode = row.IataCode, Municipality = row.Municipality, Country = row.Country });
            ctx.CountNode(nameof(Airport));
            AddTerms(ctx, node, row.Terms);
        }

        await ctx.Graph.CommitPendingAsync();

        if (!ctx.DryRun)
        {
            foreach (var hash in hashes) ctx.Cache.EnqueueHash(Version, hash);
        }

        await ctx.CommitCacheAsync();
    }

    //One hash per row: an unchanged vocabulary costs nothing on the hourly run.
    private static bool Unchanged<TRow>(ConnectorContext ctx, TRow row, List<UID.UID128> hashes) where TRow : class
    {
        var hash = ctx.Cache.Hash(row);

        if (ctx.Cache.ContainsHash(Version, hash))
        {
            ctx.CountOutcome("reference entry unchanged (cached)");
            return true;
        }

        hashes.Add(hash);
        return false;
    }

    //Case-sensitive: the spotters are, and "LEAP" the engine is not "leap" the verb.
    private static void AddTerms(ConnectorContext ctx, Node node, string terms)
    {
        foreach (var term in Split(terms))
        {
            ctx.Graph.AddAlias(node, Language.Any, term, ignoreCase: false);
            ctx.CountOutcome("reference term");
        }
    }

    private static IEnumerable<string> Split(string value) => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
