#!/usr/bin/env python3
"""The issue history the completed pre-analyses imply, computed from the dataset.

A pre-analysis work order that closed VALIDATED means the modification went ahead, so the
documentation impacts it found were acted on. Each such finding is one **issue** of the target
document: the document as it stood when the finding was raised, and the document after the
finding's resolution was applied. Issues live in the document's own file, which is an array
ordered oldest first; the last element is the document as it stands.

This script derives that plan and nothing else. It is the worklist the authoring agents work
from and the manifest the result is checked against, so it must be deterministic and must not
invent anything: every issue it lists is traceable to a finding in a closed work order.

    python3 tools/issue_plan.py                 # summary for every program
    python3 tools/issue_plan.py --program M     # the worklist for one program
    python3 tools/issue_plan.py --program M --json > plan-P.json
    python3 tools/issue_plan.py --check         # compare the plan against what is on disk
"""

import argparse
import json
import pathlib
import sys
from collections import defaultdict

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOCUMENTS = ROOT / "data" / "documents"

#: Only this outcome produced documentation rework. A rejected pre-analysis means the
#: modification did not proceed; a delegated one was handed to another team and its result is
#: not recorded here. Both leave their targets at their original issue.
ACTED_ON = "VALIDATED"

#: Issue letters, in order. No document in the dataset has anything like this many.
LETTERS = "ABCDEFGH"

#: How a type spells the revision of its Nth issue. Every type but the service bulletin uses
#: the issue letter itself; a bulletin is numbered, and 01 is its first issue.
NUMERIC_REVISION_TYPES = {"SB"}


def revision_for(doc_type, index):
    """The revision token for the issue at 1-based ``index``."""
    if doc_type in NUMERIC_REVISION_TYPES:
        return f"{index:02d}"
    return LETTERS[index - 1]


def issue_letter_for(index):
    return LETTERS[index - 1]


def load(path):
    """Every issue in a document file, oldest first."""
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def latest(path):
    """The document as it stands: the last issue in its file."""
    return load(path)[-1]


def load_documents():
    documents = {}
    for path in sorted(DOCUMENTS.glob("*/*/*.json")):
        documents[latest(path)["doc_id"]] = path
    return documents


def build_plan():
    """One entry per target document, with its issues in order."""
    documents = load_documents()

    # Findings first, grouped by the document they hit. Ordered by the work order's completion
    # date so the issues of a document come out in the order they would have been raised.
    findings = defaultdict(list)
    for path in sorted(DOCUMENTS.glob("*/PAWO/*.json")):
        pawo = latest(path)
        data = pawo["type_data"]
        if data.get("pawo_status") != "COMPLETED" or data.get("outcome") != ACTED_ON:
            continue

        for finding in data.get("expected_findings", []):
            findings[finding["doc_id"]].append(
                {
                    "pawo": pawo["doc_id"],
                    "pawo_raised": data.get("raised_date"),
                    "pawo_completed": data.get("completed_date"),
                    "subject_mod": data.get("subject_mod"),
                    "finding_id": finding["finding_id"],
                    "finding_type": finding["finding_type"],
                    "severity": finding["severity"],
                    "rationale": finding.get("rationale"),
                    "resolution": finding.get("resolution"),
                }
            )

    plan = []
    for doc_id in sorted(findings):
        path = documents.get(doc_id)
        if path is None:
            print(f"  finding target not found: {doc_id}", file=sys.stderr)
            continue

        document = latest(path)
        doc_type = document["doc_type"]
        against = sorted(findings[doc_id], key=lambda f: (f["pawo_completed"] or "", f["pawo"]))

        issues = [
            {
                "letter": issue_letter_for(1),
                "revision": revision_for(doc_type, 1),
                "supersedes": None,
                "raised_by": None,
                "role": "original",
            }
        ]
        for index, finding in enumerate(against, start=2):
            issues.append(
                {
                    "letter": issue_letter_for(index),
                    "revision": revision_for(doc_type, index),
                    "supersedes": issue_letter_for(index - 1),
                    "raised_by": finding,
                    "role": "rework",
                }
            )

        plan.append(
            {
                "doc_id": doc_id,
                "doc_type": doc_type,
                "program": document["program"],
                "path": str(path.relative_to(ROOT)),
                "current_revision": document.get("revision"),
                "current_dates": document.get("dates"),
                "issues": issues,
            }
        )

    return plan


