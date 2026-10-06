# CLAUDE.md — working agreement for the sample application

> This folder is the **reference implementation** of the template it sits in. The template's
> own working agreement is [`../CLAUDE.md`](../CLAUDE.md); this one records how *this*
> application was built, and is what the skills point at when they say "in the reference
> implementation".

This folder produces a **synthetic but internally coherent** dataset for a demo of
Aeralis Technical Data authoring, focused on **MOD Pre-Analysis**, plus the
Curiosity connector that ingests it and the front-end that presents it.
Read [`README.md`](README.md) for the use case before doing anything.

Three deliverables, in dependency order:

| # | What | Where | Skill |
| --- | --- | --- | --- |
| 1 | The dataset | `data/` | `aeralis-dataset-conventions` + the per-type skills |
| 2 | The graph connector | `src/Aeralis.Connector/` | `aeralis-graph-connector` |
| 3 | The front-end + endpoints | `src/Aeralis.FrontEnd/`, `src/Aeralis.Endpoints/` | `aeralis-frontend` |

Two projects sit beside them and do not read `data/` at all:

- [`src/Aeralis.Presentation/`](src/Aeralis.Presentation/README.md) renders **S1000D**
  CSDB objects to PDF — one XSL-FO presentation stylesheet per object type — on top of
  the `S1kdTools.Core` NuGet package. Its sample objects are synthetic like everything
  else, and its rendered PDFs are a checked-in snapshot under `samples/presentation/`,
  refreshed with `dotnet test src/Aeralis.Presentation.Tests`.
- [`src/Aeralis.Illustrations/`](src/Aeralis.Illustrations/README.md) draws Aeralis'
  general-arrangement three-views with technical callouts, plain or laid out as an A4
  manual page, optionally rendered as a scan of a printed one. The drawings are generated
  by `tools/gen_aircraft_views.py` — Aeralis is fictional, so there is no published sheet
  to trace.

[`src/Aeralis.Attachments/`](src/Aeralis.Attachments/README.md) is the join between
those two and the dataset: it turns every document into the file a technical data system
would hold for it — the published PDF with its illustrations, or the manual page a figure
is printed on — and the connector uploads that as an attachment of the document's node.
It belongs to deliverable 2, not beside it: change what a document renders to and you
change what the graph holds.

**The connector's graph schema is the front-end's contract.** Finish and build
the connector before writing a renderer against a node type.

---

## 0. Non-negotiables

1. **Everything under `data/` is synthetic.** Never copy real Aeralis part numbers,
   MSNs, SB numbers, MOD numbers or manual text. Generate plausible-looking identifiers
   using the patterns in
   [`.claude/skills/aeralis-dataset-conventions`](.claude/skills/aeralis-dataset-conventions/SKILL.md).
   Every document carries `"_meta": {"synthetic": true}`. Aeralis itself is invented, and
   so is every one of its aircraft programmes, types, engines and sites — nothing in this
   repository names a real airframer, a real aircraft or a real engine.
2. **Coherence is a hard gate.** Any identifier a document references —
   part number, FIN, drawing, software, MSN, zone, ATA 6D, or another document —
   **must already exist** in the dataset. `python3 tools/validate.py` must exit 0.
3. **Program isolation.** A document in program `M` references only `M`
   reference data and only `M` documents. The one exception is
   `cross_program_note`, a free-text field; it never carries a resolvable ID.
4. **One JSON file per document**, at
   `data/documents/{PROGRAM}/{DOC_TYPE}/{DOC_ID_SAFE}.json`. The file is an **array of
   issues**, oldest first; the last element is the document as it stands. Most documents
   have one issue. See `aeralis-dataset-conventions` §6.2.
5. **Tabular master data is CSV**, at `data/reference/*.csv`, UTF-8, `\n`,
   quoted only when needed, header row always present.
