# Curiosity application template

Start a new Curiosity application here. The repository carries three things:

| | |
| --- | --- |
| [`CLAUDE.md`](../CLAUDE.md) | The working agreement, with the bracketed sections to fill in. It is the file your agent reads first. |
| [`.claude/skills/`](../.claude/skills) | Fourteen skills — one per layer of a Curiosity application, nine of them with complete reference implementations. |
| [`sample-application/`](../sample-application/README.md) | A worked example built end to end against the skills: corpus, connector, workspace definitions, endpoints, AI layer and front end. |

Nothing here is about any one industry. Everything here is about how you turn *a
customer's corpus and a customer's decision* into a Curiosity workspace that answers it.
Where a skill says *"in the reference implementation"*, it means `sample-application/`,
and the path it names is real.

## Getting started

1. Read [`CLAUDE.md`](../CLAUDE.md) and replace everything in `<ANGLE BRACKETS>`.
2. Read [`curiosity-use-case-discovery`](../.claude/skills/curiosity-use-case-discovery/SKILL.md)
   and write `docs/use-case.md` from the answers.
3. Read [`curiosity-solution-blueprint`](../.claude/skills/curiosity-solution-blueprint/SKILL.md)
   for the five deliverables, then
   [`curiosity-toolchain-and-packages`](../.claude/skills/curiosity-toolchain-and-packages/SKILL.md)
   before creating any project.
4. Work down the layers in the order §1 of `CLAUDE.md` gives, opening the skill for each
   layer as you reach it and cross-checking against `sample-application/`.

Delete `sample-application/` once you no longer need it beside you — nothing outside it
depends on it.

---

## Who this is for

A developer with:

- the **Curiosity product documentation**,
- the **Tesserae** source (`curiosity-ai/tesserae`),
- the `Curiosity.Library`, `Curiosity.FrontEnd*` and `Tesserae` NuGet packages,
- an LLM coding agent,
- and **no access to the `mosaik` source**.

That last point is why these skills exist and why they are shaped the way they are. The
sample application was built with the product source open beside it, and every non-obvious
API fact was verified against it. Those facts are written down here so the next project
does not need it. **When a skill states a signature, a constraint or a failure mode, treat
it as verified.** When you need something a skill does not cover, ask for it rather than
guessing from a doc page — the published documentation lags the product.

---

## The stack, in one paragraph

A Curiosity application is **C# end to end**. The connector is an ordinary .NET console app on
`Curiosity.Library`. The endpoints and the AI layer are C# *scripts* stored in the workspace.
The front end is C# too — compiled to JavaScript by the **Transpose** compiler (a global dotnet
tool, `tps`, that the build shells out to) and rendered with **Tesserae**, the C# UI component
library whose source is public and is the contract for every component's real API. No
TypeScript, no React.

> **Before creating any project, update every NuGet package to the latest release** — in its own
> commit, before the work. The Curiosity packages ship continuously and the published
> documentation lags them. `curiosity-toolchain-and-packages` has the commands, the per-project
> package lists, and what breaks when versions drift apart.

## The skills

Read `curiosity-use-case-discovery` first on a new engagement,
`curiosity-solution-blueprint` immediately after, and `curiosity-toolchain-and-packages` before
creating any project. The rest are reference, opened when you reach the layer they describe.

