# Workspace endpoints

The server side of the front-end, in the layout a workspace **definitions import**
reads back: `code/endpoints/<path>.cs`, one file per endpoint, each beginning with
its `[endpoint: Curiosity.Endpoints.*]` attribute header, a blank line, then the code.

Verified against `Mosaik/Graph/src/Schema/Nodes/Internal/_CodeEndpoint.cs`
(`ExportAsCode` / `FromExportedCode`) and `GraphDefinitionsExporter.cs`
(`ZipPaths.Endpoints = "code/endpoints"`).

## The contract

Two rules shape every file here.

**Node types and field names come from the generated `N` / `E`.** The workspace
prepends both to each script before compiling it
(`CodeManager.AppendSchemaHelperClassesAndImports`), so it is
`N.PreAnalysisWorkOrder.Type`, `N.AtaChapter.Code`, `E.InAta2d` — and
`N._User.UserName`, `E._HasAttachment` for the types the graph owns. A literal
`"Ata2d"` that no longer matches the schema returns nothing, forever, in silence;
`N.Modification.Ata2d` fails the import instead.

The consequence is an ordering one: **ingest before importing the endpoints.**
`N` is generated from the graph's current schema, so an endpoint naming a node
type the connector has not created yet will not compile.

**Everything crossing the wire is a `UID128`.** Requests carry `PawoUID`,
`TargetUID`, `RuleUID`, `UserUID`; responses carry `Uid`, `AtaUID`, `InboxUIDs`,
… . The DTOs are declared in `aeralis/_lib/aeralis-helpers.cs` and again,
identically, in `src/Aeralis.FrontEnd/src/Api/Dto.cs` — the endpoints return
those classes rather than anonymous objects, both ends use Newtonsoft's default
resolver, so the two declarations *are* the wire format. Change one, change the
other.

`UID128` rather than `string` on purpose: here it is a struct with a
`[JsonConverter]` that reads and writes a plain JSON string, and in the front-end
a `UID128` *is* a JS string at run time. Identical bytes on the wire, and a
compile error when a UID reaches the wrong parameter.

A UID also means the endpoint stops guessing. `confirm-impact` used to try each
of the eight impactable node types in turn to work out what a document
identifier referred to; now the UID says. And a response of UIDs is directly
renderable — the front-end hands it to `NeighborsLinks` or to a `SearchArea`
with `WithTargetUIDs` and each row is drawn by that node type's own renderer, so
no title is ever copied through the wire to go stale.

## Importing

Zip the `code/` folder and import it through **Settings → Import definitions**
(`POST /api/graph/import-definitions`, system-admin only).

```bash
cd src/Aeralis.Endpoints && zip -r ../../aeralis-endpoints.zip code
```

## The set

| Path | Read-only | Purpose |
| --- | --- | --- |
| `aeralis/_lib/aeralis-helpers` | yes | Shared types and constants. Fails closed — it is a library, not an API. |
| `aeralis/dashboard/users` | yes | Engineers for the perspective dropdown |
| `aeralis/dashboard/summary` | yes | The four metric tiles |
| `aeralis/dashboard/performance` | yes | Weekly throughput, hours per order, outcome split |
| `aeralis/pawo/confirmed-impacts` | yes | Initial state for every Confirm button |
| `aeralis/pawo/confirm-impact` | **no** | Writes `ConfirmedImpact` |
| `aeralis/pawo/remove-impact` | **no** | Removes it |
| `aeralis/pawo/impact-matrix` | yes | ATA 2D × document type counts |
| `aeralis/pawo/draft-report` | **no** | AI draft (stub) |
| `aeralis/similarity/affected-documents` | yes | Text + entity + ATA scoring |
| `aeralis/similarity/previous-analysis` | yes | Similar modifications → their work orders |
| `aeralis/similarity/suggested-ata` | yes | Document scores summed onto ATA chapters |
| `aeralis/rules/applicable` | yes | Rules binding this work order |
| `aeralis/rules/add-to-pawo` | **no** | Proposes a rule |

`AccessMode` is `AllUsers` throughout except the library, which is `AdminOnly`.
The four writes are deliberately **not** marked `ReadOnly`: the runtime hands a
read-only endpoint a `ReadOnlyGraph`, and the write would fail at run time.

