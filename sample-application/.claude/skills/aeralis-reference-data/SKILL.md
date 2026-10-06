---
name: aeralis-reference-data
description: How to generate the tabular CSV master data for the Aeralis MOD pre-analysis demo — part numbers, FINs, MSN registry, drawings, software/LSAP, zones, operators, suppliers and the ATA 2D/4D/6D catalogue. Covers exact column schemas, row volumes, generation patterns, seeding rules and the lookup tool authoring agents use. Use when creating or extending anything under data/reference/.
---

# Reference (master) data

All reference data is **programmatically generated**, **seeded**, and
**idempotent**. It is produced by `tools/gen_reference.py`
(`RANDOM_SEED = 20260807`) using the ATA catalogue in `tools/ata_catalog.py`.

Never hand-edit `data/reference/*.csv`. Change the generator and re-run:

```bash
python3 tools/gen_reference.py
```

CSV rules: UTF-8, LF, header row, `,` delimiter, minimal quoting, no index
column, deterministic row order (sorted by primary key).

The `programs` column, wherever it appears, is a `|`-separated subset of
`P C R N L`. A document in program `X` may only use rows whose `programs`
contains `X`.

---

## 1. `ata_chapters.csv` — ~600 rows

The backbone. Everything else hangs off ATA.

| Column | Type | Example |
| --- | --- | --- |
| `code` | str | `53-21-11` |
| `level` | int (2\|4\|6) | `6` |
| `parent` | str | `53-21` |
| `title` | str | `Fuselage Section 13 — Frames` |
| `domain` | enum | `STRUCTURE` |
| `programs` | str | `P\|C\|R\|N\|L` |
| `airframe_section` | enum | `Fuselage` |
| `view_hint` | enum | `Side` |

`domain` ∈ `STRUCTURE` · `SYSTEMS` · `AVIONICS` · `CABIN` · `POWERPLANT` ·
`ELECTRICAL` · `STANDARD_PRACTICES`.

`airframe_section` and `view_hint` say **where on the aircraft an illustration
callout for this chapter should point** — see §7.1. They are the *fallback* used
for a document that lists no zone (all FCOM documents, and a handful of others);
`zones.csv` is the primary source. Both come from `AIRFRAME_SECTION_2D` in
`tools/ata_catalog.py`, keyed by the 2D chapter; 4D and 6D rows inherit their 2D
chapter's pair, so the three levels of one chapter always agree. Every 2D chapter
in `ATA_2D` must have an entry — the module raises on import if one is missing.

Chapters covered (2D): 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 38
42 44 45 46 49 51 52 53 54 55 56 57 71 72 73 76 78 79 80 92.

Roughly 3–6 4D per 2D and 2–4 6D per 4D. Titles must be aviation-plausible;
the 6D titles are generated from a curated suffix list per domain
(`General`, `Installation`, `Removal/Installation`, `Control`, `Indicating`,
`Distribution`, `Frames`, `Skin Panels`, `Stringers`, `Fittings`, …).

---

## 2. `part_numbers.csv` — 6 000 rows

| Column | Type | Example |
| --- | --- | --- |
| `part_number` | str | `D5321101820000` |
| `description` | str | `FRAME FITTING, FWD CARGO DOOR SURROUND, LH` |
| `pn_type` | enum | `AERALIS` |
| `ata_6d` | str | `53-21-11` |
| `ata_2d` | str | `53` |
| `programs` | str | `P` |
| `supplier_code` | str | `AERA` |
| `uom` | enum | `EA` |
| `mass_kg` | float 3dp | `1.842` |
| `unit_price_eur` | int | `4820` |
| `lifecycle` | enum | `ACTIVE` |
| `interchangeable_with` | str | `` or a PN |

- `pn_type` mix: `AERALIS` 55 %, `VENDOR` 30 %, `STANDARD` 15 %. Patterns in
  the conventions skill §2.2. `AERALIS` PNs embed their own ATA 6D digits —
  `D` + `532111` + 7 chars — so entity linking can partially resolve ATA from
  a bare part number. This is deliberate and the demo shows it off.
- `lifecycle` ∈ `ACTIVE` (75 %) · `OBSOLETE` (12 %) · `SUPERSEDED` (8 %) ·
  `NEW` (5 %). `SUPERSEDED` rows must populate `interchangeable_with` with an
  `ACTIVE` PN of the same ATA 6D and program set.
- `uom` ∈ `EA` (90 %) · `M` · `KG` · `L` · `SET`.
- **Shared parts are the point.** ~12 % of rows carry 2+ program letters
  (standard parts, common systems). ~4 % of `AERALIS`/`VENDOR` rows are
  deliberately assigned a `description` that fits two different systems so a
  part can plausibly appear in unrelated documents.
