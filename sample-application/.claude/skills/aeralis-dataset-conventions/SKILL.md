---
name: aeralis-dataset-conventions
description: Shared conventions for every artefact in the Aeralis MOD pre-analysis demo dataset — program letters, qualified MSN format, identifier patterns for MOD/SB/AMM/IPC/SRM/TSM/WDM/FCOM/IO/PAWO, part number and FIN patterns, the common JSON document envelope, applicability and embodiment-status models, controlled vocabularies, and folder/file naming. Read this FIRST before generating or editing any reference CSV or document JSON in this repository.
---

# Aeralis dataset conventions

Everything in `data/` obeys this document. The per-type skills
(`aeralis-mod-documents`, `aeralis-sb-documents`, …) extend it; they never
contradict it.

---

## 1. Programs

| Letter | Program | Aircraft types | Models (examples) | SB prefix | MOD number block |
| --- | --- | --- | --- | --- | --- |
| `M` | MERIDIAN Family | `MERIDIAN-42`, `MERIDIAN-46`, `MERIDIAN-42F` | `MRD-42K1`, `MRD-46K1`, `MRD-42FK1` | `MERIDIAN` | `160000`–`169999` |
| `C` | CIRRANE | `CIRRANE-11`, `CIRRANE-13` | `CRN-11G1`, `CRN-13G1` | `CIRRANE` | `110000`–`119999` |
| `T` | TITAN | `TITAN-88` | `TTN-88W1`, `TTN-88W2`, `TTN-88E1` | `TITAN` | `140000`–`149999` |
| `Z` | ZEPHYR Family | `ZEPHYR-18`, `ZEPHYR-20`, `ZEPHYR-22`, `ZEPHYR-24` | `ZPH-18C2`, `ZPH-20N5`, `ZPH-22C4`, `ZPH-22N1`, `ZPH-24C1`, `ZPH-24N7` | `ZEPHYR-22` | `150000`–`159999` |
| `S` | SOLANO Family | `SOLANO-30`, `SOLANO-34`, `SOLANO-38`, `SOLANO-39`, `VANTOR-64`, `VANTOR-68` | `SLN-30R3`, `SLN-34R3`, `SLN-38R1`, `SLN-39R1`, `VTR-64C3`, `VTR-68R2` | `SOLANO` | `130000`–`139999` |

**Program isolation is absolute.** A document under `data/documents/M/` may only
reference reference-data rows whose `programs` column contains `P`, and only
other documents under `data/documents/M/`.

### Qualified MSN

```
{ProgramLetter}{5 digits}      M00417   C00512   T00042   Z12085   S01739
```

MSN blocks per program (see `msn_registry.csv`):

| Program | MSN range | Count |
| --- | --- | --- |
| `M` | `M00001`–`M00700` | 240 |
| `C` | `C00001`–`C00900` | 200 |
| `T` | `T00001`–`T00260` | 120 |
| `Z` | `Z00001`–`Z12500` | 400 |
| `S` | `S00001`–`S02100` | 240 |

Only MSNs present in `msn_registry.csv` may be used in applicability.

---

## 2. Identifier patterns

### 2.1 Documents

| Type | Pattern | Example | File name |
| --- | --- | --- | --- |
| `IO` | `IO-{PROG}-{YY}{NNNN}` | `IO-M-250417` | `IO-M-250417.json` |
| `MOD` | `MOD-{5-6 digits in program block}` | `MOD-166204` | `MOD-166204.json` |
| `PAWO` | `PAWO-{mod digits}-{2 digits}` | `PAWO-166204-01` | `PAWO-166204-01.json` |
| `SB` | `{SBPREFIX}-{ATA2D}-{4 digits}` | `MRD-53-1042` | `MRD-53-1042.json` |
| `AMM` | `AMM-{PROG}-{ATA6D}-{3}-{3}-{Letter}` | `AMM-M-53-21-11-000-001-A` | same `.json` |
| `IPC` | `IPC-{PROG}-{ATA6D}-FIG-{2 digits}` | `IPC-M-53-21-11-FIG-01` | same `.json` |
| `SRM` | `SRM-{PROG}-{ATA6D}-{3 digits}` | `SRM-M-53-21-11-002` | same `.json` |
| `TSM` | `TSM-{PROG}-{ATA6D}-{3}-{3}` | `TSM-M-27-51-00-810-801` | same `.json` |
| `WDM` | `WDM-{PROG}-{ATA4D}-{2 digits}` | `WDM-M-92-21-11` | same `.json` |
| `FCOM` | `FCOM-{PROG}-{DSC\|PRO\|LIM\|PER}-{ATA4D}-{2 digits}` | `FCOM-M-PRO-32-40-03` | same `.json` |

