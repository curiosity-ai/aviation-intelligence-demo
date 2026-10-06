---
name: aeralis-io-pawo-documents
description: How to author IO (Internal Order) and PAWO (Pre-Analysis Work Order) documents for the Aeralis MOD pre-analysis demo dataset, including the ground-truth expected_findings and expected_non_findings blocks that make the demo measurable. Use when generating or editing anything under data/documents/*/IO/ or data/documents/*/PAWO/.
---

# IO and PAWO documents

These two types bracket the use case: the **IO** is the request that starts
everything, the **PAWO** is the pre-analysis work order that the application
under demo actually supports — and it carries the **ground truth** the search
results are scored against.

Read `aeralis-dataset-conventions` and `aeralis-mod-documents` first.

**Volumes per program: IO 6, PAWO 6 open + 20 historical (§6).**

---

## 1. IO — Internal Order (6 per program)

`doc_id` = `IO-{PROG}-{YY}{NNNN}`, e.g. `IO-M-260118` (year 2026, seq 0118).

An IO is a *business* document, not an engineering one. Its language is the
operator's, not the design office's — deliberately less precise. That gap is
part of what makes the semantic-search demo interesting: the IO says
"cracks around the front cargo door", the MOD says "fatigue findings on the
FWD cargo door surround frame lower flange radii".

Of the 6 IOs: **5 create a candidate MOD** (the 5 that drive PAWOs) and 1 is
`REJECTED` or `ON_HOLD` with no MOD. One additional PAWO covers a candidate
MOD raised internally without an IO — see §2.

```json
"type_data": {
  "io_number": "IO-M-260118",
  "raised_by": {
    "requester_type": "OPERATOR",
    "operator_code": "SIA",
    "contact_role": "Fleet Engineering Manager",
    "oem_focal_department": "EYIS",
    "oem_focal_site": "Valmont"
  },
  "request_category": "STRUCTURE",
  "priority": "HIGH",
  "io_status": "CONVERTED_TO_MOD",
  "fleet_scope": {
    "aircraft_count": 26,
    "msns": ["M00417", "M00423", "M00460"],
    "aircraft_types": ["MERIDIAN-42"]
  },
  "business_justification": "Repeat findings are driving unscheduled downtime …",
  "commercial": {
    "funding": "CUSTOMER_FUNDED",
    "budget_keur": 900,
    "contract_reference": "CSA-2024-SIA-0417"
  },
  "target_dates": {
    "requested_response": "2026-05-15",
    "requested_embodiment_start": "2027-01-10"
  },
  "created_mod": "MOD-166204",
  "closure_note": null
}
```

`closure_note` is **required** (40–110 words) when `io_status` is `REJECTED`,
`ON_HOLD` or `CLOSED`, and `null` otherwise. It records the engineering
position that closed the order — why no MOD was raised, and what the requester
was offered instead. It is the one place in the dataset where a *rejected*
line of enquiry is written down, which makes it useful demo material: a later
IO on the same subject should surface it. When `io_status` is
`CONVERTED_TO_MOD`, `created_mod` is set and `closure_note` is `null`; the two
are mutually exclusive.

Vocabularies:
- `requester_type` ∈ `OPERATOR` · `LESSOR` · `OEM_INTERNAL` · `MRO` ·
  `AUTHORITY` · `SUPPLIER`
- `request_category` ∈ `STRUCTURE` · `SYSTEM` · `CABIN` · `SOFTWARE` ·
  `REPAIR` · `POWERPLANT` · `AVIONICS` · `WEIGHT` · `OBSOLESCENCE`
- `priority` ∈ `AOG` · `HIGH` · `MEDIUM` · `LOW`
- `io_status` ∈ `RAISED` · `UNDER_REVIEW` · `CONVERTED_TO_MOD` · `REJECTED` ·
  `ON_HOLD` · `CLOSED`
- `funding` ∈ `CUSTOMER_FUNDED` · `OEM_FUNDED` · `WARRANTY` · `SHARED` ·
  `CONTRACTUAL`

`sections`:
- `request` (80–170 w) — the operator's own words. Operational, imprecise,
  mentions symptoms and consequences rather than part numbers.
- `background` (50–120 w) — history: prior findings, prior SBs attempted,
  MRO feedback, dispatch reliability numbers.
- `expected_outcome` (40–90 w) — what "done" looks like for the operator.

