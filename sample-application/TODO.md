# TODO — dataset generation plan & progress

Legend: `[ ]` not started · `[~]` in progress · `[x]` done

---

## Phase 0 — Repository setup

- [x] `README.md` — use case description
- [x] `CLAUDE.md` — working agreement
- [x] `TODO.md` — this file
- [x] Skill: `aeralis-dataset-conventions` (IDs, envelope, applicability, vocabularies)
- [x] Skill: `aeralis-reference-data`
- [x] Skill: `aeralis-mod-documents`
- [x] Skill: `aeralis-sb-documents`
- [x] Skill: `aeralis-manual-documents` (AMM / IPC / SRM / TSM / WDM)
- [x] Skill: `aeralis-io-pawo-documents`
- [x] Skill: `aeralis-dataset-coherence`
- [x] Skill: `aeralis-dataset-dotnet-reader`

## Phase 1 — Tooling

- [x] `tools/ata_catalog.py` — ATA 2D/4D/6D catalogue
- [x] `tools/gen_reference.py` — seeded CSV master-data generator
- [x] `tools/lookup.py` — reference slice query for authoring agents
- [x] `tools/emit.py` — JSONL → per-document JSON materialiser
- [x] `tools/validate.py` — coherence gate
- [x] `src/Aeralis.Dataset` — C# class per data type (System.Text.Json + CsvHelper)
- [x] `src/Aeralis.DatasetCheck` — parses all data, prints counts per type + MB read

> The graph is **not** built here — the application builds it downstream using
> embeddings, entity linking and the metadata this dataset carries.

## Phase 2 — Reference master data (`data/reference/`)

| File | Target rows | Status |
| --- | --- | --- |
| `ata_chapters.csv` | ~600 (2D + 4D + 6D) | [x] |
| `part_numbers.csv` | 6 000 | [x] |
| `fins.csv` | 2 500 | [x] |
| `msn_registry.csv` | 1 200 | [x] |
| `drawings.csv` | 1 500 | [x] |
| `software.csv` | 300 | [x] |
| `zones.csv` | ~400 | [x] |
| `operators.csv` | 60 | [x] |
| `suppliers.csv` | 80 | [x] |

## Phase 3 — Documents (one sub-agent per program)

Per-program volumes (identical for every program):

| Doc type | Count | Notes |
| --- | --- | --- |
| `IO` | 6 | 1 of them drives the hero scenario |
| `MOD` | 30 | 20 released/in-service + 10 candidates under pre-analysis |
| `PAWO` | 6 | one per candidate-driving IO, with `expected_findings` |
| `SB` | 22 | each embodies exactly one MOD |
| `AMM` | 30 | |
| `IPC` | 10 | |
| `SRM` | 8 | includes repair drawings |
| `TSM` | 8 | |
| `WDM` | 5 | |
| **total** | **125** | |

| Program | Types | Agent | Authored | Emitted | Validated |
| --- | --- | --- | --- | --- | --- |
| `M` MERIDIAN | MERIDIAN-42 / -46 / -42F | [x] | [x] | [x] | [x] |
| `C` CIRRANE | CIRRANE-11 / -13 | [x] | [x] | [x] | [x] |
| `T` TITAN | TITAN-88 | [x] | [x] | [x] | [x] |
| `Z` ZEPHYR | ZEPHYR-18 / -20 / -22 / -24 (classic + Evo) | [x] | [x] | [x] | [x] |
| `S` SOLANO | SOLANO-30/-34/-38/-39, VANTOR-64/-68 | [x] | [x] | [x] | [x] |

**Delivered: 625 documents (125 per program), 3.82 MB. Both gates exit 0.**

## Phase 4 — Gates

- [x] `python3 tools/validate.py` exits 0 with zero errors
- [x] Hero scenario reachable in every program (IO → MOD → PAWO → findings)
- [x] `dotnet run --project src/Aeralis.DatasetCheck -- --root . --strict` exits 0
- [x] Checker reports file counts per type and total MB read

## Phase 5 — Dataset extensions for the application

Driven by what the connector and front-end need; see `aeralis-graph-connector` §5.

- [x] `data/reference/users.csv` — engineers with ATA 2D specialities and sites
- [x] `data/reference/rules.csv` — engineering rules dataset (Rules & IPC tab)
- [x] Historical (closed) PAWOs against released MODs, so throughput-per-week
      and last-month status splits have something to compute from
- [x] `aeralis-io-pawo-documents` updated for the historical-PAWO shape
- [x] `validate.py` + the C# model updated to match

## Phase 6 — Graph connector (`src/Aeralis.Connector`)

