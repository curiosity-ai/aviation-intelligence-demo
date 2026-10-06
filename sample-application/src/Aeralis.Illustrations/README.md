# Aeralis.Illustrations

Draws Aeralis' general-arrangement three-views with technical callouts pointing at named
parts of the airframe — either as a plain image or laid out as an A4 manual page.

The drawings themselves are **generated**, not published artwork: Aeralis is a fictional
airframer, so [`tools/gen_aircraft_views.py`](../../tools/gen_aircraft_views.py) draws every
type of the fleet as a parametric airliner — a lofted fuselage, a swept wing, a tail and its
nacelles, all sized from the type's overall length, span and height — and writes the three
projections of it as gzipped SVG into `Resources/`.

```csharp
// One callout, aimed a quarter of the way out along the left wing.
AircraftIllustrator.Save(
    new IllustrationRequest
    {
        Aircraft = AircraftType.Zephyr22,
        View = ViewKind.Side,
        Annotations = [new Annotation(AircraftSection.LeftWing, "Slat track can", 0.25)],
        ShowDimensions = true,
        Seed = 42,
    },
    "wing.png");

// The same view as a manual page.
AircraftIllustrator.SaveDocumentPage(
    new DocumentPageRequest
    {
        Aircraft = AircraftType.Zephyr22,
        View = ViewKind.Side,
        DocumentType = DocumentKind.AircraftMaintenanceManual,
        DocumentNumber = "06-10-00",
        Title = "Dimensions and Areas",
        Heading = "ZEPHYR-18/ZEPHYR-20/ZEPHYR-22/ZEPHYR-24",
        Effectivity = "201-225, 551-599",
        Orientation = PageOrientation.Portrait,
        ShowDimensions = true,
        Annotations = [new Annotation(AircraftSection.Nose, "Radome latch", 0.5)],
    },
    "page.png");
```

`Draw` returns an `SKBitmap` if you would rather compose further; `Save`/`SaveDocumentPage`
write a PNG. There is also a short overload for the one-callout case:
`AircraftIllustrator.Draw(aircraft, view, section, text, position, seed, width)`.

## Inputs

| | |
| --- | --- |
| `Aircraft` | One of the 24 types Aeralis publishes a general-arrangement sheet for. |
| `View` | `Plan`, `Side` or `Front` — but ask `AircraftIllustrator.AvailableViews(aircraft)` first. Aeralis publishes the **CIRRANE-11 and CIRRANE-13 as a plan view alone**, with no side or front drawing to fall back on, so those two have `Plan` and nothing else. Every other type has all three. Asking for a view a type does not have throws `ArgumentOutOfRangeException` naming the views it does have. |
| `Annotations` | Any number of callouts, each a section, its text (newlines allowed) and an optional position. |
| `Position` | `0`–`1` along the section: 0 is the forward or inboard end — nose for the fuselage, root for a wing — and 1 the aft or outboard end. Null picks anywhere in the section. |
| `ShowDimensions` | Adds overall dimension lines in feet over metres. |
| `Seed` | Fixes the random targets so a call is reproducible. |
| `Orientation`, `Dpi` | Pages only. A4 at 150 dpi is 1240 × 1754. |
| `Scanned`, `ScanAmount` | Pages only. Renders the page as a scan of a printed one. Null turns it on for pre-2000 aircraft. |
| `TextStyle` | `Modern` or `Typewriter`. Null gives pre-2000 pages the typewriter face. |

## How a target is chosen

Each section has a rough outline in view fractions ([`SectionMap`](SectionMap.cs)) — a box, or
a quad following a wing's sweep. One set of outlines covers the whole fleet, because every
drawing is in the same orientation and airliners of this class share their proportions closely.

That orientation is not a coincidence to be relied on quietly: the generator draws every view
in the orientation `SectionMap` documents — plan nose-down, side nose-left, front fin-up — and
the outlines are written against those. A plan view drawn nose-up would need a half turn, never
a mirror: mirroring a plan view exchanges port and starboard and draws a different aeroplane.

