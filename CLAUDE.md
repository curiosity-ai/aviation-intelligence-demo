# CLAUDE.md — working agreement for Aviation Market Intelligence

> Started from the Curiosity application template ([`docs/curiosity-application-template.md`](docs/curiosity-application-template.md)).
> It encodes what building the reference implementation under
> [`sample-application/`](sample-application/README.md) taught; the reasoning behind each rule
> is in the skills under [`.claude/skills/`](.claude/skills).

This repository builds a **Curiosity-based application** that monitors 248 real aviation &
aerospace RSS feeds, reads every new article in full, and produces daily and weekly market
intelligence reports as HTML pages and PowerPoint decks.
Read [`README.md`](README.md) and [`docs/use-case.md`](docs/use-case.md) before doing anything.

**Picking this up in a new session:** read [`TODO.md`](TODO.md) for what is done and what is
open, then [`docs/running-locally.md`](docs/running-locally.md) to rebuild the local server and
re-ingest. The previous session's network blocked the real feed hosts and had no LLM key, so the
P0 tasks in `TODO.md` are the ones that need an unrestricted environment.

Five deliverables, in dependency order:

| # | What | Where | Skill |
| --- | --- | --- | --- |
| 1 | The feed directory (real sources) | `data/feeds/` | — |
| 2 | The graph connector | `src/AviationIntel.Connector/` | `curiosity-data-connector`, `curiosity-graph-modelling` |
| 3 | The workspace configuration | `src/AviationIntel.Definitions/config/` | `curiosity-workspace-configuration`, `curiosity-entity-linking-nlp` |
| 4 | Endpoints + the AI layer | `src/AviationIntel.Definitions/code/`, `.../templates/` | `curiosity-code-endpoints`, `curiosity-ai-tools-and-agents` |
| 5 | The front end | `src/AviationIntel.FrontEnd/` | `curiosity-frontend-setup`, `curiosity-node-renderers`, `curiosity-dashboards-and-lists`, `curiosity-chat-experience` |

**The connector's graph schema is the contract for everything below it.** Finish and run the
connector before writing an endpoint or a renderer against a node type.

## The stack

C# end to end. The connector is a .NET console app on `Curiosity.Library`; the endpoints and
the AI layer are C# *scripts* stored in the workspace; the front end is C# compiled to
JavaScript by the **Transpose** compiler (`tps`, a global dotnet tool the build shells out to)
and rendered with **Tesserae**, the C# UI component library. No TypeScript, no React.

**Before creating any project — and at the start of any substantial piece of work — update
every NuGet package to the latest release, in its own commit.** The Curiosity packages ship
continuously and the docs lag them. See `curiosity-toolchain-and-packages` for the commands,
the per-project package sets, and the toolchain checks (`dotnet --list-sdks`, `tps --help`)
that must pass before you write code.

---

## 0. Non-negotiables

1. **`AddOrUpdate` writes the node's whole content.** One `AddOrUpdate` per node per run,
   carrying everything. A second pass with three computed fields blanks the rest. Compute
   derived state before the node is written, or use the partial-update overload.
2. **Never write a node type or a field name as a string literal.** Use the generated
   `N.<Type>.<Field>` / `E.<Edge>` constants — in the connector, in every endpoint, and in the
   front end. A literal fails silently, forever; a constant fails the build.
3. **Everything crossing a boundary is a `UID128`.** Endpoint requests and responses, AI tool
   results, front-end state. Never a title, never a business identifier.
4. **The connector writes only the relationships the source states.** Anything the application
   is meant to *discover* is left unlinked; NLP relationships arrive separately as
   `_Mentions` / `_AppearsIn`, and the UI never filters them out.
5. **Every node type has a renderer.** A type without one has no label and no icon and renders
   as a raw UID wherever it is reachable — including the three-line metadata types.
6. **Every metric is computed live from the graph.** Nothing pre-aggregated, nothing
   hard-coded. If the data cannot support a number, the number does not go on the page.
7. **Derived data is deterministic.** Seed every choice from a stable hash of the record's own
   identifier. Same corpus ⇒ same graph, every run.
8. **The workspace lives in git.** Export definitions after every admin-UI change and commit.
   A fresh environment must be rebuildable from this repository alone.
