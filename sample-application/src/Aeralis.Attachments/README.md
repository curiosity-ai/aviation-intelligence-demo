# Aeralis.Attachments

Turns every document in [`data/`](../../data) into the file a technical data system would
actually hold for it — the published PDF, or the manual page a figure is printed on — and
hands it to the connector to upload.

It is the join between the two rendering projects and the dataset:

| | |
| --- | --- |
| [`Aeralis.Presentation`](../Aeralis.Presentation/README.md) | lays an **S1000D CSDB object** out as a PDF |
| [`Aeralis.Illustrations`](../Aeralis.Illustrations/README.md) | draws an **annotated three-view** of the aircraft, plain or as an A4 manual page |
| [`Aeralis.Dataset`](../Aeralis.Dataset) | reads the documents and the reference CSVs |

```bash
# every document, into a directory
dotnet run --project src/Aeralis.Attachments -- --root . --out build/attachments

# one document, to look at what it renders to
dotnet run --project src/Aeralis.Attachments -- --root . --out /tmp/x --doc MOD-160142

# a slice
dotnet run --project src/Aeralis.Attachments -- --root . --out /tmp/x --type AMM --program Z --limit 5
```

`--parallel N` sets how many documents render at once; it defaults to half the cores.

The output directory holds three things:

```
build/attachments/
  MOD-160142.pdf                                  the published file
  source/MOD-160142.xml                           what it was rendered from
  images/ICN-MRD-A-532113-A-U8025-00001-A-001-01.png   the artwork that source names
```

The connector calls the same code — see [`AttachmentsIngest`](../Aeralis.Connector/Ingest/AttachmentsIngest.cs)
— so what this writes to a directory is exactly what a run uploads. A rendered sample of every
document type is checked in under [`samples/attachments/`](../../samples/attachments).

## What each document gets

| Source | Attachment |
| --- | --- |
| **IPC** figure | the drawing laid out as the **A4 manual page** it is printed on, as PNG — and nothing else, because the page *is* the artefact |
| **MOD**, **IO**, **PAWO** | a **descriptive** data module, as PDF |
| **AMM**, **SRM** | a **procedural** data module |
| **SB** | a **service bulletin** data module |
| **TSM** | a **fault isolation** data module |
| **WDM** | a **wiring data** data module |
| **FCOM** | a **crew** data module |

Every PDF carries **one or two annotated illustrations** in its content, pointing at the part
of the airframe the document is about (below). A publication for an aircraft that entered
service **before 2000** is additionally rebuilt as a **searchable scan**, and the clean PDF is
not kept: what a technical data system holds for a manual of that age is the scan.

## Keeping the input, not only the output

A rendered page is an artefact of markup nobody has seen. `Build` therefore returns a
[`DocumentRendering`](AttachmentBuilder.cs): the published `Files`, and the `Source` they came
from — the CSDB object as indented text, its data module code, and every illustration it names
by ICN.

That is what an author needs when a page looks wrong: the markup that produced it and the
artwork it referenced, without re-running anything. The connector lands it as a
`DocumentationSource` node keyed `{DocType}:{DocId}`, carrying the markup as a property and the
illustrations as attachments of that node — see §5.4 of the
[connector skill](../../.claude/skills/aeralis-graph-connector/SKILL.md).

Keyed by both parts because an identifier alone does not say which type it is: a service
bulletin is numbered `MRD-53-1042`, and nothing in that says "SB".

A **figure has no source** — it is a drawing, with no CSDB object behind it — so an IPC
document produces a page and nothing else.

## The S1000D projection

The dataset is not S1000D. It is a document store shaped around MOD pre-analysis, with prose in
a `sections` map and structure in `type_data`. S1000D is the publication format the same
content would be *issued* in, and the two line up type by type — which is what
[`CsdbProjection`](Csdb/CsdbProjection.cs) is: a mapping, and nothing more. Every value in the
output comes from the document.

ATA and the data module code line up almost exactly, so the code is derived rather than looked
up: ATA 6D `53-21-13` is system `53`, subsystem `2`, sub-subsystem `1`, assembly `13`. The
information code is the one part ATA does not give and comes from the kind of document —
except for an AMM task, whose own task number already carries it (`21-20-13-**100**-052-A` is a
cleaning task).

There is **one** place where content is derived rather than copied: the **wire list** of a
wiring sheet. The dataset records `wire_count` and `wire_gauge_range`, not the individual
wires, so the rows are laid out from the connectors the sheet *does* list, in the count it
declares, with the gauges it gives. Nothing is written back to `data/`, and the layout is
seeded from the document's id like everything else. See
[`CsdbProjection.Wiring.cs`](Csdb/CsdbProjection.Wiring.cs).

### Why the steps are flat

A procedural step lays out as a list item, and the layout engine does not break a list item
across a page. Nesting thirteen steps inside one titled step therefore produces a block taller
than a page that cannot be split — which comes out as a blank page followed by content running
off the bottom of the next. Steps are emitted at the top level of the procedure for that
reason, and a repair's prose sections become one step each rather than one step per sentence.

## Where a callout points

