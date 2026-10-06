#!/usr/bin/env python3
"""Date each manual's original issue from the aircraft it documents, not from the window.

A maintenance task, a parts figure, a repair, a fault-isolation procedure, a wiring sheet
and a crew entry are all first issued around the time their aircraft enters service, and
revised for decades afterwards. The dataset's date window opens in 2016, so every original
issue was written inside it — which had an ZEPHYR-22 AMM task claiming to have been created in
2016, twenty-six years after the ZEPHYR-22 entered service, and put the archived paper copy of
its superseded issue in the same decade as the current one.

This dates the **original** issue of every long-lived document from its type's entry into
service (`schema.entry_into_service`, taken from the first delivery in `msn_registry.csv`),
and carries `dates.created` / `dates.released` — which describe that original publication —
onto every later issue of the same file, where they already sat unchanged.

What it does not touch:

* **Any later issue's own dates.** A rework is dated by the pre-analysis that caused it, and
  those are current. On a later issue only `created` / `released` move, and they were never
  that issue's own. On the original, `revised` and `effective` move with it — `revised`
  equals the issue's own date throughout this dataset, and on an original it says the
  document has been revised no further.
* **Documents the dataset holds a single issue of.** Nothing in the file claims to be the
  original — the history simply is not modelled — so there is nothing to re-date.
* **MOD, SB, IO and PAWO.** A change artefact is dated to its change, and every change here
  is current. See `schema.LONG_LIVED_DOC_TYPES`.

Deterministic and idempotent: every date is derived from the document's identifier, its
type's entry into service, and the date of the issue that superseded it — never from the
value already on disk — so re-running produces byte-identical files.

    python3 tools/backdate_original_issues.py --dry-run
    python3 tools/backdate_original_issues.py
"""

import argparse
import csv
import datetime
import hashlib
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import schema

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOCUMENTS = ROOT / "data" / "documents"
REFERENCE = ROOT / "data" / "reference"

#: How long after its type entered service an original issue may be dated. A manual is
#: published with the aircraft and reissued over the years that follow; three years covers
#: the difference between a type's first delivery and a task written for a later variant.
MAX_YEARS_AFTER_EIS = 3

#: The gap between an original issue being drafted and being released.
MIN_DRAFTING_DAYS, MAX_DRAFTING_DAYS = 45, 300

#: An original issue must be superseded by a clear margin, or the history reads as two
#: issues in the same month.
MIN_MARGIN_BEFORE_NEXT_DAYS = 90

#: Where the effective date sits after release, when the file does not already say.
DEFAULT_EFFECTIVE_GAP_DAYS = 21


def stable(doc_id, salt, span):
    """A value in [0, span) drawn from the document's identifier. Stable everywhere."""
    digest = hashlib.blake2b(f"{doc_id}\0{salt}".encode("utf-8"), digest_size=8).digest()
    return int.from_bytes(digest, "big") % span if span > 0 else 0


def iso(date):
    return date.isoformat()


def parse(value):
    return datetime.date.fromisoformat(value) if value else None


def plan_dates(doc_id, eis, next_issue_date, effective_gap):
    """When the original issue was drafted, released and became effective.

    Returns None when the aircraft's entry into service leaves no room before the issue
    that superseded it — an CIRRANE document whose second issue is from 2017 is already as
    close to its type's entry into service as it can get.
    """
    latest_allowed = next_issue_date - datetime.timedelta(days=MIN_MARGIN_BEFORE_NEXT_DAYS)
    if eis >= latest_allowed:
        return None

    span = min((latest_allowed - eis).days, MAX_YEARS_AFTER_EIS * 365)
    released = eis + datetime.timedelta(days=stable(doc_id, "released", span + 1))

    drafting = MIN_DRAFTING_DAYS + stable(doc_id, "drafting",
                                          MAX_DRAFTING_DAYS - MIN_DRAFTING_DAYS + 1)
    created = released - datetime.timedelta(days=drafting)
    effective = released + datetime.timedelta(days=effective_gap)

    return created, released, effective


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="report without writing")
    ap.add_argument("--limit", type=int, default=12, help="example lines to print")
    args = ap.parse_args()

    with (REFERENCE / "msn_registry.csv").open(encoding="utf-8") as handle:
        msns = list(csv.DictReader(handle))

    moved, skipped, unchanged, examples = 0, 0, 0, []

    for path in sorted(DOCUMENTS.glob("*/*/*.json")):
        with path.open(encoding="utf-8") as handle:
            issues = json.load(handle)

        if len(issues) < 2:
            continue

        original = issues[0]
        if original.get("doc_type") not in schema.LONG_LIVED_DOC_TYPES:
            continue

        applicability = original.get("applicability") or {}
        eis = parse(schema.entry_into_service(msns, applicability.get("aircraft_types"),
                                              original.get("program")))
        next_issue_date = parse((issues[1].get("issue") or {}).get("date"))
        if eis is None or next_issue_date is None:
            skipped += 1
            continue

        dates = original.get("dates") or {}
        released_now, effective_now = parse(dates.get("released")), parse(dates.get("effective"))
        gap = ((effective_now - released_now).days
               if released_now and effective_now else DEFAULT_EFFECTIVE_GAP_DAYS)

        planned = plan_dates(original.get("doc_id"), eis, next_issue_date, gap)
        if planned is None:
            skipped += 1
            continue

        created, released, effective = planned

        before = json.dumps(issues, indent=2, ensure_ascii=False)
        # `dates` is the live dict about to be rewritten, so the old value is kept now.
        was = dates.get("released")

        # `created` and `released` describe the original publication, so every issue of the
        # file carries the same pair — they are already identical across issues today.
        for issue in issues:
            issue.setdefault("dates", {})
            issue["dates"]["created"] = iso(created)
            issue["dates"]["released"] = iso(released)

        original["issue"]["date"] = iso(released)

        # Every issue in this dataset carries `dates.revised` equal to its own issue date —
        # the original included, where it says the document has been revised no further. It
        # has to move with the issue it belongs to, or the file reads as released after it
        # was last revised.
        if original["dates"].get("revised"):
            original["dates"]["revised"] = iso(released)
        if effective_now:
            original["dates"]["effective"] = iso(effective)

        payload = json.dumps(issues, indent=2, ensure_ascii=False) + "\n"
        if payload == before + "\n":
            unchanged += 1
            continue

        moved += 1
        if len(examples) < args.limit:
            examples.append(f"  {original['doc_id']:<34} {was} → "
                            f"{iso(released)}  (EIS {iso(eis)})")

        if not args.dry_run:
            path.write_text(payload, encoding="utf-8")

    for line in examples:
        print(line)
    if moved > len(examples):
        print(f"  … {moved - len(examples)} more")

    print()
    print(f"{moved} original issue(s) re-dated, {unchanged} already correct, "
          f"{skipped} left alone (no room before the issue that superseded them)"
          + ("  [dry run]" if args.dry_run else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
