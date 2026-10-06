#!/usr/bin/env python3
"""Draw the Aeralis general-arrangement three-views, and the dimension table beside them.

Every aircraft in the fleet is a parametric airliner: a lofted fuselage, a swept wing, a
tail and a set of nacelles, sized from the type's overall length, span and height and from
a handful of proportions that follow from its class. The three views are the three
projections of that one body, drawn as hairline outlines the way a published
general-arrangement sheet is.

The orientations are the ones ``SectionMap`` reads:

    plan    nose at the bottom, tail at the top, span across the width
    side    nose to the left, fin at the top
    front   fin at the top, gear at the bottom, span across the width

Output is one gzipped SVG per type and view in
``src/Aeralis.Illustrations/Resources``, plus ``dimensions.csv`` beside them. Both are
embedded resources of ``Aeralis.Illustrations``.

    python3 tools/gen_aircraft_views.py

Deterministic: the same fleet table produces byte-identical drawings every run.
"""
from __future__ import annotations

import gzip
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TARGET = ROOT / "src" / "Aeralis.Illustrations" / "Resources"

INK = "#111111"
STROKE = 0.7

#: The drawing is normalised so its longest horizontal extent is this many user units.
WIDTH = 1000.0


# ---------------------------------------------------------------------------------------
# the fleet
# ---------------------------------------------------------------------------------------
#
# key: (length, span, height) in millimetres, engine count, whether it carries winglets,
# whether it is a full double-decker, and how slender the fuselage is (diameter as a
# fraction of the height).

FLEET: dict[str, tuple[tuple[int, int, int], int, bool, bool, float]] = {
    "AUSTER-20":         ((54_180, 44_260, 16_240), 2, False, False, 0.345),
    "AUSTER-24":         ((46_940, 43_120, 15_180), 2, False, False, 0.345),
    "CIRRANE-11":        ((35_240, 33_860, 11_620), 2, True,  False, 0.310),
    "CIRRANE-13":        ((39_180, 33_860, 11_620), 2, True,  False, 0.310),
    "ZEPHYR-18":         ((32_010, 33_140, 12_180), 2, False, False, 0.325),
    "ZEPHYR-20":         ((34_460, 33_140, 11_640), 2, False, False, 0.340),
    "ZEPHYR-20-winglet": ((34_580, 34_720, 11_640), 2, True,  False, 0.340),
    "ZEPHYR-22":         ((38_120, 33_140, 11_640), 2, False, False, 0.340),
    "ZEPHYR-22-winglet": ((38_240, 34_720, 11_640), 2, True,  False, 0.340),
    "ZEPHYR-24":         ((45_060, 33_140, 11_640), 2, False, False, 0.340),
    "ZEPHYR-24-winglet": ((45_180, 34_720, 11_640), 2, True,  False, 0.340),
    "SOLANO-30":         ((59_470, 59_180, 17_060), 2, False, False, 0.330),
    "SOLANO-30E":        ((59_470, 59_180, 17_560), 2, False, False, 0.325),
    "SOLANO-30F":        ((59_510, 59_240, 17_080), 2, False, False, 0.330),
    "SOLANO-34":         ((64_320, 59_180, 16_540), 2, False, False, 0.340),
    "SOLANO-38":         ((59_440, 62_880, 17_140), 2, True,  False, 0.328),
    "SOLANO-39":         ((64_350, 62_880, 16_680), 2, True,  False, 0.338),
    "VANTOR-62":         ((60_120, 59_180, 16_540), 4, False, False, 0.338),
    "VANTOR-64":         ((64_310, 59_180, 16_540), 4, False, False, 0.338),
    "VANTOR-66":         ((68_740, 62_410, 17_090), 4, False, False, 0.330),
    "VANTOR-68":         ((76_140, 62_410, 17_090), 4, False, False, 0.330),
    "MERIDIAN-42":       ((67_580, 63_620, 18_240), 2, True,  False, 0.328),
    "MERIDIAN-46":       ((74_690, 63_620, 17_680), 2, True,  False, 0.338),
    "TITAN-88":          ((73_460, 78_320, 24_480), 4, True,  True,  0.335),
}

PLAN_ONLY = {"CIRRANE-11", "CIRRANE-13"}


