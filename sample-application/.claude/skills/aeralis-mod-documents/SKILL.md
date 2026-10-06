---
name: aeralis-mod-documents
description: How to author MOD (Modification) dossier documents for the Aeralis MOD pre-analysis demo dataset — the JSON schema, status lifecycle, released vs candidate MODs, per-program volumes, conflict planting between MODs, and the writing rules for reason/description/effects text. Use when generating or editing anything under data/documents/*/MOD/.
---

# MOD documents

A MOD is the engineering vehicle for a change to an aircraft standard. It is
the central node of the whole dataset: IOs create MODs, PAWOs pre-analyse MODs,
SBs embody MODs, and manuals are impacted by MODs.

Read `aeralis-dataset-conventions` first. This skill only adds MOD specifics.

**Volume: 30 per program** — 20 *released* (already part of the aircraft
standard) + 10 *candidates* (open, in `PRE_ANALYSIS` or `PROPOSAL`).
`doc_id` = `MOD-{number in the program's block}`.

---

## 1. Released vs candidate

| | Released (20) | Candidate (10) |
| --- | --- | --- |
| `embodiment.status` | `IN_SERVICE` (12), `IN_PRODUCTION` (4), `SUPERSEDED` (3), `CANCELLED` (1) | `PRE_ANALYSIS` (5–6), `PROPOSAL` (2), `LAUNCHED` (2–3) |
| `status` | `RELEASED` | `DRAFT` |
| `dates.released` | set | `null` |
| `revision` | `A`–`D` | `-` (initial) |
| `references` include | `EMBODIED_BY` an SB, `IMPACTS` manuals | `RAISED_BY` an IO |
| Has a PAWO | no | 6 of them do — one per PAWO, see `aeralis-io-pawo-documents` §2 |

The candidates are what the demo searches *with*; the released MODs (plus SBs
and manuals) are what it searches *against*.

---

## 2. `type_data` block

```json
"type_data": {
  "mod_number": 166204,
  "mod_class": "MAJOR",
  "change_category": "STRUCTURE",
  "trigger": "IN_SERVICE_FINDING",
  "customer_driven": true,
  "certification": {
    "impact": "MAJOR_CHANGE",
    "authority": ["EASA", "FAA"],
    "cs25_paragraphs": ["CS 25.571", "CS 25.365"],
    "requires_flight_test": false
  },
  "weight_impact_kg": 4.6,
  "balance_impact": "Negligible, CG shift < 0.05 % MAC",
  "electrical_load_impact_va": 0,
  "interchangeability": "NOT_INTERCHANGEABLE",
  "parts_added": ["D5321101820000", "AGS5064-04"],
  "parts_removed": ["D5321101810000"],
  "parts_modified": [],
  "fins_affected": ["120WT"],
  "software_affected": [],
  "estimated_manhours": 148,
  "estimated_cost_keur": 620,
  "lead_time_weeks": 34,
  "production_impact": "Cut-in at MSN M00512, no line stoppage expected",
  "risk_level": "MEDIUM"
}
```

Controlled vocabularies:

- `mod_class` ∈ `MAJOR` · `MINOR` (Part 21 classification)
- `change_category` ∈ `STRUCTURE` · `SYSTEM` · `CABIN` · `SOFTWARE` ·
  `POWERPLANT` · `ELECTRICAL` · `AVIONICS` · `DOCUMENTATION` · `REPAIR`
- `trigger` ∈ `IN_SERVICE_FINDING` · `CUSTOMER_REQUEST` · `OBSOLESCENCE` ·
  `WEIGHT_SAVING` · `RELIABILITY` · `AIRWORTHINESS_DIRECTIVE` ·
  `CERTIFICATION` · `SUPPLIER_CHANGE` · `COST_REDUCTION` · `FUEL_BURN` ·
  `PRODUCTION_IMPROVEMENT` · `SAFETY`
- `certification.impact` ∈ `NO_IMPACT` · `MINOR_CHANGE` · `MAJOR_CHANGE` · `STC`
- `interchangeability` ∈ `INTERCHANGEABLE` · `ONE_WAY_INTERCHANGEABLE` ·
  `NOT_INTERCHANGEABLE`
- `risk_level` ∈ `LOW` · `MEDIUM` · `HIGH`

`parts_added` / `parts_removed` / `fins_affected` / `software_affected` must be
a subset of the corresponding `entities` arrays, and must resolve in the
reference CSVs for this program.

Numeric plausibility: `weight_impact_kg` −40 … +180 (software MODs: 0);
`estimated_manhours` 8 … 4 000; `estimated_cost_keur` 15 … 9 000;
`lead_time_weeks` 6 … 96.

---

## 3. `sections`

| Key | Length | Content |
| --- | --- | --- |
| `reason` | 60–140 w | Why the change is needed. Cite the in-service event, the finding, the AD, the customer, the obsolescence notice. Include a fleet-scale number where natural ("17 occurrences across 340 aircraft"). |
| `description` | 100–220 w | The physical/logical change. Name frames/stringers/stations, panels, harness routes, LRUs, software loads. Reference drawings and part numbers explicitly. |
| `effects` | 60–140 w | Weight & balance, electrical load, interchangeability, maintenance impact, MMEL/dispatch, manual updates required, spares impact. |
| `embodiment_note` | 30–80 w | How it gets into aircraft: production cut-in MSN, retrofit SB, kit availability, downtime. |
| `open_points` | 20–70 w, **candidates only** | What pre-analysis must resolve. This is the text the demo shows next to the search results. |

`abstract` (25–50 w) is a standalone summary, written last, in different words
from `description`.

---

## 4. ATA spread

Across the 30 MODs of a program, aim for:

| Domain | MODs | Typical ATA 2D |
| --- | --- | --- |
| Structures | 9 | 51 52 53 54 55 56 57 |
| Systems | 8 | 21 24 27 28 29 30 32 36 |
| Cabin | 5 | 25 33 38 44 |
| Avionics / software | 5 | 22 23 31 34 42 45 46 |
| Powerplant | 3 | 49 71 73 76 78 79 80 |

Use `secondary_ata` on ~12 of the 30 — that is where a structural MOD reveals a
cabin impact, or a system MOD reveals a wiring (92) impact.

---

## 5. Planted relationships (per program)

These are what the pre-analysis demo is supposed to *find*. Build them
deliberately:

1. **A supersession chain** of 3 MODs on the same ATA 6D
   (`MOD-a` ←`SUPERSEDES`— `MOD-b` ←`SUPERSEDES`— `MOD-c`), where the newest is
   a candidate.
2. **Two candidates in collision** — different ATA 4D, different wording,
   but sharing a part number, a FIN, or a zone. Neither references the other
   (the tool must discover it).
3. **A candidate vs a released MOD with near-identical intent** but different
   vocabulary — *"noise attenuation blanket"* vs *"acoustic insulation lining"*.
   Semantic-only discovery.
4. **A candidate invalidating an SB's pre-MOD effectivity** — the released MOD
   in `applicability.embodiment_condition.pre_mod` of some SB is superseded by
   the candidate.
5. **A prerequisite pair** — a candidate that cannot be embodied unless a
   released MOD is already on the aircraft (`REQUIRES`).
6. **A software chain** — a candidate updating an LSAP whose `supersedes`
   chain in `software.csv` runs through a released MOD.
7. **A concurrency pair** — two MODs that must be embodied together
   (`CONCURRENT_WITH`), one released, one candidate.
8. **A false friend** — two MODs with very similar titles and ATA that are
   genuinely unrelated (different aircraft type applicability, no shared
   entities). The demo needs precision failures to talk about.

Record 1–8 in the program's PAWOs as `expected_findings` (see
`aeralis-io-pawo-documents`), except the false friend, which goes in
`expected_non_findings`.

---

## 6. Applicability patterns

Mix scopes across the 30 MODs — roughly:
`ALL_PROGRAM` 4 · `TYPE` 8 · `MODEL` 5 · `MSN_RANGE` 8 · `MSN_LIST` 4 ·
`OPERATOR` 1.

For `MSN_LIST`, 3–15 MSNs from `msn_registry.csv`. For `MSN_RANGE`, ranges
inside the program block that actually contain registry MSNs. Use
`excluded_msns` on ~5 of the 30 (1–3 MSNs each).

Populate `embodiment_condition.pre_mod` / `post_mod` on ~10 of the 30 — this is
the field the conflict search keys on hardest.

---

## 7. Example (abridged)

```json
{
  "doc_id": "MOD-166204",
  "doc_type": "MOD",
  "title": "Reinforcement of Fwd Cargo Door Surround Frames C38 to C42",
  "program": "M",
  "program_name": "MERIDIAN Family",
  "revision": "-",
  "status": "DRAFT",
  "language": "en",
  "ata": {"ata_2d": "53", "ata_4d": "53-21", "ata_6d": "53-21-11",
          "title": "Fuselage Section 13 — Frames"},
  "secondary_ata": ["52-31", "25-52"],
  "applicability": {
    "scope": "MSN_RANGE",
    "aircraft_types": ["MERIDIAN-42"],
    "models": ["MRD-42K1"],
    "msns": [],
    "msn_ranges": [{"from": "M00120", "to": "M00512"}],
    "excluded_msns": ["M00441"],
    "engine_types": [],
    "operators": ["SIA"],
    "embodiment_condition": {"post_mod": ["MOD-166010"], "pre_mod": [],
                             "post_sb": [], "pre_sb": []}
  },
  "embodiment": {"status": "PRE_ANALYSIS", "points": ["RETROFIT"],
                 "production_effectivity": null, "production_cut_in_date": null,
                 "retrofit_via": []},
  "dates": {"created": "2026-03-19", "released": null, "revised": null,
            "effective": null},
  "abstract": "Local reinforcement of the forward cargo door surround between frames C38 and C42, in response to repeated fatigue crack findings on the lower door corner fittings of high-cycle aircraft.",
  "sections": {
    "reason": "Between 2023 and 2026, eleven operators reported ...",
    "description": "The modification introduces a machined titanium doubler ...",
    "effects": "Mass increase of 4.6 kg per aircraft, symmetric ...",
    "embodiment_note": "Retrofit only, embodiment during a scheduled C-check ...",
    "open_points": "Pre-analysis must confirm whether the new doubler footprint ..."
  },
  "entities": {
    "part_numbers": ["D5321101820000", "AGS5064-04", "AES0334-12"],
    "fins": ["120WT", "121WT"],
    "drawings": ["D53211100417", "D53211100418"],
    "software": [],
    "zones": ["128", "129"],
    "figures": [],
    "repair_drawings": [],
    "msn_mentions": ["M00441"]
  },
  "references": [
    {"doc_id": "IO-M-260118", "doc_type": "IO", "relation": "RAISED_BY"},
    {"doc_id": "MOD-166010", "doc_type": "MOD", "relation": "REQUIRES"}
  ],
  "keywords": ["cargo door", "frame reinforcement", "fatigue crack", "doubler"],
  "originator": {"department": "ESZS", "site": "Neuhafen", "role": "Design Office"},
  "type_data": { "…": "see §2" },
  "_meta": {"synthetic": true, "generator": "authored", "dataset_version": "1.0"}
}
```

---

## 8. Checklist

- [ ] 30 MODs, numbers unique and inside the program block
- [ ] 20 released / 10 candidates, status split as §1
- [ ] ATA spread as §4; ≥ 15 distinct ATA 2D across the program
- [ ] All 8 planted relationships of §5 exist and are traceable
- [ ] Every `parts_added`/`removed`/`fins_affected` resolves for this program
- [ ] Candidates have `open_points`; released MODs do not
- [ ] `SUPERSEDED` MODs carry a `SUPERSEDED_BY` reference
- [ ] `CANCELLED` MOD has no SB and no manual impacts
- [ ] No two titles differ only by a number