The two similarity endpoints return the engine's own `SimilarityResult`
unchanged. That is the type `SearchArea.WithSimilarityEngine` consumes on the
other side; a hand-built `{ source, hits }` projection of it deserialises into an
empty result.

## Relationships, not properties

The queries traverse rather than re-look-up, because that is where the facts
actually live:

- the modification a work order analyses — `.Out(N.Modification.Type, E.PreAnalysisOf)`,
  not its `SubjectMod` string;
- who owns a work order — `.Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment)`
  from the user. There is no `AssignedToUserName` property to compare against;
  the connector stores ownership only as an edge, so the dashboard's "mine" list
  and the count above it cannot disagree;
- an engineer's site, department, role and ATA specialities — edges from `_User`,
  which is why those fields are no longer empty strings;
- the rules binding a work order — `.Out(N.AtaChapter.Type, E.InAta2d).Out(N.Rule.Type, E.HasRule)`,
  not a lookup by ATA code.

## Why `ConfirmedImpact` is not in the connector

Everything else the graph knows is derived from the dataset. A confirmed impact is a
human judgement made in the UI, so it is the one impact edge only these endpoints
create — which is also what makes it meaningful to compare against the ground-truth
`ExpectedFinding` nodes the connector does write.

---

## The AI layer

Three pieces, and one of them cannot be exported.

**`code/chat-ai-tools/ConsuLt111111111111111.cs` — the consult readers.** The consult tool is
the one built-in an admin can edit, and what is editable is *only* the per-node-type readers:
the tool surface (the `consult` entry point, the UID parsing, the access and chat-context checks
and the switch on node type) is generated around them at compile time by
`_ChatAITool.ConsultTool.BuildToolSource`. So the file declares no class and no `[Tool]` method
— only methods following the fixed schema

```csharp
object Consult<NodeType>(ToolScope scope, UID128 uid) { ... }
```

There is one per document type and one per issue type, because a node type with no reader is
reported to the model as "not consultable" — and the comparison screen attaches an *issue*.
Every reader returns the node's `fields`, a `related` block (ATA, parts, FINs, drawings, zones,
software, aircraft) and an **`attachments`** array of the published PDFs, whose uids are
consultable in turn. Helper methods must not start with `Consult`; that prefix is what marks a
reader, and a helper carrying it fails the schema check at save time.

**`code/chat-ai-tools/Ti5KgKu53EQANVi7ZHKyrV.cs` — the workload tools.** `ListInbox`,
`ListUrgentActions` and `ListUnassignedWorkOrders`, in one `PreAnalysisTools` class. They read
exactly what `aeralis/dashboard/summary` reads, with the same definitions — assignment is an
edge, historical work orders are excluded, the inbox is what is not yet `VALIDATED` — because the
engineer is looking at those counters on the home page while they talk to the assistant, and an
assistant that quietly counts differently is worse than one that cannot count at all.

**Its own UID is random**, from `UID128.New()`, not a hand-shaped one. The recognisable
`AiTooLsEarCh1111111111`-style ids belong to the *built-in* tools, which are re-seeded on every
boot and have to be found again by the same id each time; a tool defined here is created once by
an import and referred to by whatever id it was given, so there is nothing for a readable id to
buy. Mint one the same way for any tool added later — never by scrambling a prefix.

Note what a UID from these can*not* do: `consult` only reads nodes attached to the conversation
or turned up by the built-in `search` (`ChatAI.IsNodeInChatContext`), so an id returned by a
custom tool is not consultable on its own. Each row therefore carries enough to answer the
workload question outright, and both the tool descriptions and its `[ToolSystemPrompt]` tell the
model to reach a work order's full text by searching for its `docId` first.

**`code/agents/*.cs` — the two agents.** The pre-analysis agent decides whether a change impacts
a set of candidates; the comparison agent reports what changed between two issues. Both carry
`[agent: Curiosity.Agents.Tool("ConsuLt111111111111111")]`, which is what puts the readers in
their hands.

**`aeralis-pre-analysis-assistant.md` — the assistant.** Enable **Pre-Analysis Tools** on it
alongside consult and search, or the workload questions fall through to a text search.
Assistant templates are *not* covered by
`GraphDefinitionsExporter`, so this one is a markdown file to paste into
**Settings → AI Assistant → System prompt** by hand. The front-end names its UID directly in
`src/Aeralis.FrontEnd/src/Components/AiAssist.cs`; change one, change the other.
