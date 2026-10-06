---
name: aeralis-dataset-coherence
description: Coherence rules and validation for the Aeralis MOD pre-analysis demo dataset — the referential-integrity contract between documents and reference CSVs, the emit pipeline from build/authored JSONL to data/documents JSON, what tools/validate.py checks, and how to diagnose and fix each error class. Use when validating, fixing or extending the dataset, or when a reference does not resolve.
---

# Dataset coherence

The demo only works if the dataset closes on itself. An SB that embodies a
non-existent MOD, or lists a part number nobody has heard of, produces a graph
with dangling edges and a demo that falls apart on the first click.

**The gate:** `python3 tools/validate.py` must exit 0.

---

## 1. The pipeline

```
tools/gen_reference.py  ──►  data/reference/*.csv          (seeded, idempotent)
                                     │
agents author  ──►  build/authored/{PROG}_{TYPE}[_n].jsonl │
                                     │                     │
                    tools/emit.py ───┴─────────────────────┘
                                     ▼
                        data/documents/{PROG}/{TYPE}/{doc_id}.json
                                     │
                    tools/validate.py ◄──────── data/reference/*.csv
```

`emit.py`:
- reads every `*.jsonl` under `build/authored/`
- normalises key order to the envelope order in `aeralis-dataset-conventions` §6
- fills missing optional keys with empty defaults (never invents identifiers)
- writes `data/documents/{program}/{doc_type}/{doc_id}.json`
  (2-space indent, `ensure_ascii=False`, trailing newline)
- refuses to write if `doc_id` does not match its type's pattern, or if the
  program letter disagrees with `program`
- `--clean` removes documents that no longer have a JSONL source

Re-running `emit.py` is always safe.

---

## 2. Referential contract

| Field | Must resolve to |
| --- | --- |
| `entities.part_numbers[]` | `part_numbers.csv:part_number`, `programs` ∋ this program |
| `entities.fins[]` | `fins.csv:fin`, program-compatible |
| `entities.drawings[]` | `drawings.csv:drawing_number`, program-compatible |
| `entities.software[]` | `software.csv:software_pn`, program-compatible |
| `entities.zones[]` | `zones.csv:zone`, program-compatible |
| `entities.msn_mentions[]` | `msn_registry.csv:msn`, `program` == this program |
| `entities.figures[]` | `FIG-` pattern; must be owned by an IPC doc in-program |
| `entities.repair_drawings[]` | `RD-` pattern; must be owned by an SRM doc in-program |
| `ata.ata_6d` | `ata_chapters.csv` where `level == 6` |
| `applicability.msns[]`, `msn_ranges` endpoints, `excluded_msns[]` | `msn_registry.csv`, same program |
| `applicability.operators[]` | `operators.csv:operator_code` |
| `applicability.aircraft_types[]` / `models[]` | the program's type/model table |
| `applicability.embodiment_condition.{pre,post}_mod[]` | a MOD doc in-program |
| `applicability.embodiment_condition.{pre,post}_sb[]` | an SB doc in-program |
| `references[].doc_id` | a document in the **same program**, matching `doc_type` |
| `type_data.*` part/FIN/software/MOD/SB/AMM identifiers | as above, per the type skill |
| PAWO `expected_findings[].doc_id` / `expected_non_findings[].doc_id` | in-program document |
| PAWO `evidence.shared_*` | present in **both** the PAWO's subject MOD and the finding's document |

---

## 3. What `validate.py` checks

**Structural**
1. Every file under `data/documents/` is valid JSON, UTF-8.
2. All envelope keys present; no unknown top-level keys.
3. `doc_type` matches the containing folder; `program` matches the program folder.
4. `doc_id` == file stem, and matches the ID regex for its type.
5. `doc_id` unique across the whole dataset.
6. MOD numbers fall inside the program's number block; SB prefix matches the
   program; `AMM|IPC|SRM|TSM|WDM|FCOM` embed the correct program letter.

**Referential** — everything in §2.

**Semantic**
7. `ata_2d` / `ata_4d` are prefixes of `ata_6d`; all three exist in the catalogue.
8. `secondary_ata[]` entries exist at level 4 or 6.
9. Dates: `created ≤ released ≤ revised ≤ effective`, all within
   2016-01-01 … 2026-08-07, no future `released` — except the original issue of a
   long-lived manual (`schema.LONG_LIVED_DOC_TYPES` with a history), which is dated
   from its aircraft type's entry into service and may reach back to 1990. Its
   `created` / `released` are carried onto every later issue of the same file, so
   those two keys are backdatable wherever they appear; `revised` and `effective`
   on a later issue belong to that rework and stay inside the window.
