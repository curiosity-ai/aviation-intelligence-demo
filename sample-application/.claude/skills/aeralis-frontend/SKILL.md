---
name: aeralis-frontend
description: How to build the Curiosity.FrontEnd + Tesserae front-end for the Aeralis MOD pre-analysis demo — project setup and App.Initialize, one NodeRendererBase-derived INodeRenderer per data type with Overview/Graph/360 tabs, the six-tab PAWO renderer, the dashboard that replaces App.Settings.HomeView, the endpoint DTOs and the Endpoints path constants, and the workspace-import endpoint files. Use when writing or changing anything under src/Aeralis.FrontEnd/ or src/Aeralis.Endpoints/.
---

# The front-end — `Aeralis.FrontEnd`

C# transpiled to JavaScript by Transpose, rendered with Tesserae. No React.
Every API named here was verified against `curiosity-ai/mosaik` and
`curiosity-ai/tesserae` — **the repos are the contract, the published docs lag
behind them.** When you need a signature, read the source file, do not assume.

---

## 1. Project shape

```
src/Aeralis.FrontEnd/                  netstandard2.1, Sdk="Transpose.Build.Target"
├── Aeralis.FrontEnd.csproj            Curiosity.FrontEnd + Tesserae
├── tps.json / tps.Release.json         bundle name + minification
├── src/
│   ├── AeralisApp.cs                  Main → App.Initialize(Configure, OnLoad)
│   ├── Schema/N.cs                     GENERATED — node types + field names (§1.1)
│   ├── Api/
│   │   ├── Endpoints.cs                const string per endpoint path, XML-documented
│   │   ├── Dto.cs                      request/response DTOs — mirrored server-side (§7.1)
│   │   └── Api.Aeralis.cs             one method per endpoint, UID128 in and out
│   ├── Renderers/                      one INodeRenderer per node type
│   ├── Dashboard/                      the HomeView replacement, the Inbox page, the viewer
│   └── Components/                     shared pieces (View360, SimilarityList, ImpactDecisions)
└── src/Aeralis.Endpoints/             workspace-import .cs files (§7)
```

Entry point mirrors `Mosaik.FrontEnd.Default/src/MosaikDefaultApp.cs`:

```csharp
private static void Main()
{
    App.Initialize(Configure, OnLoad).FireAndForget();
}

private static void Configure(App.DefaultSettings settings)
{
    settings.HomeView = state => new DashboardView(state);   // §6
}
```

`App.Settings.HomeView` is `Func<Parameters, IComponent>`
(`Mosaik.FrontEnd/src/App.Settings.cs:21`); `App.DefaultRouting` calls it in
place of the built-in `HomeView`.

**Renderers are not registered.** `App.AutoDiscoverViews` (`App.cs:1841`)
reflects over the loaded assemblies for every non-abstract `INodeRenderer`
implementation and instantiates it, so a renderer joins the app simply by
existing. Two consequences worth stating, because both fail silently:

- every renderer needs a **public parameter-less constructor** — it supplies
  its own `SchemaStyleInfo` to the base rather than receiving one;
- shared bases must be **`abstract`**, or discovery will try to register them
  as renderers for a node type that does not exist.

Name them plainly (`ModificationRenderer`, `PartNumberRenderer`). Discovery
matches on the interface, never on the name, so the leading-underscore
convention seen in some Mosaik sources carries no meaning here.

### 1.1 `Schema/N.cs` is generated — do not hand-edit it

`N` and `E` are the contract between the connector, the endpoints and this
front-end, so all three spell every node type and every field the same way. The
workspace generates exactly this file for a front-end project
(`WebsiteController.CreateSchemaHelperForFrontEnd`) and injects the server-side
twin above every endpoint script (`CodeManager.AppendNodeSchemasHelperClass`) —
but only from a graph that has already been ingested, and it is the connector
that decides what that graph contains. So it is generated from the connector's
own `[Node]` classes instead:

```bash
dotnet run --project src/Aeralis.Connector -- \
    --emit-frontend-schema src/Aeralis.FrontEnd/src/Schema/N.cs
```

Re-run it after **any** change to a `[Node]` class or to `Edges.cs`. The shape is
the generated one — `N.<Type>.Type` plus one constant per field, and a flat `E` —
so `N.Modification.DocId` means the same thing here, in an endpoint and in the
connector, and a renamed property breaks the build rather than turning into an
empty label at run time. It also emits `N.DocumentTypes` / `N.All360Types` from
the schema, so a new document type joins the 360 views by existing.

Node types the graph owns (`_User`, `_FileEntry`) cannot be reflected over — they
have no `[Node]` class here — so they are listed explicitly in
`SchemaEmitter.GraphOwnedTypes`, taken from those types' own `WriteAsJson`.


### 1.2 Import conventions

Front-end files take both `using static Tesserae.UI;` and
`using static Mosaik.UI;`, and use the factories rather than `new`: `SearchArea()`,
`NeighborsLinks(...)`, `Metric(...)`, `Banner(...)`, `Card(...)`, `VStack()`.

The project takes a **direct `PackageReference` to `Tesserae`** ahead of the version
the `Curiosity.FrontEnd` packages were built against, for `Banner`, `IconTile` and
the `Metric` icon / `ValueFirst` / `ChangeInHeader` surface. The additions are
additive, so the pre-transpiled Curiosity modules keep working against it. Bump both
together when the Curiosity packages catch up.

---

## 2. Transpose constraints — read before writing any DTO

- **No runtime reflection.** You cannot enumerate an arbitrary object's
  properties. DTOs crossing the C#/JS boundary must be **flat** — no
  inheritance, no polymorphism, no nested generics beyond arrays. They are
  ordinary classes: `API.Endpoints.CallAsync` deserialises normally, so
  `[ObjectLiteral]` is neither needed nor wanted on them.
- `Script.Write<T>("…", args)` is the escape hatch, with no type safety. Avoid.
- Naming shortcuts are used pervasively and you should match them:
  `.WS()` = width 100 %, `.HS()` = height 100 %, `.P(n)` = padding n px,
  `.M(n)` = margin n px.

