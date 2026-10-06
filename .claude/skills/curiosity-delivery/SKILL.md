---
name: curiosity-delivery
description: Building, ingesting, importing, verifying and publishing a Curiosity application — the order that works, the gates at each stage, what to test at each layer, rebuilding an environment from the repository, and the pre-demo checklist. Use when standing up an environment, promoting a change, or preparing to show the application.
---

# Delivery — build, ingest, import, verify, show

The layers depend on each other in one direction only, and the deploy order is the same as the
build order. Doing it out of order produces failures that look like bugs: an endpoint that will
not compile, a spotter that finds nothing, a renderer that draws UIDs.

---

## 1. The order, every time

```
1. build          the corpus tools, the read model, the connector
2. validate       corpus validator + schema check must exit 0
3. dry-run        the connector, no writes — read the counters
4. ingest         the connector against the workspace
5. emit schema    regenerate N.cs from the connector; commit
6. import         the workspace definitions (config, nlp, code)
7. rebuild        spotters and indexes, if the catalogue changed
8. paste          anything the definitions export does not carry (e.g. assistant prompts)
9. build          the front end
10. verify        §3
```

**Why 4 before 6:** endpoints are compiled against generated schema constants, so an endpoint
naming a node type the graph has never seen does not compile.

**Why 4 before 7:** a spotter built from an empty catalogue is an empty spotter.

**Why 5 exists at all:** `N`/`E` are the shared vocabulary of three codebases. Stale constants
are how a renamed property becomes an empty label at run time instead of a build error.

---

## 2. Commands

```bash
# corpus
python3 tools/validate.py                                        # must exit 0
dotnet run --project src/<App>.DatasetCheck -- --root . --strict  # must exit 0

# connector
dotnet run --project src/<App>.Connector -- --root . --dry-run                # map, write nothing
dotnet run --project src/<App>.Connector -- --root . --dry-run --no-attachments
dotnet run --project src/<App>.Connector -- --root . --endpoint "$ENDPOINT" --token "$TOKEN"
dotnet run --project src/<App>.Connector -- --root . --reset-cache            # full re-ingest

# schema constants
dotnet run --project src/<App>.Connector -- --emit-frontend-schema src/<App>.FrontEnd/src/Schema/N.cs

# definitions
cd src/<App>.Definitions && zip -r ../../<app>-definitions.zip code config nlp
# → admin UI → Settings → Import definitions

# front end
dotnet build src/<App>.FrontEnd/<App>.FrontEnd.csproj
```

The hash cache lives next to the connector binary and is gitignored. Losing it costs one full
re-ingest and nothing else.

---

## 3. What to verify, layer by layer

Each of these has been a real failure. Check them in this order; a failure at one layer
explains everything below it.

**Corpus**
- validator and schema check both exit 0
- every ground-truth case still resolves

**Graph**
- dry-run node and edge counters are in the expected range — a count that halved is a mapping
  regression
- spot-check one record: does it have the edges you expect, in both directions?
- a metadata dimension node exists per distinct value, not per row

**Search & NLP**
- a full-text query returns results
- `_Mentions` edges exist on a record whose prose names an identifier
- a spotted identifier resolves to a node, and an unspottable one is *not* creating junk nodes

**Endpoints**
- each one returns its DTO shape, not a string
- a write endpoint actually writes (the read-only marking is a run-time failure)
- counts and the UID arrays beside them agree

**AI**
- the assistant answers a workload question with the same numbers the dashboard shows
- consult returns fields, related **and** attachments for every type
- an agent invoked by name actually runs, and quotes rather than paraphrases

**Front end**
- `dotnet build` clean
- **open one node of every type**: a label, an icon, a footer, no raw UIDs anywhere
- every counter card opens a list of exactly the number it showed
- a similarity list does not contain the seed or its subject
- the published-file tab shows the real viewer

---

## 4. Rebuilding an environment from the repository

Someone has to have done this, once, before the demo. The steps are §1 with nothing skipped,
against a fresh workspace. What it catches:

- configuration that only exists in the old workspace because someone clicked it
- anything the definitions export does not carry
- ordering assumptions that only hold on an already-populated graph
- UIDs that were assumed stable and are not

Write the result down in the definitions folder's README as the canonical sequence, including
every manual step.

---

## 5. Iterating without a full re-ingest

- **`--dry-run` for mapping changes.** Every handler runs, counters add up, nothing is written
  and no workspace is needed. This is the development loop.
- **Bump one handler's `Version`** to re-ingest just that input type.
- **`--no-attachments`** for everything that is not about the attachments; rendering them is
  the only slow step by an order of magnitude.
- **Front-end changes need no re-ingest at all** unless the schema moved.
- **Endpoint changes need a re-import**, not a re-ingest.

---

## 6. Before a demo

- [ ] Full rebuild from the repository done on the demo machine, from scratch
- [ ] The five-minute narrative walked end to end, on this data, today
- [ ] Every record named in the narrative opens, and looks right
- [ ] The assistant answers the narrative's question correctly **twice in a row**
- [ ] Every product feature the demo does not use is switched off in the interface settings
- [ ] Node visibility set so the demo user — not just an admin — sees every type
- [ ] The AI provider is reachable and has quota
- [ ] Attachments are present (they are the first thing skipped and the first thing noticed)
- [ ] No raw UIDs, no empty facets, no card with a broken label anywhere on the path
- [ ] A fallback: a screenshot or recording of the narrative, in case the network is not there

---

## 7. Checklist

- [ ] Ingest before importing endpoints, always
- [ ] Schema constants regenerated and committed after every schema change
- [ ] Definitions exported and committed after every admin-UI edit
- [ ] Everything the export does not carry is in the repo, with a note
- [ ] A rebuild-from-scratch sequence exists in the definitions README and has been proved
- [ ] Each layer verified before blaming the one below it