# ---------------------------------------------------------------------------------------
# the parametric aeroplane
# ---------------------------------------------------------------------------------------

class Aircraft:
    """Every measurement a view needs, in metres, derived once from the overall figures."""

    def __init__(self, key):
        (length, span, height), engines, winglets, double_deck, slenderness = FLEET[key]
        self.key = key
        self.L = length / 1000.0
        self.B = span / 1000.0
        self.H = height / 1000.0
        self.engines = engines
        self.winglets = winglets
        self.double_deck = double_deck

        #: Fuselage diameter. A double-decker is an upright oval, not a circle, so it is
        #: given its width here and its extra depth where the side view is drawn.
        self.D = self.H * slenderness
        self.deck_depth = self.D * (1.42 if double_deck else 1.0)

        #: Ground clearance under the fuselage, and where that puts the body in the side
        #: and front views. Measured downwards from the top of the fin, which is v = 0.
        self.gear = self.D * 0.62
        self.body_bottom = self.H - self.gear
        self.body_top = self.body_bottom - self.deck_depth
        self.centre = (self.body_top + self.body_bottom) / 2

        #: Longitudinal stations, as fractions of the length. The tail cone starts well aft
        #: of the wing and closes just short of the tail, leaving the fin something to sit on.
        self.nose_end = 0.105
        self.tailcone_start = 0.700
        self.tail_tip = 0.965

        #: The nose closes on a point a little below the section's own centre — a radome is
        #: set low on the body, not centred on it.
        self.nose_tip_y = self.centre + self.deck_depth * 0.09

        #: How far the crown and the belly have risen by the tail tip, as fractions of the
        #: depth. Their difference is what the tail cone closes on: a blunt end carrying the
        #: APU exhaust, not a point.
        self.tail_crown_rise = 0.30
        self.tail_belly_rise = 0.78

        #: Wing. The root chord runs from ``wing_le`` to ``wing_te``; the tip is swept aft —
        #: more on the leading edge than the trailing one, which is what gives an airliner
        #: wing its taper — and its chord is ``taper`` of the root's.
        self.wing_le = 0.360
        self.wing_te = 0.550
        self.sweep = 0.200 if span > 50 else 0.165
        self.sweep_te = self.sweep * 0.58
        self.taper = 0.32
        self.dihedral = 0.050 * self.B / 2

        #: Tailplane, as a fraction of the span.
        self.tail_span = self.B * (0.355 if engines == 2 else 0.385)
        self.tail_le = 0.835
        self.tail_te = 0.938
        self.tail_sweep = 0.048

        #: Fin.
        self.fin_le = 0.700
        self.fin_te = 0.925
        self.fin_tip_le = 0.815
        self.fin_tip_te = 0.945

        #: Nacelles.
        self.nac_d = self.D * (0.50 if engines == 2 else 0.40)
        self.nac_len = self.L * (0.105 if engines == 2 else 0.085)
        self.nac_stations = ([0.315] if engines == 2 else [0.290, 0.500])

        #: Landing gear.
        self.nose_gear = 0.145
        self.main_gear = 0.505
        self.track = self.B * (0.135 if engines == 2 else 0.155)

    # -- helpers -------------------------------------------------------------------------

    def half_width_at(self, s):
        """Half the fuselage width at station ``s`` (a fraction of the length), in metres."""
        r = self.D / 2
        if s <= 0:
            return r * 0.11
        if s < self.nose_end:
            t = s / self.nose_end
            # Rounded, not pointed: a radome closes on a small flat, not on a knife edge.
            return r * (0.11 + 0.89 * (1 - (1 - t) ** 2.2) ** 0.55)
        if s <= self.tailcone_start:
            return r
        if s >= self.tail_tip:
            return r * 0.085
        t = (s - self.tailcone_start) / (self.tail_tip - self.tailcone_start)
        return r * (0.085 + 0.915 * (1 - t ** 1.9))


    def crown(self, s):
        """The top of the fuselage at station ``s``, in the side view's own coordinates.

        Every part that sits on the fuselage reads its own root from here — the fin, the
        tailplane, the window line — so nothing floats above a body that has started to
        taper away underneath it.
        """
        if s <= 0:
            return self.nose_tip_y
        if s < self.nose_end:
            t = s / self.nose_end
            k = (1 - (1 - t) ** 2.0) ** 0.55
            return self.nose_tip_y - (self.nose_tip_y - self.body_top) * k
        if s <= self.tailcone_start:
            return self.body_top
        t = min(1.0, (s - self.tailcone_start) / (self.tail_tip - self.tailcone_start))
        return self.body_top + self.deck_depth * self.tail_crown_rise * t ** 2.1

    def belly(self, s):
        """The bottom of the fuselage at station ``s``, in the side view's own coordinates."""
        if s <= 0:
            return self.nose_tip_y
        if s < self.nose_end:
            t = s / self.nose_end
            k = (1 - (1 - t) ** 2.9) ** 0.48
            return self.nose_tip_y + (self.body_bottom - self.nose_tip_y) * k
        if s <= self.tailcone_start:
            return self.body_bottom
        t = min(1.0, (s - self.tailcone_start) / (self.tail_tip - self.tailcone_start))
        return self.body_bottom - self.deck_depth * self.tail_belly_rise * t ** 1.45

    def engine_offsets(self):
        """Where the nacelle centres sit across the half-span, in metres from the centreline."""
        half = self.B / 2
        return [half * f for f in self.nac_stations]