The outlines are deliberately generous, so they also cover white space beside the aircraft.
What makes a target land on the airframe is the **silhouette**: each drawing is rasterised once
and a cell counts as aircraft when it falls between the topmost and bottommost ink in its
column *and* between the leftmost and rightmost ink in its row. Targets are rejection-sampled
from the outline, the silhouette and the requested position together, relaxing one constraint
at a time if nothing fits.

## Where labels go

Label placement measures the free space on the page rather than trying a few fixed positions
([`LabelPlacer`](LabelPlacer.cs)):

1. **Rasterise the obstacles.** The aircraft and any dimension lines are drawn into a binary
   mask — by the same closure that draws them to the page, so the two cannot drift apart.
2. **Close the pockets.** White areas with no route to the edge of the page — the inside of a
   fuselage, the gap between a wing and a nacelle — are flooded shut, then the whole mask is
   dilated a few cells to leave air around the artwork.
3. **Distance transform.** Every free cell gets its distance to the nearest obstacle: zero
   along the edges of the no-go regions, rising to a ridge along the middle of each open
   corridor. That surface is what makes a position measurable — a label on the ridge of a
   corridor is as far from the artwork as that corridor allows.
4. **Fan out from the arrow.** For each callout, rays are cast from the arrow tip and the first
   radius at which the whole text box lands in free space is taken per direction. Candidates
   are scored on leader length with clearance breaking ties, so labels sit close to what they
   point at but still in open space.
5. **Then it becomes an obstacle too**, so later labels flow around it.

If nothing fits anywhere — a crowded page, a very long label — the placer returns the position
with the least overlap rather than refusing to draw. The leader may cross the airframe, as it
must; the text never does.

## Scanned pages

Documentation of this age is rarely held as clean digital artwork — what a technical data
system actually receives is a scan of paper. Pages for aircraft that entered service before
2000 are therefore rendered as scans by default, and set in **Courier Prime** rather than a
proportional sans, because manuals of that period came off monospaced output devices. Both
follow from the aircraft's own introduction; `Scanned` and `TextStyle` override either.

[`ScannedEffect`](ScannedEffect.cs) works on the finished page, since a scan is a photograph of
a printed result rather than something any element on the page knows about:

- the sheet sits askew on the platen, and drifts sideways down the page — drawn as overlapping
  horizontal strips nudged along a slow sine, which bends the page without needing a mesh
- the optics lose a little sharpness, and the response lifts black off black and white off white
- the paper is off-white, grained, and unevenly lit towards the edges
- dust on the glass shows as specks, with the odd streak along the direction of travel
- the result is stored as JPEG, which is where a scan's blocking and faint ringing come from

`ScanAmount` scales all of it; `Seed` fixes the randomness so a page can be reproduced exactly.

## Dimensions

`ShowDimensions` reads `Resources/dimensions.csv`, written by
`python3 tools/gen_aircraft_views.py` from the same fleet table the drawings come from. Every
view is drawn to those figures — the side view length by height, the plan span by length, the
front span by height — so a dimension line reports exactly what the drawing under it measures,
and the two can never drift apart.

## Resources

Everything the renderer needs is embedded, so output does not depend on the machine:

- the 68 view drawings as gzipped SVG, from `tools/gen_aircraft_views.py` — three views each
  for the 22 three-view types, plus the plan view of each CIRRANE
- `dimensions.csv`
- **Gudea** (regular, bold, italic) for body text on modern pages — SIL Open Font License, see
  [`Resources/licences/Gudea-OFL.txt`](Resources/licences/Gudea-OFL.txt)
- **Courier Prime** (regular, bold, italic) for typewriter-era pages — SIL Open Font License,
  see [`Resources/licences/CourierPrime-OFL.txt`](Resources/licences/CourierPrime-OFL.txt)