9. **Start from the reference implementations.** Every layer has a commented template under
   `.claude/skills/<skill>/examples/`, and a working version of the same layer under
   `sample-application/`. Adapt those rather than writing from a blank file — each
   non-obvious line carries the comment saying why it is that way.
10. **The corpus is real; the fixtures are not.** `data/feeds/` lists real public feeds and the
    connector ingests live news - never invent an article, a figure or a source in a report.
    Everything under `tests/fixtures/` is synthetic (fictional companies) and says so on every
    page; never ingest it into a workspace a customer will see.

---

## 1. Order of operations

```
1. docs/use-case.md                       the discovery brief   → curiosity-use-case-discovery
2. feed directory + offline fixtures      the CSV parses; the fixture dry run reads every page
3. connector                              --dry-run counters reviewed; real run completes
4. emit N.cs from the connector           committed
5. workspace configuration                indexes, spotters, linking, search, visibility
6. endpoints + AI layer                   imported AFTER an ingest has created the schema
7. front end                              dotnet build clean; every type renders
8. rebuild-from-scratch rehearsal         → curiosity-delivery
```

A layer may only depend on something produced in an **earlier** step.

---

## 2. Division of labour: what is curated, what is fetched, what is written

| Curated by an analyst | Fetched by the connector | Written by the AI layer |
| --- | --- | --- |
| The feed directory: category, priority, intended use, access, language | Feed items and article text, as published | Report JSON: selection, headlines, "so what", signals - every claim cited |

Never hand-edit fetched content, and never let the AI layer state a figure that is not in an
article it read.

## 3. Report templates

- The HTML report and the PowerPoint deck render from **one** report JSON
  (`src/AviationIntel.Definitions/templates/README.md`). Change the contract there, in the
  templates, in `render-report`'s validation and in the `aviation-market-reports` skill together.
- Templates are authored and previewed as files under `templates/`, then spliced into the sandbox
  tool with `python3 tools/embed_templates.py`. Never edit the embedded copy.
- An HTML artifact reaches nothing: inline CSS/JS, system fonts, and the two library tags the
  workspace answers (Tailwind, PptxGenJS) - see the `make-artifacts` built-in skill.

---

## 4. Validation gates

```bash
dotnet run --project src/AviationIntel.Connector -- --root . --dry-run --priority Core   # mapping — review counters
python3 tests/fixtures/serve.py &                                                        # offline feeds
dotnet run --project src/AviationIntel.Connector -- --root . --directory tests/fixtures/directory.csv --dry-run
dotnet build src/AviationIntel.FrontEnd/AviationIntel.FrontEnd.csproj                    # must be clean
python3 tools/import_definitions.py --endpoint <url>                                     # must report Success: True
```

Then `GET /api/chatai/sandboxes` must show `DefinitionError: null` for the news desk, and the
`:replay` checks in `docs/running-locally.md` must pass.

Change a schema ⇒ change the connector, re-run it, regenerate `N.cs`, export the schemas, then
update the sandbox, the endpoint and the front end that read the field.

---

## 5. Working against the Curiosity packages

`Curiosity.Library`, `Curiosity.FrontEnd*` and `Tesserae` come from NuGet. The **published
documentation lags the product**, so never write against a signature taken from a doc page.

- Tesserae's source is available — read it for any component's real constructor and fluent
  surface.
- For the product APIs, the skills under `.claude/skills/` carry what was verified. If you
  need something they do not cover, say so and ask rather than guessing.
- When you verify a new API, **write it into the relevant skill** with what you checked.

---

## 6. Commits

- Branch: the session's designated branch (currently `ccr-029ebc34-jcvr9i`).
- Never commit anything under `sample-application/` as part of your own work.
- Commit in logical chunks: docs + skills, tools, reference data, then one commit per stage.
- Never commit build output or the connector's hash cache.
- Do not open a PR unless explicitly asked.

---

## 7. When delegating to sub-agents

One sub-agent per **independent partition** of the work (a data partition, a document type, a
layer). Give it:

- the exact scope and volumes it owns,
- the instruction to read the conventions skill first, then the skill for its layer,
- the reminder that it owns *all* of its partition, so it is solely responsible for its
  cross-references closing.

Review every agent's output against the gates in §4 before accepting it.
