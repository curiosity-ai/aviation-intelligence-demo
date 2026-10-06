---
name: curiosity-data-connector
description: How to build a Curiosity.Library data connector — handler-per-input-type structure, the [Node]/[Key]/[Property]/[Timestamp] schema, linking by key, HashCache and commit ordering, the AddOrUpdate whole-content trap, minting shared metadata nodes, creating users, deterministic derived state, uploading attachments, and dry-run driven development. Use when writing or changing anything in the connector project.
---

# The data connector — `Curiosity.Library`

The connector is the only thing that decides what the graph contains. Everything else in the
application is written against its schema, so it is built first, and finished before a
renderer or an endpoint is written against a node type.

Read `curiosity-graph-modelling` first: *what* to model is decided there, *how* to write it
is here.

---

## 1. Project shape

```
src/<App>.Connector/
├── <App>.Connector.csproj      net10.0 exe; Curiosity.Library (+ CsvHelper, logging)
├── Program.cs                  args, Graph.Connect, schema sync, run order, teardown
├── ConnectorContext.cs         graph + hash cache + paths + counters + mint set
├── Edges.cs                    every edge name as a const, in both directions
├── SchemaEmitter.cs            emits the shared N/E constants for the other projects
└── Ingest/                     one handler per input type
```

**One handler per input type**, as a `static class` whose **nested types** are its own DTOs
and its own graph-schema classes, so a type never leaks between handlers:

```csharp
internal static class PartNumbersIngest
{
    public const int Version = 1;                       // bump to invalidate the cache

    private sealed class Row { … }                      // the CSV/JSON DTO
    [Node] internal sealed class PartNumber { … }        // the graph node

    public static async Task RunAsync(ConnectorContext ctx) { … }
}
```

The input schema does **not** have to match the node schema. Split one row into a parent plus
children, or merge several sources into one node, wherever the graph reads better that way.

---

## 1.1 Reading the source — which package for which format

Use boring, well-maintained packages. The connector's job is mapping, not parsing, and a
bespoke reader is a bug factory in the one place where a bug is silent.

| Source format | Package | Notes |
| --- | --- | --- |
| JSON | **`System.Text.Json`** (in-box) | Default choice. Deserialise into a DTO with `JsonSerializerOptions { PropertyNamingPolicy, UnmappedMemberHandling = Disallow }` — **fail on an unmapped member**, so a source field you never mapped is a build-time surprise rather than data silently dropped |
| JSON, awkward | `Newtonsoft.Json` | Only where you need its converter model or `JObject` traversal. Note the endpoint/front-end wire uses Newtonsoft's default resolver — that is a separate concern from ingest |
| CSV / TSV | **`CsvHelper`** | Stream it (`GetRecords<T>`), always with `CultureInfo.InvariantCulture` — the corpus writes ISO dates and `.` decimals regardless of host locale. Set `MissingFieldFound = null`, `TrimOptions.Trim` |
| Parquet | **`Parquet.Net`** | The right choice for large columnar exports. Read row groups rather than materialising the file |
| Excel | **`ClosedXML`** (xlsx) or `ExcelDataReader` (xls + xlsx, streaming) | Prefer asking for a CSV/Parquet export. Where you cannot: `ClosedXML` for convenience, `ExcelDataReader` for size |
| XML | `System.Xml.Linq` (in-box) | `XDocument` for anything hand-shaped; `XmlSerializer` where there is a real schema |
| YAML | `YamlDotNet` | Config-shaped sources |
| SQL databases | `Microsoft.Data.SqlClient` / `Npgsql` / `MySqlConnector` + **`Dapper`** | Dapper for row→DTO mapping. An ORM is overkill for read-only ingest |
| Compressed archives | `System.IO.Compression` (in-box) | Stream entries; do not extract to disk first |
| PDFs / Office documents | let the **workspace** extract them | Upload the file and let file indexing do the extraction (`curiosity-workspace-configuration` §3). Do not re-implement text extraction in the connector |
| HTTP APIs | `System.Net.Http` + `System.Text.Json` | Paginate; respect the source's rate limits; make the page loop resumable |

Rules that apply whichever you pick:

