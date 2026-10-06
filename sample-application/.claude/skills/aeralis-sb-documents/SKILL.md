---
name: aeralis-sb-documents
description: How to author Service Bulletin (SB) documents for the Aeralis MOD pre-analysis demo dataset — SB numbering, revision and compliance vocabularies, effectivity and pre-mod/post-mod conditions, accomplishment instructions, material/kit information, manpower estimates, and the MOD/AMM/IPC linkage rules. Use when generating or editing anything under data/documents/*/SB/.
---

# SB documents (Service Bulletins)

An SB is the **in-service embodiment vehicle** for a MOD: it tells an operator
how to put a modification onto an aircraft already delivered. In pre-analysis,
SBs matter because a new MOD can invalidate an SB's effectivity, contradict its
accomplishment instructions, or reuse its parts.

Read `aeralis-dataset-conventions` and `aeralis-mod-documents` first.

**Volume: 22 per program.** `doc_id` = `{SBPREFIX}-{ATA2D}-{4 digits}`,
e.g. `MRD-53-1042`, `ZPH-27-1188`, `CRN-24-1007`.

Sequence blocks by ATA 2D keep numbers plausible: `1000`–`1299` for the first
SBs of a chapter, `2000`+ for later families. Never reuse a number in a program.

---

## 1. Coverage rules

- **Every SB embodies exactly one MOD** — `references` contains
  `{"relation": "EMBODIES", "doc_type": "MOD", "doc_id": "MOD-…"}`, and that
  MOD's `embodiment.retrofit_via` lists this SB with an `EMBODIED_BY` reference
  back. Both directions are authored for the SB↔MOD pair.
- Only MODs with `embodiment.status` ∈ `IN_SERVICE` · `IN_PRODUCTION` ·
  `SUPERSEDED` may have SBs. Candidates must not.
- 22 SBs over 20 released MODs: 2 MODs get a second SB (a *related* SB — e.g.
  an inspection SB preceding the modification SB). Use
  `PREREQUISITE_OF` / `REQUIRES` between them.
- 3 SBs per program are **revised** (`revision` `02` or `03`) and carry a
  `revision_history` array. 1 SB per program is `CANCELLED` and points at its
  replacement via `SUPERSEDED_BY`.
- 6–10 SBs per program carry `IMPACTS` references to AMM tasks and IPC figures.

---

## 2. `type_data` block

```json
"type_data": {
  "sb_number": "MRD-53-1042",
  "sb_revision": "02",
  "sb_type": "MODIFICATION",
  "compliance": "RECOMMENDED",
  "compliance_time": "At the next scheduled 12-year check, or within 60 months after the date of this Service Bulletin, whichever occurs first.",
  "ad_linked": false,
  "ad_reference": null,
  "reason_code": "FATIGUE",
  "embodied_mod": "MOD-166204",
  "manpower": {
    "elapsed_time_hours": 96,
    "manhours": 148,
    "personnel": 3,
    "downtime_category": "C_CHECK"
  },
  "material": {
    "kit_number": "K5321-1042-01",
    "kit_available_from": "2024-03-01",
    "parts_required": [
      {"part_number": "D5321101820000", "qty": 2, "uom": "EA"},
      {"part_number": "AGS5064-04", "qty": 96, "uom": "EA"}
    ],
    "parts_returned": [
      {"part_number": "D5321101810000", "qty": 2, "uom": "EA"}
    ],
    "consumables": ["Sealant PR-1440 Class B", "Primer, epoxy, chromate-free"],
    "tooling": ["Drill jig 98D53211-401", "Torque wrench 5–50 Nm"]
  },
  "weight_change_kg": 4.6,
  "accomplishment_steps": 9,
  "interchangeability": "NOT_INTERCHANGEABLE",
  "warranty_applicable": true,
  "revision_history": [
    {"revision": "01", "date": "2024-02-14", "reason": "Initial issue"},
    {"revision": "02", "date": "2025-06-30",
     "reason": "Effectivity extended to MERIDIAN-46, torque values corrected in step 6"}
  ]
}
```

Vocabularies:

- `sb_type` ∈ `MODIFICATION` · `INSPECTION` · `REPAIR` · `SOFTWARE_LOAD` ·
  `CONVERSION` · `INFORMATION` · `SUPPLIER`
- `compliance` ∈ `MANDATORY` · `RECOMMENDED` · `DESIRABLE` · `OPTIONAL`
  (`MANDATORY` only when `ad_linked` is true; then `ad_reference` looks like
  `EASA AD 2024-0173` or `FAA AD 2023-14-08`)
- `downtime_category` ∈ `LINE` · `A_CHECK` · `C_CHECK` · `D_CHECK` · `AOG`
- `reason_code` ∈ `FATIGUE` · `CORROSION` · `OBSOLESCENCE` · `RELIABILITY` ·
  `SAFETY` · `PERFORMANCE` · `CUSTOMER` · `PRODUCTION` · `SOFTWARE_DEFECT` ·
  `SUPPLIER_QUALITY`

Every `parts_required` / `parts_returned` part number must exist in
`part_numbers.csv` for this program, and must also appear in
`entities.part_numbers`. `parts_returned` should overlap the embodied MOD's
`type_data.parts_removed`.

---

## 3. `sections`

| Key | Length | Content |
| --- | --- | --- |
| `planning_reason` | 60–130 w | "Reason" section of a real SB — the in-service problem, occurrence counts, consequence if not done. |
| `description` | 80–180 w | What the SB does to the aircraft. |
| `accomplishment_summary` | 100–200 w | Numbered-prose summary of the steps: access, removal, inspection (NDT method), installation, sealing, test, restore. Reference AMM task IDs inline where used. |
| `effectivity_note` | 30–90 w | Which aircraft, and crucially the pre-mod/post-mod condition in words. |
| `material_note` | 30–90 w | Kit availability, long-lead parts, return of removed items. |

`abstract` 25–50 w, standalone.

---

## 4. Effectivity & embodiment condition

SB effectivity is where the pre-analysis conflicts bite. Use
`applicability.embodiment_condition` aggressively:

- ~8 SBs per program are **pre-MOD only** (`pre_mod: ["MOD-…"]`) — they apply
  only to aircraft that have *not* got a later modification. When a candidate
  MOD supersedes that MOD, the SB's effectivity is silently invalidated. Plant
  at least one such chain per program and record it in the PAWO
  `expected_findings` with `finding_type: "EFFECTIVITY_INVALIDATION"`.
- ~5 SBs are **post-MOD only** (`post_mod`) — prerequisite standard required.
- SB `applicability.msns` / `msn_ranges` must be a **subset or equal** of the
  embodied MOD's applicability. Never broader — that is a validator error.

---

## 5. Manual linkage

SBs reference manuals in two ways:

```json
{"doc_id": "AMM-M-53-21-11-000-001-A", "doc_type": "AMM", "relation": "REFERENCES"},
{"doc_id": "IPC-M-53-21-11-FIG-01",    "doc_type": "IPC", "relation": "IMPACTS"},
{"doc_id": "SRM-M-53-21-11-002",       "doc_type": "SRM", "relation": "IMPACTS"}
```

- `REFERENCES` = the accomplishment instructions call the task out.
- `IMPACTS` = the manual content must be updated because of this SB.

AMM tasks called out in `accomplishment_summary` prose **must** appear as
`REFERENCES` entries, and vice versa.

---

## 6. Numbers & plausibility

| Field | Range |
| --- | --- |
| `manpower.manhours` | 4 … 2 400 |
| `manpower.elapsed_time_hours` | ≥ manhours / personnel |
| `manpower.personnel` | 1 … 8 |
| `accomplishment_steps` | 3 … 24 |
| `weight_change_kg` | −25 … +120 (must equal the embodied MOD's `weight_impact_kg` unless the SB is partial — then say so in `material_note`) |
| kit part quantities | fasteners 20–400, structural parts 1–8 |

---

## 7. Checklist

- [ ] 22 SBs, unique numbers, ATA 2D in the number matches `ata.ata_2d`
- [ ] Each embodies exactly one released MOD; back-reference present on the MOD
- [ ] No SB on a candidate or `CANCELLED` MOD
- [ ] 3 revised (with `revision_history`), 1 cancelled with `SUPERSEDED_BY`
- [ ] ≥ 8 with `pre_mod` effectivity conditions; ≥ 1 invalidated by a candidate
- [ ] SB effectivity ⊆ embodied MOD effectivity
- [ ] Every part in `material.*` exists and is in `entities.part_numbers`
- [ ] Every AMM task named in prose is in `references`
- [ ] `MANDATORY` ⇔ `ad_linked: true`
