---
name: curiosity-solution-blueprint
description: The architecture of a Curiosity-based application — the five deliverables, the repository layout, the strict build order and the dependency gates between stages. Use after discovery, before writing any code, and whenever deciding where a new piece of work belongs.
---

# Blueprint — the shape of a Curiosity application

A Curiosity application is **five deliverables**, and they have a strict dependency order.
Building them out of order is the single most expensive mistake available on this kind of
project, because the graph schema is the contract every later layer is written against.

```
        ┌──────────────┐
   1.   │   The data   │  files, exports, or a live source
        └──────┬───────┘
               │  what shape is it in, and what is a relationship?
        ┌──────▼───────┐
   2.   │  Connector   │  Curiosity.Library → nodes, properties, edges, files, users
        └──────┬───────┘
               │  THE GRAPH SCHEMA — the contract for everything below
        ┌──────▼───────┐
   3.   │Configuration │  indexes, spotters, linking, search, access, definitions
        └──────┬───────┘
               │  what is searchable, what NER links, what the AI can read
        ┌──────▼───────┐
   4.   │  Endpoints   │  server-side code: queries, similarity scenarios, writes
        │  + AI layer  │  AI tools, consult readers, agents, assistants
        └──────┬───────┘
               │  DTOs = the wire contract
        ┌──────▼───────┐
   5.   │  Front end   │  renderers, dashboard, lists, chat — Transpose + Tesserae
        └──────────────┘
```

**Finish and run the connector before writing a renderer or an endpoint against a node
type.** The generated `N`/`E` constants — which endpoints and the front-end are both written
against — are produced from the schema, and an endpoint naming a node type the graph has
never seen does not compile.

---

## 1. Repository layout

The reference implementation's layout, generalised. Adapt the names, keep the separation.

```
<repo>/
├── CLAUDE.md                       working agreement — see lessons-learnt/CLAUDE.md
├── README.md                       the use case, for a human
├── docs/use-case.md                the discovery brief
├── data/                           the corpus (or the scripts that fetch it)
│   ├── reference/*.csv             tabular master data
│   └── documents/**/*.json         one file per record
├── tools/                          generators + validators for the corpus
├── src/
│   ├── <App>.Dataset/              C# read model for the corpus — the executable schema
│   ├── <App>.DatasetCheck/         parses everything, fails on schema drift
│   ├── <App>.Connector/            Curiosity.Library ingest            → deliverable 2
│   ├── <App>.Definitions/          the workspace, as version-controlled source → 3 + 4
│   │   ├── config/                 general, interface, search, indexes, schemas
│   │   ├── nlp/                    spotters, linking, pipelines
│   │   └── code/                   endpoints, chat-ai-tools, agents, prompt templates
│   └── <App>.FrontEnd/             Transpose + Tesserae                → deliverable 5
└── lessons-learnt/                 this folder
```

Two conventions worth copying verbatim:

- **A dataset read-model project** (`<App>.Dataset`) that both the validator and the
  connector deserialise through. It makes the corpus schema executable: a field the model
  does not know about fails the build instead of silently vanishing at ingest.
- **A definitions project** that holds the *entire* workspace configuration as files. See
  `curiosity-workspace-configuration`; this is the difference between a workspace you can
  rebuild and a workspace someone clicked together.

---

## 2. Build order, with gates

Do not start a stage until its gate is green.

| # | Stage | Gate before starting the next |
| --- | --- | --- |
| 0 | Discovery brief | `docs/use-case.md` exists and names every data type, identifier and relationship |
| 1 | Corpus (real or synthetic) + read model | validator exits 0; `DatasetCheck --strict` exits 0 |
| 2 | Connector | `--dry-run` counters look right; a real run completes; node/edge counts match expectations |
| 3 | Generate `N.cs` from the connector's `[Node]` classes | the front-end and the endpoints compile against it |
| 4 | Workspace configuration: indexes, spotters, linking, search | search returns results; `_Mentions` edges appear |
| 5 | Endpoints + AI layer | each endpoint returns its DTO; the assistant answers a grounded question |
| 6 | Front end | `dotnet build` clean; every node type renders with a label and an icon |
| 7 | Demo rehearsal | the five-minute narrative works end to end on a fresh ingest |