`entities` on an IO are sparse: often only `msn_mentions` and maybe one FIN or
zone. **Do not** stuff an IO with part numbers — an IO that already knows the
part numbers defeats the point of the demo.

`references`: `{"doc_id": "MOD-166204", "doc_type": "MOD", "relation": "CREATED"}`
when converted.

---

## 2. PAWO — Pre-Analysis Work Order (6 per program)

`doc_id` = `PAWO-{mod digits}-{NN}`, e.g. `PAWO-166204-01`.
Always `01` unless a MOD needs a second pre-analysis round — give exactly one
MOD per program a `-02` (a re-analysis after the first found a blocker).

Each PAWO targets exactly one **candidate** MOD
(`embodiment.status` ∈ `PRE_ANALYSIS` · `PROPOSAL` · `LAUNCHED`).

### The round-2 PAWO and the one-PAWO-per-MOD rule

`validate.py` enforces **exactly one** PAWO per `PRE_ANALYSIS` MOD (check 17):
a modification sitting in pre-analysis has exactly one open work order, never
two. So the round-2 PAWO cannot simply be a second work order stacked on a
`PRE_ANALYSIS` MOD. Two resolutions are valid — pick either:

1. **The `-02` is the only PAWO on its MOD.** Round 1 concluded and was closed
   out; the re-analysis is the live work order. The MOD stays `PRE_ANALYSIS`
   and carries one PAWO, numbered `-02`, with `analysis_round: 2`.
2. **The `-02` sits on a `LAUNCHED` or `PROPOSAL` MOD.** A modification that
   passed pre-analysis and was launched can be sent back for a second look;
   the one-PAWO rule only binds `PRE_ANALYSIS`. This frees the
   `PRE_ANALYSIS` count to be 5 rather than 6.

Either way the program still has 6 PAWOs on 6 distinct candidate MODs. Because
of this, `aeralis-mod-documents` §1 gives the candidate split as
`PRE_ANALYSIS` **5–6**, with the remainder in `LAUNCHED` / `PROPOSAL`.

```json
"type_data": {
  "pawo_number": "PAWO-166204-01",
  "subject_mod": "MOD-166204",
  "source_io": "IO-M-260118",
  "pawo_status": "IN_PROGRESS",
  "raised_date": "2026-03-20",
  "due_date": "2026-05-08",
  "analysis_round": 1,
  "assigned": {
    "lead_department": "ESZS",
    "lead_site": "Neuhafen",
    "contributing_departments": ["EDSA", "EVAA", "ELSC"],
    "estimated_manhours": 120,
    "spent_manhours": 74
  },
  "search_scope": {
    "programs": ["M"],
    "aircraft_types": ["MERIDIAN-42", "MERIDIAN-46"],
    "ata_2d": ["53", "52", "25"],
    "ata_4d": ["53-21", "53-22", "52-31"],
    "doc_types": ["MOD", "SB", "AMM", "IPC", "SRM", "TSM", "WDM"],
    "msn_range": {"from": "M00120", "to": "M00512"},
    "entity_seeds": {
      "part_numbers": ["D5321101820000", "D5321101810000"],
      "fins": ["120WT", "121WT"],
      "drawings": ["D53211100417"],
      "zones": ["128", "129"],
      "software": []
    },
    "similarity_seeds": [
      "fatigue cracking at cargo door surround frame lower flange",
      "titanium doubler installation on fuselage frame"
    ]
  },
  "tasks": [
    {"task_id": "T01", "title": "Search released MODs on ATA 53-21 and 53-22",
     "method": "ENTITY_AND_SEMANTIC", "owner": "ESZS", "status": "DONE"},
    {"task_id": "T02", "title": "Check in-service repairs on the same panel",
     "method": "ENTITY", "owner": "EDSA", "status": "DONE"},
    {"task_id": "T03", "title": "Assess wiring routing impact in zone 128",
     "method": "ENTITY", "owner": "ELSC", "status": "OPEN"}
  ],
  "conclusion": "Not yet issued. Two blocking conflicts identified …",
  "recommendation": "PROCEED_WITH_CONDITIONS"
}
```

Vocabularies:
- `pawo_status` ∈ `RAISED` · `IN_PROGRESS` · `ON_HOLD` · `COMPLETED` ·
  `CANCELLED`
- `tasks[].method` ∈ `ENTITY` · `SEMANTIC` · `ENTITY_AND_SEMANTIC` ·
  `MANUAL_REVIEW` · `SME_INTERVIEW`
