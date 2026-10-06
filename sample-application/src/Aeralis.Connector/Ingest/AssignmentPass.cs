using Curiosity.Library;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// Assigns every pre-analysis work order an owner and a workflow state.
///
/// This is computed here rather than stored in the dataset so the demo's staffing model
/// can be re-tuned without re-authoring documents. It must therefore be **deterministic**:
/// every choice is seeded from a stable hash of the work order's own id, so the same
/// dataset always produces the same owners, the same statuses and the same dashboard.
/// Nothing here reads the clock or an unseeded RNG.
///
/// The pass is split in two halves around the document ingest, and that split matters:
///
/// <list type="bullet">
/// <item><see cref="Plan"/> runs **before** the documents are written, so the work order
/// node is created once, complete. <c>Graph.AddOrUpdate</c> replaces a node's whole
/// content — a second call carrying only the handful of fields this pass computes would
/// blank the title, the abstract, the section texts and every other property the document
/// pass had just written.</item>
/// <item><see cref="LinkAsync"/> runs after, and writes the ownership as **edges**.
/// Who owns a work order is a relationship, so <c>AssignedTo</c> / <c>DelegatedTo</c> are
/// where it lives — there is no <c>AssignedToUserName</c> property to fall out of step
/// with them.</item>
/// </list>
/// </summary>
internal static class AssignmentPass
{
    public const int Version = 3;   // bumped: the workflow vocabulary is SCREAMING_SNAKE now

    /// <summary>Fraction of work orders that get an owner. The remainder are the "unassigned" tile.</summary>
    private const int AssignedPercent = 82;

    /// <summary>How often an ATA specialist is preferred over any available engineer.</summary>
    private const int SpecialistPercent = 75;

    /// <summary>How often an assigned work order is also delegated to a second engineer.</summary>
    private const int DelegatedPercent = 12;

    /// <summary>A work order due within this many days counts as urgent.</summary>
    private const int UrgentWindowDays = 14;

    /// <summary>The dataset's "today". Fixed so the demo's relative dates never drift.</summary>
    private static readonly DateTimeOffset DatasetEnd = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What this pass decided about one work order. The owner and the delegate are carried as
    /// the loaded user rather than as a name, because the only thing written for them is an
    /// edge to their node.
    /// </summary>
    internal sealed record Assignment(
        UsersIngest.LoadedUser? Owner,
        UsersIngest.LoadedUser? Delegate,
        string                  WorkflowStatus,
        bool                    IsUrgent,
        double                  HoursSpent)
    {
        /// <summary>Part of the document fingerprint, so re-tuning the model re-writes the nodes.</summary>
        public string Fingerprint =>
            $"{Version}|{Owner?.UserName}|{Delegate?.UserName}|{WorkflowStatus}|{IsUrgent}|{HoursSpent:F1}";
    }

    /// <summary>Nothing was decided — used for a work order the pass never saw.</summary>
    internal static readonly Assignment None = new(null, null, "UNASSIGNED", false, 0);

