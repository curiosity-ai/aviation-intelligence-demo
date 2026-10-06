// Reference template: creating users. See ../../SKILL.md §7 and curiosity-graph-modelling §9.
//
// The user node type is OWNED BY THE GRAPH, so it is not a [Node] class here and there is
// nowhere to put properties on it. Everything you want to know about a person is therefore an
// EDGE to the same nodes your records are classified by — which is what puts someone one hop
// from the work of their own office, and what lets a screen render a profile from UIDs (each
// one opening that site, that department, that speciality) instead of from strings an endpoint
// had to spell out.

using Curiosity.Library;

namespace App.Connector.Ingest;

internal static class UsersIngest
{
    public const int Version = 1;

    private sealed class Row
    {
        public string UserName     { get; set; } = "";
        public string Email        { get; set; } = "";
        public string FirstName    { get; set; } = "";
        public string LastName     { get; set; } = "";
        public string Role         { get; set; } = "";
        public string Site         { get; set; } = "";
        public string Department   { get; set; } = "";
        /// <summary>Pipe-separated category codes this person specialises in.</summary>
        public string Specialities { get; set; } = "";
    }

    /// <summary>What the assignment pass needs to know about the people it is staffing.</summary>
    internal sealed record LoadedUser(string UserName, IReadOnlyList<string> Specialities);

    public static async Task<IReadOnlyList<LoadedUser>> RunAsync(ConnectorContext ctx)
    {
        var loaded = new List<LoadedUser>();

        foreach (var row in ctx.ReadCsv<Row>("users.csv"))
        {
            if (string.IsNullOrWhiteSpace(row.UserName)) continue;

            var specialities = ConnectorContext.Pipe(row.Specialities);

            // CreateUserAsync returns the node — there is no key to reconstruct it from, so the
            // links are made on the spot from the returned node rather than via Node.FromKey.
            var user = await ctx.Graph.CreateUserAsync(row.UserName, row.Email, row.FirstName, row.LastName);

            ctx.CountNode("_User");

            ctx.LinkKey (user, nameof(SitesIngest.Site),            row.Site,       Edges.InPartition, Edges.HasDocument);
            ctx.LinkKey (user, nameof(DepartmentsIngest.Department),row.Department, Edges.InPartition, Edges.HasDocument);
            ctx.LinkKeys(user, nameof(CategoriesIngest.Category),   specialities,   Edges.SpecialisesIn, Edges.SpecialistIs);

            // A role is a judgement about the person, not a state they are in, so it joins the
            // Classification family and goes through Classify like any other vocabulary.
            MetaLinks.Classify(ctx, user, MetaLinks.Kind.UserRole, row.Role);

            loaded.Add(new LoadedUser(row.UserName, specialities));
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();

        // Specialities should OVERLAP, so more than one person is a plausible owner for most
        // categories — and a few categories should have no specialist at all, which is what
        // makes the dashboard's "Unassigned" tile non-empty.
        return loaded;
    }
}