# ---------------------------------------------------------------------------------------
# svg plumbing
# ---------------------------------------------------------------------------------------

class Canvas:
    """Collects paths in metres and writes them out scaled to the drawing's own box."""

    def __init__(self, width_m, height_m):
        self.width_m = width_m
        self.height_m = height_m
        self.scale = WIDTH / width_m
        self.paths: list[str] = []

    def poly(self, points, close=False):
        if len(points) < 2:
            return
        d = "M" + "L".join(f"{x * self.scale:.2f} {y * self.scale:.2f}" for x, y in points)
        self.paths.append(d + ("Z" if close else ""))

    def line(self, x1, y1, x2, y2):
        self.poly([(x1, y1), (x2, y2)])

    def ellipse(self, cx, cy, rx, ry, steps=64):
        pts = [(cx + rx * math.cos(2 * math.pi * i / steps),
                cy + ry * math.sin(2 * math.pi * i / steps)) for i in range(steps)]
        self.poly(pts, close=True)

    def mirror_x(self, axis):
        """Reflects everything drawn so far about a vertical line — one wing draws both."""
        for d in list(self.paths):
            self.paths.append(_mirror(d, axis * self.scale))

    def render(self):
        height = self.height_m * self.scale
        body = "".join(f'<path d="{d}"/>' for d in self.paths)
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH:.1f}" '
                f'height="{height:.1f}" viewBox="0 0 {WIDTH:.1f} {height:.1f}">'
                f'<rect width="100%" height="100%" fill="#ffffff"/>'
                f'<g fill="none" stroke="{INK}" stroke-width="{STROKE}" '
                f'stroke-linecap="round" stroke-linejoin="round">{body}</g></svg>')


def _mirror(d, axis):
    """Reflects a path's x coordinates about ``axis``. Paths here are polylines only."""
    out = []
    for chunk in d.replace("Z", "").lstrip("M").split("L"):
        x, y = chunk.split(" ")
        out.append(f"{2 * axis - float(x):.2f} {y}")
    return "M" + "L".join(out) + ("Z" if d.endswith("Z") else "")


# ---------------------------------------------------------------------------------------
# the three views
# ---------------------------------------------------------------------------------------