- **Stream, do not load.** Every reader above has a streaming form. A connector that
  materialises the corpus is one that stops working at the customer's real volume.
- **Parse into a DTO, never into `dynamic` or a dictionary.** The DTO is where a source-schema
  change becomes a compile error.
- **Invariant culture everywhere**, for dates and numbers alike.
- **Keep the DTO nested inside its handler** (§1) so a shape from one source cannot leak into
  another's mapping.
- **Hash the DTO** for the cache (§3), which is another reason it must not carry anything
  derived at read time.

---

## 2. The node schema classes

```csharp
[Node]
internal sealed class Document
{
    [Key]       public string DocId    { get; set; } = "";
    [Timestamp] public DateTimeOffset Released { get; set; }
    [Property]  public string Title    { get; set; } = "";
    [Property]  public double WeightKg { get; set; }
}
```

- `[Key]` — exactly one, stable, derivable without a lookup.
- `[Timestamp]` — one; it is what sorts result lists and what a card's footer shows. Prefer
  the publication date, falling back to creation.
- `[Property]` — everything else. **Flatten nested source sections into one property each**
  (`ReasonText`, `DescriptionText`, …) rather than concatenating them, so the search index
  and the similarity engine see them as separate fields and can be weighted separately.

Register every class at startup with `CreateNodeSchemaAsync<T>(overwrite: true)`, and the
edge catalogue with `CreateEdgeSchemaAsync(typeof(Edges))`.

---

## 2.1 Field names the workspace refuses

A node schema is refused at `CreateNodeSchemaAsync` - not at build time, not in `--dry-run` - when a
field is named like a member of the workspace's own node API: `UID`, `Type`, `Key`, `Timestamp`,
`Edges`, `Summary`, `Emit`, `Pinned`, the `Get*`/`Set*` accessors, and C# keywords
(`SchemaNameValidation` in the product). The error is `Restricted field name used 'Summary'`.
Verified on 26.10: an `Article.Summary` property passed every dry run and failed the first real
run. Name it `Excerpt`, `Abstract`, `Lede`.

## 3. The handler contract

Every `RunAsync` is the same four beats, in this order:

```csharp
public static async Task RunAsync(ConnectorContext ctx)
{
    // 1. LOAD
    var rows = ctx.ReadCsv<Row>("part_numbers.csv");

    // 2. LOOP — the hash cache decides whether the record needs work at all
    foreach (var row in rows)
    {
        ctx.Cache.IfNotCached(Version, row, r =>
        {
            var node = ctx.Graph.AddOrUpdate(Map(r));
            ctx.Graph.Link(node, Node.FromKey(nameof(Category), r.CategoryCode),
                           Edges.InCategory, Edges.HasMember);
        });
    }

    // 3. COMMIT the graph
    await ctx.Graph.CommitPendingAsync();

    // 4. COMMIT the cache — only after the graph commit returned
    await ctx.Cache.CommitPendingHashesAsync();
}
```

Non-negotiable:

- **Commit ordering.** Cache hashes are committed *after* a successful graph commit, never
  before, and never if the graph commit threw. A hash recorded for work that did not land
  makes that record invisible **forever**.
- **Hash the source DTO**, never a shape containing `DateTime.UtcNow`, a computed UID or a
  counter — every run would miss and re-ingest everything.
- **`Version` per handler.** Bump the handler's own const when its mapping changes shape: a
  new `[Property]`, an extra `Link`, a changed `[Key]`. Handlers version independently.
- **In a dry run, do not commit hashes at all.** Nothing reached the graph, so recording them
  would make the next real run skip records it never ingested.

---

## 4. `AddOrUpdate` writes the node's *whole* content

The most expensive bug this project had. **Read this twice.**

The mapper drops `null`s and nothing else. A `string` left at `""`, a `bool` left `false`, an
`int` or `double` left `0`, a `DateTimeOffset` left at `default` are all sent — and all
overwrite what is there. So a second pass calling `AddOrUpdate` with a partial object —
only the two or three fields it computed — **blanks every other property the first pass
wrote**. In the reference implementation, an assignment pass did exactly this and cleared the
title, the abstract and every section of text from every work order.

