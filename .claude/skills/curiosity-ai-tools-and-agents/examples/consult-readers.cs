// Reference template — definitions path: code/chat-ai-tools/<consult-tool-uid>.cs
//
// The consult tool is the one BUILT-IN an admin can edit, and what is editable is ONLY the
// per-node-type readers: the tool surface — the single `consult` entry point, the UID parsing,
// the access and chat-context checks and the switch on the node's type — is generated around
// these methods at compile time. So there is NO class and NO [Tool] method here, only methods
// following the fixed schema
//
//     object Consult<NodeType>(ToolScope scope, UID128 uid) { ... }
//
// A node type with no reader is reported to the model as "NOT CONSULTABLE", which is why every
// type a user can attach has one: a user attaches whatever they are looking at, and a type the
// assistant cannot read is a question it has to answer by guessing from the title.
//
// HELPER METHODS MUST NOT START WITH "Consult" — that prefix is what marks a reader, and a
// helper carrying it fails the schema check at save time.

[tools: Curiosity.ChatAITools.UID("ConsuLt111111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Consult")]
[tools: Curiosity.ChatAITools.Description("Reads the full content of one item attached to this conversation, by its UID.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-book-open-cover")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// ---------------------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------------------

/// <summary>Every field named, skipping the ones this node has nothing for.</summary>
Dictionary<string, string> Fields(ToolScope scope, UID128 uid, string[] names)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);

    if (!scope.Graph.TryGet(uid, out var node)) return values;

    foreach (var name in names)
    {
        var value = node[name]?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(value)) values[name] = value;
    }

    return values;
}

/// <summary>
/// The files hanging off this node — the published artefacts the connector uploaded — and
/// THEIR UIDS ARE CONSULTABLE IN TURN.
///
/// This is the block people leave out. Where the published artefact is what a human actually
/// reads, a model that only ever sees fields will only ever reason about fields, and the detail
/// that decides the answer usually lives in the paragraph that was rendered.
/// </summary>
object[] Attachments(ToolScope scope, UID128 uid)
{
    var files = new List<object>();

    foreach (var fileUID in scope.Q().StartAt(uid).Out(nodeType: N._FileEntry.Type, edgeType: E._HasAttachment).AsUIDEnumerable())
    {
        if (!scope.Graph.TryGet(fileUID, out var file)) continue;

        files.Add(new
        {
            uid         = fileUID.ToString(),
            name        = file.GetString(N._FileEntry.OriginalName),
            contentType = file.GetString(N._FileEntry.ContentType),
            consultable = true,
        });
    }

    return files.ToArray();
}

/// <summary>
/// The reference data this record points at, grouped by what kind of thing it is.
///
/// An impact question turns on exactly these: two records sharing a part, a category or a
/// system is the ENTITY half of the retrieval story, and it is the half a model cannot infer
/// from prose. CAPPED PER TYPE, because the answer is about the overlap, not the inventory.
/// </summary>
Dictionary<string, string[]> Related(ToolScope scope, UID128 uid)
{
    var related = new Dictionary<string, string[]>(StringComparer.Ordinal);

    void Add(string label, string nodeType, string keyField)
    {
        var keys = new List<string>();

        foreach (var neighbour in scope.Q().StartAt(uid).Out(nodeType: nodeType).AsEnumerable())
        {
            var key = neighbour.GetString(keyField);
            if (!string.IsNullOrWhiteSpace(key) && !keys.Contains(key)) keys.Add(key);
            if (keys.Count >= 25) break;
        }

        if (keys.Count > 0) related[label] = keys.ToArray();
    }

    Add("categories",  N.Category.Type,   N.Category.Code);
    Add("partNumbers", N.PartNumber.Type, N.PartNumber.Pn);
    Add("status",      N.Status.Type,     N.Status.Value);

    return related;
}

/// <summary>One record, however it is typed: its fields, what it references, and its files.</summary>
object Record(ToolScope scope, UID128 uid, string kind, string titleField, string[] names)
{
    if (!scope.Graph.TryGet(uid, out var node)) return new { ok = false, error = kind + " not found" };

    var title = node.GetString(titleField);

    // What the user watching the conversation sees the tool doing.
    scope.SetToolCallDisplayName(string.IsNullOrWhiteSpace(title) ? "Reading a " + kind : "Reading '" + title + "'");

    return new
    {
        ok          = true,
        uid         = uid.ToString(),
        type        = node.Type,
        kind        = kind,
        fields      = Fields(scope, uid, names),
        related     = Related(scope, uid),
        attachments = Attachments(scope, uid),
    };
}

// ---------------------------------------------------------------------------------------
// the readers — ONE PER NODE TYPE A USER CAN ATTACH, revision types included
// ---------------------------------------------------------------------------------------

object ConsultCase(ToolScope scope, UID128 uid) => Record(scope, uid, "case", N.Case.Title, new[]
{
    N.Case.DocId, N.Case.Title, N.Case.Abstract, N.Case.Partition, N.Case.Status,
    N.Case.CategoryL2, N.Case.CategoryL4, N.Case.Keywords,
    N.Case.ObjectiveText, N.Case.MethodText, N.Case.FindingsText,
    N.Case.WorkflowStatus, N.Case.DueDate, N.Case.IsUrgent,
    N.Case.FindingCount, N.Case.BlockingFindingCount,
});

object ConsultChange(ToolScope scope, UID128 uid) => Record(scope, uid, "change", N.Change.Title, new[]
{
    N.Change.DocId, N.Change.Title, N.Change.Abstract, N.Change.Partition, N.Change.Status,
    N.Change.CategoryL2, N.Change.CategoryL4, N.Change.Keywords,
    N.Change.ReasonText, N.Change.DescriptionText, N.Change.EffectsText,
    N.Change.RiskLevel,
});

object ConsultManual(ToolScope scope, UID128 uid) => Record(scope, uid, "manual entry", N.Manual.Title, new[]
{
    N.Manual.DocId, N.Manual.Title, N.Manual.Abstract, N.Manual.Partition, N.Manual.Status,
    N.Manual.CategoryL2, N.Manual.CategoryL4, N.Manual.PurposeText, N.Manual.ProcedureText,
});

// The revision types matter because the COMPARISON screen attaches a revision and the current
// record side by side and asks what changed: without a reader the revision is "not
// consultable" and the model can only read half the pair.
object ConsultChangeRevision(ToolScope scope, UID128 uid) => Record(scope, uid, "change revision", N.ChangeRevision.Title, new[]
{
    N.ChangeRevision.RevisionId, N.ChangeRevision.DocId, N.ChangeRevision.RevisionLetter,
    N.ChangeRevision.Supersedes, N.ChangeRevision.RevisionReason, N.ChangeRevision.RaisedByFinding,
    N.ChangeRevision.IsLatest, N.ChangeRevision.Title, N.ChangeRevision.Abstract,
    N.ChangeRevision.ReasonText, N.ChangeRevision.DescriptionText, N.ChangeRevision.EffectsText,
});