Rules:
- `doc_id` **is** the file stem. No spaces, no `/`, no lowercase.
- MOD numbers must fall in the program's block — that is what makes
  `MOD-166204` unambiguously MERIDIAN without any other lookup.
- SB `{ATA2D}` must equal the SB's own `ata.ata_2d`.
- AMM `{3}-{3}` is the *function code* / *sequence*: use realistic function
  codes — `000` remove, `400` install, `200` inspect, `300` repair,
  `100` clean, `700` test, `500` disconnect, `600` drain, `800` servicing,
  `900` other.
- TSM `810-8xx` = fault-isolation procedures; `812-8xx` = fault symptom.
- WDM uses ATA 92 for wiring, or the system's own ATA 4D.
- FCOM sits on the system's own ATA 4D — never on 92, never on a structure
  chapter below ATA 50 unless the crew genuinely has a control for it.

### 2.2 Structured entity identifiers

| Entity | Pattern | Examples |
| --- | --- | --- |
| **Aeralis part number** | `D` + 6 ATA digits + 7 alphanumerics | `D5321101820000`, `D2751003100200` |
| **Vendor part number** | `{2-4 upper}{3-6 digits}-{2-3 alnum}` | `HTC4021-17`, `SFIM90233-004` |
| **Standard part** | `{AGS\|AES\|EN\|LN}{4-6 digits}{-suffix}` | `AGS5064-04`, `AES0334-12`, `EN6114K4` |
| **FIN** | `{1-4 digits}{2-3 upper}{optional digit}` | `12HQ`, `4000VU`, `1QA1`, `210VU` |
| **Drawing number** | `D` + 6 ATA digits + 5 digits | `D53211100417` |
| **Repair drawing** | `RD-{ATA6D}-{3 digits}` | `RD-53-21-11-004` |
| **IPC figure** | `FIG-{ATA6D}-{2 digits}` | `FIG-53-21-11-01` |
| **Software / LSAP** | `L{A-Z}{1 digit}{A-Z}{7 alnum}` | `LA1P4C1520100` |
| **Software name** | `{SYS}_{FUNC}_V{major}.{minor}` | `FCGU_OPS_V4.2`, `CIDS_DIR_V11.6` |
| **Zone** | 3 digits (major/sub) | `128`, `521`, `831` |

All of the above except repair drawings, IPC figures and zones live in
`data/reference/*.csv` and **must be looked up, never invented**. Repair
drawings and IPC figures are minted by the SRM/IPC documents that own them.

---

## 3. ATA references

Every document carries an `ata` block:

```json
"ata": {
  "ata_2d": "53",
  "ata_4d": "53-21",
  "ata_6d": "53-21-11",
  "title": "Fuselage Section 13 — Frames"
}
```

- `ata_6d` must exist in `data/reference/ata_chapters.csv` (`level=6`).
- `ata_4d` and `ata_2d` must be its literal prefixes.
- `secondary_ata` (optional, array of 4D or 6D strings) records cross-chapter
  impact — e.g. a structural MOD that also touches ATA 25 furnishings. This is
  a *major* source of graph edges; use it on ~40 % of MODs and SBs.

---

## 4. Applicability model

