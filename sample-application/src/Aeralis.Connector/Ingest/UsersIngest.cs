using Curiosity.Library;
using CsvHelper.Configuration.Attributes;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// `users.csv` → workspace users. `_User` is an internal node type the graph owns, so
/// unlike every other handler this one has no `[Node]` class: it calls
/// <c>CreateUserAsync</c> and keeps the returned <see cref="Node"/> for the assignment
/// pass to link against.
/// </summary>
internal static class UsersIngest
{
    public const int Version = 1;

    internal sealed class Row
    {
        [Name("user_name")] public string UserName { get; set; } = "";
        [Name("email")] public string Email { get; set; } = "";
        [Name("first_name")] public string FirstName { get; set; } = "";
        [Name("last_name")] public string LastName { get; set; } = "";
        [Name("site")] public string Site { get; set; } = "";
        [Name("department")] public string Department { get; set; } = "";
        [Name("role")] public string Role { get; set; } = "";
        [Name("ata_specialities")] public string AtaSpecialities { get; set; } = "";
        [Name("programs")] public string Programs { get; set; } = "";
    }

    /// <summary>A loaded user plus the graph node it maps to.</summary>
    internal sealed record LoadedUser(Row Source, Node? Node)
    {
        public string UserName => Source.UserName;
        public string[] Specialities => ConnectorContext.Pipe(Source.AtaSpecialities);
        public string[] Programs => ConnectorContext.Pipe(Source.Programs);
    }

    public static async Task<IReadOnlyList<LoadedUser>> RunAsync(ConnectorContext ctx)
    {
        var loaded = new List<LoadedUser>();

        foreach (var row in ctx.ReadCsv<Row>("users.csv"))
        {
            Node? node = null;

            if (!ctx.DryRun)
            {
                node = await ctx.Graph.CreateUserAsync(
                    userName: row.UserName,
                    email: row.Email,
                    firstName: row.FirstName,
                    lastName: row.LastName);
            }

            ctx.CountNode("_User");

            if (node is object)
            {
                // Specialities are what the assignment pass routes work by, and what the
                // dashboard's "no specialist for this chapter" case is derived from.
                foreach (var ata in ConnectorContext.Pipe(row.AtaSpecialities))
                    ctx.LinkKey(node, nameof(AtaChaptersIngest.AtaChapter), ata,
                                Edges.SpecialisesIn, Edges.SpecialistIs);

                // Site, department and role used to be read and thrown away: `_User` belongs
                // to the graph, so there is nowhere to put them as properties. As edges to the
                // very nodes the documents are already classified by, an engineer sits one hop
                // from the work of their own office.
                MetaLinks.UserOrganisation(ctx, node, row.Site, row.Department, row.Role);

                foreach (var letter in ConnectorContext.Pipe(row.Programs))
                    ctx.LinkKey(node, nameof(ProgramsIngest.Program), letter,
                                Edges.InProgram, Edges.HasDocument);
            }

            loaded.Add(new LoadedUser(row, node));
        }

        await ctx.Graph.CommitPendingAsync();
        return loaded;
    }
}