Therefore:

- **One `AddOrUpdate` per node per run, carrying everything.**
- If a later pass genuinely must touch an existing node, use the **partial-update overload**,
  which sends only the fields named:

  ```csharp
  ctx.Graph.AddOrUpdate(Node.FromKey(nameof(Foo), key), op => op.Set(nameof(Foo.Bar), bar));
  ```

- **Better: compute the value before the node is written and pass it in.** Split a derived-
  state pass into a *plan* phase that runs before the main ingest and writes nothing, and a
  *link* phase that runs after and writes only edges. See §7.
- **Relationships never need either.** A `Link` is additive and cannot clobber a property.

`TryAdd` vs `AddOrUpdate`: `TryAdd` for immutable reference rows another handler may already
have minted by key; `AddOrUpdate` for anything carrying properties from this handler's source.

---

## 5. Linking

```csharp
ctx.Graph.Link(fromNode, Node.FromKey(nameof(TargetType), targetKey), forward, reverse);
```

- **Never fetch a node just to link to it.** The edge stores by key and activates when both
  ends exist — which is also what lets a handler link *forward* to a type ingested later in
  the run.
- **Every edge is a pair**, declared as two `const string`s in one `Edges.cs`, and registered
  from that class. Name them from each end's point of view (`InCategory` / `HasMember`).
- **Never reuse one edge name for two different relationships.** Traversing it then returns a
  mixture and no caller can ask a precise question. One vocabulary, one edge pair.
- Where an edge pair is derived from a name (`Has{Kind}` / `{Kind}Of`), look the constants up
  and **throw at startup if either is missing** — otherwise the run writes thousands of links
  over an edge type the schema never registered.

### Mint once, link always

Metadata dimensions are a few dozen distinct values shared by tens of thousands of rows.
Calling `TryAdd` per row makes the pending buffer do work proportional to *rows × values* and
turns a three-second run into a multi-minute one. Keep a `HashSet<string>` of
`{type}\0{key}` on the context, `TryAdd` on first sight only, and **link by key every time**:

```csharp
if (ctx.Mint(nameof(Status), value)) ctx.Graph.TryAdd(new Status { Value = value });
ctx.LinkKey(node, nameof(Status), value, Edges.HasDocumentStatus, Edges.DocumentStatusOf);
```

---

## 6. Run order

Order matters for readability and for counters, not for correctness — `Node.FromKey` means a
link never blocks on its target existing. Keep it dependency-shaped anyway:

```
 1. derived reference nodes (programmes, types, models — no source file)
 2. hierarchical catalogues (parent/child chains)
 3. tabular master data, one handler per CSV
 4. users
 5. the derived-state PLAN (writes nothing)
 6. documents  — one handler with a switch on record type, per-type work in nested classes
 7. revisions  — one node per revision of each record
 8. the derived-state LINK pass (edges only)
 9. attachments — render + upload; by far the slowest, and skippable
```

Print per-step node/edge deltas and elapsed time. It is the cheapest possible regression test
for a mapping change.

---

## 7. Users, ownership and derived workflow state

**Users.** One `CreateUserAsync(userName, email, firstName, lastName)` per source row. The
user node type is owned by the graph, so there is nowhere to put properties: everything else
about a user is an **edge** to the same nodes documents are classified by — site, department,
role (as a classification), specialities (as category nodes).

**Ownership is an edge and only an edge.** No `AssignedToUserName` property. A name beside the
edge is a second copy of one fact and the two drift the moment either is rewritten. Every
"whose is this?" question becomes a traversal — which is what guarantees a dashboard count and
the list behind it agree.

**Derived state must be deterministic.** Where the connector computes assignment, status,
urgency, or synthetic history, seed every choice from a stable hash of the record's own
identifier — never `Random()` without a seed, never wall-clock time. Same input ⇒ same graph,
every run.

**Split the pass in two around the document ingest:**

```csharp
var plan = AssignmentPass.Plan(ctx, users);          // before: reads, writes nothing
await DocumentsIngest.RunAsync(ctx, plan);           // folds the plan's fields into the
                                                     // single AddOrUpdate that creates the node
await AssignmentPass.LinkAsync(ctx, plan);           // after: writes the edges only
```

