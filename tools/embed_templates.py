"""Splices the report templates into the news-desk sandbox tool.

The templates are authored and previewed as ordinary files under
src/AviationIntel.Definitions/templates/; the sandbox serves them from string constants, because
an AI tool is one C# file in the workspace. This regenerates the region between the
`// <templates>` and `// </templates>` markers. Run it after editing a template:

    python3 tools/embed_templates.py
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMPLATES = os.path.join(ROOT, "src", "AviationIntel.Definitions", "templates")
TOOL = os.path.join(ROOT, "src", "AviationIntel.Definitions", "code", "chat-ai-tools", "AVsandboX1111111111111.cs")

CONSTANTS = [
    ("TemplatesReadme", "README.md"),
    ("ReportTemplate", "report.html"),
    ("DeckTemplate", "deck.html"),
    ("SampleDaily", "sample-daily.json"),
]


def raw_literal(text):
    # A C# raw string literal: the delimiter must be longer than any run of quotes inside.
    longest = max((len(m) for m in re.findall(r'"+', text)), default=0)
    quotes = '"' * max(3, longest + 1)
    return quotes + "\n" + text.rstrip("\n") + "\n" + quotes


def main():
    with open(TOOL, encoding="utf-8") as f:
        code = f.read()

    start = code.index("    // <templates>")
    end = code.index("    // </templates>")

    parts = ["    // <templates>\n"]
    for name, file in CONSTANTS:
        with open(os.path.join(TEMPLATES, file), encoding="utf-8") as f:
            text = f.read()
        parts.append(f"    private const string {name} = {raw_literal(text)};\n\n")

    code = code[:start] + "".join(parts) + code[end:]

    with open(TOOL, "w", encoding="utf-8", newline="\n") as f:
        f.write(code)

    print(f"embedded {len(CONSTANTS)} templates into {os.path.relpath(TOOL, ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
