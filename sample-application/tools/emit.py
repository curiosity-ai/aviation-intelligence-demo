#!/usr/bin/env python3
"""Materialise agent-authored JSONL into one JSON file per document.

    build/authored/{PROGRAM}_{DOC_TYPE}[_n].jsonl
        → data/documents/{PROGRAM}/{DOC_TYPE}/{doc_id}.json

A document file holds an **array of issues**, oldest first, and the last element is the
document as it stands. One JSONL line is one issue: lines that share a ``doc_id`` are collected
into one array and ordered by ``issue.letter``. A line with no ``issue`` block is taken as
issue A.

Normalises key order, fills missing optional keys with empty defaults, and
refuses to write a document whose identifier does not match its type or whose
program letter disagrees with its ``program`` field. Never invents identifiers.

    python3 tools/emit.py                 # emit everything
    python3 tools/emit.py --program M     # one program
    python3 tools/emit.py --clean         # also delete orphaned JSON files
    python3 tools/emit.py --dry-run
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import schema  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AUTHORED = os.path.join(ROOT, "build", "authored")
DOCS = os.path.join(ROOT, "data", "documents")


def merge_defaults(doc):
    """Return the document with every envelope key present, in envelope order."""
    base = schema.default_document()
    out = {}
    for key in schema.ENVELOPE_ORDER:
        if key in doc and doc[key] is not None:
            value = doc[key]
            if key == "entities" and isinstance(value, dict):
                value = {k: value.get(k, []) or [] for k in schema.ENTITY_KEYS}
            elif key == "applicability" and isinstance(value, dict):
                merged = dict(base["applicability"])
                merged.update(value)
                cond = dict(base["applicability"]["embodiment_condition"])
                cond.update(value.get("embodiment_condition") or {})
                merged["embodiment_condition"] = {k: cond.get(k, []) or []
                                                  for k in schema.EMBODIMENT_CONDITION_KEYS}
                value = {k: merged.get(k) for k in schema.APPLICABILITY_KEYS}
            elif key == "dates" and isinstance(value, dict):
                value = {k: value.get(k) for k in schema.DATE_KEYS}
            elif key == "issue" and isinstance(value, dict):
                merged = schema.default_issue()
                merged.update(value)
                value = {k: merged.get(k) for k in schema.ISSUE_KEYS}
            elif key == "_meta" and isinstance(value, dict):
                merged = dict(base["_meta"])
                merged.update(value)
                value = merged
            out[key] = value
        else:
            out[key] = base[key]
    # surface any key the author added that the envelope does not know about;
    # the caller reports it rather than silently dropping it.
    extra = [k for k in doc if k not in schema.ENVELOPE_ORDER]
    return out, extra


def safe_name(doc_id):
    return doc_id.replace("/", "_").replace(" ", "_")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--program", choices=schema.PROGRAMS)
    ap.add_argument("--clean", action="store_true",
                    help="delete JSON files with no JSONL source")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.isdir(AUTHORED):
        print(f"no authored directory at {AUTHORED}")
        return 0

    pattern = f"{args.program}_*.jsonl" if args.program else "*.jsonl"
    files = sorted(glob.glob(os.path.join(AUTHORED, pattern)))
    if not files:
        print(f"no JSONL files matching {pattern} in {AUTHORED}")
        return 0

    documents = {}   # doc_id -> {letter: (doc, source)}
    errors, warnings = [], []
    lines_read = 0

    for path in files:
        rel = os.path.relpath(path, ROOT)
        with open(path, encoding="utf-8") as fh:
            for lineno, line in enumerate(fh, 1):
                line = line.strip()
                if not line or line.startswith("//"):
                    continue
                lines_read += 1
                where = f"{rel}:{lineno}"
                try:
                    raw = json.loads(line)
                except json.JSONDecodeError as exc:
                    errors.append(f"{where}: invalid JSON — {exc}")
                    continue

                doc_id = raw.get("doc_id")
                doc_type = raw.get("doc_type")
                program = raw.get("program")
                if not doc_id or doc_type not in schema.DOC_TYPES:
                    errors.append(f"{where}: missing doc_id or unknown doc_type {doc_type!r}")
                    continue
                if program not in schema.PROGRAMS:
                    errors.append(f"{where}: unknown program {program!r} for {doc_id}")
                    continue
                if not schema.ID_PATTERNS[doc_type].match(doc_id):
                    errors.append(f"{where}: {doc_id} does not match the {doc_type} ID pattern")
                    continue
                encoded = schema.program_of_doc_id(doc_id, doc_type)
                if encoded and encoded != program:
                    errors.append(
                        f"{where}: {doc_id} encodes program {encoded} but declares {program}")
                    continue

                doc, extra = merge_defaults(raw)
                if extra:
                    warnings.append(f"{where}: {doc_id} has unknown top-level keys {extra}")

                if doc["issue"] is None:
                    doc["issue"] = schema.default_issue()
                letter = doc["issue"].get("letter")
                if letter not in schema.ISSUE_LETTERS:
                    errors.append(f"{where}: {doc_id} has issue letter {letter!r}")
                    continue

                issues = documents.setdefault(doc_id, {})
                if letter in issues:
                    warnings.append(
                        f"{where}: duplicate {doc_id} issue {letter} — overriding "
                        f"{issues[letter][1]}")
                issues[letter] = (doc, where)

    written, unchanged = 0, 0
    expected_paths = set()
    for doc_id, issues in sorted(documents.items()):
        ordered = [issues[letter][0]
                   for letter in sorted(issues, key=schema.ISSUE_LETTERS.index)]
        latest = ordered[-1]
        out_dir = os.path.join(DOCS, latest["program"], latest["doc_type"])
        out_path = os.path.join(out_dir, safe_name(doc_id) + ".json")
        expected_paths.add(os.path.abspath(out_path))
        payload = json.dumps(ordered, indent=2, ensure_ascii=False) + "\n"
        if os.path.exists(out_path):
            with open(out_path, encoding="utf-8") as fh:
                if fh.read() == payload:
                    unchanged += 1
                    continue
        if not args.dry_run:
            os.makedirs(out_dir, exist_ok=True)
            with open(out_path, "w", encoding="utf-8") as fh:
                fh.write(payload)
        written += 1

    removed = 0
    if args.clean:
        scope = os.path.join(DOCS, args.program) if args.program else DOCS
        for path in glob.glob(os.path.join(scope, "**", "*.json"), recursive=True):
            if os.path.abspath(path) not in expected_paths:
                if not args.dry_run:
                    os.remove(path)
                removed += 1

    print(f"read      {lines_read} JSONL line(s) from {len(files)} file(s)")
    total_issues = sum(len(v) for v in documents.values())
    print(f"documents {len(documents)}  issues {total_issues}  "
          f"written {written}  unchanged {unchanged}"
          + (f"  removed {removed}" if args.clean else ""))
    for w in warnings:
        print(f"  warning: {w}")
    for e in errors:
        print(f"  ERROR:   {e}")
    if errors:
        print(f"\n{len(errors)} error(s) — those lines were not emitted")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