def draw_side(a: Aircraft):
    """Nose left, tail right, fin at the top, ground at the bottom."""
    c = Canvas(a.L, a.H)
    L, top, bottom = a.L, a.body_top, a.body_bottom

    # Fuselage: nose, crown, tail cone and belly as one closed outline. The tail cone does
    # not run to a point — the crown and the belly stop short of each other and the end is
    # closed square, which is the flat the APU exhausts through.
    steps = 120
    stations = [a.tail_tip * i / steps for i in range(steps + 1)]
    crown = [(s * L, a.crown(s)) for s in stations]
    belly = [(s * L, a.belly(s)) for s in stations]
    c.poly(crown + list(reversed(belly)), close=True)

    # APU exhaust, on the flat the tail cone closes on
    tail_x = a.tail_tip * L
    c.poly([(tail_x, a.crown(a.tail_tip) + a.deck_depth * 0.02),
            (L * 0.995, a.crown(a.tail_tip) + a.deck_depth * 0.05),
            (L * 0.995, a.belly(a.tail_tip) - a.deck_depth * 0.03),
            (tail_x, a.belly(a.tail_tip) - a.deck_depth * 0.02)], close=True)

    # Flight deck. The windscreen is a shallow wedge set into the crown, and the radome
    # joint is the ring the nose cone is hinged on.
    _flight_deck(c, a)

    # Cabin windows, at the pitch a cabin is actually laid out on, interrupted by the doors.
    window_y = a.centre - a.deck_depth * 0.15
    doors = _doors(a)
    _windows(c, a, window_y, doors)
    if a.double_deck:
        _windows(c, a, a.centre + a.deck_depth * 0.19, doors, first=0.16)

    # Passenger doors, drawn to a real door's size rather than to a fraction of the drawing.
    door_h, door_w = 1.90, 0.85
    for s in doors:
        x = s * L
        c.poly([(x, window_y - door_h * 0.42), (x + door_w, window_y - door_h * 0.42),
                (x + door_w, window_y + door_h * 0.58), (x, window_y + door_h * 0.58)],
               close=True)

    # wing, seen close to edge on — the tip is aft of the root and lifted by the dihedral,
    # so the projection reads as a shallow sliver rather than a line
    wing_y = bottom - a.deck_depth * 0.09
    c.poly([(a.wing_le * L, wing_y),
            (a.wing_te * L, wing_y),
            ((a.wing_te + a.sweep_te) * L, wing_y - a.dihedral * 0.5),
            ((a.wing_le + a.sweep) * L, wing_y - a.dihedral * 0.5)], close=True)

    # Fin. Its root is the crown itself, sampled between the two root stations, so the fin
    # closes on the fuselage instead of floating over a tail cone that has dropped away.
    root = [(s * L, a.crown(s)) for s in _span(a.fin_le, a.fin_te, 24)]
    c.poly(root + [(a.fin_tip_te * L, 0.0), (a.fin_tip_le * L, 0.0)], close=True)

    # Tailplane, on the side of the tail cone below the fin root — anchored on the crown so
    # it moves with the taper. Root chord first, then the tip chord swept aft and lifted by
    # its own dihedral; in that order the quad cannot cross itself.
    tp_y = a.crown((a.tail_le + a.tail_te) / 2) + a.deck_depth * 0.26
    tp_rise = a.H * 0.036
    c.poly([(a.tail_le * L, tp_y),
            ((a.tail_le + a.tail_sweep) * L, tp_y - tp_rise),
            ((a.tail_te + a.tail_sweep * 0.6) * L, tp_y - tp_rise),
            (a.tail_te * L, tp_y)], close=True)

    # nacelles, drawn once at their own station — the outboard pair sits slightly forward
    for i, s in enumerate(a.nac_stations):
        cx = (a.wing_le - 0.030 + (0.0 if i == 0 else -0.028)) * L
        cy = bottom + a.nac_d * 0.30 + i * a.nac_d * 0.12
        _nacelle(c, lambda u, v, x=cx, y=cy: (x - a.nac_len / 2 + u, y + v), a.nac_len, a.nac_d)
        # pylon, a closed wedge from the nacelle crown up to the wing leading edge
        c.poly([(cx + a.nac_len * 0.16, cy - a.nac_d * 0.46),
                (cx + a.nac_len * 0.50, cy - a.nac_d * 0.46),
                ((a.wing_le + 0.055) * L, wing_y),
                ((a.wing_le + 0.012) * L, wing_y)], close=True)

    # Wing-body fairing: the belly bulge that closes the wing root, which is most of what
    # the underside of an airliner reads as between the gear bays.
    fair_le, fair_te = a.wing_le - 0.075, a.wing_te + 0.115
    fair_depth = a.deck_depth * 0.22
    fairing = []
    for s in _span(fair_le, fair_te, 40):
        u = (s - fair_le) / (fair_te - fair_le)
        fairing.append((s * L, a.belly(s) + fair_depth * math.sin(math.pi * u) ** 0.75))
    c.poly([(fair_le * L, a.belly(fair_le))] + fairing + [(fair_te * L, a.belly(fair_te))])

    # landing gear
    _gear_leg(c, a.nose_gear * L, bottom, a.H, a.D * 0.11)
    _gear_leg(c, a.main_gear * L, bottom, a.H, a.D * 0.14, wheels=2)
    c.line(0, a.H, L, a.H)                                   # ground line
    return c