---

## 3. `NodeRendererBase` — the one base to derive from

`FrontEnd/Mosaik.FrontEnd/src/Components/Data/NodeRendering/NodeRendererBase.cs`.
Derive, override `BuildTabs`, and every surface (search row, preview sheet,
full page, peek panel) picks the change up at once because the host calls
through `this`.

```csharp
internal sealed class _ModificationRenderer : NodeRendererBase
{
    public _ModificationRenderer(SchemaStyleInfo style) : base(style) { }

    protected override IEnumerable<NodeTab> BuildTabs(
        Node node, IReadOnlyList<RelationshipGroup> groups, NodeView nodeView)
    {
        yield return Tab("overview", "Overview", () => RenderOverview(node));
        yield return new NodeTab { Id = "graph", Label = "Graph".t(), Scrolls = false,
                                   TabTitle = SegmentTitle("Graph".t()),
                                   TabContent = () => RenderGraphSegment(node, nodeView.OpenGraphNodePeek) };
        yield return Tab360(node);                              // §4
        foreach (var g in groups) yield return RelationshipTab(g, nodeView);
    }
}
```

Signature notes taken from the source, not guessed:
- `BuildTabs(Node, IReadOnlyList<RelationshipGroup>, NodeView)` is
  `protected virtual IEnumerable<NodeTab>`.
- `RenderGraphSegment(Node, Action<UID128, GraphExplorer>)` already handles the
  mount-then-load-then-fit dance. Reuse it; do not build a `GraphExplorer`
  inside a `Defer`.
- `RenderRelationshipSegment(Node, RelationshipGroup, Action<Node, RelationshipGroup>)`
  is the per-edge tab. Keep it for the relationship tabs.
- The base constructor takes `SchemaStyleInfo` and throws on null.

**Every type gets Overview + Graph + 360.** `PreAnalysisWorkOrder` gets the six
tabs in §5 instead.

### 3.0 The card and the modal — what a renderer declares

`AeralisRendererBase`'s constructor takes two things beyond the style, and they
are what the type's **card** says wherever it is listed:

```csharp
public ModificationRenderer() : base(new SchemaStyleInfo { … },
    N.Modification.Abstract,                                    // the body
    N.Modification.Status, N.Modification.EmbodimentStatus,      // the footer
    N.Modification.ModClass, N.Modification.Ata6d, N.Modification.ProgramLetter) { }
```

- **Body** (`CompactView` → `CreateCompactViewFor`). The base draws *every*
  property as a stack of labelled rows, which for a MOD is thirty rows inside one
  row of a result list. The override draws one property — the abstract — as
  `TextBlock(value).Tiny().Secondary()`, and `SetContent(null)` when the type has
  none, which collapses the row rather than leaving a gap. Reference types pass
  `null`: a part number has a description, not a summary.
- **Footer** (`WithFooter`). The handful of fields that place a node without
  opening it: where it stands (`Status`), what it is about (`Ata6d`), which fleet
  it belongs to (`ProgramLetter`, `AircraftType`), plus the type's own one or two
  discriminators. Each becomes an `InlineLabel` appended with `AddFooterEntry`,
  tooltipped with the property name, after the timestamp `NodeResult.For` already
  put there — `SetFooterEntries` *clears*, `AddFooterEntry` appends, so never
  reach for the former here.

  These are **node properties, not edge walks**, on purpose: a result list draws
  dozens of cards at once and a footer that queried the graph would be dozens of
  round-trips deep before the first row appeared. The values named are the same
  ones the edges were built from, so the footer says what the graph says,
  synchronously. Values over 48 characters are dropped rather than allowed to
  push the line off the card — check a candidate field against the data before
  adding it (`TsmProcedure.FaultSymptom` is a sentence; `MelItem` is empty on
  every procedure in this dataset).

The same footer is on the modal, because `PreviewAsync` builds from the same
`NodeResult.For` row and `NodeResult.PageFromAsync` calls `ModalKeepsFooter()`.

`PreviewAsync` is also overridden for **size**: the base opens at
`ModalSize(80.vw(), 80.vh())`, which is a preview of a handful of properties.
These nodes open onto a tab strip with a graph, a faceted 360 search and — on the
work order — a split screen inside the tab, so the override is `92.vw() × 90.vh()`
via the `ModalWidth` / `ModalHeight` virtuals. Both overrides keep the
`GetCustomRendererIfAny()` branch the base has: a workspace-configured renderer
still wins.

`NodeRendererDefinitionPreview` must be brought in with a **using alias**, not a
`using Mosaik.Components.NodeRendering;` — that namespace also declares a
`NeighborsLinks`, and importing it makes every use of the one this code wants
ambiguous (CS0104).

### 3.1 `LabelField` — a field name, and it fails silently

`SchemaStyleInfo.LabelField` is **not** a caption. It is a list of *node field
names* separated by `|`, and `Labels.Get`
(`FrontEnd/Mosaik.FrontEnd.Core/src/Helpers/Code/Labels.cs:17`) takes the first
one that has a value. When none of them do — including when the field simply does
not exist on that node type — the label falls back to `"<uid> [Display Name]"`
without an error anywhere. A typo turns every label of that type into a raw UID.

So: write it as `N.<Type>.<Field>`, never as a literal, and name a fallback after
any field that can legitimately be empty:

```csharp
LabelField = N.IpcItem.Nomenclature + "|" + N.IpcItem.PartNumber + "|" + N.IpcItem.ItemId,
```

A label that needs more than one field at once uses the **interpolated** form —
`$` followed by `{Field}` placeholders (`NodeLabelInterpolator`), with an optional
`{Field:format}`:

```csharp
LabelField = "$ATA {Code} — {Title}",         // an ATA chapter reads as both
LabelField = "${Value} ({Kind})",             // Classification is keyed "{Kind}:{Value}"
```

