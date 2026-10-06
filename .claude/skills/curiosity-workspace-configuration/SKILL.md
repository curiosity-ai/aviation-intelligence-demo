---
name: curiosity-workspace-configuration
description: How to treat a Curiosity workspace as version-controlled source — the definitions export/import cycle, the layout of a definitions folder, what each config file controls (general, interface, search, indexes, schemas, file indexing, chat policy), node visibility and access, and the ordering rules that make an import succeed. Use when setting up a workspace, promoting configuration between environments, or when a setting must be reproducible.
---

# Workspace configuration — the workspace as source

A Curiosity workspace is configurable enough that "someone clicked it together" is a real
failure mode: nothing is reproducible, nothing is reviewable, and a fresh environment is a
week of archaeology. The fix is to keep the **whole workspace in the repository** and treat
the admin UI as an editor of files you own.

---

## 1. The cycle

```
   admin UI  ──export definitions──▶  a zip  ──unpack──▶  src/<App>.Definitions/  ──▶ git
        ▲                                                          │
        └──────────── import definitions ◀────── zip ◀─────────────┘
```

- **Export once, early**, as soon as the workspace has anything worth keeping. Unpack it into
  the definitions project and commit. From then on the repository is the source of truth.
- **Edit files, then import.** Editing in the UI is fine for exploration — but re-export and
  commit before you forget, or the next import overwrites it.
- Import is system-admin only and is done through the admin UI (or the corresponding API).
- Everything is a plain file: JSON for configuration, `.cs` for code, `.json` for NLP.

**Not everything exports.** In the reference implementation, assistant templates are outside
the definitions export, so the assistant's system prompt lives in the repo as a markdown file
that a human pastes in. Find out early what your product version does and does not export,
and put anything that does not into the repo with a note saying so — an unexported piece of
configuration is the thing that will be missing on the demo machine.

---

## 2. Layout of a definitions folder

```
src/<App>.Definitions/
├── README.md                  what is here, what is NOT exported, how to import
├── <assistant>.md             system prompts that the export does not carry
├── config/
│   ├── general.json           workspace name, AI provider defaults, node visibility
│   ├── interface.json         which product features are on
│   ├── search.json            search engine tuning
│   ├── file-indexing.json     which file types are extracted, image captioning
│   ├── chat-policy-*.json     chat governance
│   ├── schemas/
│   │   ├── edges.json         every edge type
│   │   └── nodes/<Type>.json  every node type and its fields
│   └── indexes/               one file per (node type, field) index
├── nlp/
│   ├── spotters/              one file per (node type, field) spotter
│   ├── linking/               one file per (entity × node type × field) rule
│   └── pipelines/             the parsing pipelines
└── code/
    ├── endpoints/<path>.cs    server-side endpoints            → curiosity-code-endpoints
    ├── chat-ai-tools/<uid>.cs AI tools and consult readers     → curiosity-ai-tools-and-agents
    ├── agents/<uid>.cs        agents                            → curiosity-ai-tools-and-agents
    └── ai-prompt-templates/   prompt templates
```

The `config/schemas/` folder mirrors what the connector created. It is **generated output**,
not a place to design: change the connector's `[Node]` classes and re-run, then re-export.

---

## 3. What each file controls

### `general.json`

- **Workspace name** — appears throughout the product.
- **Default AI provider tasks** — which model/provider answers by default, and separately for
  the admin assistant and for compaction.
- **`IsPublicNodeType`** — the list of node types that are visible without per-record access
  control. **This is the setting people miss.** A node type that is not listed is invisible to
  ordinary users, so a renderer works for an admin and shows nothing for everyone else. When
  the corpus is workspace-wide, list every type you created.
- Audit logging, retention, and various engine limits.

### `interface.json`

Feature switches: graph, teams, apps, sharing, contacts, inbox, calendar, notes, clipboard,
guest accounts, self-signup, uploads. **Turn off everything the use case does not use.** A
sidebar full of product features the demo never touches is the fastest way to make a focused
application look like a generic one.

### `search.json`