A callout needs an airframe section, a view and a position along that section. The dataset says
which **zones** a document touches, and `data/reference/zones.csv` carries
`airframe_section`, `view_hint` and `section_position` per zone —
so zone 111, "Fwd Cargo Compartment, LH — Fwd Section", is the forward fuselage at 0.20 on the
side view. `ata_chapters.csv` carries the first two per chapter, which is the fallback for the
documents that list no zone (every FCOM entry, and five others out of 1500).

What a callout **says** is chosen in this order, so it names equipment rather than geography
wherever the document gives it:

1. a **FIN** the document lists that is installed in that zone — `9469HM / Sensor, cargo temperature`
2. a **part** the document lists in the same ATA chapter — `Bolt, hexagon head / AES495354-07`
3. the zone itself — `Zone 111 / Fwd Cargo Compartment, LH`

Two zones can produce the same label; the repeat falls back to the zone name, because the same
words twice on one drawing say nothing the second time.

Figures are grouped **by view** — all the callouts that land on the side view go on one side
view — and a publication gets at most two, which is what a location figure pair looks like.

## Which aircraft, which era

A document usually applies to a whole family. The type it is drawn as is the one **most of the
fleet is**, counted from `msn_registry.csv` — not the first type listed. Taking the first would
illustrate an zephyr-family document with an ZEPHYR-18, of which the demo fleet holds fourteen against
the ZEPHYR-22's two hundred, and would date the publication to 2003 rather than 1988. That date
decides three things:

| | |
| --- | --- |
| the **mark** in the header | the Aeralis lock-up in use when the aircraft entered service — `AeralisMark.EraFor` |
| the **typeface** | Courier for the printed era, Helvetica after it |
| whether it is held as a **scan** | before 2000 **and only for a superseded issue** — see below |

Aeralis publishes the CIRRANE as a **plan view alone**, so program C is illustrated on the plan: a
zone's `view_hint` says which view shows the feature best across the fleet, and for those two
types the hint is re-pointed rather than dropped. The section and the position along it still
hold — the outlines cover every section on every view. Ask
`AircraftIllustrator.AvailableViews` before offering a choice of view.

### Only a superseded issue is a scan

Being of the printed era is necessary and not sufficient. The issue a document is currently at
is the live one: whatever it looked like when it was first printed, what you open today is the
digital publication, and rendering it as a scan says the current manual is a photocopy. What
sits in the archive as paper is the issue that was *replaced*.

So `IllustrationPlanner.IsScanned` is `!isLatest && IsPrintEra(aircraft)`, and an issue history
of a print-era document reads as two different artefacts — the superseded issue as a scan of a
page from around the aircraft's entry into service, the current one as clean digital artwork.
`samples/attachments` holds four such pairs.

The **typeface** is not part of that test. Courier belongs to the era the publication was output
in, so an ZEPHYR-22 page is typewritten whether you are looking at the archived paper copy or at
today's issue of it.

## The searchable scan

Three steps per page, in [`Pdf/`](Pdf):

1. **[`PdfPageRasterizer`](Pdf/PdfPageRasterizer.cs)** draws the page onto a bitmap. There is no
   PDF rasteriser in the stack — FOP.Sharp writes PDFs, PdfPig reads them, neither draws one —
   so the page is taken apart with PdfPig (every glyph with its baseline and size, every vector
   path with its commands and paint, every embedded image with its placement) and drawn again
   with Skia. Glyphs are placed one at a time from their own baselines, so the substituted
   typeface does not have to match the metrics of the one the PDF was written with: the layout
   has already happened and every character carries where it landed.
2. **[`ScannedEffect`](../Aeralis.Illustrations/ScannedEffect.cs)**, from the illustrations
   project, applies the scan: the sheet askew on the platen, optical softening, off-white
   grained paper, uneven lighting, dust, JPEG blocking.
3. **[`ScannedPdfBuilder`](Pdf/ScannedPdfBuilder.cs)** writes a new page holding that image at
   full bleed, with the original words drawn over it in the **invisible** text rendering mode,
   word by word at the position each was laid out at.

The invisible text is the point. A scan on its own is a picture: it cannot be searched, indexed,
embedded or quoted, and a demo whose corpus is half pictures demos nothing. Writing the words
back at their own coordinates is what an OCR pass produces, except that here the "OCR" is exact
— the text never left the pipeline. Selecting a line in a viewer highlights it where it appears
on the scan.

```bash
pdftotext scanned.pdf -    # the words are there
pdfimages -list scanned.pdf   # so is the scan, one JPEG per page at 200 dpi
```

## Determinism

Every random choice is drawn from the document's own identifier through
[`Seeded`](Seeded.cs) — an FNV-1a hash, because .NET randomises `string.GetHashCode` per
process. That covers the illustration's target sampling, which callouts are kept when more are
available than fit, the wire list, and the scan's skew, grain and dust. A document therefore
produces the same drawing and the same scan on every run and on every machine.

The PDF **bytes** are not promised to be identical — the renderer stamps its own metadata — but
their content is.

## Fonts

Nothing reads a font from the host. Body text in a rasterised page is set in the typefaces
embedded in the illustrations project (Gudea, Courier Prime), reached through
`Aeralis.Illustrations.PageFonts`, so a page rasterises the same way wherever it runs.