**Stage 3 is a real stage.** `N` and `E` are the shared vocabulary of three codebases —
connector, endpoints, front end. Regenerate them after *any* `[Node]` or edge change, and
never hand-edit them. In the reference implementation the connector emits them:

```bash
dotnet run --project src/<App>.Connector -- --emit-frontend-schema src/<App>.FrontEnd/src/Schema/N.cs
```

The workspace also generates the same shape for endpoint scripts and injects it above every
one, which is why a string literal for a field name is always the wrong call — see
`curiosity-code-endpoints`.

---

## 3. What lives where — the routing table

When a new piece of work arrives, this decides which deliverable it belongs to.

| The work | Where it goes | Skill |
| --- | --- | --- |
| A field should be filterable | connector: node + edge | `curiosity-graph-modelling` |
| Two records should be linked from data | connector: edge | `curiosity-data-connector` |
| Two records should be linked from text | configuration: spotter + linking | `curiosity-entity-linking-nlp` |
| A field should be searchable / embeddable | configuration: index on that field | `curiosity-workspace-configuration` |
| A ranked list of candidates | endpoint: similarity scenario | `curiosity-similarity-scenarios` |
| A count, a matrix, a projection | endpoint: read-only query | `curiosity-code-endpoints` |
| A user decision that must persist | endpoint: write + an edge only it creates | `curiosity-code-endpoints` |
| "Read this record in full for me" | AI: consult reader | `curiosity-ai-tools-and-agents` |
| "Judge these records against that one" | AI: agent | `curiosity-ai-tools-and-agents` |
| "What is on my desk" | AI: custom tool over the same edges the dashboard reads | `curiosity-ai-tools-and-agents` |
| How a record looks anywhere it appears | front end: renderer | `curiosity-node-renderers` |
| A screen that is not a record | front end: route + view | `curiosity-frontend-setup` |

---

## 4. The three contracts

Most cross-layer bugs on this project were a contract being restated instead of shared.

**Contract 1 — the graph schema.** Owned by the connector's `[Node]` classes and `Edges.cs`.
Expressed to everyone else as generated `N` / `E` constants. *Nobody writes a node type or
field name as a string literal, anywhere, ever.*

**Contract 2 — the wire format.** Endpoint DTOs. Declared **twice, identically**: once in the
front-end's `Api/Dto.cs` and once in a shared endpoint library imported by every endpoint.
Both ends serialise with the same default resolver, so the two declarations literally are the
wire format — and a field renamed on one side fails to compile instead of deserialising to
null. Everything identifying a node in a DTO is a `UID128`.

**Contract 3 — the corpus schema.** Owned by the dataset read-model project. The validator
and the connector both go through it, so the corpus cannot drift away from what the connector
expects without failing a build.

---

## 5. Sizing

From the reference implementation, as a calibration point rather than a target:

| | |
| --- | --- |
| Document types | 10, plus 10 parallel "issue" (revision) types |
| Reference/master types | ~15, plus ~15 metadata dimension types |
| Records | ~625 documents, ~12 000 reference rows |
| Node types total | ~55 |
| Edge type pairs | ~90 |
| Endpoints | 16 |
| Renderers | one per node type — ~55, most of them 5 lines |
| Front-end source | ~4 000 lines |
| Connector source | ~3 000 lines |

The two numbers that surprise people: **every node type needs a renderer** (a type without
one shows as a raw UID), and **metadata dimensions outnumber the interesting types**. Budget
for both.

---

## 6. Do this differently next time

Lessons the reference implementation paid for.

- **Model the metadata dimensions as nodes on the first pass.** Promoting `Status`,
  `Department`, `Keyword`, `EngineType` and thirty others from properties to nodes after the
  fact meant touching the connector, the endpoints, the renderers and the property-link table
  at once.
- **Decide "does this type have revisions?" before writing its `[Node]` class.** Adding an
  issue/revision type per document type later doubled the schema, needed ten more renderers,
  ten more consult readers and a re-run of every index.
- **Write the ground-truth cases into the corpus from the start.** They are what makes the
  retrieval story falsifiable, and they are nearly impossible to add convincingly afterwards.
- **Put the user-facing *write* in the plan at the beginning.** Every one of these apps needs
  the user to record a decision; it is the thing that turns a search demo into an application,
  and it is always discovered last.
- **Keep the workspace in git.** Export definitions early, commit them, and treat the admin
  UI as an editor of files you own — not as the source of truth.