**Every type with an identifier and a title uses it, through
`LabelFields.IdAndTitle`.** The `|` form takes the *first* field with a value, so
`Title|DocId` shows the title and never the identifier — the wrong way round for
technical data, where `AMM-C-20-40-11-800-017-A` alone does not say what the task
is and *"Sealant Application at Pressurised Fuselage Joints"* alone does not say
which of the four sealant tasks it is:

```csharp
LabelField = LabelFields.IdAndTitle(N.AmmTask.DocId, N.AmmTask.Title),  // "${DocId} > {Title}"
```

It is **composed, never typed out as a literal**: the placeholders are field
names, a name that does not exist renders as empty, and nothing fails. Built from
`N.<Type>.<Field>`, a rename is a build error. A missing value leaves no hole —
the interpolator trims separators (`>`, `-`, `:`, whitespace…) off both ends, so a
document with no title still reads as its identifier alone.

### 3.1.1 `DisplayName` is the abbreviation, upper-case

`MOD`, `SB`, `IO`, `AMM`, `IPC`, `SRM`, `TSM`, `WDM`, `FCOM`, `PAWO`, `MSN`,
`ATA`, `AD`, `PN`. These are the names on the documents and in every conversation
about them — an engineer asks for "the MOD", never for "the Modification" — and
the type name is read hundreds of times a session, in facet lists, result rows and
graph nodes, where the short form is what keeps a mixed list scannable.

Only where the discipline *has* an abbreviation. `Drawing`, `Zone`, `Operator`,
`Supplier` keep their words; nothing is contracted just to be short. Two traps
worth naming, because both were live: `AtaChapter` is **`ATA`**, not `ATA2D` —
one node type holds the 2D, 4D and 6D levels and `Level` says which — and
`SoftwareLoad` is **not** `LSAP`, because only 174 of the 301 loads are one.

Two traps worth naming, because both were live here:
- a field that exists but holds a *paragraph* is technically valid and useless —
  `ExpectedFinding` labelled by `Rationale` produced link text a screen wide;
- **a node type with no renderer has no `LabelField` at all**, so it renders as a
  UID. Every type reachable from a `NeighborsLinks` or a search result needs one —
  that is why `Program`, `AircraftType`, `AircraftModel`, `Operator`, `Supplier`
  and the metadata dimensions all have renderers even though they only ever show
  Overview/Graph/360.
### 3.2 A property that is a reference renders as the thing it references

`AeralisRendererBase` overrides `RenderProperties` and asks
`Schema/PropertyLinks.cs` about every property before drawing it. Where the
property is really a reference, the row shows `NeighborsLinks` across that edge
instead of the string.

This is most of the details grid, not an edge case. `Ata2d` is a chapter,
`ProgramLetter` a programme, `Keywords` a list of keywords, `SubjectMod` another
document, `Trigger` / `RiskLevel` / `Compliance` classifications, `ModNumber` the
change itself. As text every one of them is a dead end — you can read "53" and not
ask what else is in chapter 53.

**Every status-like property points at `N.Status.Type`, not `N.Classification.Type`.**
`Status`, `EmbodimentStatus`, `IoStatus`, `PawoStatus`, `WorkflowStatus`, `Outcome`,
`Lifecycle`, `CertificationStatus` and an `Msn`'s `Status` all reach the same node type,
keyed by the value alone, over nine different edges — so a row still shows one answer
while the node itself gathers everything in that state across every type. See
`aeralis-graph-connector` §4.3b.

Three things make this safe:

- **The property stays on the node.** It is what the search index reads, and it is
  what the row falls back to (`showIfNone`) when the edge is absent — so a type
  that has the property but not the edge (an `IpcItem` carries `ProgramLetter` but
  is linked to its figure, not to the programme) degrades to plain text rather
  than to a blank row.
- **The per-type table wins over the envelope one.** Two types use an envelope
  property's name for something else: a `Rule` reaches its chapter over
  `RuleForAta`, not `InAta2d`, and an `Msn`'s `Status` travels over
  `HasAircraftStatus`, not `HasDocumentStatus` — the same `Status` node at the far
  end, reached by the edge that says which field it came from. Envelope-first sent
  both down an edge that does not exist for them.
- **Every entry is written `N.<Type>.<Field>` → `E.<Edge>`**, so a rename on
  either side is a build error.

Adding a property to the table is the second half of promoting a string to a
relationship in the connector. The first half is the edge; without this the new
node is reachable only from the 360 view.

### 3.4 The ten issue renderers

`Renderers/IssueRenderers.cs`. One per document type, because the connector gives each its
own `[Node]` class (`aeralis-graph-connector` §4.5) and Curiosity reads the schema off the
concrete class. Without a renderer an issue node has no `LabelField` and draws as
`<uid> [Display Name]` — which is what every `HasIssue` traversal used to show.

Everything shared is in `AeralisIssueRendererBase`; each type states four things: node
type, display name, tile text, and the document icon it carries.

**The tile text and `DisplayName` are separate here, and have to be.**
`AeralisRendererBase.CompactView` puts `DisplayName` on the card's icon tile, and the tile
is 34px — three or four letters. So an issue overrides `CompactView` to put the
*document's* abbreviation on the tile (`AMM`) while `DisplayName` stays the readable
`AMM issue` for facets and modal headers. `IconTile.SetIcon(string, …)` does **not**
auto-size text (Tesserae `IconTile.cs:81`), so a long tile string simply overflows.

Nothing extra is needed to keep an issue apart from its document: `NodeRendererBase`
derives the tile colour from the type name, so `AmmTaskIssue` and `AmmTask` tint
differently on their own.

**The issue letter is a badge, not a footer entry** — which revision you are looking at is
the reason the node exists separately from its document, and the badge is read before the
title. The current issue says so (`IsLatest`); the others are history by elimination.

`PropertyLinks.Issue(nodeType, property)` answers all ten at once, before the per-type
table, since they declare the same fields under the same names: `Supersedes` is the chain
back through the history, `RaisedByFinding` is the pre-analysis finding that forced the
revision — the link that makes a pre-analysis falsifiable — and `DocId` is the document
itself.

### 3.3 Every renderer sets an `Icon`