- Descriptions are UPPERCASE, comma-separated noun phrases, ≤ 60 chars,
  built from a vocabulary keyed on ATA domain, with position qualifiers
  (`LH`, `RH`, `FWD`, `AFT`, `UPPER`, `LOWER`, `INBD`, `OUTBD`) and
  assembly qualifiers (`ASSY`, `SUB-ASSY`, `KIT`, `BRACKET`, `DOUBLER`,
  `HARNESS`, `CONNECTOR`, `PANEL`, `SEAL`, `VALVE`, `SENSOR`, `ACTUATOR`).
- Distribution across ATA follows the document distribution: structures
  (51–57) ≈ 35 %, systems (21–38) ≈ 30 %, avionics/cabin (42–46) ≈ 15 %,
  powerplant (71–80) ≈ 12 %, electrical/wiring (92, 24) ≈ 8 %.

---

## 3. `fins.csv` — 2 500 rows

FINs identify a *function in a location* rather than a piece of hardware, so
they are the strongest cross-document linking signal in the dataset.

| Column | Type | Example |
| --- | --- | --- |
| `fin` | str | `120WT` |
| `description` | str | `CARGO DOOR PROXIMITY SENSOR, FWD, LH` |
| `ata_6d` | str | `52-71-11` |
| `ata_2d` | str | `52` |
| `system` | str | `Door Warning` |
| `programs` | str | `P\|R` |
| `zone` | str | `128` |
| `default_part_number` | str | `D5271104110700` |
| `lru` | bool | `true` |

- FIN pattern: `{1-4 digits}{2-3 uppercase}{optional digit}`. The letter block
  is drawn from a per-ATA table (`WT` doors, `HQ` air conditioning,
  `QA` fuel, `VU` panels/racks, `KS` flight controls, `GX` electrical,
  `MC` avionics, `PP` powerplant, `RH` lights, …) so FINs cluster by system
  exactly as real ones do.
- `default_part_number` must exist in `part_numbers.csv` with a compatible
  `programs` set. ~15 % of FINs share their `default_part_number` with another
  FIN — another deliberate linking opportunity.
- `zone` must exist in `zones.csv`.
- `lru` true for ~35 % (line-replaceable units).

---

## 4. `msn_registry.csv` — 1 200 rows

| Column | Type | Example |
| --- | --- | --- |
| `msn` | str | `M00417` |
| `program` | str | `P` |
| `aircraft_type` | str | `MERIDIAN-42` |
| `model` | str | `MRD-42K1` |
| `line_number` | int | `417` |
| `operator_code` | str | `SIA` |
| `engine_type` | str | `AUR-8400` |
| `delivery_date` | date | `2021-06-14` |
| `registration` | str | `9V-SMX` |
| `status` | enum | `IN_SERVICE` |

- Counts per program: P 240, C 200, R 120, N 400, L 240.
- `status` ∈ `IN_SERVICE` (86 %) · `STORED` (7 %) · `IN_PRODUCTION` (5 %) ·
  `WITHDRAWN` (2 %).
- `delivery_date` increases with MSN within a program; entry-into-service
  floors: P 2015, C 2016, R 2007, N 1990, L 1994.
- `engine_type` per type: AUR-8400/-97 (P), PYR-1500G (C),
  AUR-9000/CVX-7200 (R), VDK-56B/VNT-2500/VDK-12/PYR-1100G (N),
  AUR-7700/CVX-6080/PYR-4170/AUR-7000/VDK-56C (L).
- `registration` uses plausible per-operator prefixes and must be unique.

---

## 5. `drawings.csv` — 1 500 rows

| Column | Type | Example |
| --- | --- | --- |
| `drawing_number` | str | `D53211100417` |
| `title` | str | `FWD CARGO DOOR SURROUND — FRAME C40 ASSY` |
| `ata_6d` | str | `53-21-11` |
| `programs` | str | `P` |
| `drawing_type` | enum | `ASSEMBLY` |
| `issue` | str | `C` |
| `issue_date` | date | `2020-09-03` |
| `cad_system` | enum | `CATIA_V5` |

`drawing_type` ∈ `ASSEMBLY` · `DETAIL` · `INSTALLATION` · `SCHEMATIC` ·
`WIRING` · `TOOLING`. `cad_system` ∈ `CATIA_V5` · `CATIA_V6` · `3DX` ·
`LEGACY_2D`.

Pattern: `D` + the 6 ATA digits + 5 sequence digits, so drawings resolve to
ATA the same way Aeralis part numbers do.

---

## 6. `software.csv` — 300 rows

