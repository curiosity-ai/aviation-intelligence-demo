// Reference template — definitions path: code/chat-ai-tools/<randomly-minted-uid>.cs
//
// The OPERATIONAL layer: questions about the user's own work, not about the corpus.
//
// These read exactly what the dashboard's summary endpoint reads, in the same order, with the
// same definitions — assignment is an edge, the inbox is what is not yet finished. That is
// deliberate and it is the whole point: the user is looking at those counters on the home page
// while they talk to the assistant, and an assistant that quietly counts differently is worse
// than one that cannot count at all.
//
// ITS UID IS RANDOM (UID128.New()), not a hand-shaped one. The recognisable ids belong to the
// BUILT-IN tools, which are re-seeded on every boot and have to be found again by the same id
// each time; a tool defined here is created once by an import and referred to by whatever id it
// was given, so there is nothing a readable id buys — and not every 22-char string round-trips
// through the UID parser.

[tools: Curiosity.ChatAITools.UID("<randomly minted uid>")]
[tools: Curiosity.ChatAITools.DisplayName("Case Tools")]
[tools: Curiosity.ChatAITools.Description("Reads an engineer's workload straight from the graph: the cases open on their desk (ListInbox), the ones marked urgent (ListUrgentActions), and the ones nobody owns yet (ListUnassigned). These are the same numbers the workspace home page shows, computed the same way, so the assistant and the dashboard never disagree. Use these for any 'what is on my desk', 'what is urgent', 'what should I pick up' question INSTEAD OF SEARCHING — a search ranks records by text, these read the assignment edges. Each result carries the case's id, title, status, category, due date, urgency and finding counts.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-clipboard-list")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// A UID returned by a CUSTOM tool is not consultable: `consult` only reads nodes attached to
// the conversation or turned up by the built-in `search`. So every row below carries enough to
// answer the workload question outright, and both the tool descriptions and this system prompt
// tell the model how to reach a case's full text. Returning a UID the model cannot then open
// would be a trap you have set for it.
[ToolSystemPrompt("""
                  Questions about workload — what is on my desk, what is urgent, what is
                  unassigned, what should I pick up next — are answered with the case tools,
                  never with a search: they read the assignment edges, and they return the same
                  numbers the home page shows.

                  A case listed by one of them is not consultable by its id alone. To read one
                  in full, search for its docId first and then consult what comes back.
                  """)]
public class CaseTools
{
    private const int DEFAULT_LIMIT = 25;

    [Tool("Lists the cases open on an engineer's desk — assigned to them and not yet validated. This is the 'My inbox' number on the workspace home page, computed the same way. Use it for 'what is on my desk', 'what am I working on', 'what is still open'. Returns each case's uid, docId, title, workflow status, category, due date, whether it is urgent, and how many findings it has. To read one in full, search for its docId and consult the result.")]
    public static string ListInbox(ToolScope scope,
        [Parameter("The engineer whose inbox to read — user name or full name. Leave empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        var userUID = ResolveEngineer(scope, engineer);
        if (userUID == default(UID128)) return "No such engineer.";

        var cases = Owned(scope, userUID).Where(c => !IsValidated(c)).ToList();

        scope.SetToolCallDisplayName($"Reading {cases.Count} open case(s)");

        return Respond(cases, limit);
    }

    [Tool("Lists the cases assigned to an engineer that are marked urgent — a blocking finding, or a due date inside the next two weeks. This is the 'Urgent' number on the home page. Use it for 'what is urgent', 'what is late', 'what needs me today'. Returns the same fields as ListInbox, blocking ones first.")]
    public static string ListUrgentActions(ToolScope scope,
        [Parameter("The engineer whose urgent work to read. Leave empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        var userUID = ResolveEngineer(scope, engineer);
        if (userUID == default(UID128)) return "No such engineer.";

        var cases = Owned(scope, userUID)
            .Where(c => c.GetBool(N.Case.IsUrgent))
            .OrderByDescending(c => c.GetInt(N.Case.BlockingFindingCount))
            .ToList();

        return Respond(cases, limit);
    }

    [Tool("Lists the cases nobody owns yet. This is the 'Unassigned' number on the home page. Use it for 'what is waiting for an owner', 'what could I pick up'. It is workspace-wide rather than per engineer, so it takes no engineer parameter.")]
    public static string ListUnassigned(ToolScope scope,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        // "Unowned" is "no user points at it" — not a flag beside the record that a second
        // write could leave stale.
        var assigned = new HashSet<UID128>(
            scope.Q().StartAt(N._User.Type).Out(N.Case.Type, E.HasAssignment).AsUIDEnumerable());

        var cases = scope.Q().StartAt(N.Case.Type).AsEnumerable()
            .Where(c => !assigned.Contains(c.UID))
            .OrderByDescending(c => c.GetInt(N.Case.BlockingFindingCount))
            .ToList();

        scope.SetToolCallDisplayName($"Reading {cases.Count} unassigned case(s)");

        return Respond(cases, limit);
    }

    // -----------------------------------------------------------------------------------
    // shared — ONE definition of "owned" and "finished", matching the dashboard exactly
    // -----------------------------------------------------------------------------------

    private static IEnumerable<Node> Owned(ToolScope scope, UID128 userUID)
        => scope.Q().StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsEnumerable();

    private static bool IsValidated(Node c) => c.GetString(N.Case.WorkflowStatus) == "Validated";

    private static UID128 ResolveEngineer(ToolScope scope, string engineer)
    {
        if (string.IsNullOrWhiteSpace(engineer)) return scope.CurrentUser.UID;

        foreach (var user in scope.Q().StartAt(N._User.Type).AsEnumerable())
        {
            if (string.Equals(user.GetString(N._User.UserName), engineer, StringComparison.OrdinalIgnoreCase))
                return user.UID;
        }

        return default(UID128);
    }

    /// <summary>Each row is self-sufficient — see the note at the top of this file.</summary>
    private static string Respond(List<Node> cases, int limit)
    {
        var rows = cases.Take(limit).Select(c => new
        {
            docId          = c.GetString(N.Case.DocId),
            title          = c.GetString(N.Case.Title),
            workflowStatus = c.GetString(N.Case.WorkflowStatus),
            category       = c.GetString(N.Case.CategoryL4),
            dueDate        = c.GetString(N.Case.DueDate),
            isUrgent       = c.GetBool(N.Case.IsUrgent),
            findings       = c.GetInt(N.Case.FindingCount),
            blocking       = c.GetInt(N.Case.BlockingFindingCount),
        }).ToArray();

        // Say so when the answer was cut short: a silent truncation reads as "that is all of
        // them".
        var note = cases.Count > limit ? $" (showing {limit} of {cases.Count})" : "";

        return new { count = cases.Count, note, cases = rows }.ToJson();
    }
}