10. Status vocabularies conform (per-type tables in the type skills).
11. `revision` is `-` for drafts and `A`..`Z` / `01`..`09` for released docs.
12. Every identifier mentioned in prose that *looks like* a part number, FIN,
    drawing, MOD, SB or AMM task also appears in `entities` / `references`
    (regex sweep — reported as a **warning**, not an error, since prose may
    legitimately name a tool or a consumable).

**Relational**
13. SB → MOD `EMBODIES` exists, target is a released MOD, and the MOD carries the
    matching `EMBODIED_BY`.
14. SB applicability ⊆ embodied MOD applicability (types, models, MSN sets).
15. `SUPERSEDED` MODs have a `SUPERSEDED_BY`; no supersession cycles.
16. Candidate MODs have no SB.
17. Every candidate MOD in `PRE_ANALYSIS` has exactly one PAWO.
18. No orphan manual documents (referenced by something, or referencing something).
19. PAWO `search_scope.entity_seeds` ⊆ subject MOD `entities`.

**Coverage** (reported, and failed if below floor)
20. Per program: document counts match `TODO.md` §Phase 3.
21. Per program: ≥ 15 distinct ATA 2D across MODs+SBs.
22. Per program: all `finding_type` values used; ≥ 2 pure-SEMANTIC and
    ≥ 2 pure-ENTITY findings.
23. Exactly one `hero_scenario: true` PAWO per program.

Exit codes: `0` clean · `1` errors · `2` warnings only (with `--strict`, 1).

Useful flags:
```bash
python3 tools/validate.py --program M          # one program
python3 tools/validate.py --only referential   # one check family
python3 tools/validate.py --warnings           # show warnings too
python3 tools/validate.py --json report.json   # machine-readable
```

---

## 4. Error classes and how to fix them

| Error | Cause | Fix |
| --- | --- | --- |
| `E-REF-PART` unknown part number | Invented instead of looked up | `python3 tools/lookup.py --program X --ata NN --kind parts` and use a real row. **Do not** add a row to the CSV by hand — if the generator genuinely lacks coverage, change `gen_reference.py` and regenerate. |
| `E-REF-DOC` unresolved `doc_id` | Referenced a document not authored yet, or a typo | Check ordering in `CLAUDE.md` §3. Author the target, or drop the reference. |
| `E-PROG-MISMATCH` | Cross-program reference | Program isolation is absolute. Replace with an in-program equivalent. |
| `E-ATA` bad ATA | 6D not in catalogue, or 2D/4D not a prefix | Use `lookup.py --kind ata`. |
| `E-APPL-SUPERSET` | SB effectivity broader than its MOD | Narrow the SB, or broaden the MOD if that is the intent. |
| `E-DUP-ID` | Same `doc_id` twice | Two JSONL batches produced the same ID. Renumber; `emit.py` warns about this too. |
| `E-EVIDENCE` | PAWO evidence not present in both docs | Add the shared entity to whichever document lacks it, or move the finding to `discovery: "SEMANTIC"` with empty evidence. |
| `E-COUNT` | Wrong number of documents | Author the missing ones. Never delete to satisfy a count. |
| `W-PROSE-ENTITY` | Prose names an ID not in `entities` | Usually right to add it to `entities`. If it is a tool or consumable, ignore. |

**Golden rule:** fix the data, not the validator. The validator encodes the
contract the demo application relies on.

---

## 5. Authoring order (dependency-safe)

```
IO  →  MOD  →  SB  →  AMM → IPC → SRM → TSM → WDM → FCOM  →  PAWO
```

Allowed forward references (validated after all documents exist):
- MOD → SB via `EMBODIED_BY` / `embodiment.retrofit_via`
- MOD/SB → manual documents via `IMPACTS` / `DOCUMENTED_IN`
- AMM → AMM via `referenced_tasks`

Everything else must point backwards in this order.

---

## 6. Working incrementally

```bash
# author a batch, then:
python3 tools/emit.py && python3 tools/validate.py --program M --warnings
```

Validate after **every** batch. Fixing 12 dangling references is cheap; fixing
400 is not.