| Column | Type | Example |
| --- | --- | --- |
| `software_pn` | str | `LA1P4C1520100` |
| `software_name` | str | `FCGU_OPS_V4.2` |
| `system` | str | `Flight Control Guidance Unit` |
| `ata_6d` | str | `27-93-34` |
| `programs` | str | `P` |
| `version` | str | `4.2` |
| `load_type` | enum | `LSAP` |
| `host_fin` | str | `1CE1` |
| `certification_status` | enum | `CERTIFIED` |
| `supersedes` | str | `LA1P4C1520000` |

`load_type` ∈ `LSAP` · `DATABASE` · `FIELD_LOADABLE` · `EMBEDDED`.
`certification_status` ∈ `CERTIFIED` · `IN_CERTIFICATION` · `DEVELOPMENT` ·
`WITHDRAWN`.

`host_fin` must exist in `fins.csv` with a compatible program set. Version
chains (`supersedes`) are 2–4 deep for ~40 % of entries — software update MODs
in the document set walk these chains.

---

## 7. `zones.csv` — ~400 rows

| Column | Type | Example |
| --- | --- | --- |
| `zone` | str | `128` |
| `zone_name` | str | `Fwd Cargo Compartment, LH` |
| `major_zone` | str | `100` |
| `major_zone_name` | str | `Lower Fuselage` |
| `programs` | str | `P\|C\|R\|N\|L` |
| `access_panels` | str | `128AL\|128BL` |
| `airframe_section` | enum | `ForwardFuselage` |
| `view_hint` | enum | `Side` |
| `section_position` | float 2dp | `0.35` |

Major zones follow the standard scheme: 100 lower fuselage, 200 upper
fuselage, 300 empennage, 400 powerplants/pylons, 500 LH wing, 600 RH wing,
700 landing gear, 800 doors, 900 lavatories/galleys/cabin.

### 7.1 Airframe placement — aiming an illustration callout

`src/Aeralis.Illustrations` draws the Aeralis three-view sheets and annotates
them with callouts. To aim one it needs three values, which these columns
supply: an airframe **section**, the **view** the feature actually reads on, and
a **position** along the section.

| Column | Vocabulary |
| --- | --- |
| `airframe_section` | one `AircraftSection` enum name — `Nose` · `Cockpit` · `ForwardFuselage` · `Fuselage` · `AftFuselage` · `LeftWing` · `RightWing` · `VerticalStabilizer` · `HorizontalStabilizer` · `Tail` · `LeftEngine` · `RightEngine` · `NoseLandingGear` · `MainLandingGear` |
| `view_hint` | one `ViewKind` enum name — `Plan` · `Side` · `Front` |
| `section_position` | decimal `0.05`–`0.95`, two decimals, `.` separator; 0 is the section's forward or inboard end (nose for the fuselage, root for a wing), 1 the aft or outboard end |

Both enums live in `src/Aeralis.Illustrations/Enums.cs` and must be spelled
exactly as the C# members are; `src/Aeralis.Illustrations/SectionMap.cs` shows
the outline each section covers on each view, which is what makes a choice right
or wrong. Sections are named from the **aircraft's** point of view, so `LeftWing`
is the port wing and engine 1 is the port engine.

The values are generated by `ZONE_AIRFRAME` in `tools/gen_reference.py`, a table
keyed by **sub-zone name** (the part of `zone_name` before the em dash), giving
`(airframe_section, view_hint, base_position)`; the detail suffix then nudges the
position through `ZONE_DETAIL_SHIFT` (`Fwd Section` −0.15, `Aft Section` +0.15,
`Inbd Area` −0.12, `Outbd Area` +0.12, `Access Area` +0.05,
`Equipment Rack Area` −0.05, `Stowage Area` +0.08, `Upper`/`Lower Area` 0 — they
are vertical and leave the station alone), clamped to `[0.05, 0.95]`. Every
sub-zone name in `MAJOR_ZONES` must be in the table; the module raises on import
if one is missing rather than defaulting.

**The placement columns are derived from the row's own name, never from the
RNG.** Drawing them from a `random.Random` would shift the stream and change
every other generated value in the file. Any new column here must obey the same
rule.

Examples: `Fwd Cargo Compartment, LH` → `ForwardFuselage` / `Side` / 0.35 ·
`Aft Cabin, RH` → `AftFuselage` / `Side` / 0.65 · `Flight Compartment` →
`Cockpit` / `Side` / 0.10 · `Left Winglet` → `LeftWing` / `Front` / 0.95 ·
`Engine 1 Nacelle` → `LeftEngine` / `Plan` · `Rudder` → `VerticalStabilizer` /
`Side` · `Left Main Gear Bay` → `MainLandingGear` / `Front` · `APU Compartment`
→ `Tail` / `Side`.

---

## 8. `operators.csv` — 60 rows

