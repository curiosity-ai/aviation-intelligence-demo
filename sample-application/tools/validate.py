#!/usr/bin/env python3
"""Coherence gate for the Aeralis MOD pre-analysis demo dataset.

Every identifier a document references must resolve. See
``.claude/skills/aeralis-dataset-coherence`` for the full contract.

    python3 tools/validate.py
    python3 tools/validate.py --program M --warnings
    python3 tools/validate.py --only referential
    python3 tools/validate.py --json report.json --strict
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import glob
import json
import os
import re
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import schema  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(ROOT, "data", "reference")
DOCS = os.path.join(ROOT, "data", "documents")

FAMILIES = ["reference", "structural", "issues", "referential", "semantic", "relational",
            "coverage"]

# Identifiers that may appear in prose; used by the (warning-only) prose sweep.
PROSE_PATTERNS = {
    "part_numbers": re.compile(r"\bD\d{13}\b|\b(?:AGS|AES|EN|LN)\d{4,6}(?:-\d{1,2}[A-Z]?|K\d)\b"),
    "mod": re.compile(r"\bMOD[- ]\d{5,6}\b"),
    "sb": re.compile(r"\b(?:MERIDIAN|CIRRANE|TITAN|ZEPHYR-22|SOLANO)-\d{2}-\d{4}\b"),
}


class Report:
    def __init__(self):
        self.errors = []
        self.warnings = []
        self.stats = {}

    def error(self, code, where, message):
        self.errors.append({"code": code, "doc": where, "message": message})

    def warn(self, code, where, message):
        self.warnings.append({"code": code, "doc": where, "message": message})


def load_ref(name):
    with open(os.path.join(REF, name), newline="", encoding="utf-8") as fh:
        return list(csv.DictReader(fh))


def parse_date(value):
    try:
        return dt.date.fromisoformat(value)
    except (TypeError, ValueError):
        return None


class Dataset:
    def __init__(self, program=None):
        self.program = program
        self.parts = {r["part_number"]: r for r in load_ref("part_numbers.csv")}
        self.fins = {r["fin"]: r for r in load_ref("fins.csv")}
        self.drawings = {r["drawing_number"]: r for r in load_ref("drawings.csv")}
        self.software = {r["software_pn"]: r for r in load_ref("software.csv")}
        self.zones = {r["zone"]: r for r in load_ref("zones.csv")}
        self.msns = {r["msn"]: r for r in load_ref("msn_registry.csv")}
        self.operators = {r["operator_code"]: r for r in load_ref("operators.csv")}
        self.ata = {r["code"]: r for r in load_ref("ata_chapters.csv")}

        self.docs = {}          # doc_id -> the latest issue
        self.issues = {}        # doc_id -> every issue, oldest first
        self.all_issues = []    # every issue of every document
        self.doc_paths = {}     # doc_id -> path
        self.by_program = defaultdict(lambda: defaultdict(list))
        self.parse_errors = []
        self._load_documents()

        # entities minted by documents rather than by reference data
        self.figures = set()
        self.repair_drawings = set()
        for doc in self.docs.values():
            if doc.get("doc_type") == "IPC":
                fig = (doc.get("type_data") or {}).get("figure")
                if fig:
                    self.figures.add((doc.get("program"), fig))
            if doc.get("doc_type") == "SRM":
                for rd in (doc.get("type_data") or {}).get("repair_drawings") or []:
                    ref = rd.get("reference") if isinstance(rd, dict) else rd
                    if ref:
                        self.repair_drawings.add((doc.get("program"), ref))

    def _load_documents(self):
        """Read every document file. Each holds an array of issues, oldest first.

        ``self.docs`` maps an identifier to its **latest** issue, which is the document as it
        stands and what the relational checks are about. ``self.all_issues`` is every issue of
        every document, which is what the content checks run over: an entity that does not
        resolve is a defect whichever issue names it.
        """
        scope = os.path.join(DOCS, self.program) if self.program else DOCS
        for path in sorted(glob.glob(os.path.join(scope, "**", "*.json"), recursive=True)):
            rel = os.path.relpath(path, ROOT)
            try:
                with open(path, encoding="utf-8") as fh:
                    raw = json.load(fh)
            except (json.JSONDecodeError, UnicodeDecodeError) as exc:
                self.parse_errors.append((rel, str(exc)))
                continue

            if not isinstance(raw, list) or not raw:
                self.parse_errors.append(
                    (rel, "a document file is a non-empty array of issues, oldest first"))
                continue

            stem = os.path.splitext(os.path.basename(path))[0]
            folder_type = os.path.basename(os.path.dirname(path))
            folder_program = os.path.basename(os.path.dirname(os.path.dirname(path)))

            issues = []
            for position, doc in enumerate(raw):
                if not isinstance(doc, dict):
                    self.parse_errors.append((rel, f"issue {position} is not an object"))
                    continue
                doc["__path"] = rel
                doc["__stem"] = stem
                doc["__folder_type"] = folder_type
                doc["__folder_program"] = folder_program
                doc["__position"] = position
                doc["__is_latest"] = position == len(raw) - 1
                issues.append(doc)

            if not issues:
                continue

            doc_id = issues[-1].get("doc_id") or stem
            if doc_id in self.docs:
                self.parse_errors.append((rel, f"duplicate doc_id {doc_id}"))
            self.docs[doc_id] = issues[-1]
            self.issues[doc_id] = issues
            self.all_issues.extend(issues)
            self.doc_paths[doc_id] = rel
            self.by_program[issues[-1].get("program")][issues[-1].get("doc_type")].append(issues[-1])

    def ref_has_program(self, table, key, program):
        row = table.get(key)
        if row is None:
            return None
        progs = row.get("programs") or row.get("program", "")
        return program in progs.split("|")


# ---------------------------------------------------------------------------
# check families
# ---------------------------------------------------------------------------


def check_reference(ds, rep):
    """The reference CSVs themselves: column set and controlled vocabularies.

    The illustration placement columns (``airframe_section`` / ``view_hint`` /
    ``section_position``) are the contract between the reference data and
    ``src/Aeralis.Illustrations``, so a typo in a section name has to fail here
    rather than at drawing time.
    """
    for name, expected in schema.REFERENCE_COLUMNS.items():
        path = os.path.join(REF, name)
        with open(path, newline="", encoding="utf-8") as fh:
            header = next(csv.reader(fh), [])
        if header != expected:
            rep.error("E-REF-COLUMNS", f"data/reference/{name}",
                      f"columns {header} != expected {expected}")

    for name, rows, key in (("zones.csv", ds.zones.values(), "zone"),
                            ("ata_chapters.csv", ds.ata.values(), "code")):
        where = f"data/reference/{name}"
        for row in rows:
            ident = row.get(key)
            section = row.get("airframe_section")
            if section not in schema.AIRFRAME_SECTIONS:
                rep.error("E-REF-VOCAB", where,
                          f"{ident}: airframe_section {section!r} is not an "
                          f"AircraftSection enum name")
            view = row.get("view_hint")
            if view not in schema.VIEW_KINDS:
                rep.error("E-REF-VOCAB", where,
                          f"{ident}: view_hint {view!r} is not a ViewKind enum name")

    for zone, row in ds.zones.items():
        raw = row.get("section_position")
        try:
            pos = float(raw)
        except (TypeError, ValueError):
            rep.error("E-REF-VOCAB", "data/reference/zones.csv",
                      f"{zone}: section_position {raw!r} is not a decimal")
            continue
        if not (schema.SECTION_POSITION_MIN - 1e-9 <= pos
                <= schema.SECTION_POSITION_MAX + 1e-9):
            rep.error("E-REF-VOCAB", "data/reference/zones.csv",
                      f"{zone}: section_position {raw} outside "
                      f"[{schema.SECTION_POSITION_MIN}, {schema.SECTION_POSITION_MAX}]")


def check_structural(ds, rep):
    for rel, msg in ds.parse_errors:
        rep.error("E-PARSE", rel, msg)

    for doc in ds.all_issues:
        doc_id = doc.get("doc_id")
        where = doc["__path"]
        doc_type = doc.get("doc_type")
        program = doc.get("program")

        if doc_type not in schema.DOC_TYPES:
            rep.error("E-TYPE", where, f"unknown doc_type {doc_type!r}")
            continue
        if doc_type != doc["__folder_type"]:
            rep.error("E-FOLDER", where,
                      f"doc_type {doc_type} but folder {doc['__folder_type']}")
        if program != doc["__folder_program"]:
            rep.error("E-FOLDER", where,
                      f"program {program} but folder {doc['__folder_program']}")
        if doc_id != doc["__stem"]:
            rep.error("E-FILENAME", where, f"doc_id {doc_id} != file stem {doc['__stem']}")
        if not schema.ID_PATTERNS[doc_type].match(doc_id):
            rep.error("E-IDPATTERN", where, f"{doc_id} does not match the {doc_type} pattern")
        else:
            encoded = schema.program_of_doc_id(doc_id, doc_type)
            if encoded and encoded != program:
                rep.error("E-PROG-MISMATCH", where,
                          f"{doc_id} encodes program {encoded}, declares {program}")

        for key in schema.REQUIRED_KEYS:
            if key not in doc or doc[key] in (None, ""):
                rep.error("E-MISSING-KEY", where, f"missing required key {key!r}")
        unknown = [k for k in doc if not k.startswith("__")
                   and k not in schema.ENVELOPE_ORDER]
        if unknown:
            rep.error("E-UNKNOWN-KEY", where, f"unknown top-level keys {unknown}")

        ent = doc.get("entities") or {}
        missing_ent = [k for k in schema.ENTITY_KEYS if k not in ent]
        if missing_ent:
            rep.error("E-ENTITY-KEYS", where, f"entities missing keys {missing_ent}")

        if not doc.get("abstract"):
            rep.error("E-ABSTRACT", where, "empty abstract")
        elif len(doc["abstract"].split()) < 15:
            rep.warn("W-ABSTRACT-SHORT", where,
                     f"abstract is {len(doc['abstract'].split())} words")
        if not doc.get("sections"):
            rep.error("E-SECTIONS", where, "empty sections")


def _check_entity_list(ds, rep, doc, key, table, code):
    where, program = doc["__path"], doc.get("program")
    for value in (doc.get("entities") or {}).get(key) or []:
        state = ds.ref_has_program(table, value, program)
        if state is None:
            rep.error(code, where, f"entities.{key}: {value} not in reference data")
        elif state is False:
            rep.error("E-PROG-MISMATCH", where,
                      f"entities.{key}: {value} is not applicable to program {program}")


def check_referential(ds, rep):
    for doc in ds.all_issues:
        doc_id = doc.get("doc_id")
        where, program = doc["__path"], doc.get("program")
        _check_entity_list(ds, rep, doc, "part_numbers", ds.parts, "E-REF-PART")
        _check_entity_list(ds, rep, doc, "fins", ds.fins, "E-REF-FIN")
        _check_entity_list(ds, rep, doc, "drawings", ds.drawings, "E-REF-DRAWING")
        _check_entity_list(ds, rep, doc, "software", ds.software, "E-REF-SOFTWARE")
        _check_entity_list(ds, rep, doc, "zones", ds.zones, "E-REF-ZONE")

        ent = doc.get("entities") or {}
        for msn in ent.get("msn_mentions") or []:
            row = ds.msns.get(msn)
            if row is None:
                rep.error("E-REF-MSN", where, f"msn_mentions: {msn} not in msn_registry")
            elif row["program"] != program:
                rep.error("E-PROG-MISMATCH", where, f"msn_mentions: {msn} is not program {program}")
        for fig in ent.get("figures") or []:
            if not schema.FIGURE_RE.match(fig):
                rep.error("E-FIGURE-PATTERN", where, f"figures: {fig} bad pattern")
            elif (program, fig) not in ds.figures:
                rep.error("E-REF-FIGURE", where, f"figures: {fig} owned by no IPC document")
        for rd in ent.get("repair_drawings") or []:
            if not schema.REPAIR_DRAWING_RE.match(rd):
                rep.error("E-RD-PATTERN", where, f"repair_drawings: {rd} bad pattern")
            elif (program, rd) not in ds.repair_drawings:
                rep.error("E-REF-RD", where, f"repair_drawings: {rd} owned by no SRM document")

        appl = doc.get("applicability") or {}
        for msn in (appl.get("msns") or []) + (appl.get("excluded_msns") or []):
            row = ds.msns.get(msn)
            if row is None:
                rep.error("E-REF-MSN", where, f"applicability: {msn} not in msn_registry")
            elif row["program"] != program:
                rep.error("E-PROG-MISMATCH", where, f"applicability: {msn} is not program {program}")
        for rng in appl.get("msn_ranges") or []:
            lo, hi = rng.get("from"), rng.get("to")
            for endpoint in (lo, hi):
                if not endpoint or not schema.MSN_RE.match(endpoint or ""):
                    rep.error("E-MSN-RANGE", where, f"bad MSN range endpoint {endpoint!r}")
                elif endpoint[0] != program:
                    rep.error("E-PROG-MISMATCH", where,
                              f"MSN range endpoint {endpoint} is not program {program}")
            if lo and hi and schema.MSN_RE.match(lo or "") and schema.MSN_RE.match(hi or ""):
                if lo >= hi:
                    rep.error("E-MSN-RANGE", where, f"MSN range {lo}..{hi} is not ascending")
                elif not any(m["program"] == program and lo <= m["msn"] <= hi
                             for m in ds.msns.values()):
                    rep.warn("W-MSN-RANGE-EMPTY", where,
                             f"MSN range {lo}..{hi} contains no registered aircraft")
        for op in appl.get("operators") or []:
            if op not in ds.operators:
                rep.error("E-REF-OPERATOR", where, f"unknown operator {op}")
        meta = schema.PROGRAM_META.get(program, {})
        for t in appl.get("aircraft_types") or []:
            if t not in meta.get("types", []):
                rep.error("E-TYPE-UNKNOWN", where, f"aircraft_type {t} is not in program {program}")
        for m in appl.get("models") or []:
            if m not in meta.get("models", []):
                rep.error("E-MODEL-UNKNOWN", where, f"model {m} is not in program {program}")

        cond = appl.get("embodiment_condition") or {}
        for field, expected_type in (("post_mod", "MOD"), ("pre_mod", "MOD"),
                                     ("post_sb", "SB"), ("pre_sb", "SB")):
            for target in cond.get(field) or []:
                _check_doc_ref(ds, rep, doc, target, expected_type,
                               f"embodiment_condition.{field}")

        for ref in doc.get("references") or []:
            target = ref.get("doc_id")
            rel_type = ref.get("relation")
            if rel_type not in schema.VOCAB["relation"]:
                rep.error("E-VOCAB", where, f"references: unknown relation {rel_type!r}")
            _check_doc_ref(ds, rep, doc, target, ref.get("doc_type"), "references")

        emb = doc.get("embodiment") or {}
        for sb in emb.get("retrofit_via") or []:
            _check_doc_ref(ds, rep, doc, sb, "SB", "embodiment.retrofit_via")


def _check_doc_ref(ds, rep, doc, target, expected_type, field):
    where, program = doc["__path"], doc.get("program")
    if not target:
        rep.error("E-REF-DOC", where, f"{field}: empty doc_id")
        return
    other = ds.docs.get(target)
    if other is None:
        rep.error("E-REF-DOC", where, f"{field}: {target} does not exist")
        return
    if other.get("program") != program:
        rep.error("E-PROG-MISMATCH", where,
                  f"{field}: {target} is program {other.get('program')}, not {program}")
    if expected_type and other.get("doc_type") != expected_type:
        rep.error("E-REF-TYPE", where,
                  f"{field}: {target} is a {other.get('doc_type')}, declared {expected_type}")


#: Which dates may precede the dataset window, by the part an issue plays in its history.
#:
#: A long-lived manual is first issued around the time its aircraft enters service — long
#: before 2016 for an ZEPHYR-22 — so the dataset holds original issues that genuinely predate its
#: window. `created` and `released` describe that original publication and are copied onto
#: every later issue of the file, so they are backdatable wherever they appear; `revised`
#: and `effective` on a later issue belong to that rework, which is current. On the original
#: issue itself every date is of its own era.
_BACKDATABLE_KEYS = {
    "original": ("created", "released", "revised", "effective"),
    "later": ("created", "released"),
    "only": (),
}


def _issue_role(ds, doc):
    """Whether this issue is the original of a modelled history, a later one, or alone.

    A document the dataset holds a single issue of does not claim to hold its original — the
    history simply is not modelled — so nothing about it may predate the window.
    """
    if doc.get("doc_type") not in schema.LONG_LIVED_DOC_TYPES:
        return "only"

    issues = ds.issues.get(doc.get("doc_id")) or []
    if len(issues) < 2:
        return "only"

    return "original" if doc is issues[0] else "later"


def _date_floor(ds, doc, default):
    """How far back this document may reach: its aircraft's entry into service, less the
    lead time its manuals were being written in."""
    eis = parse_date(schema.entry_into_service(
        ds.msns.values(), (doc.get("applicability") or {}).get("aircraft_types"),
        doc.get("program")))

    return min(default, eis - dt.timedelta(days=schema.AUTHORING_LEAD_DAYS)) if eis else default


def check_semantic(ds, rep):
    start, end = parse_date(schema.DATASET_START), parse_date(schema.DATASET_END)
    for doc in ds.all_issues:
        doc_id = doc.get("doc_id")
        where = doc["__path"]
        ata = doc.get("ata") or {}
        six, four, two = ata.get("ata_6d"), ata.get("ata_4d"), ata.get("ata_2d")
        if not six or not schema.ATA6_RE.match(six or ""):
            rep.error("E-ATA", where, f"bad ata_6d {six!r}")
        elif six not in ds.ata or ds.ata[six]["level"] != "6":
            rep.error("E-ATA", where, f"ata_6d {six} is not in the catalogue at level 6")
        else:
            if four != six[:5]:
                rep.error("E-ATA", where, f"ata_4d {four!r} is not a prefix of {six}")
            if two != six[:2]:
                rep.error("E-ATA", where, f"ata_2d {two!r} is not a prefix of {six}")
            if not ds.ref_has_program(ds.ata, six, doc.get("program")):
                rep.error("E-PROG-MISMATCH", where,
                          f"ata_6d {six} is not applicable to program {doc.get('program')}")
        for sec in doc.get("secondary_ata") or []:
            if not (schema.ATA4_RE.match(sec) or schema.ATA6_RE.match(sec)):
                rep.error("E-ATA", where, f"secondary_ata {sec!r} is not a 4D or 6D code")
            elif sec not in ds.ata:
                rep.error("E-ATA", where, f"secondary_ata {sec} is not in the catalogue")

        dates = doc.get("dates") or {}
        ordered = [(k, parse_date(dates.get(k))) for k in schema.DATE_KEYS]
        present = [(k, d) for k, d in ordered if d]
        floor = _date_floor(ds, doc, start)
        for key, value in present:
            lower = floor if key in _BACKDATABLE_KEYS.get(_issue_role(ds, doc), ()) else start
            if not (lower <= value <= end):
                rep.error("E-DATE-RANGE", where,
                          f"dates.{key} {value} outside {lower}..{schema.DATASET_END}")
        for (k1, d1), (k2, d2) in zip(present, present[1:]):
            if d1 > d2:
                rep.error("E-DATE-ORDER", where, f"dates.{k1} {d1} is after dates.{k2} {d2}")

        appl = doc.get("applicability") or {}
        scope = appl.get("scope")
        if scope not in schema.VOCAB["applicability.scope"]:
            rep.error("E-VOCAB", where, f"applicability.scope {scope!r}")
        elif scope == "MSN_LIST" and not appl.get("msns"):
            rep.error("E-SCOPE", where, "scope MSN_LIST but msns is empty")
        elif scope == "MSN_RANGE" and not appl.get("msn_ranges"):
            rep.error("E-SCOPE", where, "scope MSN_RANGE but msn_ranges is empty")
        elif scope == "OPERATOR" and not appl.get("operators"):
            rep.error("E-SCOPE", where, "scope OPERATOR but operators is empty")

        emb = doc.get("embodiment")
        if emb:
            if emb.get("status") and emb["status"] not in schema.VOCAB["embodiment.status"]:
                rep.error("E-VOCAB", where, f"embodiment.status {emb['status']!r}")
            for pt in emb.get("points") or []:
                if pt not in schema.VOCAB["embodiment.points"]:
                    rep.error("E-VOCAB", where, f"embodiment.points {pt!r}")
            eff = emb.get("production_effectivity")
            if eff:
                row = ds.msns.get(eff)
                if row is None:
                    rep.error("E-REF-MSN", where, f"production_effectivity {eff} unknown")
                elif row["program"] != doc.get("program"):
                    rep.error("E-PROG-MISMATCH", where,
                              f"production_effectivity {eff} is not program {doc.get('program')}")

        _check_type_vocab(doc, rep)
        _prose_sweep(ds, doc, rep)


def _vocab(doc, rep, td, key, vocab_key, container=None):
    value = (container or td).get(key)
    if value is None or value == "":
        return
    values = value if isinstance(value, list) else [value]
    for v in values:
        if v not in schema.VOCAB[vocab_key]:
            rep.error("E-VOCAB", doc["__path"], f"{vocab_key}: unexpected value {v!r}")


def _check_type_vocab(doc, rep):
    td = doc.get("type_data") or {}
    t = doc.get("doc_type")
    if t == "MOD":
        _vocab(doc, rep, td, "mod_class", "mod.mod_class")
        _vocab(doc, rep, td, "change_category", "mod.change_category")
        _vocab(doc, rep, td, "trigger", "mod.trigger")
        _vocab(doc, rep, td, "interchangeability", "mod.interchangeability")
        _vocab(doc, rep, td, "risk_level", "mod.risk_level")
        _vocab(doc, rep, td, "impact", "mod.certification.impact", td.get("certification") or {})
    elif t == "SB":
        _vocab(doc, rep, td, "sb_type", "sb.sb_type")
        _vocab(doc, rep, td, "compliance", "sb.compliance")
        _vocab(doc, rep, td, "reason_code", "sb.reason_code")
        _vocab(doc, rep, td, "downtime_category", "sb.downtime_category",
               td.get("manpower") or {})
    elif t == "IO":
        _vocab(doc, rep, td, "request_category", "io.request_category")
        _vocab(doc, rep, td, "priority", "io.priority")
        _vocab(doc, rep, td, "io_status", "io.io_status")
        _vocab(doc, rep, td, "requester_type", "io.requester_type", td.get("raised_by") or {})
        _vocab(doc, rep, td, "funding", "io.funding", td.get("commercial") or {})
        # A closed order must say why; a converted one must not pretend it was closed.
        closed = td.get("io_status") in ("REJECTED", "ON_HOLD", "CLOSED")
        note = (td.get("closure_note") or "").strip()
        if closed and not note:
            rep.error("E-IO-CLOSURE", doc["__path"],
                      f"io_status {td.get('io_status')} requires a closure_note")
        if closed and td.get("created_mod"):
            rep.error("E-IO-CLOSURE", doc["__path"],
                      f"io_status {td.get('io_status')} must not set created_mod")
        if not closed and note:
            rep.error("E-IO-CLOSURE", doc["__path"],
                      "closure_note is only for REJECTED / ON_HOLD / CLOSED orders")
        if closed and note and not 25 <= len(note.split()) <= 160:
            rep.warn("W-IO-CLOSURE-LEN", doc["__path"],
                     f"closure_note is {len(note.split())} words (expected 40-110)")
    elif t == "PAWO":
        _vocab(doc, rep, td, "pawo_status", "pawo.pawo_status")
        _vocab(doc, rep, td, "recommendation", "pawo.recommendation")
        for task in td.get("tasks") or []:
            _vocab(doc, rep, td, "method", "pawo.task.method", task)
            _vocab(doc, rep, td, "status", "pawo.task.status", task)
        for f in td.get("expected_findings") or []:
            _vocab(doc, rep, td, "finding_type", "pawo.finding_type", f)
            _vocab(doc, rep, td, "severity", "pawo.severity", f)
            _vocab(doc, rep, td, "discovery", "pawo.discovery", f)
    elif t == "AMM":
        _vocab(doc, rep, td, "task_type", "amm.task_type")
    elif t == "SRM":
        _vocab(doc, rep, td, "repair_category", "srm.repair_category")
        _vocab(doc, rep, td, "damage_type", "srm.damage_type")
        _vocab(doc, rep, td, "ndt_method", "srm.ndt_method")
    elif t == "WDM":
        _vocab(doc, rep, td, "diagram_type", "wdm.diagram_type")
    elif t == "FCOM":
        _vocab(doc, rep, td, "section", "fcom.section")
        _vocab(doc, rep, td, "operational_impact", "fcom.operational_impact")
        _vocab(doc, rep, td, "flight_phase", "fcom.flight_phase")


def _prose_sweep(ds, doc, rep):
    """Warn when prose names an identifier that the entities block omits."""
    text = " ".join(str(v) for v in (doc.get("sections") or {}).values())
    text += " " + (doc.get("abstract") or "")
    ent = doc.get("entities") or {}
    declared_parts = set(ent.get("part_numbers") or [])
    declared_docs = {r.get("doc_id") for r in doc.get("references") or []}
    declared_docs |= set((doc.get("embodiment") or {}).get("retrofit_via") or [])
    cond = (doc.get("applicability") or {}).get("embodiment_condition") or {}
    for key in schema.EMBODIMENT_CONDITION_KEYS:
        declared_docs |= set(cond.get(key) or [])

    for pn in set(PROSE_PATTERNS["part_numbers"].findall(text)):
        if pn and pn not in declared_parts:
            rep.warn("W-PROSE-ENTITY", doc["__path"],
                     f"prose names part {pn} but entities.part_numbers does not")
    for mod in set(PROSE_PATTERNS["mod"].findall(text)):
        norm = mod.replace(" ", "-")
        if norm != doc.get("doc_id") and norm not in declared_docs:
            rep.warn("W-PROSE-ENTITY", doc["__path"],
                     f"prose names {mod} but references does not")
    for sb in set(PROSE_PATTERNS["sb"].findall(text)):
        if sb != doc.get("doc_id") and sb not in declared_docs:
            rep.warn("W-PROSE-ENTITY", doc["__path"],
                     f"prose names {sb} but references does not")


def check_relational(ds, rep):
    mods = {i: d for i, d in ds.docs.items() if d.get("doc_type") == "MOD"}
    sbs = {i: d for i, d in ds.docs.items() if d.get("doc_type") == "SB"}
    pawos = {i: d for i, d in ds.docs.items() if d.get("doc_type") == "PAWO"}

    referenced = set()
    for doc in ds.all_issues:
        for ref in doc.get("references") or []:
            if ref.get("doc_id"):
                referenced.add(ref["doc_id"])
        referenced.update((doc.get("embodiment") or {}).get("retrofit_via") or [])

    # --- SB ↔ MOD ---------------------------------------------------------
    for sb_id, sb in sbs.items():
        where = sb["__path"]
        embodied = [r["doc_id"] for r in sb.get("references") or []
                    if r.get("relation") == "EMBODIES"]
        td_mod = (sb.get("type_data") or {}).get("embodied_mod")
        if len(embodied) != 1:
            rep.error("E-SB-MOD", where,
                      f"expected exactly one EMBODIES reference, found {len(embodied)}")
            continue
        mod_id = embodied[0]
        if td_mod and td_mod != mod_id:
            rep.error("E-SB-MOD", where,
                      f"type_data.embodied_mod {td_mod} != EMBODIES reference {mod_id}")
        mod = mods.get(mod_id)
        if mod is None:
            continue
        status = (mod.get("embodiment") or {}).get("status")
        if status in schema.CANDIDATE_STATUSES:
            rep.error("E-SB-CANDIDATE", where,
                      f"embodies {mod_id} which is a candidate ({status})")
        back = [r["doc_id"] for r in mod.get("references") or []
                if r.get("relation") == "EMBODIED_BY"]
        if sb_id not in back:
            rep.error("E-SB-BACKREF", mod["__path"],
                      f"missing EMBODIED_BY reference to {sb_id}")
        if sb_id not in ((mod.get("embodiment") or {}).get("retrofit_via") or []):
            rep.warn("W-SB-RETROFIT", mod["__path"],
                     f"embodiment.retrofit_via does not list {sb_id}")

        # effectivity must not be broader than the MOD it embodies
        sa, ma = sb.get("applicability") or {}, mod.get("applicability") or {}
        for field in ("aircraft_types", "models"):
            sset, mset = set(sa.get(field) or []), set(ma.get(field) or [])
            if mset and not sset <= mset:
                rep.error("E-APPL-SUPERSET", where,
                          f"{field} {sorted(sset - mset)} not covered by {mod_id}")
        s_msns, m_msns = set(sa.get("msns") or []), set(ma.get("msns") or [])
        if m_msns and s_msns and not s_msns <= m_msns:
            rep.error("E-APPL-SUPERSET", where,
                      f"msns {sorted(s_msns - m_msns)} not covered by {mod_id}")

        compliance = (sb.get("type_data") or {}).get("compliance")
        ad_linked = (sb.get("type_data") or {}).get("ad_linked")
        if compliance == "MANDATORY" and not ad_linked:
            rep.error("E-SB-AD", where, "compliance MANDATORY requires ad_linked true")
        if ad_linked and compliance != "MANDATORY":
            rep.error("E-SB-AD", where, f"ad_linked true but compliance is {compliance}")

    # --- MOD lifecycle ----------------------------------------------------
    supersedes = {}
    for mod_id, mod in mods.items():
        where = mod["__path"]
        status = (mod.get("embodiment") or {}).get("status")
        rels = {r.get("relation") for r in mod.get("references") or []}
        if status == "SUPERSEDED" and "SUPERSEDED_BY" not in rels:
            rep.error("E-MOD-SUPERSEDE", where,
                      "status SUPERSEDED but no SUPERSEDED_BY reference")
        if status in schema.CANDIDATE_STATUSES and "EMBODIED_BY" in rels:
            rep.error("E-MOD-CANDIDATE-SB", where,
                      f"candidate MOD ({status}) must not have an SB")
        if status == "CANCELLED" and ("EMBODIED_BY" in rels or "IMPACTS" in rels):
            rep.error("E-MOD-CANCELLED", where,
                      "cancelled MOD must not carry SB or manual impacts")
        for r in mod.get("references") or []:
            if r.get("relation") == "SUPERSEDES":
                supersedes.setdefault(mod_id, set()).add(r["doc_id"])

    for start_id in supersedes:
        seen, stack = set(), [start_id]
        while stack:
            cur = stack.pop()
            if cur == start_id and seen:
                rep.error("E-MOD-CYCLE", ds.doc_paths.get(start_id, start_id),
                          f"supersession cycle through {start_id}")
                break
            if cur in seen:
                continue
            seen.add(cur)
            stack.extend(supersedes.get(cur, ()))

    # --- PAWO -------------------------------------------------------------
    pawo_subjects = defaultdict(list)
    for pawo_id, pawo in pawos.items():
        where = pawo["__path"]
        td = pawo.get("type_data") or {}
        subject = td.get("subject_mod")
        if not subject:
            rep.error("E-PAWO-SUBJECT", where, "missing type_data.subject_mod")
            continue
        pawo_subjects[subject].append(pawo_id)
        mod = mods.get(subject)
        if mod is None:
            rep.error("E-REF-DOC", where, f"subject_mod {subject} does not exist")
            continue
        if mod.get("program") != pawo.get("program"):
            rep.error("E-PROG-MISMATCH", where, f"subject_mod {subject} is another program")
        status = (mod.get("embodiment") or {}).get("status")
        historical = bool(td.get("is_historical"))
        completed = td.get("completed_date")
        outcome = td.get("outcome")

        # An open work order sits on a candidate; a historical one records the
        # pre-analysis a released modification already went through.
        if historical:
            if status not in schema.RELEASED_STATUSES or status == "CANCELLED":
                rep.error("E-PAWO-SUBJECT", where,
                          f"historical PAWO subject {subject} is {status}, expected a released MOD")
            if not completed:
                rep.error("E-PAWO-HISTORY", where, "historical PAWO needs completed_date")
            if outcome not in ("VALIDATED", "REJECTED", "DELEGATED"):
                rep.error("E-VOCAB", where, f"historical PAWO outcome {outcome!r}")
            if td.get("hero_scenario"):
                rep.error("E-PAWO-HISTORY", where, "historical PAWO cannot be the hero scenario")
        else:
            if status not in schema.CANDIDATE_STATUSES:
                rep.error("E-PAWO-SUBJECT", where,
                          f"subject_mod {subject} is not a candidate (status {status})")
            if completed:
                rep.error("E-PAWO-HISTORY", where, "open PAWO must not carry completed_date")
            if outcome:
                rep.error("E-PAWO-HISTORY", where, f"open PAWO must not carry outcome {outcome!r}")

        if completed:
            done = parse_date(completed)
            opened = parse_date(td.get("raised_date")) or parse_date(
                (pawo.get("dates") or {}).get("created"))
            if done is None:
                rep.error("E-PAWO-HISTORY", where, f"bad completed_date {completed!r}")
            elif opened and done < opened:
                rep.error("E-DATE-ORDER", where,
                          f"completed_date {done} precedes raised_date {opened}")

        source_io = td.get("source_io")
        if source_io:
            _check_doc_ref(ds, rep, pawo, source_io, "IO", "type_data.source_io")

        mod_entities = {k: set((mod.get("entities") or {}).get(k) or [])
                        for k in schema.ENTITY_KEYS}
        seeds = td.get("search_scope", {}).get("entity_seeds") or {}
        for key, values in seeds.items():
            if key not in schema.ENTITY_KEYS:
                rep.error("E-PAWO-SEED", where, f"unknown entity_seeds key {key!r}")
                continue
            extra = set(values or []) - mod_entities.get(key, set())
            if extra:
                rep.error("E-PAWO-SEED", where,
                          f"entity_seeds.{key} {sorted(extra)} not in {subject} entities")

        findings = td.get("expected_findings") or []
        lo, hi = (2, 5) if historical else (5, 9)
        if not lo <= len(findings) <= hi:
            rep.error("E-PAWO-FINDINGS", where,
                      f"{len(findings)} expected_findings (need {lo}..{hi})")
        if not historical and not any(f.get("severity") == "BLOCKING" for f in findings):
            rep.error("E-PAWO-FINDINGS", where, "no BLOCKING finding")
        seen_ids = set()
        for f in findings:
            fid = f.get("doc_id")
            _check_doc_ref(ds, rep, pawo, fid, f.get("doc_type"), "expected_findings")
            if fid in seen_ids:
                rep.error("E-PAWO-FINDINGS", where, f"duplicate finding on {fid}")
            seen_ids.add(fid)
            target = ds.docs.get(fid)
            if target is None:
                continue
            ev = f.get("evidence") or {}
            target_ent = {k: set((target.get("entities") or {}).get(k) or [])
                          for k in schema.ENTITY_KEYS}
            for ev_key, ent_key in (("shared_part_numbers", "part_numbers"),
                                    ("shared_fins", "fins"), ("shared_zones", "zones")):
                for value in ev.get(ev_key) or []:
                    if value not in mod_entities.get(ent_key, set()):
                        rep.error("E-EVIDENCE", where,
                                  f"{ev_key} {value} not in subject MOD {subject}")
                    if value not in target_ent.get(ent_key, set()):
                        rep.error("E-EVIDENCE", where,
                                  f"{ev_key} {value} not in finding target {fid}")
            for six in ev.get("shared_ata_6d") or []:
                mod_six = {(mod.get("ata") or {}).get("ata_6d")}
                tgt_six = {(target.get("ata") or {}).get("ata_6d")}
                if six not in mod_six | set((mod.get("secondary_ata") or [])):
                    rep.error("E-EVIDENCE", where, f"shared_ata_6d {six} not on {subject}")
                if six not in tgt_six | set((target.get("secondary_ata") or [])):
                    rep.error("E-EVIDENCE", where, f"shared_ata_6d {six} not on {fid}")
        for nf in td.get("expected_non_findings") or []:
            _check_doc_ref(ds, rep, pawo, nf.get("doc_id"), nf.get("doc_type"),
                           "expected_non_findings")

    for mod_id, mod in mods.items():
        status = (mod.get("embodiment") or {}).get("status")
        if status == "PRE_ANALYSIS" and len(pawo_subjects.get(mod_id, [])) != 1:
            rep.error("E-PAWO-COVER", mod["__path"],
                      f"PRE_ANALYSIS MOD has {len(pawo_subjects.get(mod_id, []))} PAWOs (need 1)")

    # --- manual orphans ---------------------------------------------------
    for doc_id, doc in ds.docs.items():
        if doc.get("doc_type") not in ("AMM", "IPC", "SRM", "TSM", "WDM", "FCOM"):
            continue
        outgoing = bool(doc.get("references"))
        if doc_id not in referenced and not outgoing:
            rep.error("E-ORPHAN", doc["__path"], "manual document is neither referenced nor references anything")


def _check_history_spread(historical, prog, rep):
    """The dashboard's last-month and last-90-day metrics need recent completions."""
    if not historical:
        return
    end = parse_date(schema.DATASET_END)
    done = [parse_date((p.get("type_data") or {}).get("completed_date")) for p in historical]
    done = [d for d in done if d]
    last30 = sum(1 for d in done if (end - d).days <= 30)
    last90 = sum(1 for d in done if (end - d).days <= 90)
    if last90 < 20:
        rep.error("E-HISTORY-SPREAD", f"program {prog}",
                  f"{last90} historical PAWOs completed in the last 90 days (need >= 20)")
    if last30 < 8:
        rep.error("E-HISTORY-SPREAD", f"program {prog}",
                  f"{last30} historical PAWOs completed in the last 30 days (need >= 8)")

    # The throughput sparkline is per week: completions bunched into a few days
    # draw a spike, not a trend.
    weeks = {d.isocalendar()[:2] for d in done if (end - d).days <= 180}
    if len(weeks) < 14:
        rep.error("E-HISTORY-SPREAD", f"program {prog}",
                  f"completions fall in {len(weeks)} distinct weeks of the last 180 days "
                  f"(need >= 14)")

    outcomes = defaultdict(int)
    for p in historical:
        outcomes[(p.get("type_data") or {}).get("outcome")] += 1
    for value in ("VALIDATED", "REJECTED", "DELEGATED"):
        if outcomes.get(value, 0) < 6:
            rep.error("E-HISTORY-SPREAD", f"program {prog}",
                      f"{outcomes.get(value, 0)} historical PAWOs with outcome {value} "
                      f"(need >= 6)")