def _span(start, end, steps):
    """``steps`` + 1 stations from ``start`` to ``end`` inclusive."""
    return [start + (end - start) * i / steps for i in range(steps + 1)]


def _doors(a):
    """Where the passenger doors sit, as fractions of the length.

    Four on a single-aisle, and a fifth over the wing on the longest of them; a widebody
    gets its doors spread differently because the wing box takes up more of the middle.
    """
    if a.L > 55:
        return [0.100, 0.245, 0.610, 0.735]
    doors = [0.115, 0.255, 0.625, 0.760]
    if a.L > 42:
        doors.insert(2, 0.470)
    return doors


def _windows(c, a, y, doors, first=None):
    """A row of cabin windows, at the pitch a cabin is really laid out on.

    Real pitch, not a pitch chosen to look right at one size: the row reads as individual
    windows on a full-page figure and closes up into a line as the drawing is scaled down,
    which is what a published drawing does too.
    """
    pitch, size = 0.53, 0.30
    start = (first if first is not None else a.nose_end + 0.035) * a.L
    end = (a.tailcone_start + 0.075) * a.L
    door_w = 0.85

    x = start
    while x < end:
        if not any(s * a.L - door_w * 0.6 < x < s * a.L + door_w * 1.6 for s in doors):
            c.poly([(x, y - size / 2), (x + size, y - size / 2),
                    (x + size, y + size / 2), (x, y + size / 2)], close=True)
        x += pitch


def _flight_deck(c, a):
    """The windscreen and the radome joint, set into the nose."""
    L = a.L
    # Windscreen: a shallow wedge lying along the crown, raked forward.
    front, back = 0.030 * L, 0.078 * L
    c.poly([(front, a.crown(front / L) + a.deck_depth * 0.10),
            (back, a.crown(back / L) + a.deck_depth * 0.045),
            (back, a.crown(back / L) + a.deck_depth * 0.20),
            (front, a.crown(front / L) + a.deck_depth * 0.28)], close=True)
    # The pane divider, so it reads as glazing rather than as a panel.
    mid = (front + back) / 2
    c.line(mid, a.crown(mid / L) + a.deck_depth * 0.075, mid, a.crown(mid / L) + a.deck_depth * 0.24)

    # Radome joint: the ring the nose cone hinges on, drawn across the section.
    joint = 0.022 * L
    c.line(joint, a.crown(joint / L), joint, a.belly(joint / L))


def _nacelle(c, project, length, diameter):
    """A nacelle: rounded at the intake, tapering to a smaller exhaust.

    ``project(u, v)`` places a point of the nacelle in the view's own coordinates, where
    ``u`` runs along the engine from the intake lip to the exhaust and ``v`` out from its
    axis. The side and plan views point their engines different ways, and this is the only
    thing that differs between them.
    """
    r = diameter / 2
    lip = length * 0.22
    steps = 12
    half = [(lip * (1 - math.cos(math.pi / 2 * i / steps)),
             r * math.sin(math.pi / 2 * i / steps)) for i in range(steps + 1)]
    half += [(length * 0.74, r), (length, r * 0.66)]

    c.poly([project(u, -v) for u, v in reversed(half)] + [project(u, v) for u, v in half],
           close=True)
    (x1, y1), (x2, y2) = project(lip, -r * 0.98), project(lip, r * 0.98)
    c.line(x1, y1, x2, y2)


def _gear_leg(c, x, top, ground, radius, wheels=1):
    hub = ground - radius
    c.line(x, top, x, hub)
    for i in range(wheels):
        c.ellipse(x + (i - (wheels - 1) / 2) * radius * 2.1, hub, radius, radius)