```json
"applicability": {
  "scope": "MSN_LIST",
  "aircraft_types": ["MERIDIAN-42", "MERIDIAN-46"],
  "models": ["MRD-42K1", "MRD-46K1"],
  "msns": ["M00417", "M00423", "M00460"],
  "msn_ranges": [{"from": "M00400", "to": "M00512"}],
  "excluded_msns": ["M00441"],
  "engine_types": ["AUR-8400"],
  "operators": ["LHA", "SIA"],
  "embodiment_condition": {
    "post_mod": ["MOD-166010"],
    "pre_mod": ["MOD-166301"],
    "post_sb": [],
    "pre_sb": []
  }
}
```

- `scope` ∈ `ALL_PROGRAM` · `TYPE` · `MODEL` · `MSN_RANGE` · `MSN_LIST` · `OPERATOR`
- Use `msns` for `MSN_LIST`, `msn_ranges` for `MSN_RANGE`, both empty for
  `ALL_PROGRAM` / `TYPE` / `MODEL`.
- Ranges must be within the program's MSN block, `from < to`.
- Any MSN listed explicitly must exist in `msn_registry.csv` for this program.
- `embodiment_condition` is the **pre-mod / post-mod** logic that in-service
  engineering lives on: an SB applicable only to *pre-MOD-166301* aircraft is
  invalidated the moment MOD-166301 goes to production. Planting exactly these
  situations is what makes the pre-analysis demo interesting.

---

## 5. Embodiment status

```json
"embodiment": {
  "status": "IN_SERVICE",
  "points": ["PRODUCTION", "RETROFIT"],
  "production_effectivity": "M00512",
  "production_cut_in_date": "2023-11-08",
  "retrofit_via": ["MRD-53-1042"]
}
```

`embodiment.status` controlled vocabulary:

| Value | Meaning |
| --- | --- |
| `PROPOSAL` | Idea captured, nothing committed |
| `PRE_ANALYSIS` | **PAWO open** — the state this demo is about |
| `LAUNCHED` | Pre-analysis passed, design started |
| `FROZEN` | Design frozen, awaiting certification |
| `IN_PRODUCTION` | Cut into the production line |
| `IN_SERVICE` | In production **and** retrofittable via SB |
| `SUPERSEDED` | Replaced by a later MOD |
| `CANCELLED` | Dropped |

`embodiment.points` ⊆ `PRODUCTION` · `RETROFIT` · `SPARES` · `NONE`.

---

## 6. The common JSON envelope

**A document file is an array of issues, oldest first. The last element is the document as it
stands.** Every element is a complete envelope, so an issue may differ from the one before it in
any field — which is the point: an issue exists because something in the document had to change.

```json
[
  { "doc_id": "MOD-166204", "revision": "A", "issue": { "letter": "A", "…": "…" }, "…": "…" },
  { "doc_id": "MOD-166204", "revision": "B", "issue": { "letter": "B", "…": "…" }, "…": "…" }
]
```

Most documents have one issue and so a one-element array. See §6.2 for the issue block and the
rules that govern a history.

Every element has exactly these top-level keys, in this order.
Type-specific content goes in `sections` and in one type-specific block.

```json
{
  "doc_id": "MOD-166204",
  "doc_type": "MOD",
  "title": "Reinforcement of Fwd Cargo Door Surround Frames C38 to C42",
  "program": "M",
  "program_name": "MERIDIAN Family",
  "revision": "B",
  "issue": { "...": "see §6.2" },
  "status": "RELEASED",
  "language": "en",
  "ata": { "...": "see §3" },
  "secondary_ata": ["25-52"],
  "applicability": { "...": "see §4" },
  "embodiment": { "...": "see §5" },
  "dates": {
    "created": "2022-04-11",
    "released": "2023-02-27",
    "revised": "2023-11-08",
    "effective": "2023-12-01"
  },
  "abstract": "25–50 words.",
  "sections": {
    "reason": "...",
    "description": "...",
    "effects": "..."
  },
  "entities": {
    "part_numbers": ["D5321101820000", "AGS5064-04"],
    "fins": ["120WT", "121WT"],
    "drawings": ["D53211100417"],
    "software": [],
    "zones": ["128", "129"],
    "figures": ["FIG-53-21-11-01"],
    "repair_drawings": [],
    "msn_mentions": []
  },
  "references": [
    { "doc_id": "MOD-166010", "doc_type": "MOD", "relation": "SUPERSEDES" },
    { "doc_id": "MRD-53-1042", "doc_type": "SB", "relation": "EMBODIED_BY" }
  ],
  "keywords": ["cargo door", "frame reinforcement", "fatigue"],
  "originator": { "department": "ESZS", "site": "Neuhafen", "role": "Design Office" },
  "type_data": { "...": "type-specific block, see the per-type skill" },
  "_meta": {
    "synthetic": true,
    "generator": "authored",
    "dataset_version": "1.0"
  }
}
```