- `tasks[].status` ∈ `OPEN` · `IN_PROGRESS` · `DONE` · `BLOCKED`
- `recommendation` ∈ `PROCEED` · `PROCEED_WITH_CONDITIONS` · `REWORK_SCOPE` ·
  `MERGE_WITH_EXISTING_MOD` · `REJECT` · `PENDING`

`sections`:
- `objective` (40–90 w)
- `scope_note` (60–130 w) — why this search scope, what was excluded and why
- `method_note` (60–130 w) — how entity linking and similarity were combined
- `findings_summary` (100–200 w) — prose narrative of the conflicts found
- `next_steps` (40–100 w)

---

## 3. `expected_findings` — the ground truth

This is the most valuable block in the dataset. It lives inside `type_data`.

```json
"expected_findings": [
  {
    "finding_id": "F01",
    "doc_id": "MOD-166010",
    "doc_type": "MOD",
    "finding_type": "PREREQUISITE",
    "severity": "BLOCKING",
    "discovery": "ENTITY",
    "evidence": {
      "shared_part_numbers": ["D5321101810000"],
      "shared_fins": [],
      "shared_zones": ["128"],
      "shared_ata_6d": ["53-21-11"],
      "similarity_note": null
    },
    "rationale": "MOD-166010 introduced the current frame fitting standard. The new doubler footprint is only compatible with the post-MOD-166010 fitting, so embodiment is conditional on it.",
    "resolution": "Add MOD-166010 as a post-mod applicability condition."
  },
  {
    "finding_id": "F04",
    "doc_id": "MOD-166231",
    "doc_type": "MOD",
    "finding_type": "OVERLAPPING_SCOPE",
    "severity": "MAJOR",
    "discovery": "SEMANTIC",
    "evidence": {
      "shared_part_numbers": [], "shared_fins": [], "shared_zones": [],
      "shared_ata_6d": [],
      "similarity_note": "Both describe local reinforcement against fatigue at a lower-shell door corner, using entirely different vocabulary ('acoustic doubler' vs 'load-diffusion plate'). No shared identifiers."
    },
    "rationale": "…",
    "resolution": "Coordinate with the MOD-166231 design team; consider merging."
  }
]
```

Vocabularies:
- `finding_type` ∈ `DUPLICATE` · `OVERLAPPING_SCOPE` · `PREREQUISITE` ·
  `SUPERSESSION` · `EFFECTIVITY_INVALIDATION` · `PART_CONFLICT` ·
  `FIN_CONFLICT` · `SOFTWARE_CONFLICT` · `ZONE_CONFLICT` ·
  `REPAIR_CONFLICT` · `MANUAL_IMPACT` · `CERTIFICATION_IMPACT` ·
  `CONCURRENCY_REQUIRED`
- `severity` ∈ `BLOCKING` · `MAJOR` · `MINOR` · `INFORMATIONAL`
- `discovery` ∈ `ENTITY` · `SEMANTIC` · `BOTH` · `APPLICABILITY` ·
  `GRAPH_TRAVERSAL` — *how the application is expected to surface it*

Rules:
- **5–9 findings per PAWO.** Every `doc_id` must exist in the same program.
- Across a program's 6 PAWOs, cover **every `finding_type`** at least once and
  **every `discovery` mode** at least twice.
- At least 2 findings per program have `discovery: "SEMANTIC"` and **empty**
  shared-entity arrays — pure embedding wins.
- At least 2 findings per program have `discovery: "ENTITY"` with a
  `similarity_note` of `null` and genuinely dissimilar titles — pure entity-link
  wins.
- At least 1 `BLOCKING` finding per PAWO.
- `evidence.shared_*` values must genuinely appear in **both** documents'
  `entities` blocks. The validator checks this.

### `expected_non_findings`

Two per PAWO. Documents that look related and are not — the precision test.

```json
"expected_non_findings": [
  {"doc_id": "MOD-166188", "doc_type": "MOD",
   "reason": "Near-identical title and same ATA 53-21, but applicable only to MERIDIAN-42F main-deck cargo door, no MSN overlap and no shared parts."}
]
```

---

## 4. The hero scenario

Exactly one PAWO per program is the **hero**: mark it
`"hero_scenario": true` in `type_data`. It must have