Engine tuning: concurrency, highlight limits, facet limits per type, minimum token length,
thresholds that decide when a query is handled as an AI search, similarity decay, timeouts.

Start from the defaults. The two worth revisiting per use case:

- **facet limits**, if your dimension nodes are numerous and users need to see them all;
- **the AI-search thresholds**, which decide when a long natural-language query is treated as
  a question rather than as keywords.

### `indexes/`

One file per (node type, field). See `curiosity-entity-linking-nlp` §5 — this is where
searchability, boosts, facets and linguistic normalisation are set.

### `file-indexing.json`

Which file categories are extracted and indexed, extractor concurrency, and image captioning.
Relevant whenever records carry attachments — if the published artefact is the thing users
read, its **content** must be indexed, not just its existence.

### `chat-policy-*.json`

Governance over what the chat layer may do. Set it deliberately for a customer demo.

---

## 4. Ordering — what must exist before what

Import failures are almost always ordering.

1. **The graph schema must exist before endpoints import.** The workspace injects generated
   `N` / `E` constants above every endpoint script, so an endpoint naming a node type the
   connector has not created **does not compile**. → **Ingest before importing endpoints.**
2. **Nodes must exist before spotters are built.** A spotter built from an empty catalogue is
   an empty spotter and needs rebuilding after the ingest.
3. **Indexes should exist before the bulk ingest** where you can manage it, so indexing happens
   as records land rather than as one enormous catch-up.
4. **Spotters must be in a pipeline** before linking produces anything.
5. **Anything referenced by UID must be imported before the thing referencing it** — a tool
   before the agent that names it, an agent before the assistant that names it.

---

## 5. UIDs — which to hand-craft and which to mint

Some things are referenced by a stable UID from code: an agent named by a chat view, a tool
pinned in a post-message call, an assistant named by a front-end button.

- **Built-in product objects** have recognisable, stable UIDs and are re-seeded on every boot;
  refer to them by those.
- **Objects you define** are created once by an import and referred to by whatever id they were
  given. **Mint a random UID** for them. Do not hand-shape a readable one: not every 22-char
  string round-trips through the UID parser, and there is nothing a readable id buys for an
  object that is imported rather than re-seeded.
- **Whatever the UID, write it down in exactly one place per side** — a `const` in the front
  end, the filename in the definitions folder — and cross-reference the two in comments. A UID
  duplicated in three files is three places to get it wrong.

---

## 6. Access

- **Endpoint `AccessMode`** — `Unrestricted`, `AllUsers`, `AccessControlled`, `AdminOnly`.
  Pick per endpoint; a shared library endpoint that is not meant to be callable is `AdminOnly`.
- **`IsPublicNodeType`** — as above, per node type.
- **AI tool `AccessMode`** — same idea for tools.
- Where the customer needs per-record visibility, that is access groups plus per-node access
  control, and it needs to be in the discovery brief (round 4, question 7) — retrofitting it
  changes every query.

---

## 7. Promoting between environments

```bash
cd src/<App>.Definitions && zip -r ../../<app>-definitions.zip code config nlp
# → admin UI → Settings → Import definitions
```

Then, in order: ingest → import definitions → rebuild spotters/indexes if the catalogue
changed → paste any unexported prompts → smoke-test one query, one endpoint, one chat turn.

Keep a short `README.md` in the definitions folder listing exactly this sequence and anything
manual. On a demo machine, that file is what saves the morning.

---

## 8. Checklist

- [ ] Definitions exported, unpacked into the repo, and committed
- [ ] `README.md` in the definitions folder lists the import sequence and every manual step
- [ ] Everything the export does **not** carry is in the repo with a note
- [ ] `IsPublicNodeType` lists every node type the corpus needs visible
- [ ] `interface.json` has every unused product feature switched off
- [ ] Every index, spotter, linking rule and pipeline is a committed file
- [ ] Endpoints import **after** an ingest has created the schema
- [ ] Custom UIDs are randomly minted, and each is written in exactly one place per side
- [ ] A fresh environment can be rebuilt from the repo alone, and someone has proved it