Notes:
- `status` vocabulary depends on type — see the per-type skill.
- `entities.*` arrays may be empty but the **key must be present**.
- `keywords`: 3–8 lowercase phrases. These feed the keyword baseline the demo
  compares embeddings against, so make them genuinely lexical (no synonyms).
- `originator.department`: use plausible 4-letter Aeralis-style codes
  (`ESZS`, `EYIS`, `EDSA`, `EVAA`, `EGAM`, `ELSC`, `EAAM`, `ETGC`).
- `originator.site` ∈ `Valmont` · `Neuhafen` · `Ashcombe` · `Alcadara` · `Wendmark` ·
  `Vaubranche` · `Port-Rivage` · `Lorencourt` · `Mobile` · `Kingsmoor`.

### 6.1 Reference relations (`references[].relation`)

Controlled vocabulary — these become graph edge types:

| Relation | From → To | Meaning |
| --- | --- | --- |
| `SUPERSEDES` | MOD → MOD | Replaces an earlier modification |
| `SUPERSEDED_BY` | MOD → MOD | Inverse |
| `EMBODIED_BY` | MOD → SB | In-service embodiment vehicle |
| `EMBODIES` | SB → MOD | Inverse |
| `PREREQUISITE_OF` | MOD/SB → MOD/SB | Must be embodied first |
| `REQUIRES` | MOD/SB → MOD/SB | Inverse |
| `CONCURRENT_WITH` | any → any | Must be embodied at the same time |
| `IMPACTS` | MOD/SB → AMM/IPC/SRM/TSM/WDM/FCOM | Manual content must change |
| `DOCUMENTED_IN` | MOD/SB → AMM/IPC/… | Procedure lives here |
| `REFERENCES` | any → any | Generic mention |
| `RAISED_BY` | MOD → IO | The order that created it |
| `CREATED` | IO → MOD | Inverse |
| `PRE_ANALYSIS_OF` | PAWO → MOD | The work order's subject |
| `CONFLICTS_WITH` | MOD → any | Known conflict (use sparingly, only where established) |
| `REPLACES_PART_IN` | MOD/SB → IPC | Part list changes |

Relations must be **directionally correct** and their targets must exist.
You do not need to author both directions — `build_graph.py` adds inverses.

---

### 6.2 Issues

```json
"issue": {
  "letter": "B",
  "supersedes": "A",
  "date": "2018-06-12",
  "reason": "Item 20 split into a pre and post modification pair with a matching effectivity note.",
  "raised_by_pawo": "PAWO-160142-01",
  "raised_by_finding": "F03",
  "finding_type": "MANUAL_IMPACT"
}
```

| Rule | |
| --- | --- |
| **Letters from A** | `A`, `B`, `C` … in file order, no gaps, no repeats. |
| **`revision` follows the letter** | `A`→`A`, `B`→`B` … except a service bulletin, which is numbered: `A`→`01`, `B`→`02`. A first issue may also be spelled `-`, which is how the dataset writes an original that was never lettered. |
| **`supersedes`** | the letter before it. `null` only when the letter is `A`. A one-element array whose issue is `C` still says `"supersedes": "B"` — earlier issues existed, this dataset just does not hold them. |
| **Dates ascend** | and `dates.revised` of the last element equals its `issue.date`. |
| **Every issue after the first has a cause** | `raised_by_pawo` + `raised_by_finding` must name a finding of a **completed** work order that targets *this* document, and `finding_type` must match that finding. The issue's date must not precede the work order closing. |
| **`reason`** | one sentence, in the register a change record uses — what changed, not why the modification happened. |