def draw_plan(a: Aircraft):
    """Nose at the bottom, tail at the top; the aircraft's left wing lands on the right."""
    c = Canvas(a.B, a.L)
    B, L, mid = a.B, a.L, a.B / 2

    def y(s):
        return (1 - s) * L                                   # station → view coordinate

    # fuselage, one side then mirrored
    side = [(mid + a.half_width_at(a.tail_tip * i / 120), y(a.tail_tip * i / 120))
            for i in range(121)]
    c.poly(side)
    c.poly([(mid, y(0.0)), (mid, y(a.tail_tip))])            # centreline
    # the flats the nose and the tail cone close on
    c.line(mid - a.half_width_at(0), y(0), mid + a.half_width_at(0), y(0))
    c.line(mid - a.half_width_at(a.tail_tip), y(a.tail_tip),
           mid + a.half_width_at(a.tail_tip), y(a.tail_tip))

    # wing
    root_le, root_te = a.wing_le, a.wing_te
    tip_le = root_le + a.sweep
    tip_te = root_te + a.sweep_te
    tip_x = mid + B / 2 * (0.985 if not a.winglets else 0.955)
    c.poly([(mid + a.D / 2 * 0.9, y(root_le)), (tip_x, y(tip_le)),
            (tip_x, y(tip_te)), (mid + a.D / 2 * 0.9, y(root_te))], close=True)
    if a.winglets:
        c.poly([(tip_x, y(tip_le)), (mid + B / 2, y(tip_le + 0.006)),
                (mid + B / 2, y(tip_te - 0.004)), (tip_x, y(tip_te))], close=True)

    # tailplane
    t_tip = mid + a.tail_span / 2
    c.poly([(mid + a.half_width_at(a.tail_le) * 0.95, y(a.tail_le)),
            (t_tip, y(a.tail_le + a.tail_sweep)),
            (t_tip, y(a.tail_te - 0.004)),
            (mid + a.half_width_at(a.tail_te) * 0.95, y(a.tail_te))], close=True)

    # fin, seen edge on along the centreline
    c.poly([(mid + a.D * 0.045, y(a.fin_le)), (mid + a.D * 0.045, y(a.fin_tip_te))])

    # nacelles, ahead of the leading edge at their own spanwise station
    for off in a.engine_offsets():
        s = a.wing_le + a.sweep * (off / (B / 2)) - 0.075
        _nacelle(c, lambda u, v, x=mid + off, s0=s: (x + v, y(s0 + u / L)), a.nac_len, a.nac_d)
        c.line(mid + off, y(s + a.nac_len / L), mid + off, y(s + a.nac_len / L + 0.050))

    # Wing-body fairing, seen from above as a bulge on the fuselage side.
    fair_le, fair_te = a.wing_le - 0.075, a.wing_te + 0.115
    fairing = []
    for s in _span(fair_le, fair_te, 40):
        u = (s - fair_le) / (fair_te - fair_le)
        fairing.append((mid + a.D / 2 + a.D * 0.20 * math.sin(math.pi * u) ** 0.75, y(s)))
    c.poly(fairing)

    # Passenger doors, as the marks a plan view carries: a short pair of ticks on the side.
    for s in _doors(a):
        w = a.half_width_at(s)
        c.line(mid + w - a.D * 0.06, y(s), mid + w + a.D * 0.02, y(s))
        c.line(mid + w - a.D * 0.06, y(s + 0.85 / a.L), mid + w + a.D * 0.02, y(s + 0.85 / a.L))

    # main gear bay
    c.poly([(mid + a.D * 0.18, y(a.main_gear - 0.02)),
            (mid + a.track / 2, y(a.main_gear - 0.02)),
            (mid + a.track / 2, y(a.main_gear + 0.03)),
            (mid + a.D * 0.18, y(a.main_gear + 0.03))], close=True)

    c.mirror_x(mid)

    # nose gear doors sit on the centreline, so they are drawn after the mirror
    c.poly([(mid - a.D * 0.10, y(a.nose_gear - 0.018)), (mid + a.D * 0.10, y(a.nose_gear - 0.018)),
            (mid + a.D * 0.10, y(a.nose_gear + 0.022)), (mid - a.D * 0.10, y(a.nose_gear + 0.022))],
           close=True)
    return c