Most skills carry an `examples/` folder of **complete, heavily commented reference
implementations** — see [Reference implementations](#reference-implementations) below.

### Getting started

| Skill | What it covers |
| --- | --- |
| [`curiosity-use-case-discovery`](../.claude/skills/curiosity-use-case-discovery/SKILL.md) | **The interrogation.** The questions to put to the developer/customer before any code exists, in the order that makes the next answer cheaper, and the design brief they add up to. |
| [`curiosity-solution-blueprint`](../.claude/skills/curiosity-solution-blueprint/SKILL.md) | The five deliverables of a Curiosity application, the repository layout, the strict build order, and what must be true before each stage starts. |
| [`curiosity-toolchain-and-packages`](../.claude/skills/curiosity-toolchain-and-packages/SKILL.md) | The Transpose compiler, Tesserae, the SDK, the update-packages-first rule, and the NuGet package set for each kind of project. |

### Data & the graph

| Skill | What it covers |
| --- | --- |
| [`curiosity-graph-modelling`](../.claude/skills/curiosity-graph-modelling/SKILL.md) | The central decision: for each field, **property, node + edge, NER-linked, or nothing**. The rules that decide it, and what each choice costs and buys. |
| [`curiosity-data-connector`](../.claude/skills/curiosity-data-connector/SKILL.md) | The `Curiosity.Library` connector — handler-per-input-type, `[Node]`/`[Key]`/`[Property]`, `HashCache`, commit ordering, `AddOrUpdate` clobbering, users, attachments, deterministic derived state. |
| [`curiosity-entity-linking-nlp`](../.claude/skills/curiosity-entity-linking-nlp/SKILL.md) | Spotters, index-linking, NLP pipelines and text indexes — how `_Mentions` / `_AppearsIn` get created, and which fields deserve which. |

### Back end

| Skill | What it covers |
| --- | --- |
| [`curiosity-workspace-configuration`](../.claude/skills/curiosity-workspace-configuration/SKILL.md) | The definitions export/import cycle, what each `config/` file controls, node visibility, search settings, and treating the whole workspace as version-controlled source. |
| [`curiosity-code-endpoints`](../.claude/skills/curiosity-code-endpoints/SKILL.md) | Server-side code endpoints — the file format, `AccessMode`, `ReadOnly`, the injected `N`/`E`/`Q()`, shared DTO libraries, and the UID-not-key discipline. |
| [`curiosity-similarity-scenarios`](../.claude/skills/curiosity-similarity-scenarios/SKILL.md) | The similarity engine — signals, weights, fusion, rules, negative signals, and how graph structure becomes a retrieval signal. |
| [`curiosity-ai-tools-and-agents`](../.claude/skills/curiosity-ai-tools-and-agents/SKILL.md) | AI tools, `consult` readers, agents, assistants and system prompts — what belongs at each layer, and the context rules that decide whether an answer is grounded. |

### Front end

| Skill | What it covers |
| --- | --- |
| [`curiosity-frontend-setup`](../.claude/skills/curiosity-frontend-setup/SKILL.md) | The Transpose project, `App.Initialize`, generated `N`/`E`, routes, the sidebar, and the Transpose constraints that shape every DTO. |
| [`curiosity-node-renderers`](../.claude/skills/curiosity-node-renderers/SKILL.md) | `INodeRenderer` / `NodeRendererBase` — auto-discovery, the card, the footer, `LabelField`, tabs, the 360 view, and property-as-relationship rendering. |
| [`curiosity-dashboards-and-lists`](../.claude/skills/curiosity-dashboards-and-lists/SKILL.md) | Replacing the home view, metric tiles that open the list they counted, live-computed performance, work lists, and `SearchArea` as the universal list. |
| [`curiosity-chat-experience`](../.claude/skills/curiosity-chat-experience/SKILL.md) | Custom `ChatView`, pinned assistants and capability sets, and handing a question to an assistant with its evidence already attached. |

### Shipping

| Skill | What it covers |
| --- | --- |
| [`curiosity-delivery`](../.claude/skills/curiosity-delivery/SKILL.md) | Build, ingest, import, verify, publish — the order that works, what breaks if you reorder it, and what to check before a demo. |

### The working agreement

[`CLAUDE.md`](../CLAUDE.md) — the root file of this repository, and the one to fill in first.
It encodes the working agreement, the build order and the non-negotiables the reference
implementation learned the hard way.

---

## Reference implementations

Nine of the skills carry an `examples/` folder: complete, heavily commented templates for the
layer they describe, written against **one small generic domain** so they read as a single
application rather than nine fragments.

That domain is deliberately boring: a **`Case`** (the unit of work) analysing a **`Change`**
(its subject) against **`Manual`** records, over **`PartNumber`** and **`Category`** reference
data, with shared **`Status`** / **`Classification`** / **`Keyword`** metadata and users who
specialise in categories. Map your own nouns onto those five and every file transfers.

| Layer | Where |
| --- | --- |
| Connector — csproj, `Program`, context, edges, a CSV handler, a JSON handler, metadata vocabularies, deterministic assignment, users, schema emitter | [`curiosity-data-connector/examples/`](../.claude/skills/curiosity-data-connector/examples/) |
| Endpoints — the shared DTO library, a read endpoint, a write endpoint | [`curiosity-code-endpoints/examples/`](../.claude/skills/curiosity-code-endpoints/examples/) |
| A full similarity scenario | [`curiosity-similarity-scenarios/examples/`](../.claude/skills/curiosity-similarity-scenarios/examples/) |
| AI layer — consult readers, a workload tool, an agent, an assistant prompt | [`curiosity-ai-tools-and-agents/examples/`](../.claude/skills/curiosity-ai-tools-and-agents/examples/) |
| Front end shell — csproj, `tps.json`, entry point, the `Api` layer | [`curiosity-frontend-setup/examples/`](../.claude/skills/curiosity-frontend-setup/examples/) |
| Renderers — the shared base, concrete renderers, label fields, property links, the 360 view | [`curiosity-node-renderers/examples/`](../.claude/skills/curiosity-node-renderers/examples/) |
| Dashboard — home view, work lists, the viewer | [`curiosity-dashboards-and-lists/examples/`](../.claude/skills/curiosity-dashboards-and-lists/examples/) |
| Chat — a custom `ChatView`, the ask-with-evidence handoff | [`curiosity-chat-experience/examples/`](../.claude/skills/curiosity-chat-experience/examples/) |

**They are templates, not a compiling solution.** A few referenced handlers and views are
elided, the UIDs are placeholders, and the package versions need updating before use. Read them
as the skill's prose made concrete — every non-obvious line carries the comment explaining why
it is that way.

---

## The five things the reference implementation got wrong first

Read these before the skills; each one is a section in a skill, but they are the load-bearing
lessons.

1. **A string is not a relationship.** Property facets exist (string, enum, time) but filter
   one node type only; anything more than one type carries — and anything a similarity signal
   keys on — has to be a node on the other end of an edge. Half the schema work on the
   sample application was promoting properties to nodes after the fact.
   → `curiosity-graph-modelling`

2. **`AddOrUpdate` writes the node's whole content.** A second pass that calls it with three
   computed fields blanks everything the first pass wrote. Compute derived state *before*
   the node is written, or use the partial-update overload.
   → `curiosity-data-connector` §4

3. **Identify things by UID across every boundary.** Endpoint requests, endpoint responses,
   AI tool results, front-end state. A response of UIDs renders itself through the registered
   renderers; a response of titles goes stale and forces the server to guess types.
   → `curiosity-code-endpoints` §3

4. **A node type with no renderer has no label and no icon**, and shows as a raw UID
   wherever it is reachable. Every type — including three-line key-only metadata types —
   needs one.
   → `curiosity-node-renderers` §2

5. **The connector must not create the links the application is supposed to discover.** If
   the demo's claim is "we find impacts nobody catalogued", an edge written from the source
   data for that same relationship makes the demo prove nothing.
   → `curiosity-graph-modelling` §5