- [x] Project + `Graph.Connect` + schema sync + run order
- [x] One handler per input type, DTOs and `[Node]` classes nested inside it
- [x] `HashCache` per handler with independent `Version`
- [x] Users created and PAWOs assigned deterministically
- [x] `--dry-run` reports node/edge counters without connecting

## Phase 7 — Front-end (`src/Aeralis.FrontEnd`, `src/Aeralis.Endpoints`)

- [x] Core project, `App.Initialize`, renderer registration
- [x] `INodeRenderer` per data type (Overview + Graph + 360)
- [x] Six-tab `_PreAnalysisWorkOrderRenderer`
- [x] Dashboard replacing `App.Settings.HomeView`
- [x] `Endpoints` constants + DTOs + REQ wrappers
- [x] Workspace-import endpoint files
- [x] `dotnet build` clean

## Phase 8 — Delivery

- [x] Commit docs, skills and tools
- [x] Commit reference data
- [x] Commit per-program documents
- [x] Push to `claude/aeralis-mod-dataset-generation-is4fig`

---

## Phase 9 — Doubling the dataset for the demo UI

The dataset was sized to prove the pipeline. This phase sizes it to *demo*: every
screen that looked thin gets the data it needs, one sub-agent per program.

New per-program volumes (was → is):

| Type | Was | Is |
| --- | --- | --- |
| `IO` | 6 | 12 |
| `MOD` | 30 | 60 |
| `PAWO` | 26 | 52 |
| `SB` | 22 | 44 |
| `AMM` | 30 | 60 |
| `IPC` | 10 | 20 |
| `SRM` | 8 | 16 |
| `TSM` | 8 | 16 |
| `WDM` | 5 | 10 |
| `FCOM` | — | 10 |
| **total** | **145** | **300** |

- [x] New document type **FCOM** (Flight Crew Operating Manual entry) — the only
      crew-facing type. It exists so the impact matrix has a column an engineer
      forgets, and because crew register versus maintenance register is the
      cleanest source of semantic-only findings. Skill §6, `FcomEntryData`,
      `DocumentSchema.FcomEntry`, `FcomEntryRenderer`, `TD.Fcom`.
- [x] Scale the coverage gates in `validate.py`: 20 candidate MODs, 12 open and
      40 historical PAWOs, ≥ 22 distinct ATA 2D, ≥ 6 pure-SEMANTIC and
      ≥ 6 pure-ENTITY findings per program, ≥ 6 of each historical outcome, and
      completions spread over ≥ 14 distinct weeks so the throughput sparkline
      draws a trend rather than a spike.
- [x] Author the new documents (one sub-agent per program)
- [x] `rules.csv` 30 → 80 rows, so the Rules & IPC tab always has something to say
- [x] `tools/validate.py` exits 0
- [x] `src/Aeralis.DatasetCheck --strict` exits 0
- [x] Connector dry run clean — 21,599 nodes, 149,882 edges in 2.6 s

### What each screen needed

| Screen | Was thin because | Fixed by |
| --- | --- | --- |
| Dashboard throughput | 20 historical PAWOs/program, bunched dates | 40/program, ≥ 14 distinct completion weeks |
| Dashboard outcome split | some outcomes near zero | ≥ 6 VALIDATED / REJECTED / DELEGATED each |
| Urgent Actions · Unassigned | no near-due or unowned-ATA work | ≥ 2 of each per program |
| Impact matrix | six columns, no crew-facing row source | FCOM added as a seventh column |
| Analysis (similarity) | 2 semantic-only, 2 entity-only per program | ≥ 6 of each |
| Issue Comparison | nothing explicitly comparable | ≥ 3 MOD issue families per program |
| Facets / 360 | few originators, engine types, operators | spread deliberately |

---

## Known follow-ups / ideas not yet done

- [ ] Pre-computed embedding vectors (would need a model; out of scope here)
- [ ] Negative/near-miss set for precision measurement beyond `expected_findings`
- [ ] CMM (Component Maintenance Manual) and ASM document types
- [ ] Multi-program "fleet" documents (currently every document is single-program)
- [ ] Illustrations / figure images (documents carry figure IDs but no artwork)
- [ ] Cross-program documents (fleet-wide SBs, shared standard-practice chapters)

---

## Delivered

| | |
| --- | --- |
| Dataset | 1,500 documents + 12,609 reference rows; `validate.py` and the C# strict check both clean |
| Connector | 21,599 nodes, 149,882 edges in 2.6 s (dry run) |
| Front-end | builds; 20 renderers, six-tab work order, dashboard |
| Endpoints | 13 + a shared library, in workspace-import layout |

Running the front-end needs the Transpose compiler on PATH:
`dotnet tool install -g Transpose.Compiler`.