def draw_front(a: Aircraft):
    """Fin at the top, gear at the bottom; the aircraft's left wing lands on the right."""
    c = Canvas(a.B, a.H)
    B, mid = a.B, a.B / 2

    # fuselage section
    c.ellipse(mid, a.centre, a.D / 2, a.deck_depth / 2)

    # wing, with dihedral lifting the tip
    root_y = a.body_bottom - a.deck_depth * 0.20
    tip_x = mid + B / 2 * (0.985 if not a.winglets else 0.955)
    tip_y = root_y - a.dihedral
    thick = a.deck_depth * 0.055
    c.poly([(mid + a.D / 2 * 0.85, root_y - thick), (tip_x, tip_y - thick * 0.4),
            (tip_x, tip_y + thick * 0.4), (mid + a.D / 2 * 0.85, root_y + thick)], close=True)
    if a.winglets:
        c.poly([(tip_x, tip_y - thick * 0.4), (mid + B / 2, tip_y - a.H * 0.075),
                (mid + B / 2 - thick * 0.9, tip_y - a.H * 0.075), (tip_x, tip_y + thick * 0.4)],
               close=True)

    # Nacelles, hung under the local wing line on a short pylon.
    for off in a.engine_offsets():
        wing_here = root_y - a.dihedral * (off / (B / 2))
        cy = wing_here + a.nac_d * 0.72
        c.poly([(mid + off - a.nac_d * 0.16, wing_here),
                (mid + off + a.nac_d * 0.16, wing_here),
                (mid + off + a.nac_d * 0.13, cy),
                (mid + off - a.nac_d * 0.13, cy)], close=True)
        c.ellipse(mid + off, cy, a.nac_d / 2, a.nac_d / 2)
        c.ellipse(mid + off, cy, a.nac_d * 0.31, a.nac_d * 0.31)

    # tailplane, seen end on: a thin wedge from the fin root out to its own tip
    tp_root, tp_tip = a.body_top + a.deck_depth * 0.26, a.body_top + a.deck_depth * 0.08
    tp_thick = a.deck_depth * 0.035
    tp_inboard = mid + a.half_width_at(a.tail_le) * 0.92
    c.poly([(tp_inboard, tp_root - tp_thick), (mid + a.tail_span / 2, tp_tip - tp_thick * 0.5),
            (mid + a.tail_span / 2, tp_tip + tp_thick * 0.5), (tp_inboard, tp_root + tp_thick)],
           close=True)

    # Main gear: a leg down to a pair of wheels seen end on.
    hub = a.H - a.D * 0.15
    c.line(mid + a.track / 2, a.body_bottom, mid + a.track / 2, hub)
    for k in (-1, 1):
        c.ellipse(mid + a.track / 2 + k * a.D * 0.10, hub, a.D * 0.085, a.D * 0.15)

    c.mirror_x(mid)

    # fin and nose gear are on the centreline
    fin_w = a.D * 0.20
    c.poly([(mid - fin_w, a.body_top), (mid + fin_w, a.body_top),
            (mid + fin_w * 0.42, 0.0), (mid - fin_w * 0.42, 0.0)], close=True)
    c.line(mid, a.body_bottom, mid, a.H - a.D * 0.11)
    c.ellipse(mid, a.H - a.D * 0.11, a.D * 0.075, a.D * 0.11)
    c.line(0, a.H, B, a.H)
    return c


# ---------------------------------------------------------------------------------------

VIEWS = {"plan": draw_plan, "side": draw_side, "front": draw_front}


def main():
    TARGET.mkdir(parents=True, exist_ok=True)
    written = 0
    for key in FLEET:
        aircraft = Aircraft(key)
        for view, draw in VIEWS.items():
            if key in PLAN_ONLY and view != "plan":
                continue
            svg = draw(aircraft).render().encode("utf-8")
            path = TARGET / f"{key}-{view}.svgz"
            # mtime fixed so a re-run is byte-identical
            with gzip.GzipFile(path, "wb", compresslevel=9, mtime=0) as fh:
                fh.write(svg)
            written += 1

    rows = ["aircraft_type,length,span,height"]
    for key, ((length, span, height), *_rest) in FLEET.items():
        rows.append(f"{key},{length},{span},{height}")
    (TARGET / "dimensions.csv").write_text("\n".join(rows) + "\n", encoding="utf-8")

    print(f"wrote {written} drawings and dimensions.csv into {TARGET.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