- 7–9 `expected_findings` spanning ≥ 5 `finding_type` values,
- ≥ 2 `BLOCKING`,
- findings across ≥ 4 different `doc_type`s (MOD, SB, SRM/AMM/IPC/WDM),
- a `source_io` whose `sections.request` is written in plainly non-engineering
  language,
- a `findings_summary` that reads as a genuine narrative an engineer could
  present in a review.

The P (MERIDIAN) hero is the flagship for the demo — the forward cargo door
surround reinforcement described in `README.md` §5.

---

## 5. Checklist (per program)

- [ ] 6 IOs — 5 `CONVERTED_TO_MOD`, 1 rejected/on-hold
- [ ] the non-converted IO carries a `closure_note`; converted IOs do not
- [ ] IO prose is operator-flavoured, sparse `entities`
- [ ] 6 PAWOs, each on a distinct candidate MOD, exactly one `analysis_round: 2`
- [ ] Exactly one PAWO with `hero_scenario: true`
- [ ] 5–9 `expected_findings` each; all `doc_id`s resolve in-program
- [ ] All `finding_type` values used at least once across the program
- [ ] ≥ 2 pure-SEMANTIC and ≥ 2 pure-ENTITY findings per program
- [ ] 2 `expected_non_findings` per PAWO
- [ ] `search_scope.entity_seeds` ⊆ the subject MOD's `entities`

---

## 6. Historical (closed) PAWOs — 20 per program

Every *released* modification once went through pre-analysis. Those closed work
orders are what give the application a past: the dashboard computes weekly
throughput, average hours per work order and a last-month status split from
them, and the "similar previous analysis" search has nothing to find without
them.

`doc_id` = `PAWO-{mod digits}-01` on a **released** MOD
(`embodiment.status` ∈ `IN_SERVICE` · `IN_PRODUCTION` · `SUPERSEDED`).
20 of the 20 released MODs per program get one. `CANCELLED` MODs do not.

### 6.1 What differs from an open PAWO

| | Open (6) | Historical (20) |
| --- | --- | --- |
| subject MOD | candidate | released |
| `pawo_status` | `IN_PROGRESS` / `RAISED` / `ON_HOLD` | `COMPLETED` (18) · `CANCELLED` (2) |
| `is_historical` | `false` | `true` |
| `completed_date` | `null` | set — see §6.2 |
| `outcome` | `null` | `VALIDATED` · `REJECTED` · `DELEGATED` |
| `recommendation` | any | matches the outcome |
| `expected_findings` | 5–9 | **2–5** — what was actually found, not a search target |
| `expected_non_findings` | 2 | 0–1 |
| `hero_scenario` | one per program | never |
| `source_io` | usually set | often `null` (raised internally) |
| `sections` | all five | `objective`, `findings_summary`, `next_steps` only |

Prose stays real. These documents are searched by the similarity engine as
"previous analysis", so a templated paragraph repeated twenty times makes that
feature look broken. 60–150 words per section is enough.

### 6.2 Dates and effort — the dashboard depends on these

- `completed_date` must be on or after the subject MOD's `created` date and
  within 2016-01-01 … 2026-08-07. It does **not** have to precede the MOD's
  `released` date: a released modification is still pre-analysed afterwards for
  a retrofit or spares release, an effectivity extension, or a second analysis
  round. Requiring `completed ≤ released` and "≥ 8 completions in the last 90
  days" at once is unsatisfiable for any program whose newest MOD was released
  more than 90 days ago, which is all of them — so the release bound is the one
  that gives way. Frame a post-release completion as one of those three cases
  in the prose, not as a plain first analysis.
- **Spread them.** At least 8 of the 20 must complete within the 90 days before
  2026-08-07, and at least 4 within the last 30 days, or the "last month"
  metrics are empty. The rest spread back over the fleet's history.
- `assigned.spent_manhours` is the real effort: 20–260 h, log-normal-ish, with
  a few outliers. `estimated_manhours` is what was planned — they should differ.
- `outcome` split across the 20, per program: `VALIDATED` 13,
  `REJECTED` 4, `DELEGATED` 3.

### 6.3 `type_data` additions

Present on **every** PAWO, open and historical:

```json
"is_historical": true,
"completed_date": "2026-07-18",
"outcome": "VALIDATED"
```

Open work orders carry `"is_historical": false`, `"completed_date": null`,
`"outcome": null`. The validator enforces the correspondence in both
directions: completed ⇔ historical ⇔ subject MOD released.
