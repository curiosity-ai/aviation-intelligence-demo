// Reference template: a CSV reference-data handler. See ../../SKILL.md §1–§3.
//
// The canonical handler shape — a static class whose NESTED types are its own DTO and its own
// [Node] class, so neither can leak into another handler's mapping. Four beats: load, loop
// behind the hash cache, commit the graph, commit the cache.

using Curiosity.Library;

namespace App.Connector.Ingest;

internal static class PartNumbersIngest
{
    /// <summary>
    /// Bump when THIS handler's mapping changes shape — a new [Property], an extra Link, a
    /// changed [Key]. Handlers version independently; there is no global version.
    /// </summary>
    public const int Version = 1;

    // -------------------------------------------------------------------------------------
    // the source row — matches the CSV, and nothing else
    // -------------------------------------------------------------------------------------

    private sealed class Row
    {
        public string Pn           { get; set; } = "";
        public string Description  { get; set; } = "";
        public string PnType       { get; set; } = "";
        public string Lifecycle    { get; set; } = "";
        public string CategoryCode { get; set; } = "";
        public double MassKg       { get; set; }
    }

    // -------------------------------------------------------------------------------------
    // the graph node — need NOT match the row
    // -------------------------------------------------------------------------------------

    [Node]
    internal sealed class PartNumber
    {
        [Key]      public string Pn          { get; set; } = "";
        [Property] public string Description { get; set; } = "";
        [Property] public string PnType      { get; set; } = "";
        [Property] public string Lifecycle   { get; set; } = "";
        [Property] public string Category    { get; set; } = "";
        [Property] public double MassKg      { get; set; }
    }

    // -------------------------------------------------------------------------------------

    public static async Task RunAsync(ConnectorContext ctx)
    {
        // 1. LOAD — streamed, invariant culture (see ConnectorContext.ReadCsv)
        foreach (var row in ctx.ReadCsv<Row>("part_numbers.csv"))
        {
            if (string.IsNullOrWhiteSpace(row.Pn)) continue;

            // 2. LOOP — the hash cache decides whether this record needs work at all.
            //    Hash the SOURCE row: a shape containing DateTime.UtcNow or a counter would
            //    miss on every run and re-ingest the whole corpus every time.
            ctx.Cache.IfNotCached(Version, row, r =>
            {
                // ONE AddOrUpdate per node per run, carrying everything. AddOrUpdate writes the
                // node's WHOLE content — a later partial call would blank the rest. See §4.
                var node = ctx.Graph.AddOrUpdate(new PartNumber
                {
                    Pn          = r.Pn,
                    Description = r.Description,
                    PnType      = r.PnType,
                    Lifecycle   = r.Lifecycle,
                    Category    = r.CategoryCode,
                    MassKg      = r.MassKg,
                });

                ctx.CountNode(nameof(PartNumber));

                // Links are made BY KEY, never by fetching the target: the edge activates once
                // both ends exist, which is also what lets this link forward to a type that is
                // ingested later in the run.
                ctx.LinkKey(node, nameof(CategoriesIngest.Category), r.CategoryCode,
                            Edges.InCategory, Edges.HasCategoryDocument);

                // Lifecycle is a state the part is in and can move out of, so it joins the
                // shared Status vocabulary rather than being a bare string.
                MetaLinks.SetStatus(ctx, node, MetaLinks.StatusField.Lifecycle, r.Lifecycle);
            });
        }

        // 3. COMMIT the graph …
        await ctx.Graph.CommitPendingAsync();

        // 4. … and only then the cache. A hash recorded for work that did not land makes that
        //    record invisible forever.
        await ctx.CommitCacheAsync();
    }
}
