#!/usr/bin/env python3
"""Fills a report template with a report JSON, the way the sandbox's `render-report` does, so a
template or a sample can be looked at in a browser without a workspace.

    python3 tools/preview_report.py src/AviationIntel.Definitions/templates/sample-weekly.json
    python3 tools/preview_report.py <report.json> --deck --out /tmp/deck.html

The deck page loads PptxGenJS from its CDN tag, so it needs a network to build the .pptx; the
report page needs nothing.
"""
import argparse
import html
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMPLATES = os.path.join(ROOT, "src", "AviationIntel.Definitions", "templates")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("data")
    p.add_argument("--deck", action="store_true", help="render deck.html instead of report.html")
    p.add_argument("--out", help="where to write the page (default: next to the JSON)")
    args = p.parse_args()

    with open(args.data, encoding="utf-8") as f:
        root = json.load(f)

    template_name = "deck.html" if args.deck else "report.html"
    with open(os.path.join(TEMPLATES, template_name), encoding="utf-8") as f:
        template = f.read()

    # Same as RenderReportAsync: compact JSON with "</" escaped, and the report's title in <title>.
    data = json.dumps(root, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    title = html.escape(root["title"] + (" (deck)" if args.deck else ""))
    page = re.sub(r"<title>[^<]*</title>", lambda _: f"<title>{title}</title>", template).replace("/*REPORT_DATA*/", data)

    out = args.out or os.path.splitext(args.data)[0] + ("-deck" if args.deck else "") + ".preview.html"
    with open(out, "w", encoding="utf-8") as f:
        f.write(page)

    print(f"wrote {out}")


if __name__ == "__main__":
    main()
