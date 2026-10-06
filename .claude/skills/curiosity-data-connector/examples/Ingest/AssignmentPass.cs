// Reference template: deterministic derived state, split in two around the document ingest.
// See ../../SKILL.md §4 and §7.
//
// WHY IT IS SPLIT. AddOrUpdate writes the node's WHOLE content, so a second pass that called
// it with only the three computed fields would blank the title, the abstract and every section
// of text the document pass had just written. So:
//
//   Plan(...)      runs BEFORE the documents, reads everything, writes NOTHING.
//   <document ingest folds the plan's fields into the node's single AddOrUpdate>
//   LinkAsync(...) runs AFTER, once both ends exist, and writes ONLY edges.
//
// Ownership is never a property. There is no AssignedToUserName: a name beside the edge is a
// second copy of one fact and the two drift the moment either is rewritten. Every "whose is
// this?" question is a traversal, which is what guarantees a dashboard count and the list
// behind it agree.

using Curiosity.Library;

namespace App.Connector.Ingest;

internal static class AssignmentPass
{
    public const int Version = 1;

    internal sealed class CaseState
    {
        public string?         Owner          { get; init; }
        public string?         Delegate       { get; init; }
        public string          WorkflowStatus { get; init; } = "Unassigned";
        public bool            IsUrgent       { get; init; }
        public double          HoursSpent     { get; init; }
        public DateTimeOffset? CompletedDate  { get; init; }

        /// <summary>
        /// Part of the document's cache key, so re-tuning the staffing model rewrites the
        /// records it moved even though nothing in the corpus changed.
        /// </summary>
        public string Fingerprint => $"{Owner}|{Delegate}|{WorkflowStatus}|{IsUrgent}|{HoursSpent}";
    }

    internal sealed class Plan
    {
        private readonly Dictionary<string, CaseState> _states = new(StringComparer.Ordinal);

        public void Add(string caseId, CaseState state) => _states[caseId] = state;

        public CaseState For(string caseId) => _states.GetValueOrDefault(caseId) ?? new CaseState();

        public IEnumerable<KeyValuePair<string, CaseState>> All => _states;
    }

    // -------------------------------------------------------------------------------------
    // planning — reads, decides, writes nothing
    // -------------------------------------------------------------------------------------

    public static Plan Compute(ConnectorContext ctx, IReadOnlyList<UsersIngest.LoadedUser> users)
    {
        var plan = new Plan();

        foreach (var (caseId, categoryL2, dueDate, hasBlockingFinding) in ReadCaseFacts(ctx))
        {
            // DETERMINISTIC: every choice is seeded from a stable hash of the record's own
            // identifier. Never Random() without a seed, never wall-clock time — same corpus
            // must produce the same graph on every run, on every machine.
            var seed = StableSeed(caseId);

            // ~82 % get an owner; the rest stay unassigned ON PURPOSE, so the "Unassigned"
            // counter on the dashboard is non-empty and the demo has work to pick up.
            var assigned = seed % 100 < 82;

            var owner = assigned ? PickOwner(seed, categoryL2, users) : null;

            plan.Add(caseId, new CaseState
            {
                Owner    = owner,
                // ~12 % of assigned work is also delegated to a second person.
                Delegate = assigned && (seed / 100) % 100 < 12 ? PickOther(seed, owner, users) : null,

                WorkflowStatus = owner is null ? "Unassigned"
                               : (seed / 10_000) % 100 switch
                                 {
                                     < 45 => "In progress",
                                     < 70 => "Validated",
                                     < 80 => "Rejected",
                                     < 90 => "Delegated",
                                     _    => "On hold",
                                 },

                IsUrgent   = hasBlockingFinding || (dueDate is object && dueDate < DateTimeOffset.UtcNow.AddDays(14)),
                HoursSpent = 2 + (seed % 40) * 0.5,

                // Historical work is what a throughput chart and a last-month outcome split are
                // drawn from. Five open items is far too thin for either.
                CompletedDate = (seed / 10_000) % 100 is >= 45 and < 90
                    ? DateTimeOffset.UtcNow.AddDays(-(seed % 90))
                    : null,
            });
        }

        return plan;
    }

    // -------------------------------------------------------------------------------------
    // linking — edges only, after both ends exist
    // -------------------------------------------------------------------------------------

    public static async Task LinkAsync(ConnectorContext ctx, Plan plan)
    {
        foreach (var (caseId, state) in plan.All)
        {
            if (state.Owner is object)
                ctx.LinkKey(Node.FromKey(nameof(CasesIngest.Case), caseId), "_User", state.Owner,
                            Edges.AssignedTo, Edges.HasAssignment);

            if (state.Delegate is object)
                ctx.LinkKey(Node.FromKey(nameof(CasesIngest.Case), caseId), "_User", state.Delegate,
                            Edges.DelegatedTo, Edges.HasDelegation);
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }

    // -------------------------------------------------------------------------------------

    /// <summary>FNV-1a over the identifier. Stable across runs, machines and .NET versions —
    /// which string.GetHashCode() is not.</summary>
    private static int StableSeed(string id)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in id) hash = (hash ^ c) * 16777619;
            return hash & 0x7FFFFFFF;
        }
    }

    /// <summary>A specialist in the record's own category ~75 % of the time, anyone otherwise.</summary>
    private static string? PickOwner(int seed, string category, IReadOnlyList<UsersIngest.LoadedUser> users)
    {
        var specialists = users.Where(u => u.Specialities.Contains(category)).ToList();

        var pool = specialists.Count > 0 && seed % 100 < 75 ? specialists : users;

        return pool.Count == 0 ? null : pool[(seed / 7) % pool.Count].UserName;
    }

    private static string? PickOther(int seed, string? notThis, IReadOnlyList<UsersIngest.LoadedUser> users)
    {
        var pool = users.Where(u => u.UserName != notThis).ToList();
        return pool.Count == 0 ? null : pool[(seed / 13) % pool.Count].UserName;
    }

    private static IEnumerable<(string CaseId, string CategoryL2, DateTimeOffset? DueDate, bool Blocking)>
        ReadCaseFacts(ConnectorContext ctx) => throw new NotImplementedException("read the corpus here");
}