6. **Deterministic generators.** Anything programmatic is seeded
   (`RANDOM_SEED = 20260807`) and idempotent — re-running produces byte-identical
   output.

   **One exception, and it is load-bearing.** `data/reference/fins.csv`,
   `software.csv` and the row order of `part_numbers.csv` were carried across the
   rename from the airframer this application was first written for, not regenerated:
   the generator draws a FIN's default part from an in-memory list ordered by part
   number, and a software load carries its programme letter inside its own part number,
   so both streams moved when the identifiers changed. `gen_reference.py` still produces
   a corpus of exactly the same shape and passes every gate — but a different one, and
   the 1500 documents under `data/documents/` cite the rows that are committed. **Do not
   re-run it on this dataset.** Re-running is for standing up a *new* corpus, which then
   has to be authored against the reference data it produced.
   `ata_catalog.py` has no such dependency and does reproduce byte-identically.

---

## 1. Division of labour: programmatic vs authored

| Generated by a **script** (`tools/gen_reference.py`) | **Authored** by a model |
| --- | --- |
| Part numbers + descriptions | MOD titles, reason-for-change, description, effects |
| FINs + descriptions | SB titles, reason, accomplishment summary |
| MSN registry, operators, suppliers | AMM/IPC/SRM/TSM/WDM/FCOM prose |
| Drawing numbers, software/LSAP, zones | IO request text, PAWO findings & rationale |
| ATA 2D/4D/6D catalogue | All document-to-document relationships |

Never hand-author reference CSV rows, and never script-template document prose.
The whole point of the demo is that the *text* is realistic enough for
embeddings to do meaningful work.

---

## 2. The authoring pipeline

Agents **do not** write `data/documents/**` directly. They write JSONL to
`build/authored/` and the emitter materialises it:

```
build/authored/{PROGRAM}_{DOC_TYPE}.jsonl     ← one JSON document per line
        │
        │  python3 tools/emit.py
        ▼
data/documents/{PROGRAM}/{DOC_TYPE}/{id}.json ← pretty-printed, key-ordered
```

Why: 125 documents per program is ~125 `Write` calls otherwise. JSONL lets an
agent emit 10–15 documents per call, and the emitter guarantees consistent
formatting, key ordering and file naming.

**Appending is safe.** `emit.py` reads every `.jsonl` in `build/authored/` and
overwrites the corresponding JSON files; later lines with the same `doc_id` win,
and a warning is printed. Write JSONL in batches — do not try to produce one
enormous file in a single call.

`build/` is gitignored. `data/` is committed.

---

## 3. Order of operations (dependencies are strict)

```
1. tools/ata_catalog.py + tools/gen_reference.py   → data/reference/*.csv
2. IO documents          (depend on: reference)
3. MOD documents         (depend on: reference, IO)
4. SB documents          (depend on: reference, MOD)
5. AMM/IPC/SRM/TSM/WDM/FCOM  (depend on: reference, MOD, SB)
6. PAWO documents        (depend on: everything above — findings point at them)
7. tools/validate.py                     → must exit 0
8. src/Aeralis.DatasetCheck --strict    → must exit 0
9. tools/issue_plan.py --check           → must exit 0 (the issue history the
   completed pre-analyses imply)
```

The graph itself is **not** built in this repository. The application builds it
downstream from applicability, ATA, embodiment condition, entities and
references. Our job is to make sure every one of those fields is populated and
resolves.

A document may only reference something produced in an **earlier or equal**
step. `SB → MOD` is fine; `MOD → SB` is expressed with
`"embodied_by": ["MRD-53-1042"]` and is the *one* allowed forward reference —
the validator understands it and checks it after all documents exist.

---

## 4. Writing realistic text

- Use Aeralis/aerospace register: *embodiment*, *effectivity*, *applicability*,
  *post-mod*, *pre-mod*, *interchangeability*, *concession*, *accomplishment*,
  *station*, *stringer*, *frame*, *doubler*, *LSAP*, *dispatch*, *MEL*.
- Titles read like real ones: verb-less noun phrases, uppercase-ish nouns —
  *"Reinforcement of Fwd Cargo Door Surround Frames C38 to C42"*, not
  *"Fix the cargo door"*.
- Descriptions: 80–220 words. Abstracts: 25–50 words. They must be *different*
  texts, not one truncated from the other — the demo embeds both.
- **Vary vocabulary deliberately.** Semantic search only demos well if some
  related documents share *no* keywords: one says *"skin panel doubler"*, the
  related one says *"external repair patch on lower shell"*. Plant a few such
  pairs per program and record them in the PAWO `expected_findings` with
  `"discovery": "SEMANTIC"`.