Make the plan's fingerprint part of the document's cache key, so re-tuning the model rewrites
the records it moved even though nothing in the source changed.

**Populate history, not just the present.** A dashboard cannot draw a throughput chart or a
last-month outcome split from the handful of currently-open items. The corpus needs closed
units of work too; the connector treats them identically and only their status and completion
date differ.

---

## 8. Attachments

Where a record has a published artefact, render it, upload it, and hang it off the node:

```csharp
var file = await ctx.Graph.UploadFileAsync(stream, fileName, SourceName,
                                           checkHash: true, silent: true);
ctx.Graph.Link(file, Node.FromKey(nodeType, docId), Edges.AttachmentOf, Edges.HasAttachment);
```

- Use the workspace's **own** attachment edge pair (the underscore-prefixed one). The product
  already renders it as the attachment list and already has a viewer.
- `UploadFileAsync` returns the node and there is no key to reconstruct it from, so this is
  the one place a handler links from a returned node rather than from a key.
- **One source name** for the whole corpus, so it groups in the workspace and can be replaced
  wholesale.
- **Render in parallel, upload serially.** The pending buffer is not safe for concurrent
  writers; put the rendering across cores behind a semaphore that serialises the upload.
- **Fingerprint on the record's own content** so an untouched record is neither re-rendered
  nor re-uploaded. A render that throws leaves its hash unrecorded and is retried next run
  without abandoning the pass.
- **Make it skippable.** `--no-attachments`, `--attachment-threads N`, `--attachments-out
  <dir>`. The rest of the mapping runs in seconds; this does not.

---

## 8.1 Reading web pages (RSS and other web sources)

Verified building `src/AviationIntel.Connector` (Playwright 1.63, SmartReader 0.11, CodeHollow.FeedReader 1.2.6):

- **Load the page in a browser, then run Readability over the rendered DOM**:
  `new SmartReader.Reader(page.Url, await page.ContentAsync()).GetArticle()`. A plain GET (what
  `SmartReader.ParseArticleAsync(url)` does) misses client-rendered bodies and consent walls.
  Block `image`/`media`/`font` requests in a context route; gate one page per host with a delay,
  and parallelise across hosts.
- **Consult the hash cache before loading anything.** `HashCache.Hash(dto)` +
  `ContainsHash(version, hash)` to select the misses, extract them in parallel, write serially,
  `EnqueueHash` after `CommitPendingAsync`. `IfNotCached` would serialise the page loads.
- **Key articles by canonical URL** (tracking parameters, fragment and trailing slash removed)
  and group items across feeds first: a syndicated release is one node with an edge per feed.
- **Record how much of the article you have.** Store which text became the body (page, feed full
  text, feed summary) and why - a report must never present a teaser as the article.
