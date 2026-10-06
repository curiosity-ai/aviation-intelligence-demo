# Aeralis Technical Data — the sample application

The worked example for the [Curiosity application template](../README.md): a complete
application for **authoring aircraft technical data**, focused on the **Pre-Analysis** step
of the **MOD (Modification)** lifecycle. Corpus, connector, workspace definitions, endpoints,
AI layer and front end, built layer by layer against the skills in
[`../.claude/skills/`](../.claude/skills).

> ⚠️ **Everything here is invented.** Aeralis is a fictional airframer; so are its five
> aircraft programmes, every type and variant, every engine, every operator, supplier and
> site, and every part number, MSN, Service Bulletin and modification dossier. Identifiers
> are generated to *look* plausible so that a demo feels real; they are not, and must not be
> treated as, airworthiness data.

Read [`CLAUDE.md`](CLAUDE.md) for the working agreement this was built under.

---

## 1. The business use case

### 1.1 The lifecycle we are demonstrating

```
   Airline / Operator need
            │
            ▼
   ┌────────────────────┐
   │ IO — Internal Order│   "What the operator needs done"
   └────────┬───────────┘   structural change, repair, cabin change,
            │               system upgrade, software load, …
            ▼
   ┌────────────────────┐
   │ MOD — Modification │   A candidate modification is opened against the IO
   └────────┬───────────┘
            │
            ▼
   ┌──────────────────────────────┐
   │ PAWO — Pre-Analysis Work Order│  ◀── THE STEP THIS DEMO IS ABOUT
   └──────────────────────────────┘
            │
            ▼
   Launch / Freeze / Reject decision → design → SB → in-service embodiment
```

1. An **IO (Internal Order)** is raised. It carries what the airline needs:
   a structural change, a repair, a cabin reconfiguration, a system upgrade,
   a software update, a weight-saving initiative, an obsolescence replacement, …
2. Once the IO is accepted, a **MOD** is created as the engineering vehicle for
   the change.
3. A **PAWO (Pre-Analysis Work Order)** is raised against that MOD.

### 1.2 What actually happens during Pre-Analysis

Pre-analysis is a **search-and-impact problem**, not a design problem. The
engineer must answer:

> *"Across every piece of Aeralis technical documentation applicable to this
> aircraft / type / program, what already exists that this MOD will conflict
> with, depend on, supersede, or invalidate?"*

Concretely, the pre-analyst is hunting for:

| Question | Example |
| --- | --- |
| Is someone else already changing this? | Another candidate MOD touching the same frame/stringer intersection |
| Does a released MOD already cover it? | A post-MOD standard already embodies the requested improvement |
| Will this break an in-service repair? | An SRM repair drawing on the same skin panel |
| Which manuals must be updated? | AMM tasks, IPC figures, TSM procedures, WDM sheets |
| Which SBs become invalid? | An SB whose pre-MOD effectivity no longer holds |
| Is the hardware shared? | Same **Part Number** or **FIN** used by an unrelated system |
| Is the software coupled? | Same LSAP / software load part number |
| Is the routing shared? | Same zone, same bracket, same drawing |

### 1.3 How the application finds those impacts

Two complementary retrieval strategies over one graph:

**A. Semantic similarity (embeddings)**
- Title ↔ title similarity
- Abstract / description ↔ description similarity
- "Reason for change" ↔ "Reason for change" similarity
- Used to catch *conceptually* related work that shares no identifiers.

**B. Entity linking (exact, pattern-based)**
- **Part Numbers** — Aeralis, vendor and standard-part patterns
- **FINs** — Functional Item Numbers
- **Drawing Numbers**
- **Software / LSAP** part numbers and names
- **ATA references** (2D / 4D / 6D)
- **Structured identifiers** — IPC *Figures*, SRM *Repair Drawings*,
  AMM *Task numbers*, TSM *fault codes*, WDM *sheet numbers*
- **Zones** and **MSNs**

**The graph.** The application builds a graph in which every document is a node.
Edges are derived from:
- **Applicability** — qualified MSN, aircraft model, type, program
- **ATA** — 2D / 4D / 6D hierarchy
- **Embodiment status** — pre-MOD / post-MOD chains, production vs retrofit
- **Entity co-occurrence** — shared PN, FIN, drawing, software, zone, figure
- **Explicit document references** — SB *embodies* MOD, MOD *supersedes* MOD,
  SB *references* AMM task, IPC figure *lists* PN, …

A candidate MOD is then scored against the graph: nearest neighbours by
embedding, plus n-hop entity overlap, plus applicability intersection.

> **Scope of the dataset:** the graph itself is *not* built by the dataset — it is
> built downstream by the application using the techniques above. What this
> repository delivers is the **source data** the graph is built from: the
> reference master data and the documents, with every edge-bearing field
> (applicability, ATA, embodiment condition, entities, references) populated
> and guaranteed to resolve.

### 1.4 Why the dataset must be coherent