`Icon = UIcons.Screw.ToString()`. Without one `NodeRendererBase` falls back to a
colour derived from the type name, which tells types apart but says nothing about
what they are — and these are read side by side in a mixed result list, where
"wrench" against "aeroplane tail" is the difference between scanning and reading.
Names come from Tesserae's `UIcons` enum, so a wrong one is a build error rather
than a blank square (`UIcons.Bandage` does not exist; `UIcons.Patch` does).

**A node type with no renderer has no icon *and* no `LabelField`.** Any type
reachable from a `NeighborsLinks`, a search result or a property row needs one —
which now includes the small key-only types (`ModNumber`, `ServiceKit`,
`AirworthinessDirective`, `Keyword`, `EngineType`, `AtaDomain`, `MajorZone`).
They only ever show Overview/Graph/360, so each is five lines.


---

## 4. The 360 tab

One search area, deliberately filtered **broadly**, so the user sees every kind
of object that touches this node — including the NLP-derived ones.

```csharp
protected IComponent Render360(Node node) =>
    new SearchArea()
        .WithFacets()
        .OnSearch(sr => sr.WithTargetQuery(
            API.Query.StartAt(node.UID)
                     .Out(nodeTypes: Related360Types, edgeTypes: null)   // all edges
                     .TakeAll()));
```

- `SearchArea.OnSearch(Action<SearchRequest>)` and
  `SearchRequest.WithTargetQuery(SimpleQueryBuilder)` are the verified names
  (`Search/SearchArea.cs:136`, `Core/src/Schema/Search/SearchRequest.cs:220`).
- Pass **no edge type filter**. NLP relationships arrive as `_AppearsIn` /
  `_Mentions` and must show up here alongside the connector's structured edges.
- `Related360Types` differs per renderer — a `PartNumber` shows documents that
  use it; a `Modification` shows everything. Abstract the shell into
  `Components/View360.cs` and let each renderer pass its own type list.

---

## 5. `_PreAnalysisWorkOrderRenderer` — six tabs

The centrepiece. Build sections with `SplitView`, `Card(...).SetTitle(...)`,
`DetailsGrid`, `InlineLabel`, `NeighborsLinks`, `Badge`.

**No inner tab strips.** Every tab here is one screen, not a pivot of screens:
the work is a comparison, and a `SegmentedPivot` inside a tab puts two levels of
navigation in front of the engineer while hiding the half of the comparison they
are meant to be reading against. Where a tab has two sides, it is a **single**
`SplitView` — never a `SplitView` inside a segment of a pivot inside a tab.

### 5.1 Overview
One `SplitView().Left(…).Right(…)`. Left is **the analysis as it stands**, right is
**the change it is against**.

- **Left** — *ATA coverage*, one row per chapter carrying both what the impact matrix
  found and what similarity still suggests, with the suggested-but-uncovered chapters
  sorted to the top and badged. This was two cards (the matrix, and a suggested-ATA list
  with a banner counting the gap); the reader had to hold one in their head to read the
  other, and the banner was a third rendering of a fact the rows can state themselves.
  Then *confirmed impacts* as a `SearchArea().NoSearchBox().SearchFor(new
  SearchRequest().WithTargetUIDs(uids))` over the UIDs `Endpoints.PawoConfirmedImpacts`
  returned, so each row is drawn by that document type's own renderer. Then
  *applicability* — see §5.1b.
- **Right** — the **modification under analysis**, and only it: the published PDF at full
  height, with a button through to the modification above it.

The written report is deliberately **not** here. It is an output, not something read while
deciding coverage, and at the bottom of the left column it made that column scroll past
the two things that are.

**The dossier is the real PDF viewer, not a picture of its pages.** Ask the workspace's own
`_FileEntry` renderer for the body it would put in a preview sheet:

```csharp
var files = await API.Query.StartAt(ownerUID)
    .Out(N._FileEntry.Type, E.HasAttachment).TakeAll().GetUIDsAsync();

var file = await API.Nodes.GetAsync(files.UIDs[0]);

// showHeader/showFooter off: the card already says what this is and the tab is not a
// modal. enableHotkeys off: the PDF's Ctrl+F must not take the shortcut from the page it
// is embedded in. Footer off is also what puts Download in the viewer's toolbar.
var row = await new _FileEntryRenderer(showHeader: false, showFooter: false, enableHotkeys: false)
    .PreviewAsync(file, null);

var body = row is object ? await row.GetModalContentAsync() : null;
```

`_FileEntryRenderer` is `Mosaik.Components.Nodes._FileEntryRenderer`, public with that
three-flag constructor
(`FrontEnd/Mosaik.FrontEnd/src/Components/Nodes/Files/_FileEntryRenderer.cs:48`);
`OmniResult.GetModalContentAsync` (Tesserae `OmniResult.cs:1122`) is the public way to take
the body back out — the same call `NodeResult.PageFromAsync` makes to turn a row into a
page. With no custom `_FileEntry` renderer registered the body's own pivot is a single
"File Content" tab and `HideIfSingle()` hides it, so what lands in the card is the viewer
and nothing else. `PreAnalysisWorkOrderRenderer.AttachedFileBodyAsync` is the helper; the
issue comparison uses it too.

### 5.1b Applicability — link over the edge that carries the fact

Ask for one node type over one **named** edge per row, not several types at once with no
edge:

```csharp
NeighborsLinks(pawoUID, N.AircraftType.Type, E.AppliesToType, wrap: true,
               maxItemsToShow: 8, showIfNone: () => TextBlock("—".t()).Secondary())
```

`NeighborsLinks(uid, string[] nodeTypes, …)` joins the types into one `";"`-separated
string for the server (`API.Aggregated.GetNodeNeighbors`, mosaik `API.Aggregated.cs:241`),
and with no edge to disambiguate them there is nothing to say which dimension came back
empty. A programme is reached over `InProgram`; aircraft types over `AppliesToType`;
models over `AppliesToModel`; MSNs over `AppliesToMsn`. They are different facts and the
row that shows each says so.