An issue history exists because a pre-analysis that closed `VALIDATED` found something the
document had to say differently. `python3 tools/issue_plan.py` derives which documents need
which issues from the work orders themselves; `--check` is the gate.

**Authoring a history.** The document in the dataset is the **end state** — it already reflects
the resolution. So the earlier issue is written by taking the resolution back out: issue A must
match what the finding's `rationale` describes as wrong, and the last issue must be the document
with the `resolution` applied. Both are yours to alter as far as that needs.

---

## 7. Folder & file naming

```
data/documents/{PROGRAM}/{DOC_TYPE}/{doc_id}.json
```

- `{PROGRAM}` ∈ `P` `C` `R` `N` `L`
- `{DOC_TYPE}` ∈ `IO` `MOD` `PAWO` `SB` `AMM` `IPC` `SRM` `TSM` `WDM` `FCOM`
- JSON: 2-space indent, UTF-8, LF, trailing newline, `ensure_ascii=false`.

Agents write JSONL to `build/authored/{PROGRAM}_{DOC_TYPE}.jsonl`
(and `..._2.jsonl`, `..._3.jsonl` for further batches);
`python3 tools/emit.py` materialises the JSON files.

**One JSONL line is one issue.** Lines sharing a `doc_id` are collected into that document's
array and ordered by `issue.letter`, so a two-issue document is two lines. A line with no
`issue` block is taken as issue A. Emitting a document rewrites its whole array, so **every**
issue of it must be in the JSONL, not only the new one.

---

## 8. Realism checklist for authored text

- [ ] Title is a noun phrase with concrete structure/system names and, where it
      fits, station/frame/stringer/zone identifiers.
- [ ] `abstract` (25–50 w) is not a prefix of `sections.description`.
- [ ] `sections.reason` explains *why now* — in-service finding, fatigue,
      obsolescence, customer request, weight, certification, reliability (MTBUR),
      supplier change, ADs, fuel burn.
- [ ] `sections.description` (80–220 w) names parts, zones, access panels,
      and the actual physical/logical change.
- [ ] `sections.effects` covers weight & balance delta, electrical load,
      interchangeability, MMEL/dispatch, certification classification.
- [ ] Every identifier mentioned in prose also appears in `entities`.
- [ ] Every identifier in `entities` exists in reference data for this program.
- [ ] Dates ordered and plausible for the aircraft's service history.

---

## 9. Anti-patterns

- ❌ Sequentially numbered near-duplicate titles (`… Frames C38 to C42`,
  `… Frames C43 to C47`, `… Frames C48 to C52` …). Two or three such families
  per program is realistic; twenty is a template.
- ❌ Every document using the same 6 ATA chapters. Spread across at least 15.
- ❌ `entities` arrays that are all the same length, or always exactly 3 items.
- ❌ Round dates (every document released on the 1st).
- ❌ Prose that mentions a part number not in `entities.part_numbers`.
- ❌ Referencing another program's documents or MSNs.

---

## 10. Worked exemplar

[`reference/exemplar.json`](reference/exemplar.json) contains five fully worked
documents — an IO, a released MOD, its SB, a candidate MOD, and the PAWO that
pre-analyses it — forming one complete IO → MOD → PAWO chain with real,
resolvable program-`P` reference identifiers.

**Read it before authoring anything.** It is the single best answer to "how long
should this text be", "what does a good `reason` section sound like", and "how
do the entity blocks line up between related documents".

Two rules about it:

1. It is documentation. It lives in the skill, not in `build/authored/`, and is
   never emitted into `data/`.
2. Its identifiers are **reserved**. Never author a document with
   `MOD-1699xx`, `IO-M-2699xx`, `MRD-53-99xx` or `PAWO-1699xx-xx`.