def check_coverage(ds, rep):
    programs = [ds.program] if ds.program else schema.PROGRAMS
    for prog in programs:
        by_type = ds.by_program.get(prog, {})
        counts = {t: len(by_type.get(t, [])) for t in schema.DOC_TYPES}
        ds_stats = rep.stats.setdefault("counts", {})
        ds_stats[prog] = counts
        for doc_type, expected in schema.EXPECTED_COUNTS.items():
            actual = counts.get(doc_type, 0)
            if actual != expected:
                rep.error("E-COUNT", f"program {prog}",
                          f"{doc_type}: {actual} documents, expected {expected}")

        mods = by_type.get("MOD", [])
        sbs = by_type.get("SB", [])
        atas = {(d.get("ata") or {}).get("ata_2d") for d in mods + sbs}
        atas.discard(None)
        if mods and len(atas) < 22:
            rep.error("E-ATA-SPREAD", f"program {prog}",
                      f"only {len(atas)} distinct ATA 2D across MODs and SBs (need >= 22)")

        candidates = [m for m in mods
                      if (m.get("embodiment") or {}).get("status") in schema.CANDIDATE_STATUSES]
        if mods and len(candidates) != 20:
            rep.error("E-MOD-SPLIT", f"program {prog}",
                      f"{len(candidates)} candidate MODs, expected 20")

        all_pawos = by_type.get("PAWO", [])
        pawos = [p for p in all_pawos if not (p.get("type_data") or {}).get("is_historical")]
        historical = [p for p in all_pawos if (p.get("type_data") or {}).get("is_historical")]
        if all_pawos and len(pawos) != 12:
            rep.error("E-COUNT", f"program {prog}",
                      f"{len(pawos)} open PAWOs, expected 12")
        if all_pawos and len(historical) != 40:
            rep.error("E-COUNT", f"program {prog}",
                      f"{len(historical)} historical PAWOs, expected 40")
        _check_history_spread(historical, prog, rep)
        heroes = [p for p in pawos if (p.get("type_data") or {}).get("hero_scenario")]
        if pawos and len(heroes) != 1:
            rep.error("E-HERO", f"program {prog}",
                      f"{len(heroes)} hero PAWOs, expected exactly 1")

        findings = [f for p in pawos for f in (p.get("type_data") or {}).get("expected_findings") or []]
        if pawos:
            used_types = {f.get("finding_type") for f in findings}
            missing = [v for v in schema.VOCAB["pawo.finding_type"] if v not in used_types]
            if missing:
                rep.error("E-FINDING-COVER", f"program {prog}",
                          f"finding_type values never used: {missing}")
            pure_sem = [f for f in findings if f.get("discovery") == "SEMANTIC"
                        and not any((f.get("evidence") or {}).get(k)
                                    for k in ("shared_part_numbers", "shared_fins",
                                              "shared_zones", "shared_ata_6d"))]
            pure_ent = [f for f in findings if f.get("discovery") == "ENTITY"
                        and any((f.get("evidence") or {}).get(k)
                                for k in ("shared_part_numbers", "shared_fins", "shared_zones"))]
            if len(pure_sem) < 6:
                rep.error("E-FINDING-COVER", f"program {prog}",
                          f"{len(pure_sem)} pure-SEMANTIC findings (need >= 6)")
            if len(pure_ent) < 6:
                rep.error("E-FINDING-COVER", f"program {prog}",
                          f"{len(pure_ent)} pure-ENTITY findings (need >= 6)")
            for p in pawos:
                nf = (p.get("type_data") or {}).get("expected_non_findings") or []
                if len(nf) < 2:
                    rep.error("E-FINDING-COVER", p["__path"],
                              f"{len(nf)} expected_non_findings (need >= 2)")