**Always pass `showIfNone`.** Most work orders are scoped by type and model with no MSN
list at all, so an empty MSN row is the normal case — it has to read as "—" rather than
collapse, or the whole card looks like it failed to render.

### 5.2 Analysis
A `SplitView` of the two candidate lists — **possible affected documents** on the left,
**similar previous analysis** on the right — each a `SearchArea` wired to the similarity
engine so scores render themselves. They answer two halves of one question, and the second
is useful precisely while the first is on screen.

**Both are multi-select and both carry the decision commands.** A pre-analysis is a sweep
of dozens of suggestions, and deciding one row at a time is the slow way to do the only job
this screen has:

```csharp
SearchArea()
   .WithFacets()
   .WithSimilarityEngine(_ => Api.SimilarityAsync(endpointPath, pawoUID))
   .OnSearch(sr => sr.WithExcludeUIDs(excluded))   // the seed and the subject MOD
   .Renderer(r => r
       .WithSelection()
       .CustomizeResult(result => decisions.Customize(result, onChanged)))
```

**Exclude the seed and the subject modification.** A node is the closest match in the corpus
to itself, so without `WithExcludeUIDs` the work order ranks first in its own list of things
it might affect; the modification it analyses ranks near it, and that is the *change* rather
than something the change affects. Both are rows nobody can confirm or reject, in the two
places the best real candidates should be. Same pattern as
`Shared/Mosaik.Shared/NodeRendererEditor/Implementations/SimilarSearchContent.cs:38`.

The subject-MOD lookup is started **once** by the tab and the same `Task<UID128>` is handed
to both lists, so the query runs once and the two lists cannot end up excluding different
things. The exclusion set is applied to both even though a modification could never appear
in the previous-analysis list, whose results are work orders — one rule for both lists is one
thing to be right about.

`Components/ImpactDecisions.cs` owns both the badges and the commands, so a row's badge and
the command that set it cannot disagree about what "confirmed" means. Three rules:

- **Record the rejection too.** `ConfirmedImpact` was the only edge, so a dismissed
  candidate was one *without* an edge — indistinguishable from one nobody has looked at,
  which makes the sweep impossible to pause. `RejectedImpact` is the other half; the two
  are mutually exclusive by construction, and `set-impact-decision` clears one when it
  writes the other.
- **Load both sets before the first row draws** (`impact-decisions`, one call). A row badged
  from a half-loaded answer says "not reviewed" about something settled last week.
- **Register commands with `ShowForMultiSelection(uniqueID, node, handleMulti)`.** The
  unique id is what collapses the same command across twenty selected rows into one entry;
  without it a selection offers twenty identical "Confirm impact" commands. The handler
  takes `Node[]` and the request is batched.

Re-running the search is what repaints the badges — the decision a row shows is read while
the row is being built — so the change handler calls `area.TriggerSearch()`.

### 5.3 History & Sources
- **Left** — a `Plan("Lifecycle")` with the MOD/PAWO chain: IO raised (id +
  date) → MOD created → PAWO opened → **Pre-Analysis (active, current step)** →
  WO Created (pending, id derived from the IO id) → Embodiment MPD/SB
  (pending). `Plan` has `Title`, `HeaderCommands`, `Progress(position, total)`,
  `FooterMessage`, `FooterCommands`, and `PlanStatus` for per-step state.
- **Right** — key source data (FINs, drawings, part numbers, AMM tasks) as
  `NeighborsLinks`, each opening the 360 tab.

### 5.4 Rules & IPC
- **Left** — applicable rules from `Endpoints.RulesApplicable`, each with a **Propose**
  button calling `Endpoints.RulesAddToPawo`. Rules are the work: each one is a question
  the engineer answers.
- **Right** — **Related IPC**: one faceted `SearchArea` over
  `Query.StartAt(pawoUID).Out(nodeTypes: [IpcFigure, IpcItem, PartNumber, Zone, Fin])`.
  It was two cards of `NeighborsLinks` chips that overlapped each other and could not be
  searched; as a search area the reader filters by type and finds a part among forty by
  typing it.

### 5.5 Issue Comparison
`SplitView`: the subject modification on the left, a previous issue of it on the right,
each drawn as **the card its own renderer makes of it** over **the whole published PDF**.

- The card is `SearchArea().NoSearchBox().SearchFor(new SearchRequest().WithTargetUIDs(new[] { uid }))`
  at a fixed height — one node, rendered by the registered renderer, so it keeps that
  type's icon, label, footer and commands.
- The PDF is `AttachedFileBodyAsync(uid)`, the same embed §5.1 uses: every issue has its
  own rendered file (`AttachmentsIngest` attaches per issue), so both sides are real
  documents.
- The picker is filled from `Endpoints.ModIssues` and **defaults to the newest issue that
  is not the current one** — "what changed" is the question the tab is opened with.
- Every issue type needs its own renderer or its nodes draw as raw UIDs. All ten have one —
  see §3.4.

**Hand a `Dropdown` all of its items in one `Items(...)` call.** `Dropdown.Items(params
Item[])` clears what is already there before adding (Tesserae `Dropdown.cs:539`), so
calling it once per item inside a loop leaves a dropdown holding only the last one. That
is exactly what the dashboard's engineer picker did, which is why it only ever offered one
engineer.

### 5.6 360 View
As §4, but centred on the **MOD** the PAWO analyses, not the PAWO itself.

> **The tab set is still not the right shape.** The read → find → confirm loop is split
> between §5.1 and §5.2, and there is nowhere to put the recommendation the work order
> exists to produce. `docs/pawo-view-proposal.md` proposes three working tabs plus a
> persistent header instead, and records which of its steps have landed. Read it before
> making this view bigger.

---

## 6. The dashboard (`App.Settings.HomeView`)

A user `Dropdown` at the top switches the whole view's perspective — this demo
has no real login, so the dashboard renders "as" the selected user.

**A notice strip first.** Whatever is actually waiting on the selected engineer is
said in words before it is said in digits, as a `Banner`: urgent actions still open
(`.Danger()`, `SetIcon(UIcons.Flame)`, `SetBadge("Urgent")`), or — when nothing is
urgent — work orders with no owner (`.Primary()`). Its `Action` selects the matching
segment of the work-order pivot below, so the number always has a list behind it.
Nothing waiting means no strip at all: a banner that is always there reads as chrome.