- Conversely plant pairs that share **no wording at all** but do share a part
  number or FIN, with `"discovery": "ENTITY"`.
- Dates are plausible and ordered: `created ≤ released ≤ revised`, within
  2016-01-01 … 2026-08-07 — with one exception. Where the dataset holds a
  long-lived manual's **original** issue (AMM, IPC, SRM, TSM, WDM, FCOM with more
  than one issue), that issue is dated from its aircraft type's entry into
  service, which for the ZEPHYR-22 family is 1990. `created` and `released` describe
  that original publication and are carried onto every later issue of the file.
  See `tools/backdate_original_issues.py`; `validate.py` allows it only there.

---

## 5. Reference-data lookups

Before authoring for program `X`, load the slice you are allowed to use:

```bash
python3 tools/lookup.py --program M --ata 53 --limit 40
```

prints matching part numbers, FINs, drawings, zones and MSNs for that program
and ATA chapter. Use only what it prints. If you need something it does not
have, that is a signal the reference data is missing a row — say so, do not
invent the identifier.

---

## 6. Validation

`python3 tools/validate.py` checks:

- JSON parses; required envelope keys present; `doc_type` matches folder
- `doc_id` matches its file name and the ID pattern for its type
- Program letter consistent between `program`, folder, MSNs and SB prefix
- Every `part_numbers` / `fins` / `drawings` / `software` / `zones` entry exists
  in the reference CSVs **and is applicable to that program**
- Every `ata_6d` exists in `ata_chapters.csv`, and `ata_2d`/`ata_4d` are its
  prefixes
- Every `references[].doc_id` resolves to a document in the same program
- Every MSN in `applicability` exists in `msn_registry.csv` with the right program
- `expected_findings[].doc_id` in PAWOs resolve
- No duplicate `doc_id` across the dataset
- Date ordering, revision-letter sanity, and status-vocabulary conformance

Fix the data, not the validator, unless the validator is genuinely wrong.

### 6.1 The C# schema gate

```bash
dotnet run --project src/Aeralis.DatasetCheck -- --root . --strict
```

`src/Aeralis.Dataset` holds one C# class per data type — `System.Text.Json`
for documents, `CsvHelper` for reference CSVs. It is the **executable schema**:
if a document deserialises with an unmapped JSON member, or carries a
controlled-vocabulary value the enums do not know, the run fails. The checker
also prints the file count per document type and the total megabytes read.

Change a schema ⇒ change the skill, then the C# model, then `validate.py`.
See `.claude/skills/aeralis-dataset-dotnet-reader`.

---

## 6.2 Working against the Curiosity packages

`Curiosity.Library`, `Curiosity.FrontEnd` and `Tesserae` are consumed from
NuGet, but their **source is the contract**:

| Repo | Clone | Use it for |
| --- | --- | --- |
| `curiosity-ai/mosaik` | `/workspace/mosaik` | `NodeRendererBase`, `SearchArea`, `App.Settings`, endpoint execution scopes, the definitions import format |
| `curiosity-ai/tesserae` | `/workspace/tesserae` | every UI component's real constructor and fluent surface |
| `curiosity-ai/documentation` | `/workspace/documentation` | orientation only |

**The published documentation lags the repos.** Never write against a signature
you took from a doc page — open the source file and read it. Every API named in
`aeralis-graph-connector` and `aeralis-frontend` carries the file and line it was
verified at; keep that habit when you add more.

---

## 7. Commits

- This is the reference implementation of the template it sits in. Commit changes to
  it separately from changes to the template itself.
- Commit in logical chunks: docs+skills, tools, reference data, then one commit
  per program, then graph.
- Never commit `build/`.
- Do not open a PR unless explicitly asked.

---

## 8. When delegating to sub-agents

One sub-agent per **aircraft program**. Give it:
- its program letter and the aircraft types it owns,
- the exact per-type volumes from `TODO.md`,
- the instruction to read
  `.claude/skills/aeralis-dataset-conventions/SKILL.md` first, then the skill for
  each doc type,
- the reminder that it owns *all* documents in its program, so it is solely
  responsible for their cross-references closing.

Review every agent's output with `tools/validate.py` before accepting it.
