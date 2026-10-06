# CLAUDE.md — working agreement for `<APPLICATION NAME>`

> **This is a template.** Replace everything in `<ANGLE BRACKETS>`, delete what does not
> apply, and keep the non-negotiables. It encodes what building the reference
> implementation under [`sample-application/`](sample-application/README.md) taught; the
> reasoning behind each rule is in the skills under [`.claude/skills/`](.claude/skills).

This repository builds a **Curiosity-based application** for `<ONE-SENTENCE USE CASE>`.
Read [`README.md`](README.md) and [`docs/use-case.md`](docs/use-case.md) before doing anything.

Five deliverables, in dependency order:

| # | What | Where | Skill |
| --- | --- | --- | --- |
| 1 | The corpus | `data/` | `<per-type data skills>` |
| 2 | The graph connector | `src/<App>.Connector/` | `curiosity-data-connector`, `curiosity-graph-modelling` |
| 3 | The workspace configuration | `src/<App>.Definitions/config/`, `.../nlp/` | `curiosity-workspace-configuration`, `curiosity-entity-linking-nlp` |
| 4 | Endpoints + the AI layer | `src/<App>.Definitions/code/` | `curiosity-code-endpoints`, `curiosity-similarity-scenarios`, `curiosity-ai-tools-and-agents` |
| 5 | The front end | `src/<App>.FrontEnd/` | `curiosity-frontend-setup`, `curiosity-node-renderers`, `curiosity-dashboards-and-lists`, `curiosity-chat-experience` |

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
10. `<IF SYNTHETIC:>` **Everything under `data/` is synthetic.** Never copy real
    `<customer identifiers>`. Every record carries a synthetic marker.

---

## 1. Order of operations

```
1. docs/use-case.md                       the discovery brief   → curiosity-use-case-discovery
2. corpus + read model                    validator exits 0; schema check exits 0
3. connector                              --dry-run counters reviewed; real run completes
4. emit N.cs from the connector           committed
5. workspace configuration                indexes, spotters, linking, search, visibility
6. endpoints + AI layer                   imported AFTER an ingest has created the schema
7. front end                              dotnet build clean; every type renders
8. rebuild-from-scratch rehearsal         → curiosity-delivery
```

A layer may only depend on something produced in an **earlier** step.

---

## 2. Division of labour: programmatic vs authored

`<Adjust for this project. The rule that generalises:>`

| Generated by a **script** | **Authored** by a model or a human |
| --- | --- |
| Identifiers, catalogues, registries, reference tables | Titles, summaries, descriptions, rationale |
| Anything with a pattern | Anything an embedding has to do real work on |

Never hand-author reference rows, and never script-template prose. The application's retrieval
story only demos if the *text* is realistic.

---

## 3. Writing realistic text `<if the corpus is authored>`

- Use the domain's register and the vocabulary its practitioners actually use.
- Titles read like real ones: noun phrases, not sentences.
- Summaries and descriptions are **different texts**, not one truncated from the other — both
  are embedded.
- **Vary vocabulary deliberately.** Plant pairs that are related and share *no* keywords, and
  pairs that share keywords and are *not* related. Record which is which in the ground truth
  with a `discovery` field.
- Dates are plausible and ordered.

---

## 4. Validation gates

```bash
python3 tools/validate.py                                          # coherence — must exit 0
dotnet run --project src/<App>.DatasetCheck -- --root . --strict    # executable schema — must exit 0
dotnet run --project src/<App>.Connector   -- --root . --dry-run    # mapping — review counters
dotnet build src/<App>.FrontEnd/<App>.FrontEnd.csproj               # must be clean
```

Fix the data, not the validator, unless the validator is genuinely wrong.

Change a schema ⇒ change the skill, then the read model, then the validator, then the
connector, then regenerate `N.cs`.

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

- Branch: `<branch>`
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