**Counter tiles** — one `Card` per number, each a `Metric` leading with an `IconTile` and
drawn `ValueFirst()` so the digits read first and the words under them only say what was
counted. The colour is what the number *means* — red is late, amber is nobody's yet — so
the row is read by colour before it is read by digit. All four come from
`Endpoints.DashboardSummary`, fetched **once** per engineer and awaited by the greeting,
the banner and the tiles, so the sentence and the digits can never disagree.

**Every card opens the list it counted**, through `InboxView.RouteFor(segment.Id)` — a
number on a dashboard is a question whose other half already exists as a segment of the
inbox. The whole card is the click target (the number is the thing being clicked, and it is
the biggest thing on the card), and its label, icon and colour come from
`WorkOrderLists.Segments` rather than being written out here.

**My performance** — three numbers, each drawn as what it is:

- *Completed per week* — a `Metric` whose value is the total over the window and whose
  `Chart` is a `Sparkline` of the twelve weeks, with the last week against the one
  before it in `ChangeInHeader()`. **Not** a `DetailsGrid` of twelve week/count rows:
  the only question anyone asks of a series is whether it is going up.
- *Avg hours per PAWO* — a counter tile (`Stopwatch`, `Blue500`).
- *Validated last month* — one `Metric` whose value is the validated share and whose
  `Chart` is a `ContributionBar` (`Max(100)`) of validated / rejected / delegated, with
  the month's work-order count in `ChangeInHeader()`.

All computed live in `Endpoints.DashboardPerformance` from `WorkflowStatus`,
`CompletedDate` and `HoursSpent` on the work orders — never hard-coded.

**Work lists** — `WorkOrderLists.For(userUID, summary)`: a `SegmentedPivot()` with one
segment per counter — `inbox`, `urgent`, `mine`, `unassigned`, `all`. `mine` and `all`
scope a `SearchArea` with `WithTargetQuery`; the rest are `WithTargetUIDs` over the very
UID arrays the summary counted, so opening a number never shows a different set from the
one the tile added up.

**`inbox` and `mine` are deliberately different sets** — the inbox is the work orders on
the desk that are *not finished*, "PAWOs I own" is everything ever assigned. Both numbers
are on the home page, so both need a segment: a card whose number says 7 opening a list of
34 is a card that lied.

`WorkOrderLists.Segments` is the single description of the five — id, label, icon,
colour — and the counter cards, the sidebar entries and the pivot tabs all read it. Three
renderings of one description rather than three places to keep in step by hand.

It lives in its own file because **two** surfaces show it — the home page and the
sidebar — and they have to agree: every counter card and the urgent banner send the reader
from one to the other.

---

## 6.1 The Inbox page and the sidebar

A page of the front-end's own, behind a section of sidebar entries. Registering it is
**two** places and it has to be both — a sidebar entry whose route is not registered
navigates to nothing:

```csharp
// In Configure(settings):
App.Sidebar.OnSidebarRebuild_AfterHeader += (sidebar, mode, refresh) =>
{
    sidebar.AddContent(new SidebarSeparator("AERALIS_WORK_ORDERS", "Work orders".t()));

    foreach (var segment in WorkOrderLists.Segments)
    {
        var captured = segment;

        var item = new SidebarButton("AERALIS_" + captured.Id.ToUpperInvariant(),
                                     captured.Icon, captured.Label.t())
            .OnClick(() => Router.Navigate(InboxView.RouteFor(captured.Id)));

        refresh.Add(() => item.IsSelected = InboxView.IsShowing(captured.Id));
        sidebar.AddContent(item);
    }
};

// In OnLoad():
Router.Register(InboxRoute, state => App.ShowDefault(new InboxView(state)));
```

Verified against `FrontEnd/Mosaik.FrontEnd.LicenseServer/src/LicenseApp.cs`, which is the
only other front-end that adds pages of its own this way. `SidebarSeparator(identifier,
text)` is what gives a group of entries a heading.

**One page, five entries, distinguished by a query parameter.** `InboxView.RouteFor(segment)`
builds `#/inbox?segment=urgent`, and it is the *only* place that URL is spelled — the
counter cards, the urgent banner and the sidebar all call it. The view reads the segment out
of its `Parameters`.

**Selected state is per segment, not per route.**
`window.location.hash.Contains("#/inbox")` would light all five at once, since every one is
the same page. `InboxView.IsShowing(segment)` compares the query, and treats a bare
`#/inbox` as the default segment — otherwise landing on the inbox with no query lights no
entry while showing one of them.

## 6.2 Who the demo is rendering as

`Dashboard/Viewer.cs` holds it, and both the home page and the inbox read it. It was a
field on the home page, which was fine while the home page was the only view that showed
"my" work; a second such view would otherwise let the two disagree about who "I" am.

- `Viewer.EnsureAsync()` — the current engineer, choosing the first on first use.
- `Viewer.UsersAsync()` — the list, fetched once per session.
- `Viewer.Set(user)` / `Viewer.Changed` — switch, and rebuild whatever is on screen.

The picker is a modal of `SearchableList<Row>` rather than a `Dropdown`: engineers differ
by the ATA chapters they specialise in, so the row shows site, department, role and
specialities, and the search box matches all of them — "53" finds the structures
specialists. The endpoint returns each dimension **twice**: as UIDs, which is what gets
rendered (`NeighborsLinks`, so a speciality opens its chapter), and as text, which is what
`ISearchableItem.IsMatch` reads. A UID is not something anybody types.

---

---

## 6.3 Asking the assistant

Two buttons hand a question to the **Aeralis Pre-Analysis assistant** with the documents it is
about already attached: *Compare* on the issue-comparison tab, and *Ask about impact* on the
candidate lists. Both go through `Components/AiAssist.cs`.

