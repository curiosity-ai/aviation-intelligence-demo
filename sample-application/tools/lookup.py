#!/usr/bin/env python3
"""Query the reference master data — the interface authoring agents use.

Authoring agents must never read the full CSVs into context. They ask for the
slice they are allowed to use::

    python3 tools/lookup.py --program M --ata 53 --limit 30
    python3 tools/lookup.py --program M --ata 27-51 --kind fins --limit 20
    python3 tools/lookup.py --program Z --kind msns --sample 25
    python3 tools/lookup.py --program M --kind software --ata 27
    python3 tools/lookup.py --program T --kind zones --major 800
    python3 tools/lookup.py --program M --kind ata --domain STRUCTURE
    python3 tools/lookup.py --verify D5321101820000 120WT M00417

Only identifiers printed here may be used in documents for that program.
"""
from __future__ import annotations

import argparse
import csv
import os
import random
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(ROOT, "data", "reference")

KINDS = ["parts", "fins", "drawings", "software", "zones", "msns", "ata",
         "operators", "suppliers"]

FILES = {
    "parts": ("part_numbers.csv", "part_number"),
    "fins": ("fins.csv", "fin"),
    "drawings": ("drawings.csv", "drawing_number"),
    "software": ("software.csv", "software_pn"),
    "zones": ("zones.csv", "zone"),
    "msns": ("msn_registry.csv", "msn"),
    "ata": ("ata_chapters.csv", "code"),
    "operators": ("operators.csv", "operator_code"),
    "suppliers": ("suppliers.csv", "supplier_code"),
}

COLUMNS = {
    "parts": ["part_number", "description", "pn_type", "ata_6d", "programs",
              "lifecycle", "mass_kg"],
    "fins": ["fin", "description", "ata_6d", "system", "programs", "zone",
             "default_part_number", "lru"],
    "drawings": ["drawing_number", "title", "ata_6d", "programs", "drawing_type", "issue"],
    "software": ["software_pn", "software_name", "system", "ata_6d", "programs",
                 "version", "load_type", "host_fin", "certification_status"],
    "zones": ["zone", "zone_name", "major_zone", "programs", "access_panels",
              "airframe_section", "view_hint", "section_position"],
    "msns": ["msn", "aircraft_type", "model", "operator_code", "engine_type",
             "delivery_date", "status"],
    "ata": ["code", "level", "title", "domain", "programs", "airframe_section",
            "view_hint"],
    "operators": ["operator_code", "operator_name", "country", "region", "fleet_programs"],
    "suppliers": ["supplier_code", "supplier_name", "country", "domains"],
}


def load(kind):
    name, _ = FILES[kind]
    with open(os.path.join(REF, name), newline="", encoding="utf-8") as fh:
        return list(csv.DictReader(fh))


def in_program(row, program):
    if program is None:
        return True
    if "programs" in row:
        return program in row["programs"].split("|")
    if "fleet_programs" in row:
        return program in row["fleet_programs"].split("|")
    if "program" in row:
        return row["program"] == program
    return True


def matches_ata(row, ata):
    if not ata:
        return True
    value = row.get("ata_6d") or row.get("code") or ""
    return value == ata or value.startswith(ata + "-") or value.startswith(ata) and (
        len(value) > len(ata) and value[len(ata)] == "-")


def render(kind, rows, limit):
    cols = COLUMNS[kind]
    rows = rows[:limit]
    if not rows:
        print(f"  (no {kind} match)")
        return
    widths = [max(len(c), max((len(str(r.get(c, ""))) for r in rows), default=0))
              for c in cols]
    widths = [min(w, 46) for w in widths]
    print("  " + "  ".join(c.ljust(w) for c, w in zip(cols, widths)))
    print("  " + "  ".join("-" * w for w in widths))
    for r in rows:
        print("  " + "  ".join(str(r.get(c, ""))[:w].ljust(w) for c, w in zip(cols, widths)))


def verify(identifiers):
    tables = {k: {r[FILES[k][1]]: r for r in load(k)} for k in KINDS}
    rc = 0
    for ident in identifiers:
        hits = [(k, t[ident]) for k, t in tables.items() if ident in t]
        if not hits:
            print(f"  ✗ {ident}  — NOT FOUND in reference data")
            rc = 1
            continue
        for kind, row in hits:
            progs = row.get("programs") or row.get("fleet_programs") or row.get("program", "")
            label = (row.get("description") or row.get("title")
                     or row.get("zone_name") or row.get("software_name")
                     or row.get("operator_name") or row.get("aircraft_type") or "")
            print(f"  ✓ {ident}  [{kind}] programs={progs or '-'}  {label}")
    return rc


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--program", choices=list("MCTZS"), help="program letter filter")
    ap.add_argument("--kind", choices=KINDS + ["all"], default="all")
    ap.add_argument("--ata", help="ATA prefix, e.g. 53 or 53-21 or 53-21-11")
    ap.add_argument("--domain", help="ATA domain filter (STRUCTURE, SYSTEMS, ...)")
    ap.add_argument("--major", help="major zone filter, e.g. 800")
    ap.add_argument("--contains", help="substring match on description/title")
    ap.add_argument("--limit", type=int, default=25)
    ap.add_argument("--sample", type=int, help="random sample of N (seeded by query)")
    ap.add_argument("--verify", nargs="+", metavar="ID",
                    help="check whether identifiers resolve")
    args = ap.parse_args()

    if args.verify:
        return verify(args.verify)

    kinds = KINDS if args.kind == "all" else [args.kind]
    if args.kind == "all":
        kinds = ["ata", "parts", "fins", "drawings", "software", "zones", "msns"]

    for kind in kinds:
        rows = [r for r in load(kind) if in_program(r, args.program)]
        if args.ata and kind in ("parts", "fins", "drawings", "software", "ata"):
            rows = [r for r in rows if matches_ata(r, args.ata)]
        if args.domain and kind == "ata":
            rows = [r for r in rows if r.get("domain") == args.domain]
        if args.major and kind == "zones":
            rows = [r for r in rows if r.get("major_zone") == args.major]
        if args.contains:
            needle = args.contains.lower()
            rows = [r for r in rows
                    if needle in (r.get("description", "") + r.get("title", "")
                                  + r.get("zone_name", "") + r.get("software_name", "")).lower()]
        if args.sample and len(rows) > args.sample:
            rng = random.Random(f"{kind}|{args.program}|{args.ata}|{args.sample}")
            rows = sorted(rng.sample(rows, args.sample),
                          key=lambda r: r[FILES[kind][1]])

        limit = args.sample or args.limit
        print(f"\n{kind.upper()}  ({len(rows)} match, showing {min(limit, len(rows))})")
        render(kind, rows, limit)
    print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