    /// <summary>
    /// Decides an owner, a delegate and a workflow state for every work order in the dataset.
    /// Pure: it reads the documents and the users and writes nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, Assignment> Plan(
        ConnectorContext ctx, IReadOnlyList<UsersIngest.LoadedUser> users)
    {
        var plan = new Dictionary<string, Assignment>(StringComparer.Ordinal);

        if (users.Count == 0)
        {
            Console.WriteLine("  no users loaded — every work order stays unassigned");
            return plan;
        }

        var set = DatasetLoader.LoadDocuments(ctx.DatasetRoot);
        var workOrders = set.Documents.Where(d => d.Envelope.DocType == DocType.PAWO);

        foreach (var loaded in workOrders)
        {
            var doc = loaded.Envelope;
            var payload = loaded.Payload as PreAnalysisWorkOrderData;
            var seed = StableSeed(doc.DocId);

            var program = doc.Program.ToString();
            var ata2d = doc.Ata?.Ata2d ?? "";

            var eligible = users
                .Where(u => u.Programs.Length == 0 || u.Programs.Contains(program))
                .ToList();
            if (eligible.Count == 0) eligible = users.ToList();

            var specialists = eligible.Where(u => u.Specialities.Contains(ata2d)).ToList();

            UsersIngest.LoadedUser? owner = null;
            if (Percent(seed, 1) < AssignedPercent)
            {
                var pool = (specialists.Count > 0 && Percent(seed, 2) < SpecialistPercent)
                    ? specialists
                    : eligible;
                owner = pool[(int)(Bucket(seed, 3) % (uint)pool.Count)];
            }

            UsersIngest.LoadedUser? delegate2 = null;
            if (owner is object && Percent(seed, 4) < DelegatedPercent && eligible.Count > 1)
            {
                var others = eligible.Where(u => u.UserName != owner.UserName).ToList();
                delegate2 = others[(int)(Bucket(seed, 5) % (uint)others.Count)];
            }

            var isHistorical = payload?.IsHistorical ?? false;
            var outcome = payload?.Outcome ?? PawoOutcome.Unknown;
            var pawoStatus = payload?.PawoStatus ?? PawoStatus.Unknown;
            var dueDate = ParseDate(payload?.DueDate);
            var blocking = payload?.ExpectedFindings.Count(f => f.Severity == Severity.BLOCKING) ?? 0;

            var workflow = ResolveWorkflowStatus(isHistorical, outcome, pawoStatus, owner is object,
                                                 delegate2 is object);

            var isUrgent = !isHistorical
                           && (blocking > 0
                               || (dueDate is object
                                   && (dueDate.Value - DatasetEnd).TotalDays <= UrgentWindowDays));

            // Historical work orders record real effort; open ones report what has been
            // spent so far. Where the dataset gives nothing, derive a stable figure rather
            // than leaving a hole the dashboard would average over as zero.
            var hours = payload?.Assigned?.SpentManhours ?? 0;
            if (hours <= 0 && isHistorical) hours = 24 + (Bucket(seed, 6) % 210);

            plan[doc.DocId] = new Assignment(owner, delegate2, workflow, isUrgent, hours);
        }

        var assigned = plan.Values.Count(a => a.Owner is object);
        var delegated = plan.Values.Count(a => a.Delegate is object);

        Console.WriteLine($"  planned {plan.Count}: {assigned} assigned, "
                          + $"{plan.Count - assigned} unassigned, {delegated} also delegated");

        return plan;
    }

    /// <summary>
    /// Writes the ownership decided by <see cref="Plan"/> as edges. Runs after the documents,
    /// so both ends of every edge already exist.
    /// </summary>
    public static async Task LinkAsync(ConnectorContext ctx, IReadOnlyDictionary<string, Assignment> plan)
    {
        foreach (var (docId, assignment) in plan)
        {
            var workOrder = Node.FromKey(nameof(DocumentSchema.PreAnalysisWorkOrder), docId);

            if (assignment.Owner?.Node is object)
            {
                ctx.Graph.Link(workOrder, assignment.Owner.Node, Edges.AssignedTo, Edges.HasAssignment);
                ctx.CountEdge(Edges.AssignedTo);
            }

            if (assignment.Delegate?.Node is object)
            {
                ctx.Graph.Link(workOrder, assignment.Delegate.Node, Edges.DelegatedTo, Edges.HasDelegation);
                ctx.CountEdge(Edges.DelegatedTo);
            }
        }

        await ctx.Graph.CommitPendingAsync();
    }

    private static string ResolveWorkflowStatus(bool isHistorical, PawoOutcome outcome,
                                                PawoStatus status, bool hasOwner, bool hasDelegate)
    {
        if (isHistorical)
        {
            return outcome switch
            {
                PawoOutcome.VALIDATED => "VALIDATED",
                PawoOutcome.REJECTED => "REJECTED",
                PawoOutcome.DELEGATED => "DELEGATED",
                _ => status == PawoStatus.CANCELLED ? "REJECTED" : "VALIDATED",
            };
        }

        if (!hasOwner) return "UNASSIGNED";
        if (hasDelegate) return "DELEGATED";

        return status switch
        {
            PawoStatus.IN_PROGRESS => "IN_PROGRESS",
            PawoStatus.ON_HOLD => "ON_HOLD",
            _ => "ASSIGNED",
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    /// <summary>FNV-1a over the identifier. Stable across runs, processes and platforms.</summary>
    private static uint StableSeed(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    /// <summary>A distinct derived value per decision, so two choices never correlate.</summary>
    private static uint Bucket(uint seed, int salt)
    {
        unchecked
        {
            var hash = seed ^ (uint)(salt * 2654435761u);
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return hash;
        }
    }

    private static int Percent(uint seed, int salt) => (int)(Bucket(seed, salt) % 100);
}