```csharp
var chat = await API.ChatAI.NewChatWithContext(
    App.InterfaceSettings.ChatAIProvider?.TaskUID,
    UID128.Parse(AiAssist.AssistantUID),   // 2Bg21UPLPKEREykWdKis4q
    uids);

API.ChatAI.PostMessage(chat.UID, question).FireAndForget();

NodePreview.CloseAll();
NodePreview.CloseAll();
Router.Navigate(DefaultRoutes.ChatAI.OpenChat(chat.UID, null), reload: true);
```

Three things about that sequence are deliberate:

- **The context is the point.** A question typed into a bare chat makes the assistant hunt for
  documents by title; the same question with the nodes attached lets it `consult` them by id.
  Attach the subject of the question too, not only the candidates — an impact verdict against a
  modification the assistant cannot read is a guess.
- **The assistant is named by UID**, not taken from `App.InterfaceSettings.SelectedAIAssistantTemplate`.
  This is not the workspace's general assistant being asked a technical-data question; it is the
  one carrying the pre-analysis system prompt, the two agents and the consult readers.
- **The post is not awaited.** Awaiting it holds the user on the old screen until the whole
  answer is generated. Firing it first means the request is already with the server, so the chat
  opens onto an answer that is already streaming.

The generated question ends with `AiAssist.UseAgent(name)`, naming which agent should answer.
That is a hint to the assistant, not a bypass — the assistant still decides, and still answers
if the agent is unavailable.

**Where the back end is.** The prompts and readers these buttons depend on live in
`src/Aeralis.Definitions/`: the two agents under `code/agents/`, the per-node-type consult
readers in `code/chat-ai-tools/ConsuLt111111111111111.cs`, and the assistant's own system prompt
in `aeralis-pre-analysis-assistant.md` — assistants are not part of the definitions export, so
that one is imported by hand.

---

## 7. Endpoints

### 7.1 Front-end side
`Api/Endpoints.cs` holds one `const string` per path with an XML doc comment
saying what it does. `Api/Api.Aeralis.cs` wraps each in an
`API.Endpoints.CallAsync<T>` call. DTOs live in `Api/Dto.cs`, flat and **not**
`[ObjectLiteral]` — `CallAsync` deserialises them with Newtonsoft normally.

**The DTOs are declared twice, identically.** Every class in `Api/Dto.cs` also
exists inside `TD` in `aeralis/_lib/aeralis-helpers.cs`, and the endpoints
return *those* types rather than anonymous objects. Both ends are serialised by
Newtonsoft with the default resolver, so the two declarations literally are the
wire format; a field renamed on one side only stops compiling instead of
deserialising to null. Add a field to one, add it to the other.

**Identify things by `UID128`, never by a document id or a code.** Requests carry
`PawoUID`, `TargetUID`, `RuleUID`, `UserUID`; responses carry `AtaUID`, `Uid`,
`InboxUIDs`, … . Two reasons:

- the endpoint stops guessing. Confirming an impact used to try each of the eight
  impactable node types in turn to work out what `"AMM-M-53-11-04"` referred to;
- the response is directly renderable. A list of UIDs goes straight into
  `NeighborsLinks(uids)` or a `SearchArea` with
  `sr.WithTargetUIDs(uids)`, and every row is drawn by that node type's own
  registered renderer — so nothing has to copy a title through the wire and no
  label can go stale.

Use the `UID128` type itself in the DTOs, on both sides. In the front-end a
`UID128` *is* a JS string at run time (`Mosaik.FrontEnd.Core/.../UID128.cs` spells
this out); server-side it is a struct carrying a `[JsonConverter]` that reads and
writes the same plain string. The wire format is identical to `string` either
way, so this costs nothing and buys a compile error when a UID is handed to the
wrong parameter.

### 7.2 Workspace-import side
`src/Aeralis.Endpoints/` holds one `.cs` file per endpoint in exactly the
format `GraphDefinitionsExporter` reads back — verified in
`Mosaik/Graph/src/Schema/Nodes/Internal/_CodeEndpoint.cs:160`
(`ExportAsCode`) and `GraphDefinitionsExporter.cs:1065`
(`ZipPaths.Endpoints = "code/endpoints"`):

```csharp
[endpoint: Curiosity.Endpoints.Path("aeralis/pawo/confirmed-impacts")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

var req = Body.FromJson<TD.PawoRequest>();
var pawoUID = req?.PawoUID ?? default(UID128);
if (pawoUID == default(UID128)) return BadRequest("pawoUID is required");
if (!Graph.TryGet(pawoUID, out var pawo) || pawo.Type != N.PreAnalysisWorkOrder.Type)
    return NotFound($"Work order '{pawoUID}' not found.");

var uids = Q().StartAt(pawoUID).Out(edgeType: E.ConfirmedImpact).AsUIDEnumerable().ToArray();

return new TD.ConfirmedImpactsDto { Uids = uids };
```

Rules:
- Attribute lines first, then a blank line, then the code.
- `AccessMode` is one of `Unrestricted`, `AllUsers`, `AccessControlled`,
  `AdminOnly` (`Shared/Mosaik.Shared/SharedTypesCore.cs:3309`).
- Mark an endpoint `ReadOnly` when it only queries — the runtime then hands it
  a `ReadOnlyCodeEndpointExecutionScope`, and a write attempt fails loudly.
  The confirm/remove-impact and add-rule endpoints must **not** be read-only.
- Available identifiers inside the body: `Body`, `Graph`, `Q()` / `Query()`,
  `CurrentUser`, `Logger`, `CancellationToken`, `ChatAI`, `AgentAI`,
  `GetSecretAsync` (`CodeScopes.cs:937`).
- Share types between endpoints with `//ImportEndpoint("path")`, which appends
  the imported source. Put shared DTOs in `aeralis/_lib/…`, marked
  `AccessMode("AdminOnly")` so it cannot be called as an API.