- **A 200 can be a bot check.** Detect an HTML page where a feed was expected and retry through
  the browser, re-requesting through `context.APIRequest` (it carries the challenge's cookies).
  Some publishers (Imperva-protected ones) still refuse a headless browser - leave them failing
  and visible, do not build evasion.
- **Atom entries often carry only `<updated>`**: FeedReader's `PublishingDate` is `<published>`, so
  fall back to `((AtomFeedItem)item.SpecificItem).UpdatedDate`.
- **Proxies:** Chromium ignores `HTTPS_PROXY`; pass it as the launch `Proxy` with
  `Bypass = "localhost,127.0.0.1,[::1]"` (`<-loopback>` *removes* the loopback bypass).
- **In a dry run, silence the library's logging**: it still announces the connector to the
  placeholder endpoint and retries in the background.

## 9. Program.cs

```
--root <path>            where the corpus lives
--endpoint / --token     the workspace (or CURIOSITY_ENDPOINT / CURIOSITY_TOKEN)
--dry-run                map everything, write nothing
--reset-cache            drop the hash cache
--emit-frontend-schema   generate the shared N/E constants and exit
```

- **`--dry-run` is the development loop.** `WithDryRun(true)` makes every add and link a
  no-op, so the whole mapping still executes and the counters still add up — without a
  workspace and without a wait. Check a mapping change here first, every time.
- **Set an auto-commit cost** for real runs so the pending buffer flushes periodically rather
  than growing to the size of the corpus.
- **Ending the process is not automatic.** `Graph.Connect` leaves a foreground worker running;
  simply returning parks the run. Dispose on a background task with a timeout and then exit
  explicitly — `Dispose` blocks indefinitely when the endpoint is unreachable, which is
  exactly the dry-run case.
- **On failure, do not commit hashes.** Pending hashes are dropped when the cache is disposed
  without a commit, so the next run re-ingests whatever failed to land. Catch, report, return
  non-zero.

---

## 10. Emitting the shared schema constants

The workspace generates `N` / `E` constants for a front-end project and injects the same shape
above every endpoint script — but only from a graph that has already been ingested. Since it
is the connector that decides what that graph contains, generate them from the connector's own
`[Node]` classes by reflection instead, and commit the result:

```bash
dotnet run --project src/<App>.Connector -- --emit-frontend-schema src/<App>.FrontEnd/src/Schema/N.cs
```

Emit `N.<Type>.Type` plus one constant per field, and a flat `E`. Also emit the useful *sets*
— `N.DocumentTypes`, `N.All360Types` — from the schema, so a new record type joins the generic
views by existing rather than by being listed somewhere.

Node types the graph owns (users, file entries) cannot be reflected over; list them
explicitly in the emitter.

**Re-run it after any `[Node]` or `Edges.cs` change.** Never hand-edit the output.

---

## 11. Checklist

- [ ] One handler per input type; DTOs and `[Node]` classes nested inside it
- [ ] Every handler bumps its own `Version` when its mapping changes
- [ ] `CommitPendingAsync` before `CommitPendingHashesAsync`, always; neither on a dry run
- [ ] Exactly one `AddOrUpdate` per node per run, carrying everything
- [ ] Links use `Node.FromKey`, never a fetch
- [ ] Every edge in the catalogue, in both directions, one meaning each
- [ ] Shared metadata nodes minted once and linked by key
- [ ] Derived state computed from a stable hash — same corpus ⇒ same graph
- [ ] Ownership stored as an edge, never also as a property
- [ ] No edge for a relationship the application is meant to discover
- [ ] `--dry-run` counters reviewed before every real run
- [ ] `N.cs` regenerated and committed

---

## Reference implementation

Complete, commented templates in [`examples/`](examples/) — a generic `App` domain, not the sample application's:

| File | Shows |
| --- | --- |
| [`App.Connector.csproj`](examples/App.Connector.csproj) | The package set, with the source-reader packages of §1.1 commented in |
| [`Program.cs`](examples/Program.cs) | Arg parsing, `Graph.Connect`, schema sync, run order, the plan/link split, teardown |
| [`ConnectorContext.cs`](examples/ConnectorContext.cs) | Streaming readers, `LinkKey`, `Mint`, counters, the dry-run cache rule |
| [`Edges.cs`](examples/Edges.cs) | The edge catalogue, both directions, with the reasoning per group |
| [`Ingest/PartNumbersIngest.cs`](examples/Ingest/PartNumbersIngest.cs) | The canonical CSV handler — the four beats end to end |
| [`Ingest/CasesIngest.cs`](examples/Ingest/CasesIngest.cs) | A JSON handler: child nodes, metadata links, derived state folded into one `AddOrUpdate`, and what is deliberately **not** linked |
| [`Ingest/MetaLinks.cs`](examples/Ingest/MetaLinks.cs) | Shared `Status` / `Classification` / `Keyword` vocabularies, canonical keys, edge-pair validation |
| [`Ingest/AssignmentPass.cs`](examples/Ingest/AssignmentPass.cs) | Deterministic derived state, split in two around the document ingest |
| [`Ingest/UsersIngest.cs`](examples/Ingest/UsersIngest.cs) | Users as graph citizens — every attribute an edge |
| [`SchemaEmitter.cs`](examples/SchemaEmitter.cs) | Generating the shared `N` / `E` constants by reflection |

They are **templates, not a compiling project**: a few referenced handlers are elided, and the package versions need updating before use.