The demo is only convincing if the graph closes. If an SB says it embodies
`MOD 166204`, that MOD must exist. If it lists part number `D5321101820000`,
that part must exist in `part_numbers.csv`, be applicable to the same program,
and sit under a compatible ATA chapter. Coherence is enforced by
`tools/validate.py` and is a hard gate — see
[`.claude/skills/aeralis-dataset-coherence`](.claude/skills/aeralis-dataset-coherence/SKILL.md).

---

## 2. Aircraft programs

Aeralis program letters are used as the top-level partition of the dataset, and
as the first character of every qualified MSN.

| Letter | Program | Aircraft types in dataset | SB prefix |
| --- | --- | --- | --- |
| `M` | MERIDIAN Family | MERIDIAN-42, MERIDIAN-46, MERIDIAN-42F | `MRD` |
| `C` | CIRRANE | CIRRANE-11, CIRRANE-13 | `CRN` |
| `T` | TITAN | TITAN-88 | `TTN` |
| `Z` | ZEPHYR Family | ZEPHYR-18, ZEPHYR-20, ZEPHYR-22, ZEPHYR-24 (classic + Evo) | `ZPH` |
| `S` | SOLANO Family | SOLANO-30/-34, SOLANO-38/-39, VANTOR-64/-68 | `SLN` |

A type is identified two ways: by its **marketing designation** — `MERIDIAN-42` — and, where a
document is specific about the engine and rating, by its **type designation** — `MRD-42K1`.
Both appear in `applicability`, as `aircraft_types` and `models`.

**Qualified MSN** = `{ProgramLetter}{5 digits}` — e.g. `M00417`, `Z12085`, `T00042`.

---

## 3. Repository layout

```
.
├── README.md                     ← you are here
├── CLAUDE.md                     ← working agreement for agents in this repo
├── TODO.md                       ← generation plan & progress
├── .claude/skills/               ← one skill per data type (how to generate it)
│   ├── aeralis-dataset-conventions/    shared IDs, schema envelope, applicability
│   ├── aeralis-reference-data/         CSV master data (PN, FIN, MSN, …)
│   ├── aeralis-mod-documents/          MOD dossiers
│   ├── aeralis-sb-documents/           Service Bulletins
│   ├── aeralis-manual-documents/       AMM / IPC / SRM / TSM / WDM
│   ├── aeralis-io-pawo-documents/      IO + PAWO (incl. ground-truth findings)
│   ├── aeralis-dataset-coherence/      referential validation
│   └── aeralis-dataset-dotnet-reader/  the C# reader / schema check
├── tools/                        ← generators & validator (stdlib Python 3)
│   ├── ata_catalog.py
│   ├── gen_reference.py
│   ├── lookup.py
│   ├── emit.py
│   └── validate.py
├── src/                          ← C# reader (net10.0)
│   ├── Aeralis.slnx
│   ├── Aeralis.Dataset/         one class per data type
│   ├── Aeralis.DatasetCheck/    parses everything, prints counts + MB
│   ├── Aeralis.Connector/       ingests the dataset into a Curiosity workspace graph
│   ├── Aeralis.Presentation/    S1000D → PDF: one XSL-FO stylesheet per object type
│   ├── Aeralis.Presentation.Tests/  a sample object per type + its rendered PDF
│   ├── Aeralis.Illustrations/   annotated three-views and A4 manual pages
│   └── Aeralis.Attachments/     every document → the file it would be published as
├── samples/
│   ├── presentation/             ← the rendered sample PDFs, one per CSDB object type
│   └── attachments/              ← one rendered document per type, with source + artwork
├── build/authored/               ← agent-authored JSONL staging (gitignored)
└── data/
    ├── reference/                ← CSV master data
    │   ├── ata_chapters.csv
    │   ├── part_numbers.csv
    │   ├── fins.csv
    │   ├── msn_registry.csv
    │   ├── operators.csv
    │   ├── suppliers.csv
    │   ├── drawings.csv
    │   ├── software.csv
    │   └── zones.csv
    └── documents/
        └── {M|C|T|Z|S}/
            ├── MOD/   MOD-166204.json
            ├── SB/    MRD-53-1042.json
            ├── AMM/   AMM-M-53-21-11-000-001-A.json
            ├── IPC/   IPC-M-53-21-11-FIG-01.json
            ├── SRM/   SRM-M-53-21-11-002.json
            ├── TSM/   TSM-M-27-51-00-810-801.json
            ├── WDM/   WDM-M-92-21-11.json
            ├── IO/    IO-M-250417.json
            └── PAWO/  PAWO-166204-01.json
```

Every document is **one JSON file**, partitioned **by program**, then by
**document type**.

---

## 3.1 Rendering S1000D objects to PDF

The demo dataset under `data/` is JSON, but the publications an operator
actually receives are S1000D. `src/Aeralis.Presentation` covers that side: an
XSL-FO presentation stylesheet for **every S1000D CSDB object type** — the 22
data module schemas plus publication modules, data management lists, data
dispatch notes, comments, ICN metadata files, SCORM content packages and data
update files — and an API that renders one to a PDF stream.