- **Never write a node type or a field name as a string literal.** The workspace
  injects a generated `N` and `E` above every endpoint script
  (`CodeManager.AppendSchemaHelperClassesAndImports`, `Mosaik/Graph/src/Queries/CodeManager.cs:708`),
  so it is `N.AtaChapter.Type`, `N.AtaChapter.Code`, `E.InAta2d` — including for
  graph-owned types, `N._User.UserName` and `E._HasAttachment`. A typo then fails
  the import instead of returning an empty result forever. The corollary: an
  endpoint referring to `N.Foo` only compiles once the connector has created that
  node type, so **ingest before importing the endpoints**.
- **Return a value, not a JSON string.** The controller serialises whatever the
  script returns (`EndpointsController.cs:442`); returning `x.ToJson()` sends it
  as `text/plain` and, worse, lets the returned shape drift from the DTO the
  front-end deserialises. The similarity endpoints return the engine's own
  `SimilarityResult` for exactly this reason — that is the type
  `SearchArea.WithSimilarityEngine` consumes, and a hand-built projection of it
  deserialises into an empty result.
- **Traverse, don't re-look-up.** The subject of a work order is reached with
  `.Out(N.Modification.Type, E.PreAnalysisOf)`, not by reading its `SubjectMod`
  string and fetching a node by that key; ownership with
  `.Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment)` from the user, not by
  comparing a name.

### 7.3 The endpoint set

| Path | Read-only | Purpose |
| --- | --- | --- |
| `aeralis/dashboard/summary` | yes | Inbox, urgent, owned, unassigned counts for a user |
| `aeralis/dashboard/performance` | yes | Weekly throughput, avg hours/PAWO, status split |
| `aeralis/dashboard/users` | yes | The engineers the picker offers, each dimension as UIDs *and* as text |
| `aeralis/pawo/confirmed-impacts` | yes | Confirmed impacts alone |
| `aeralis/pawo/impact-decisions` | yes | Confirmed **and** rejected, in one call, for the row badges |
| `aeralis/pawo/set-impact-decision` | no | `CONFIRMED` / `REJECTED` / `NONE` for a whole selection |
| `aeralis/pawo/confirm-impact` | no | Adds `ConfirmedImpact` (single, superseded by the batch one) |
| `aeralis/pawo/remove-impact` | no | Removes it |
| `aeralis/mod/issues` | yes | A modification's issue history, newest first |
| `aeralis/pawo/impact-matrix` | yes | ATA 2D × document-type counts |
| `aeralis/pawo/draft-report` | no | AI draft of the analysis report (stub) |
| `aeralis/similarity/affected-documents` | yes | Possible affected documents (§8) |
| `aeralis/similarity/previous-analysis` | yes | Similar previous analyses (§8) |
| `aeralis/similarity/suggested-ata` | yes | Document scores folded into ATA 2D scores |
| `aeralis/rules/applicable` | yes | Rules applicable to a PAWO |
| `aeralis/rules/add-to-pawo` | no | Proposes a rule against the PAWO |

---

## 8. The similarity endpoints

Written as **similarity scenarios**, not hand-rolled loops:
`Graph.Query().StartAt(seed).ToSimilarity(...).AddSignal(...).Fuse(...).AddRule(...).ExecuteAsync(ct)`.
The engine fuses the signals, keeps the per-signal breakdown in
`ScoreInfo.Components` — so the UI can say *why* a result surfaced — and runs the
rules after fusion.

The graph signals use `HasCommonNeighborsWith(seedUID, nodeType, edgeType)`,
which is why the connector links metadata as nodes: it answers "shares an ATA
chapter / part / FIN with the seed" in one hop.

Shapes worth keeping:

- **Positive signals** carry a `Weight` and a `Describe`; ATA proximity is three
  signals (6D / 4D / 2D) with descending weights rather than one branch.
- **Already-confirmed impacts are a negative signal**, not a rule — they should
  be demoted, not hidden, so the list stays a complete picture. `FuseFinal`
  is `Fusion.Discount`.
- **Program isolation is a rule**, because it is absolute in this dataset.
- Scores survive *narrowing* (`Where`, `Except`, `IsRelatedTo`) but not
  *expansion* (`Out`), so a two-hop signal contributes by rank. That is the right
  weight for an inference anyway — say so in a comment rather than pretending.

Both endpoints mix text similarity with graph signals; neither is text-only.

**`affected-documents`** — candidates are `SB`, `MOD`, `AMM`, `IPC`, `SRM`,
`TSM`, `WDM`, `FCOM`:
1. Text similarity from the subject MOD's title + abstract + description.
2. Graph boost for each shared `PartNumber` or `Fin` with the subject.
3. Boost by ATA proximity: same ATA 6D > same 4D > same 2D.
4. Hard filters: same `Program`, and ATA 2D within the PAWO's search scope.
5. Drop anything already in `ConfirmedImpact`.

**`previous-analysis`** — find similar *documents* first, then walk
`HasPreAnalysis` / `PreAnalysisOf` to the work orders that analysed them, and
score each work order by the best score among its documents.

**`suggested-ata`** — takes the `Dictionary<UID128, float>` the similarity
engine returned, resolves each document's ATA 2D, and **sums** the scores per
chapter into a new `Dictionary<UID128, float>` keyed by `AtaChapter` UID. The
UI compares that list against the impact matrix and banners anything missing.

---

## 9. Checklist

- [ ] Every DTO crossing to JS is flat and `[ObjectLiteral]`
- [ ] Every renderer derives from `NodeRendererBase` and is registered
- [ ] Overview + Graph + 360 on every type; six tabs on the PAWO
- [ ] `DisplayName` is the discipline's abbreviation, upper-case, where one exists
- [ ] Every type with an identifier and a title labels with `LabelFields.IdAndTitle`
- [ ] Every type declares its card body and footer properties in its constructor
- [ ] No `SegmentedPivot` inside a tab — a two-sided tab is one `SplitView`
- [ ] 360 areas filter broadly and do **not** exclude `_AppearsIn` / `_Mentions`
- [ ] Dashboard metrics computed from graph data, never hard-coded
- [ ] Endpoint files carry the attribute header and the right `AccessMode`
- [ ] Write endpoints are not marked `ReadOnly`
- [ ] `dotnet build` is clean — the front-end is not otherwise tested for now