def check_issues(ds, rep):
    """The issue history of every document.

    A document file is an array of issues, oldest first, and the last one is the document as it
    stands. Anything that would let the array lie about that history is an error here: a gap or
    a repeat in the letters, a revision that disagrees with the issue index, a date that runs
    backwards, or an issue that claims to have been raised by a finding that does not exist.
    """
    completed = {}
    for doc_id, doc in ds.docs.items():
        if doc.get("doc_type") != "PAWO":
            continue
        data = doc.get("type_data") or {}
        if data.get("pawo_status") != "COMPLETED":
            continue
        for finding in data.get("expected_findings") or []:
            completed[(doc_id, finding.get("finding_id"))] = (doc, finding)

    for doc_id, issues in sorted(ds.issues.items()):
        where = issues[-1]["__path"]
        doc_type = issues[-1].get("doc_type")

        identity = {(d.get("doc_id"), d.get("doc_type"), d.get("program")) for d in issues}
        if len(identity) > 1:
            rep.error("E-ISSUE-IDENTITY", where,
                      "issues disagree on doc_id, doc_type or program")

        previous_letter, previous_date = None, None
        for position, doc in enumerate(issues):
            issue = doc.get("issue")
            if not isinstance(issue, dict):
                rep.error("E-ISSUE", where, f"issue {position} has no issue block")
                continue

            letter = issue.get("letter")
            index = schema.issue_index(letter) if isinstance(letter, str) else None
            if index is None:
                rep.error("E-ISSUE", where, f"issue {position} has letter {letter!r}")
                continue

            # Letters run A, B, C … in file order. A single-issue document may start anywhere,
            # because the dataset holds only the issue it is at; a history must start at A.
            if position == 0:
                if len(issues) > 1 and letter != "A":
                    rep.error("E-ISSUE-ORDER", where,
                              f"an issue history starts at A, not {letter}")
            elif index != schema.issue_index(previous_letter) + 1:
                rep.error("E-ISSUE-ORDER", where,
                          f"issue {letter} follows {previous_letter}")

            # Inside the array an issue supersedes the one before it. The first element may
            # name the letter before it instead: a document the dataset holds only the current
            # issue of still had earlier ones, and saying so is truthful.
            if position:
                if issue.get("supersedes") != previous_letter:
                    rep.error("E-ISSUE-ORDER", where,
                              f"issue {letter} supersedes {issue.get('supersedes')!r}, "
                              f"expected {previous_letter!r}")
            else:
                implied = schema.ISSUE_LETTERS[index - 2] if index > 1 else None
                if issue.get("supersedes") != implied:
                    rep.error("E-ISSUE-ORDER", where,
                              f"issue {letter} supersedes {issue.get('supersedes')!r}, "
                              f"expected {implied!r}")

            accepted = schema.revision_tokens(doc_type, index)
            if doc.get("revision") not in accepted:
                rep.error("E-ISSUE-REVISION", where,
                          f"issue {letter} has revision {doc.get('revision')!r}, "
                          f"expected one of {sorted(accepted)}")

            date = parse_date(issue.get("date"))
            if issue.get("date") and date is None:
                rep.error("E-ISSUE-DATE", where, f"issue {letter} has date {issue['date']!r}")
            elif date and previous_date and date < previous_date:
                rep.error("E-ISSUE-DATE", where,
                          f"issue {letter} is dated before {previous_letter}")

            pawo_id = issue.get("raised_by_pawo")
            finding_id = issue.get("raised_by_finding")
            if position == 0:
                if pawo_id or finding_id:
                    rep.error("E-ISSUE-CAUSE", where,
                              f"issue {letter} is the original and cannot be raised by a finding")
            elif not pawo_id or not finding_id:
                rep.error("E-ISSUE-CAUSE", where,
                          f"issue {letter} names no finding — every rework has a cause")
            else:
                found = completed.get((pawo_id, finding_id))
                if found is None:
                    rep.error("E-ISSUE-CAUSE", where,
                              f"issue {letter} names {pawo_id} {finding_id}, "
                              "which is not a finding of a completed work order")
                else:
                    pawo, finding = found
                    if finding.get("doc_id") != doc_id:
                        rep.error("E-ISSUE-CAUSE", where,
                                  f"issue {letter} names {pawo_id} {finding_id}, which targets "
                                  f"{finding.get('doc_id')}")
                    if pawo.get("program") != doc.get("program"):
                        rep.error("E-ISSUE-CAUSE", where,
                                  f"issue {letter} names {pawo_id} from another program")
                    if issue.get("finding_type") != finding.get("finding_type"):
                        rep.error("E-ISSUE-CAUSE", where,
                                  f"issue {letter} says {issue.get('finding_type')!r}, "
                                  f"the finding says {finding.get('finding_type')!r}")
                    closed = parse_date((pawo.get("type_data") or {}).get("completed_date"))
                    if date and closed and date < closed:
                        rep.error("E-ISSUE-DATE", where,
                                  f"issue {letter} is dated before {pawo_id} closed")

            if not (issue.get("reason") or "").strip():
                rep.error("E-ISSUE", where, f"issue {letter} gives no reason")

            previous_letter, previous_date = letter, date or previous_date

        # The document's own dates have to agree with the issue it is at.
        latest = issues[-1]
        revised = parse_date((latest.get("dates") or {}).get("revised"))
        stamped = parse_date((latest.get("issue") or {}).get("date"))
        if len(issues) > 1 and revised and stamped and revised != stamped:
            rep.error("E-ISSUE-DATE", where,
                      f"dates.revised {revised} is not the latest issue's date {stamped}")


