---
name: aeralis-manual-documents
description: How to author the manual document types for the Aeralis MOD pre-analysis demo dataset — AMM tasks, IPC figures, SRM repairs with repair drawings, TSM fault isolation procedures, WDM wiring sheets and FCOM flight-crew entries. Covers per-type JSON schema, task/figure/repair numbering, effectivity, part list structure and volumes. Use when generating or editing anything under data/documents/*/AMM|IPC|SRM|TSM|WDM|FCOM/.
---

# Manual documents — AMM · IPC · SRM · TSM · WDM · FCOM

These are the documents a MOD *impacts*. In pre-analysis they answer
"what will we have to rewrite, and what in-service procedure does this break?"

Read `aeralis-dataset-conventions` first. All five types use the common
envelope; only `sections` and `type_data` differ.

**Volumes per program:** AMM 30 · IPC 10 · SRM 8 · TSM 8 · WDM 5.

All five must sit on ATA 6D codes that are **actually used** by the program's
MODs and SBs — otherwise the graph has orphan islands. Rule of thumb: at least
60 % of manual documents share their `ata_6d` with a MOD or SB, and the rest
sit on neighbouring 4D codes.

---

## 1. AMM — Aircraft Maintenance Manual task (60 per program)

`doc_id` = `AMM-{PROG}-{ATA6D}-{function}-{seq}-{variant}`
e.g. `AMM-M-53-21-11-000-001-A`.

Function codes: `000` removal · `400` installation · `200` inspection/check ·
`300` repair · `100` cleaning · `500` disconnect · `600` drain · `700` test ·
`800` servicing · `900` other. Pair `000`/`400` tasks (removal + installation
of the same item) for ~8 of the 30 — same `seq`, same variant.

```json
"type_data": {
  "task_number": "53-21-11-000-001-A",
  "task_type": "REMOVAL",
  "effectivity_note": "Aircraft post MOD-166010",
  "manhours": 6.5,
  "personnel": 2,
  "elapsed_time_hours": 4.0,
  "access": {
    "zones": ["128"],
    "access_panels": ["128AL", "128BL"],
    "doors": ["Fwd cargo door"]
  },
  "job_setup": {
    "safety_precautions": ["Put the safety barriers in position.", "..."],
    "tooling": [{"reference": "98D53211-401", "description": "Drill jig"}],
    "consumables": ["Sealant PR-1440 Class B"],
    "expendables": [{"part_number": "AGS5064-04", "qty": 24}]
  },
  "referenced_tasks": ["AMM-M-52-31-00-010-001-A"],
  "steps": 11,
  "warnings": 2,
  "cautions": 3
}
```

`task_type` ∈ `REMOVAL` · `INSTALLATION` · `INSPECTION` · `REPAIR` ·
`CLEANING` · `TEST` · `SERVICING` · `DEACTIVATION` · `ADJUSTMENT`.

`sections`:
- `purpose` (25–60 w)
- `job_setup_note` (40–90 w)
- `procedure` (120–260 w) — imperative, numbered-prose, Simplified Technical
  English: short sentences, one instruction each, "Remove the …", "Make sure
  that …", "Do the … procedure." Include at least one WARNING and one CAUTION
  in upper case.
- `close_out` (30–70 w)

`referenced_tasks` must resolve to other AMM docs in the same program (allow
forward references within the AMM set — the validator resolves after emit).

---

## 2. IPC — Illustrated Parts Catalogue figure (20 per program)

`doc_id` = `IPC-{PROG}-{ATA6D}-FIG-{NN}`.

```json
"type_data": {
  "figure": "FIG-53-21-11-01",
  "figure_title": "FWD CARGO DOOR SURROUND — FRAMES C38 TO C42",
  "sheet_count": 2,
  "items": [
    {"item": "10",   "part_number": "D5321101820000",
     "nomenclature": "FRAME FITTING, FWD CARGO DOOR SURROUND, LH",
     "qty_per_assy": 1, "units_per_assy": "1", "effectivity": "POST MOD-166204",
     "fin": "", "vendor": "AERA"},
    {"item": "10A",  "part_number": "D5321101810000",
     "nomenclature": "FRAME FITTING, FWD CARGO DOOR SURROUND, LH",
     "qty_per_assy": 1, "units_per_assy": "1", "effectivity": "PRE MOD-166204",
     "fin": "", "vendor": "AERA"},
    {"item": "20",   "part_number": "AGS5064-04",
     "nomenclature": "BOLT, HEXAGON HEAD", "qty_per_assy": 48,
     "units_per_assy": "48", "effectivity": "ALL", "fin": "", "vendor": "AERA"}
  ],
  "next_higher_assembly": "D5321100000000",
  "related_figures": ["FIG-53-21-12-01"]
}
```

- 8–24 items per figure. Item numbers ascend by 10 with letter suffixes for
  alternates (`10`, `10A`, `20`, `20A`, `30`, …).
- **Pre/post-MOD item pairs are mandatory**: at least 3 figures per program
  carry an item pair whose `effectivity` reads `PRE MOD-xxxxxx` /
  `POST MOD-xxxxxx`, referencing a real MOD in the program. This is the
  strongest embodiment-status signal in the dataset.
- Every `part_number` must exist for the program. `fin` may be empty; when set
  it must exist in `fins.csv`.
- `entities.part_numbers` must contain every item part number.
- `sections`: `purpose` (20–50 w), `figure_note` (40–100 w — what the figure
  shows, LH/RH symmetry, sheet split, effectivity caveats).

---

## 3. SRM — Structure Repair Manual repair (16 per program)

`doc_id` = `SRM-{PROG}-{ATA6D}-{NNN}`. ATA must be structural (51–57).

```json
"type_data": {
  "repair_number": "53-21-11-002",
  "repair_category": "PERMANENT",
  "damage_type": "CRACK",
  "allowable_damage_limits": "Cracks up to 25 mm in length, not closer than 15 mm to a fastener row, and no more than one crack per bay.",
  "repair_drawings": [
    {"reference": "RD-53-21-11-004",
     "title": "External doubler, frame C40 lower flange",
     "sheets": 3}
  ],
  "materials": [
    {"part_number": "AES0334-12", "description": "SHEET, TITANIUM, 1.6 MM",
     "qty": 1, "uom": "EA"}
  ],
  "fastener_pattern": "Two rows, 24 fasteners AGS5064-04, 20 mm pitch, 15 mm edge distance",
  "ndt_method": "EDDY_CURRENT",
  "static_strength_restored_pct": 100,
  "fatigue_life_note": "Repair is life-limited to 12 000 flight cycles; a repeat eddy current inspection is required every 3 000 flight cycles.",
  "weight_penalty_kg": 1.9,
  "aerodynamic_smoothness": "Class B",
  "requires_oem_approval": false
}
```

`repair_category` ∈ `TEMPORARY` · `INTERIM` · `PERMANENT`.
`damage_type` ∈ `CRACK` · `CORROSION` · `DENT` · `SCRATCH` · `PUNCTURE` ·
`DELAMINATION` · `LIGHTNING_STRIKE` · `IMPACT` · `HOLE`.
`ndt_method` ∈ `VISUAL` · `EDDY_CURRENT` · `ULTRASONIC` · `X_RAY` ·
`DYE_PENETRANT` · `THERMOGRAPHY` · `TAP_TEST`.

- `repair_drawings[].reference` uses the `RD-{ATA6D}-{NNN}` pattern and is
  **minted here** — SRM docs own their repair drawings. Copy them into
  `entities.repair_drawings`.
- At least 2 SRM repairs per program must sit on an ATA 6D also used by a
  **candidate MOD** — that is the "your MOD lands on top of an existing
  in-service repair" finding.
- `sections`: `purpose`, `damage_assessment` (60–140 w), `repair_procedure`
  (120–240 w), `inspection_requirements` (50–110 w).

---

## 4. TSM — Trouble Shooting Manual procedure (16 per program)

`doc_id` = `TSM-{PROG}-{ATA6D}-{8xx}-{8NN}`, e.g. `TSM-M-27-51-00-810-801`.

```json
"type_data": {
  "procedure_number": "27-51-00-810-801",
  "fault_symptom": "FLAP SYS 1 FAULT displayed on ECAM",
  "fault_codes": ["275100001", "275100017"],
  "warning_messages": ["F/CTL FLAP SYS 1 FAULT"],
  "affected_fins": ["1CE1", "4KS2"],
  "possible_causes": [
    {"cause": "Slat Flap Control Computer 1 internal failure",
     "probability_pct": 45, "fin": "1CE1"},
    {"cause": "Position sensor out of rig", "probability_pct": 30, "fin": "4KS2"}
  ],
  "isolation_steps": 7,
  "confirmation_task": "AMM-M-27-51-00-710-001-A",
  "dispatch": {"mel_item": "27-51-01A", "dispatchable": true,
               "dispatch_condition": "One system inoperative, both slat channels serviceable"}
}
```

- `fault_codes` are 9 digits built from the ATA 6D + 3-digit sequence.
- `affected_fins` and `possible_causes[].fin` must exist in `fins.csv` for the
  program, and be in `entities.fins`.
- `confirmation_task` must be an AMM doc in the same program.
- `sections`: `purpose`, `fault_description` (50–110 w), `isolation_procedure`
  (110–220 w), `dispatch_note` (30–70 w).

---

## 5. WDM — Wiring Diagram Manual sheet (10 per program)

`doc_id` = `WDM-{PROG}-{ATA4D}-{NN}`, normally on ATA 92
(e.g. `WDM-M-92-21-11`), occasionally on the system's own chapter.

```json
"type_data": {
  "sheet_number": "92-21-11",
  "sheet_title": "FWD CARGO DOOR WARNING — LH SIDE",
  "diagram_type": "WIRING_DIAGRAM",
  "harnesses": ["1210W", "1211W"],
  "connectors": [
    {"reference": "1210VC", "part_number": "HTC4021-17",
     "location_zone": "128", "contacts": 37}
  ],
  "equipment": [
    {"fin": "120WT", "description": "CARGO DOOR PROXIMITY SENSOR, FWD, LH",
     "zone": "128"}
  ],
  "wire_count": 46,
  "wire_gauge_range": "AWG 22 to AWG 12",
  "routes": ["Zone 128 → Zone 121 via FR38 to FR44 upper cable route"],
  "related_sheets": ["WDM-M-92-21-12"]
}
```

`diagram_type` ∈ `WIRING_DIAGRAM` · `SCHEMATIC` · `EQUIPMENT_LIST` ·
`CONNECTOR_LIST` · `GROUND_POINT_LIST`.

- `equipment[].fin` and `connectors[].part_number` must resolve for the program.
- `sections`: `purpose`, `sheet_note` (50–110 w), `routing_note` (40–100 w).
- Harness references use `{4 digits}W`. Connector references use `{4 digits}VC`.

---

## 6. FCOM — Flight Crew Operating Manual entry (10 per program)

The only document type written for the **flight crew** rather than for
maintenance. It exists so the PAWO impact matrix has a sixth column with real
content: a structural or systems MOD that changes what the crew sees, does or
is limited by lands here, and that is the impact an engineer is most likely to
miss.

`doc_id` = `FCOM-{PROG}-{SECTION}-{ATA4D}-{NN}`, e.g. `FCOM-M-PRO-32-40-03`.

`SECTION` ∈ `DSC` (Description) · `PRO` (Procedures) · `LIM` (Limitations) ·
`PER` (Performance). Aim for roughly 4 DSC, 3 PRO, 2 LIM, 1 PER per program.

```json
"type_data": {
  "section": "PRO",
  "section_title": "Abnormal and Emergency Procedures — Braking",
  "entry_number": "PRO-ABN-32-40-03",
  "flight_phase": "LANDING",
  "operational_impact": "PROCEDURE_CHANGE",
  "crew_action": "ECAM actions followed by the residual braking procedure.",
  "affects_qrh": true,
  "affects_mel": true,
  "mel_items": ["32-40-01A"],
  "systems": ["Brake Control Unit", "Alternate Braking"],
  "related_entries": ["FCOM-M-DSC-32-40-01"],
  "supersedes_entry": null
}
```

- `operational_impact` ∈ `NONE` · `PROCEDURE_CHANGE` · `LIMITATION_CHANGE` ·
  `PERFORMANCE_CHANGE` · `TRAINING_REQUIRED`.
- `flight_phase` ∈ `PREFLIGHT` · `TAXI` · `TAKEOFF` · `CLIMB` · `CRUISE` ·
  `DESCENT` · `APPROACH` · `LANDING` · `ALL_PHASES`.
- `ata_4d` must be a real 4D code; `ata_6d` is the `-00` roll-up of it when the
  entry covers the whole sub-chapter.
- `sections`: `purpose`, `description` (110–200 w), `crew_procedure` (60–140 w),
  `limitation_note` (40–90 w, `LIM` and `PER` entries only).
- **No part numbers.** An FCOM entry names systems and controls, not hardware —
  `entities.part_numbers` and `entities.drawings` stay empty. It may carry
  `fins` when it names an indicator or a control unit, and `zones` never.
- **Write it in crew register, not maintenance register.** "Select the ALTN
  BRK pushbutton OFF", not "de-energise the solenoid". This is deliberate: an
  FCOM entry and the AMM task for the same system must share almost no
  vocabulary, which is what makes them a semantic-only finding pair.

Each program needs **≥ 4 FCOM entries referenced by a MOD or SB** with relation
`IMPACTS`, and **≥ 2 that are only reachable semantically** — no shared entity
with the MOD that should have found them. Record the latter in the owning
PAWO's `expected_findings` with `"finding_type": "MANUAL_IMPACT"` and
`"discovery": "SEMANTIC"`.

---

## 7. Cross-type coherence rules

1. Every manual doc must be reachable: it is referenced by ≥ 1 MOD or SB, **or**
   it references ≥ 1 AMM/IPC of its own program. No orphans.
2. If an IPC figure carries `PRE/POST MOD-x` effectivity, `MOD-x` should list
   that figure under `IMPACTS` or `REPLACES_PART_IN`.
3. A TSM `confirmation_task` AMM must exist; an AMM `referenced_tasks` entry
   must exist.
4. WDM `equipment[].fin` should overlap the FINs used by at least one MOD or SB
   in the program — the wiring-impact finding depends on it.
5. Part numbers appearing in an IPC figure should overlap the part numbers of
   the MOD/SB on the same ATA 6D — not identically, but with real intersection.

---

## 8. Checklist (per program)

- [ ] AMM 60 (incl. ~16 removal/installation pairs), IPC 20, SRM 16, TSM 16, WDM 10, FCOM 10
- [ ] ≥ 60 % of manual docs share `ata_6d` with a MOD or SB
- [ ] ≥ 6 IPC figures with pre/post-MOD item effectivity
- [ ] ≥ 4 SRM repairs on candidate-MOD ATA 6D codes
- [ ] All repair drawings minted with the `RD-` pattern and in `entities`
- [ ] All FINs / part numbers / zones resolve for the program
- [ ] No orphan manual documents
- [ ] AMM procedure text is Simplified Technical English with WARNING/CAUTION