- **Aeralis Special** for the type designation in the page heading
- the Aeralis marks, split into roundel and wordmark

## Which mark a page carries

Two marks cover the fleet, and a page takes the one Aeralis was using when its aircraft entered
service — so an AUSTER-20 page carries the mark of its own vintage and a MERIDIAN-46 page the
current one. Winglet variants take their own introduction, not the base type's: a wingletted
ZEPHYR-22 is a 2012 aeroplane even though the ZEPHYR-22 is a 1988 one.

| Era | Mark | Aircraft |
| --- | --- | --- |
| `Heritage` (before 2015) | the mark adopted with the Zephyr and carried through the Solano and Vantor years | AUSTER-20, AUSTER-24, every ZEPHYR, SOLANO-30/-30E/-30F/-34, VANTOR-62/-64/-66/-68, TITAN-88, CIRRANE-11, CIRRANE-13 |
| `Modern` (2015 on) | the mark adopted with the Meridian programme | MERIDIAN-42, MERIDIAN-46, SOLANO-38, SOLANO-39 |

Set `LogoEra` on a page request to override the choice.

A page drawn elsewhere needs the same mark, so the choice and the artwork are also available on
their own through `AeralisMark`:

```csharp
LogoEra era = AeralisMark.EraFor(AircraftType.Zephyr22);   // Heritage — a 1988 aeroplane
bool scan  = AeralisMark.IsPrintEra(AircraftType.Zephyr22); // true, so its manuals were printed
byte[] png = AeralisMark.RenderPng(era, 300);               // on opaque white
```

`Aeralis.Attachments` uses it to put the right mark in the running header of a rendered S1000D
publication, so a ZEPHYR-22 dossier carries the heritage mark and a MERIDIAN one the current
mark — the same rule as a drawn page. `PageFonts` and `ScannedEffect` are public for the same
reason: the effect works on a finished raster and does not care what drew it.

The heritage mark also prints at the foot of the page, as it did on the printed pages of that
era; the current identity says nothing in the footer that the heading has not already said.
`ShowFooterMark` overrides that.

Both marks are emblems with no lettering of their own, so there is no wordmark to split off and
no part to choose — a heading is the mark followed by the type designation, and it reads
"MERIDIAN-46".

`tools/prepare_logos.py` turns the exported artwork into what the renderer loads. The export is
a filled plate with the mark punched out of it, so the first group is dropped, the viewBox is
cropped to what is left, and each remaining layer is mapped to a **grey of its own luminance**.
Greys rather than one flat black because both marks are built from stacked layers: flatten them
and the heritage mark in particular collapses into a solid blob. The range stops short of white
so the lightest layer still reads on paper.

Svg.Skia renders a picture's whole content regardless of the root viewport, so the crop is
re-applied as a clip when drawing.

```bash
python3 tools/prepare_logos.py   # Logos/aeralis-*.svg -> Resources/logo-{era}.svg
```

## Regenerating the drawing resources

```bash
python3 tools/gen_aircraft_views.py   # Resources/*.svgz and Resources/dimensions.csv
```

Deterministic: the same fleet table produces byte-identical drawings, so a re-run with nothing
changed leaves the working tree clean.

## Demo

`dotnet run --project src/Aeralis.Illustrations -- out/` writes a set of sample views and
both page orientations.

`-- diag` prints silhouette coverage for every embedded drawing, which is the quickest way to
see whether a drawing was measured at all — a plan view sits around 22–26 %, a side view around
36–38 %, a front view around 15 %, and anything near zero means the measurement failed. Name a
type and a view and it also prints where each section would put its arrow, in view fractions
with (0,0) at the top left, which is how you tell that a section outline is aimed where you
think it is:

```bash
dotnet run --project src/Aeralis.Illustrations -- diag                # coverage, every drawing
dotnet run --project src/Aeralis.Illustrations -- diag Cirrane13 Plan  # + a target per section
```
