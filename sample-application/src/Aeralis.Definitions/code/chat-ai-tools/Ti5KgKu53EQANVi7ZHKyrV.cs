[tools: Curiosity.ChatAITools.UID("Ti5KgKu53EQANVi7ZHKyrV")]
[tools: Curiosity.ChatAITools.DisplayName("Pre-Analysis Tools")]
[tools: Curiosity.ChatAITools.Description("Reads an engineer's pre-analysis workload straight from the graph: the work orders open on their desk (ListInbox), the ones marked urgent (ListUrgentActions), and the ones nobody owns yet (ListUnassignedWorkOrders). These are the same four numbers the workspace home page shows, computed the same way, so the assistant and the dashboard never disagree. Use these for any 'what is on my desk', 'what is urgent', 'what should I pick up' question instead of searching - a search ranks documents by text, these read the assignment edges. Each result carries the work order's id, title, status, ATA chapter, due date, urgency and finding counts, plus the modification it analyses.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-clipboard-list")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The workload side of the pre-analysis assistant: what is on an engineer's desk, rather than
// what is inside a document.
//
// These three read exactly what `aeralis/dashboard/summary` reads, in the same order, with the
// same definitions - assignment is an edge, "historical" work orders are excluded, the inbox is
// what is not yet VALIDATED. That is deliberate and it is the whole point: the engineer is
// looking at those four counters on the home page while they talk to the assistant, and an
// assistant that quietly counts differently is worse than one that cannot count at all.
//
// A note on what these UIDs can and cannot do. `consult` only reads nodes that were attached to
// the conversation or that the built-in `search` tool has turned up
// (Mosaik.AI.ChatAI.IsNodeInChatContext) - a UID returned by a custom tool is NOT consultable on
// its own. So every row here carries enough to answer a workload question outright, and the
// descriptions tell the model to reach a work order's full text by searching for its docId
// first. Returning a UID the model then cannot open would be a trap.

[ToolSystemPrompt("""
                  Questions about workload - what is on my desk, what is urgent, what is unassigned, what
                  should I pick up next - are answered with the pre-analysis tools, never with a search:
                  they read the assignment edges, and they return the same numbers the home page shows.
                  A work order listed by one of them is not consultable by its id alone. To read one in
                  full, search for its docId first and then consult what comes back.
                  """)]
public class PreAnalysisTools
{
    /// <summary>How many work orders a single call will return before it starts truncating.</summary>
    private const int DEFAULT_LIMIT = 25;

    [Tool("Lists the pre-analysis work orders open on an engineer's desk - assigned to them, not yet validated, and not historical. This is the 'My inbox' number on the workspace home page, computed the same way. Use it for 'what is on my desk', 'what am I working on', 'what is still open'. Returns each work order's uid, docId, title, workflow and pre-analysis status, ATA chapter, programme, due date, whether it is urgent, how many findings it has and how many of those are blocking, and the modification it analyses. To read one in full, search for its docId and consult the result.")]
    public static string ListInbox(ToolScope scope,
          [Parameter("The engineer whose inbox to read - their user name or full name. Leave empty for the current user.", required: false)] string engineer = null,
          [Parameter("Maximum work orders to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        return ForEngineer(scope, engineer, limit, "inbox", (orders) =>
            orders.Where(o => !IsValidated(o)).ToList());
    }

    [Tool("Lists the pre-analysis work orders assigned to an engineer that are marked urgent - a blocking finding, or a due date inside the next two weeks. This is the 'Urgent actions' number on the workspace home page. Use it for 'what is urgent', 'what is late', 'what needs me today'. Returns the same fields as ListInbox, ordered with the blocking ones first. To read one in full, search for its docId and consult the result.")]
    public static string ListUrgentActions(ToolScope scope,
          [Parameter("The engineer whose urgent work to read - their user name or full name. Leave empty for the current user.", required: false)] string engineer = null,
          [Parameter("Maximum work orders to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        return ForEngineer(scope, engineer, limit, "urgent", (orders) =>
            orders.Where(o => o.IsUrgent)
                  .OrderByDescending(o => o.BlockingFindingCount)
                  .ToList());
    }

    [Tool("Lists the pre-analysis work orders nobody owns yet - no engineer assigned, and not historical. This is the 'Unassigned' number on the workspace home page. Use it for 'what is waiting for an owner', 'what could I pick up', 'what is nobody working on'. It is workspace-wide rather than per engineer, so it takes no engineer parameter. Returns the same fields as ListInbox, ordered with the blocking ones first. To read one in full, search for its docId and consult the result.")]
    public static string ListUnassignedWorkOrders(ToolScope scope,
          [Parameter("Maximum work orders to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        //Assignment is an edge, so "unowned" is "no user points at it" — not a flag beside it that
        //a second write could leave stale.
        var assigned = new HashSet<UID128>();

        foreach (var uid in scope.Q().StartAt(N._User.Type).Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment).AsUIDEnumerable())
        {
            assigned.Add(uid);
        }

        var orders = new List<WorkOrder>();

        foreach (var node in scope.Q().StartAt(N.PreAnalysisWorkOrder.Type).AsEnumerable())
        {
            if (assigned.Contains(node.UID)) continue;

            var order = Read(node);
            if (!order.IsHistorical) orders.Add(order);
        }

        var ordered = orders.OrderByDescending(o => o.BlockingFindingCount).ToList();

        scope.SetToolCallDisplayName($"Reading {ordered.Count} unassigned work order(s)");

        return Respond(ordered, limit, "unassigned", null);
    }

    // ---------------------------------------------------------------------------------------
    // shared
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the engineer, walks their assignment edges, applies <paramref name="filter"/> and
    /// renders the answer.
    ///
    /// Historical work orders are dropped before the filter ever sees them, exactly as the
    /// dashboard does: they are the corpus's closed past, and an engineer asking what is on their
    /// desk is not asking about 2019.
    /// </summary>
    private static string ForEngineer(ToolScope scope, string engineer, int limit, string kind,
                                      Func<List<WorkOrder>, List<WorkOrder>> filter)
    {
        if (!TryResolveEngineer(scope, engineer, out var userUID, out var userName, out var error))
        {
            return error;
        }

        var mine = new List<WorkOrder>();

        foreach (var node in scope.Q().StartAt(userUID).Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment).AsEnumerable())
        {
            var order = Read(node);
            if (!order.IsHistorical) mine.Add(order);
        }

        var selected = filter(mine);

        scope.SetToolCallDisplayName($"Reading {userName}'s {kind} ({selected.Count})");

        return Respond(selected, limit, kind, userName);
    }

    /// <summary>
    /// Which engineer the question is about: the one named, or the caller.
    ///
    /// A named engineer is matched on user name first and full name second, case-insensitively,
    /// because a person asking about a colleague types the name they see on the screen. When
    /// nothing matches — and when the caller themselves has no user node, which is the normal case
    /// for an administrator poking at the demo — the error lists who there *is* to ask about,
    /// rather than leaving the model to guess a name and fail again.
    /// </summary>
    private static bool TryResolveEngineer(ToolScope scope, string engineer, out UID128 userUID, out string userName, out string error)
    {
        userUID = default(UID128);
        userName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(engineer))
        {
            if (scope.CurrentUser.IsNotNull() && scope.Graph.TryGet(scope.CurrentUser, out var current) && current.Type == N._User.Type)
            {
                userUID = scope.CurrentUser;
                userName = NameOf(current);
                return true;
            }

            error = new
            {
                ok = false,
                error = "There is no engineer to read this for. Name one with the 'engineer' parameter.",
                engineers = Engineers(scope),
            }.ToJson();

            return false;
        }

        var wanted = engineer.Trim();

        foreach (var node in scope.Q().StartAt(N._User.Type).AsEnumerable())
        {
            var name = node.GetString(N._User.UserName) ?? "";
            var full = node.GetString(N._User.FullName) ?? "";

            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)
             || string.Equals(full, wanted, StringComparison.OrdinalIgnoreCase))
            {
                userUID = node.UID;
                userName = NameOf(node);
                return true;
            }
        }

        error = new
        {
            ok = false,
            error = $"There is no engineer called '{engineer}' in this workspace.",
            engineers = Engineers(scope),
        }.ToJson();

        return false;
    }

    private static string[] Engineers(ToolScope scope)
    {
        var names = new List<string>();

        foreach (var node in scope.Q().StartAt(N._User.Type).AsEnumerable())
        {
            var name = NameOf(node);
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            if (names.Count >= 50) break;
        }

        return names.ToArray();
    }

    private static string NameOf(ReadOnlyNode node)
    {
        var full = node.GetString(N._User.FullName);

        return string.IsNullOrWhiteSpace(full) ? node.GetString(N._User.UserName) : full;
    }

    /// <summary>
    /// The answer. `total` is what was found, `returned` is what fits — a model told only the
    /// second would report "you have 25 urgent work orders" to somebody who has 60.
    /// </summary>
    private static string Respond(List<WorkOrder> orders, int limit, string kind, string engineer)
    {
        var take = limit > 0 ? limit : DEFAULT_LIMIT;

        return new
        {
            ok = true,
            kind = kind,
            engineer = engineer,
            total = orders.Count,
            returned = Math.Min(take, orders.Count),
            note = orders.Count > take
                ? $"Showing the first {take} of {orders.Count}. Ask for more with a higher limit."
                : null,
            workOrders = orders.Take(take).Select(o => new
            {
                uid = o.Uid,
                docId = o.DocId,
                title = o.Title,
                workflowStatus = o.WorkflowStatus,
                pawoStatus = o.PawoStatus,
                recommendation = o.Recommendation,
                ata = o.Ata6d,
                programme = o.ProgramLetter,
                dueDate = o.DueDate,
                isUrgent = o.IsUrgent,
                findings = o.FindingCount,
                blockingFindings = o.BlockingFindingCount,
                analyses = o.SubjectMod,
                raisedFrom = o.SourceIo,
                abstractText = o.Abstract,
            }).ToArray(),
        }.ToJson();
    }

    /// <summary>One work order, read once. The rest of the file sorts and filters this, never the node.</summary>
    private static WorkOrder Read(ReadOnlyNode node)
    {
        return new WorkOrder
        {
            Uid = node.UID.ToString(),
            DocId = node.GetString(N.PreAnalysisWorkOrder.DocId),
            Title = node.GetString(N.PreAnalysisWorkOrder.Title),
            Abstract = node.GetString(N.PreAnalysisWorkOrder.Abstract),
            WorkflowStatus = node.GetString(N.PreAnalysisWorkOrder.WorkflowStatus),
            PawoStatus = node.GetString(N.PreAnalysisWorkOrder.PawoStatus),
            Recommendation = node.GetString(N.PreAnalysisWorkOrder.Recommendation),
            Ata6d = node.GetString(N.PreAnalysisWorkOrder.Ata6d),
            ProgramLetter = node.GetString(N.PreAnalysisWorkOrder.ProgramLetter),
            DueDate = node.GetString(N.PreAnalysisWorkOrder.DueDate),
            SubjectMod = node.GetString(N.PreAnalysisWorkOrder.SubjectMod),
            SourceIo = node.GetString(N.PreAnalysisWorkOrder.SourceIo),
            IsUrgent = node.GetBool(N.PreAnalysisWorkOrder.IsUrgent),
            IsHistorical = node.GetBool(N.PreAnalysisWorkOrder.IsHistorical),
            FindingCount = node.GetInt(N.PreAnalysisWorkOrder.FindingCount),
            BlockingFindingCount = node.GetInt(N.PreAnalysisWorkOrder.BlockingFindingCount),
        };
    }

    /// <summary>
    /// A work order is in the inbox until it comes out validated — the same rule the home page's
    /// "My inbox" tile uses, spelled once here so the two cannot drift.
    /// </summary>
    private static bool IsValidated(WorkOrder order)
        => string.Equals(order.WorkflowStatus ?? "", "VALIDATED", StringComparison.Ordinal);

    private class WorkOrder
    {
        public string Uid { get; set; }
        public string DocId { get; set; }
        public string Title { get; set; }
        public string Abstract { get; set; }
        public string WorkflowStatus { get; set; }
        public string PawoStatus { get; set; }
        public string Recommendation { get; set; }
        public string Ata6d { get; set; }
        public string ProgramLetter { get; set; }
        public string DueDate { get; set; }
        public string SubjectMod { get; set; }
        public string SourceIo { get; set; }
        public bool IsUrgent { get; set; }
        public bool IsHistorical { get; set; }
        public int FindingCount { get; set; }
        public int BlockingFindingCount { get; set; }
    }
}

return new PreAnalysisTools();