def summarise(plan):
    by_program = defaultdict(lambda: defaultdict(int))
    for entry in plan:
        row = by_program[entry["program"]]
        row["documents"] += 1
        row["issues"] += len(entry["issues"])
        row["rework"] += len(entry["issues"]) - 1

    print(f"{'program':>8}  {'documents':>9}  {'issues':>7}  {'rework':>7}")
    total = defaultdict(int)
    for program in sorted(by_program):
        row = by_program[program]
        print(f"{program:>8}  {row['documents']:>9}  {row['issues']:>7}  {row['rework']:>7}")
        for key, value in row.items():
            total[key] += value
    print(f"{'TOTAL':>8}  {total['documents']:>9}  {total['issues']:>7}  {total['rework']:>7}")


def show(plan, program):
    entries = [entry for entry in plan if entry["program"] == program]
    print(f"{program}: {len(entries)} documents, "
          f"{sum(len(entry['issues']) for entry in entries)} issues\n")

    by_type = defaultdict(list)
    for entry in entries:
        by_type[entry["doc_type"]].append(entry)

    for doc_type in sorted(by_type):
        print(f"--- {doc_type} ({len(by_type[doc_type])} documents)")
        for entry in by_type[doc_type]:
            letters = "".join(issue["letter"] for issue in entry["issues"])
            print(f"  {entry['doc_id']:<34} rev {entry['current_revision'] or '-':<3} "
                  f"-> issues {letters}")
            for issue in entry["issues"][1:]:
                raised = issue["raised_by"]
                print(f"      {issue['letter']}  {raised['pawo']} {raised['finding_id']} "
                      f"{raised['finding_type']} ({raised['severity']}) "
                      f"completed {raised['pawo_completed']}")
        print()


def check(plan):
    """Compare the plan against the issue arrays on disk.

    The plan says which issues each target document must have and why. This reports what is
    missing, what is there that the plan does not account for, and what disagrees — it is the
    gate the authoring is accepted against.
    """
    missing, extra, wrong = [], [], []

    for entry in plan:
        written = load(ROOT / entry["path"])
        if not isinstance(written, list):
            wrong.append(f"{entry['doc_id']}: not an array of issues")
            continue

        by_letter = {(issue.get("issue") or {}).get("letter"): issue for issue in written}

        for planned in entry["issues"]:
            letter = planned["letter"]
            issue = by_letter.get(letter)
            if issue is None:
                missing.append(f"{entry['doc_id']} issue {letter}")
                continue

            if issue.get("revision") not in _revision_tokens(entry["doc_type"], letter):
                wrong.append(f"{entry['doc_id']} issue {letter}: "
                             f"revision {issue.get('revision')!r}, expected {planned['revision']!r}")

            raised = planned["raised_by"]
            block = issue.get("issue") or {}
            if raised is None:
                if block.get("raised_by_pawo"):
                    wrong.append(f"{entry['doc_id']} issue {letter}: "
                                 "the original issue names a finding")
            elif (block.get("raised_by_pawo"), block.get("raised_by_finding")) != \
                    (raised["pawo"], raised["finding_id"]):
                wrong.append(f"{entry['doc_id']} issue {letter}: raised by "
                             f"{block.get('raised_by_pawo')} {block.get('raised_by_finding')}, "
                             f"planned {raised['pawo']} {raised['finding_id']}")

        planned_letters = {issue["letter"] for issue in entry["issues"]}
        for letter in by_letter:
            if letter not in planned_letters:
                extra.append(f"{entry['doc_id']} issue {letter}")

        # Order matters: the last element is the document as it stands.
        order = [(issue.get("issue") or {}).get("letter") for issue in written]
        if order != sorted(order):
            wrong.append(f"{entry['doc_id']}: issues are not in order — {order}")

    for label, items in (("missing", missing), ("unplanned", extra), ("wrong", wrong)):
        if items:
            print(f"{len(items)} {label}:")
            for item in items[:20]:
                print(f"  {item}")
            if len(items) > 20:
                print(f"  … and {len(items) - 20} more")

    ok = not (missing or extra or wrong)
    print("issue plan OK" if ok else "issue plan NOT satisfied")
    return 0 if ok else 1


def _revision_tokens(doc_type, letter):
    """Accepted revision spellings for an issue, matching tools/schema.revision_tokens."""
    index = LETTERS.index(letter) + 1
    canonical = revision_for(doc_type, index)
    return {canonical, "-"} if index == 1 else {canonical}


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--program", help="show the worklist for one program")
    parser.add_argument("--json", action="store_true", help="emit the plan as JSON")
    parser.add_argument("--check", action="store_true", help="compare the plan against disk")
    args = parser.parse_args()

    plan = build_plan()

    if args.json:
        selected = [e for e in plan if not args.program or e["program"] == args.program]
        json.dump(selected, sys.stdout, indent=2, ensure_ascii=False)
        print()
        return 0

    if args.check:
        return check(plan)

    if args.program:
        show(plan, args.program)
        return 0

    summarise(plan)
    return 0


if __name__ == "__main__":
    sys.exit(main())
