#!/usr/bin/env python3
"""Turn the Aeralis marks into the resource form the page renderer wants.

The marks in ``src/Aeralis.Illustrations/Logos/`` are exported as a filled plate with the
mark punched out of it: the first group covers the whole canvas in near-white and every
later group carries the artwork itself. A page wants the mark on its own, so this drops
the plate, crops the viewBox to what is left, and converts the ink to greys — these sit on
hairline line drawings, where the mark's own palette would be the only colour on the page.
Greys rather than one flat black because both marks are built from stacked layers, and
flattening them loses the construction entirely.

    python3 tools/prepare_logos.py

Writes ``logo-{era}.svg`` into ``src/Aeralis.Illustrations/Resources/``, where they are
embedded resources of ``Aeralis.Illustrations``.
"""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "src" / "Aeralis.Illustrations" / "Logos"
TARGET = ROOT / "src" / "Aeralis.Illustrations" / "Resources"

SVG = "{http://www.w3.org/2000/svg}"
#: The darkest and lightest ink a converted mark uses. A mark whose layers are all forced to
#: one value loses its construction — the heritage mark in particular collapses into a blob —
#: so each layer is mapped to a grey of its own luminance instead, inside this range.
DARKEST, LIGHTEST = 0.06, 0.78

#: era -> source file. Aeralis has used two marks: the one adopted with the Zephyr in the
#: late eighties, and the one it moved to when the Meridian entered service.
MARKS = {"heritage": "aeralis-heritage.svg", "modern": "aeralis-modern.svg"}

#: How much of the mark's own size to leave as breathing room around the crop.
PADDING = 0.02

NUMBER = re.compile(r"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?")
COMMAND = re.compile(r"([MmLlHhVvCcSsQqTtAaZz])")


def path_bounds(d):
    """A path's bounding box, control points included — generous, which is what a crop wants."""
    tokens = COMMAND.split(d)
    x = y = start_x = start_y = 0.0
    xs, ys = [], []
    i = 1
    while i < len(tokens):
        command = tokens[i]
        args = [float(v) for v in NUMBER.findall(tokens[i + 1])]
        i += 2
        relative, kind, k = command.islower(), command.upper(), 0
        while True:
            if kind == "M":
                dx, dy = args[k:k + 2]; k += 2
                x, y = (x + dx, y + dy) if relative else (dx, dy)
                start_x, start_y, kind = x, y, "L"
            elif kind == "L":
                dx, dy = args[k:k + 2]; k += 2
                x, y = (x + dx, y + dy) if relative else (dx, dy)
            elif kind == "H":
                dx = args[k]; k += 1
                x = x + dx if relative else dx
            elif kind == "V":
                dy = args[k]; k += 1
                y = y + dy if relative else dy
            elif kind in ("C", "S", "Q"):
                n = 6 if kind == "C" else 4
                values = args[k:k + n]; k += n
                points = [(x + values[j], y + values[j + 1]) if relative else (values[j], values[j + 1])
                          for j in range(0, n, 2)]
                for px, py in points:
                    xs.append(px); ys.append(py)
                x, y = points[-1]
            elif kind == "T":
                dx, dy = args[k:k + 2]; k += 2
                x, y = (x + dx, y + dy) if relative else (dx, dy)
            elif kind == "A":
                values = args[k:k + 7]; k += 7
                x, y = (x + values[5], y + values[6]) if relative else (values[5], values[6])
            elif kind == "Z":
                x, y = start_x, start_y
                break
            xs.append(x); ys.append(y)
            if k >= len(args):
                break
    return min(xs), min(ys), max(xs), max(ys)


RGB = re.compile(r"rgb\((\d+),\s*(\d+),\s*(\d+)\)")


def luminance(fill):
    """Perceived lightness of an ``rgb(r, g, b)`` fill, 0 (black) to 1 (white)."""
    match = RGB.match(fill or "")
    if not match:
        return 0.0
    r, g, b = (int(v) / 255 for v in match.groups())
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def extract(path):
    """Every layer of the mark itself — that is, every group after the background plate.

    Returns ``(paths, luminance)`` per layer, in paint order.
    """
    root = ET.parse(path).getroot()
    groups = root.findall(SVG + "g")
    if len(groups) < 2:
        raise SystemExit(f"{path.name}: expected a background plate and at least one ink group")
    return [([p.get("d") for p in group.iter(SVG + "path")], luminance(group.get("fill")))
            for group in groups[1:]]


def main():
    TARGET.mkdir(parents=True, exist_ok=True)
    for era, filename in MARKS.items():
        layers = extract(SOURCE / filename)
        paths = [d for group, _ in layers for d in group]
        boxes = [path_bounds(d) for d in paths]
        x0 = min(b[0] for b in boxes); y0 = min(b[1] for b in boxes)
        x1 = max(b[2] for b in boxes); y1 = max(b[3] for b in boxes)
        pad = max(x1 - x0, y1 - y0) * PADDING
        x0, y0, x1, y1 = x0 - pad, y0 - pad, x1 + pad, y1 + pad

        # Layers keep their own relative lightness, remapped into a range that stays legible
        # on white paper. Paint order is the source's, so a highlight still lands on top.
        lows = [lum for _, lum in layers]
        span = max(1e-6, max(lows) - min(lows))
        body = ""
        for group, lum in layers:
            level = DARKEST + (LIGHTEST - DARKEST) * ((lum - min(lows)) / span)
            shade = round(level * 255)
            paths_svg = "".join(f'<path d="{d}"/>' for d in group)
            body += f'<g fill="rgb({shade},{shade},{shade})" stroke="none">{paths_svg}</g>'

        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" '
               f'viewBox="{x0:.0f} {y0:.0f} {x1 - x0:.0f} {y1 - y0:.0f}">{body}</svg>')

        out = TARGET / f"logo-{era}.svg"
        out.write_text(svg, encoding="utf-8")
        print(f"{out.relative_to(ROOT)}  {len(layers)} layers, {len(paths)} paths  "
              f"viewBox {x0:.0f} {y0:.0f} {x1 - x0:.0f} {y1 - y0:.0f}")


if __name__ == "__main__":
    main()