| Column | Example |
| --- | --- |
| `operator_code` | `SIA` |
| `operator_name` | `Skyline International Airways` |
| `iata` | `SL` |
| `icao` | `SKL` |
| `country` | `Singapore` |
| `region` | `APAC` |
| `fleet_programs` | `P\|N\|L` |

Operator names are **invented** — do not use real airline names. Keep them
plausible (`Nordwind Continental`, `Cedar Gulf Airways`, `Andes Cargo Lines`).

---

## 9. `suppliers.csv` — 80 rows

| Column | Example |
| --- | --- |
| `supplier_code` | `HTC` |
| `supplier_name` | `Hartcliff Technologies` |
| `country` | `United Kingdom` |
| `domains` | `HYDRAULICS\|ACTUATION` |
| `cage_code` | `K1842` |

`AERA` is always present and is the supplier of every `AERALIS`-type part.
Supplier names are invented; the first 2–4 letters of the code seed the
vendor part-number prefix so a vendor PN is traceable to its supplier.

---

## 10. Generation order

`gen_reference.py` must build in this order — each step depends on the prior:

```
ata_chapters → zones → suppliers → operators
            → part_numbers   (needs ata, suppliers)
            → fins           (needs ata, zones, part_numbers)
            → software       (needs ata, fins)
            → drawings       (needs ata)
            → msn_registry   (needs operators)
```

---

## 11. `tools/lookup.py` — the authoring interface

Authoring agents must not read the 6 000-row CSVs into context. They query:

```bash
python3 tools/lookup.py --program M --ata 53 --limit 30
python3 tools/lookup.py --program M --ata 27-51 --kind fins --limit 20
python3 tools/lookup.py --program Z --kind msns --sample 25
python3 tools/lookup.py --program M --kind software --ata 27
python3 tools/lookup.py --program T --kind zones --major 800
python3 tools/lookup.py --verify D5321101820000 120WT MRD-53-1042
```

`--kind` ∈ `parts` · `fins` · `drawings` · `software` · `zones` · `msns` ·
`ata` · `operators` · `all` (default). `--verify` takes any number of
identifiers and reports whether each resolves, and to what.

---

## 12. Acceptance checks

After generation:

```bash
python3 tools/gen_reference.py --check
```

- Every `fins.default_part_number` resolves and is program-compatible
- Every `software.host_fin` resolves
- Every `*.ata_6d` exists at level 6
- Every `part_numbers.interchangeable_with` resolves
- Every `zones.airframe_section` / `ata_chapters.airframe_section` is an
  `AircraftSection` name and every `view_hint` a `ViewKind` name, with
  `zones.section_position` inside `[0.05, 0.95]` — enforced by
  `python3 tools/validate.py --only reference`
- No duplicate primary keys
- Each program has ≥ 400 parts and ≥ 200 FINs (so per-program authoring never
  runs dry)
- Re-running produces a byte-identical tree (`git diff --exit-code data/reference`)

---

## 9.1 `users.csv` — 16 rows

The pre-analysis engineers the application assigns work to. Consumed by the
connector (`graph.CreateUserAsync`), never by a document.

| Column | Example |
| --- | --- |
| `user_name` | `mlefevre` |
| `email` | `marc.lefevre@example-airframer.test` |
| `first_name` / `last_name` | `Marc` / `Lefevre` |
| `site` | `Valmont` |
| `department` | `ESZS` |
| `role` | `Senior Stress Engineer` |
| `ata_specialities` | `53\|52\|57` |
| `programs` | `P\|N\|L` |

Specialities **overlap deliberately** so most ATA chapters have more than one
plausible owner, and a few chapters have none at all — that is what keeps the
dashboard's "Unassigned Items" tile honest. Emails use the reserved `.test`
TLD so nothing can be mistaken for a real address.

## 9.2 `rules.csv` — 30 rows

Engineering rules a pre-analysis is checked against. Surfaced in the PAWO
renderer's *Rules & IPC* tab.

| Column | Example |
| --- | --- |
| `rule_id` | `RUL-0007` |
| `title` | `Damage tolerance re-substantiation trigger` |
| `text` | The rule itself, 30–70 words, written as a requirement |
| `category` | `DESIGN` · `CERTIFICATION` · `SAFETY` · `MAINTAINABILITY` · `INTERCHANGEABILITY` · `WEIGHT` · `DOCUMENTATION` · `PROCESS` |
| `severity` | `MANDATORY` · `RECOMMENDED` · `ADVISORY` |
| `ata_2d` | `53\|55\|57` — the chapters it binds |
| `programs` | `P\|C\|R\|N\|L` |
| `source` | `TDH-100 Technical Design Handbook` (invented handbook names) |

Every rule is **hand-written and distinct**; the generator only assigns ids,
program applicability and source. Thirty real rules beat a hundred generated
from eight templates — a rules list that visibly repeats itself makes the
feature look broken in a demo.