CHECKS = {
    "reference": check_reference,
    "structural": check_structural,
    "issues": check_issues,
    "referential": check_referential,
    "semantic": check_semantic,
    "relational": check_relational,
    "coverage": check_coverage,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--program", choices=schema.PROGRAMS)
    ap.add_argument("--only", choices=FAMILIES, action="append")
    ap.add_argument("--warnings", action="store_true", help="print warnings")
    ap.add_argument("--strict", action="store_true", help="fail on warnings too")
    ap.add_argument("--json", metavar="PATH", help="write a machine-readable report")
    ap.add_argument("--limit", type=int, default=60, help="max messages to print")
    args = ap.parse_args()

    ds = Dataset(args.program)
    rep = Report()
    if not ds.docs:
        print("no documents found under data/documents/ — nothing to validate")
        return 0

    for family in (args.only or FAMILIES):
        CHECKS[family](ds, rep)

    scope = f"program {args.program}" if args.program else "all programs"
    print(f"validate: {len(ds.docs)} documents, {scope}")
    counts = rep.stats.get("counts") or {}
    for prog, c in sorted(counts.items()):
        line = "  ".join(f"{t}:{c.get(t, 0)}" for t in schema.DOC_TYPES)
        print(f"  {prog}  {line}  total:{sum(c.values())}")

    if args.warnings or args.strict:
        for w in rep.warnings[:args.limit]:
            print(f"  warn  [{w['code']}] {w['doc']}: {w['message']}")
        if len(rep.warnings) > args.limit:
            print(f"  ... {len(rep.warnings) - args.limit} more warnings")
    for e in rep.errors[:args.limit]:
        print(f"  ERROR [{e['code']}] {e['doc']}: {e['message']}")
    if len(rep.errors) > args.limit:
        print(f"  ... {len(rep.errors) - args.limit} more errors")

    by_code = defaultdict(int)
    for e in rep.errors:
        by_code[e["code"]] += 1
    if by_code:
        print("\nerrors by code: " + ", ".join(f"{k}={v}" for k, v in sorted(by_code.items())))
    print(f"\n{len(rep.errors)} error(s), {len(rep.warnings)} warning(s)")

    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump({"errors": rep.errors, "warnings": rep.warnings,
                       "stats": rep.stats}, fh, indent=2)
        print(f"report written to {args.json}")

    if rep.errors:
        return 1
    if rep.warnings and args.strict:
        return 1
    return 2 if rep.warnings else 0


if __name__ == "__main__":
    sys.exit(main())