```csharp
var dm = S1000DPresentation.Load("DMC-MRDX-A-27-81-00-00A-720A-A_002-00_EN-GB.XML");
using Stream pdf = S1000DPresentation.RenderToPdf(dm);      // type detected from the object
```

The layout follows a page-oriented civil aircraft manual: publisher and
publication title in the running header, object code and issue in the footer, an
identification and status title block, ATA-style step numbering
(`1.` / `A.` / `(1)` / `(a)`), boxed warnings and cautions, and the fixed table
shapes each schema calls for. Layout is transformed and rendered in-process by
the `S1kdTools.Core` NuGet package, which drives the FOP.Sharp engine — no
external XSL-FO processor and no Java.

See [`src/Aeralis.Presentation/README.md`](src/Aeralis.Presentation/README.md)
for the options, and [`samples/presentation/`](samples/presentation) for a
rendered sample of every object type.

---

## 3.2 Every document as the file it would be published as

`src/Aeralis.Attachments` joins the two rendering projects to the dataset. Each
document is projected into the S1000D CSDB object it would be issued as and
rendered to PDF, with one or two **annotated aircraft illustrations** in its
content pointing at the part of the airframe it is about; an IPC figure instead
becomes the A4 manual page it is printed on. The connector uploads the result and
hangs it off the document's node with `_HasAttachment` / `_AttachmentOf`, so a
search result opens as the published document rather than as JSON.

```bash
dotnet run --project src/Aeralis.Attachments -- --root . --out build/attachments
dotnet run --project src/Aeralis.Attachments -- --root . --out /tmp/x --doc MOD-160142
```

A publication for an aircraft that entered service **before 2000** is rebuilt as a
scan of a printed page — skew, paper grain, dust, JPEG blocking — with the original
text written back over it invisibly, so it is still searchable. Which aircraft a
document is drawn as decides all of that: the mark in the header, the typeface, and
whether it is held as a scan.

The **input** is kept too, not only the output. The CSDB object the presentation
layer was handed lands as a `DocumentationSource` node keyed `{DocType}:{DocId}`,
carrying the markup itself, with the illustrations it names attached to that node
— so a rendered page can be explained without re-running anything.

A rendered sample of every document type, with its source and artwork, is checked
in under [`samples/attachments/`](samples/attachments).

See [`src/Aeralis.Attachments/README.md`](src/Aeralis.Attachments/README.md).

---

## 4. Document types

| Type | Count / program | What it is |
| --- | --- | --- |
| `IO` | 6 | Internal Order — the operator/business request that starts everything |
| `MOD` | 30 | Modification dossier. ~20 released/in-service, ~10 candidates under pre-analysis |
| `PAWO` | 6 | Pre-Analysis Work Order, with **ground-truth expected findings** |
| `SB` | 22 | Service Bulletin — in-service embodiment of a MOD |
| `AMM` | 30 | Aircraft Maintenance Manual task |
| `IPC` | 10 | Illustrated Parts Catalogue figure |
| `SRM` | 8 | Structure Repair Manual repair, with repair drawings |
| `TSM` | 8 | Trouble Shooting Manual procedure |
| `WDM` | 5 | Wiring Diagram Manual sheet |

**≈ 125 documents × 5 programs ≈ 625 documents**, on top of ~12 000 rows of
reference master data.

---

## 5. The demo storyline

Each program carries one **hero scenario**: an IO → MOD candidate → PAWO chain
where the conflicts are *planted deliberately* and recorded in the PAWO's
`expected_findings`. That gives the demo both a narrative and a scoring set:
you can measure whether embeddings + entity linking recover the known answers.

The flagship scenario is on **P (MERIDIAN)**: a cargo-door surround reinforcement
requested by an operator, which collides with an in-service SRM repair, an
already-launched candidate MOD on the adjacent frame, an SB whose pre-MOD
effectivity is invalidated, and a shared bracket part number used by an
unrelated ATA 92 wiring route.

---

## 6. Quick start

```bash
# 1. Master data (deterministic, seeded — safe to re-run)
python3 tools/gen_reference.py

# 2. Materialise agent-authored documents from build/authored/*.jsonl
python3 tools/emit.py

# 3. Coherence gate — every reference must resolve
python3 tools/validate.py

# 4. Structural gate — parse the whole dataset with the C# models,
#    print file counts per type and megabytes read
dotnet run --project src/Aeralis.DatasetCheck -- --root . --strict
```

The Python scripts are pure standard library — no dependencies. The C# reader
targets `net10.0` and uses `System.Text.Json` + `CsvHelper`.

### The two gates

| Gate | Answers |
| --- | --- |
| `tools/validate.py` | *Does every reference resolve?* — referential integrity across documents and reference CSVs |
| `src/Aeralis.DatasetCheck` | *Does every file match the schema?* — the C# models are the executable schema; unmapped JSON members and unknown vocabulary values fail the run |

---

## 7. Regenerating / extending

Read [`CLAUDE.md`](CLAUDE.md) first, then the skill for the data type you want.
The skills are the specification; this README is the orientation.
