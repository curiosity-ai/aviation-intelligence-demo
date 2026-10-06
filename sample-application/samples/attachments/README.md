# samples/attachments — one rendered document per type

What [`src/Aeralis.Attachments`](../../src/Aeralis.Attachments/README.md) produces for a
document, and what the connector uploads: the published file, the S1000D CSDB object it was
rendered from, and the artwork that object names.

Regenerate this folder with:

```bash
dotnet run --project src/Aeralis.Attachments -- --root . --out samples/attachments \
  --doc MOD-160142 --doc ZPH-21-1218 --doc IO-C-250216 --doc PAWO-160142-01 \
  --doc AMM-M-21-20-13-100-052-A --doc IPC-Z-35-20-11-FIG-15 --doc SRM-S-53-21-13-003 \
  --doc TSM-M-21-40-11-810-805 --doc WDM-Z-92-41-11 --doc FCOM-T-DSC-23-70-01 \
  --doc IPC-M-53-21-13-FIG-01
```

The choice of documents is deliberate: one of each of the ten types, spread across programs so
that every rendering variant appears at least once, plus one extra figure so that both of the
two-issue cases below are visible.

Every print-era document here is a **two-issue** one, and that is not incidental: only a
superseded issue is held as a scan, so a single-issue document — however old its aircraft —
renders clean and would show none of the scanned pipeline. Each of the four pairs below is the
same document twice: `…-A` the archived paper copy, the unsuffixed file today's publication.

| Type | File | Aircraft | Era |
| --- | --- | --- | --- |
| MOD | `MOD-160142.pdf` + `-A` | MERIDIAN-42 | Aeralis Group mark, Helvetica — two issues, neither scanned |
| SB | `ZPH-21-1218.pdf` + `-A` | ZEPHYR-22 | Aeralis Industrie mark, Courier — **`-A` is a scan** |
| IO | `IO-C-250216.pdf` | CIRRANE-13 | Aeralis Group mark, plan view — the only view Aeralis publishes for the type |
| PAWO | `PAWO-160142-01.pdf` | MERIDIAN-42 | Aeralis Group mark |
| AMM | `AMM-M-21-20-13-100-052-A.pdf` | MERIDIAN-42 | Aeralis Group mark |
| IPC | `IPC-Z-35-20-11-FIG-15.png` + `-A` | ZEPHYR-20 | a figure is a page, not a publication — **`-A` is a scanned manual page** |
| SRM | `SRM-S-53-21-13-003.pdf` + `-A` | SOLANO-34 | **`-A` is a scan** |
| TSM | `TSM-M-21-40-11-810-805.pdf` | MERIDIAN-42 | Aeralis Group mark |
| WDM | `WDM-Z-92-41-11.pdf` + `-A` | ZEPHYR-20 | **`-A` is a scan** |
| FCOM | `FCOM-T-DSC-23-70-01.pdf` | TITAN-88 | Classic mark |

Three of the four are also dated from their aircraft's entry into service: the ZEPHYR-20 wiring
sheet's original issue is 1991-02-16, the parts figure's 1990-05-27 and the SOLANO repair's
1996-06-20 — against 2026 for the issues that superseded them. The service bulletin is not, and
should not be: a bulletin is dated to the change it carries, and every change in this dataset is
current. A scan weighs roughly ten times its clean counterpart — it is a rasterised page with the
original words written back over it invisibly.

## The three things a document produces

```
MOD-160142.pdf                                        the published file
source/MOD-160142-B.xml                               what it was rendered from
images/ICN-MRD-A-532113-A-U8025-365B809801-A-001-01.png   the artwork that source names
```

In the graph the first hangs off the document's current issue, and the other two off a
`DocumentationSource` node keyed `{DocType}:{DocId}:{Issue}` — the markup as a property, the
artwork as attachments.

`IPC-Z-35-20-11-FIG-15.png` has no source: a figure is a drawing, and there is no CSDB object
behind it.

## Every issue is its own publication

A document is stored as an array of issues, and each of them was published in its own right —
its own markup, its own artwork, its own PDF. Two documents here have a history:

| Document | Issue | File | Raised by |
| --- | --- | --- | --- |
| `MOD-160142` | A (2018-11-27) | `MOD-160142-A.pdf` | original issue |
| | B (2021-06-30) | `MOD-160142.pdf` | `PAWO-160560-01` finding F04, `OVERLAPPING_SCOPE` |
| `IPC-M-53-21-13-FIG-01` | A (2016-12-05) | `IPC-M-53-21-13-FIG-01-A.png` | original issue |
| | B (2018-09-11) | `IPC-M-53-21-13-FIG-01.png` | `PAWO-160142-01` finding F03, `MANUAL_IMPACT` |

The naming rule is that **the current issue keeps the document's own name and a superseded one
carries its letter**, so a link to `MOD-160142.pdf` always resolves to what the document is now,
and the earlier state is still addressable. Sources are always issue-qualified
(`source/MOD-160142-A.xml`, `source/MOD-160142-B.xml`) because a `DocumentationSource` is per
issue by definition.

The second row is the loop closing on itself: `PAWO-160142-01.pdf` in this folder is the
pre-analysis whose finding F03 required the figure to be split into pre- and post-mod item
pairs, and `IPC-M-53-21-13-FIG-01.png` is the issue that carried it out.

## What to look at

- **The scans** (`ZPH-21-1218-A`, `SRM-S-…-A`, `WDM-Z-…-A`) carry their text. `pdftotext` returns it
  and a viewer will select it; the words are drawn invisibly at the coordinates the layout
  engine gave them, over a JPEG of the page. The skew shows most clearly in the rules of the
  WDM wire list.
- **The era** of every mark, typeface and scan decision follows from the aircraft the document
  is drawn as, which is the type most of its fleet is — not the first type it lists.
- **The ICN** carries a hash of the document *and its issue* (`…U8025-365B8098**01**-…`), not
  just a figure index. `MOD-160142` and `PAWO-160142-01` are both ATA 53-21-13 on the MERIDIAN, so
  an index alone gave them the same information control number and one illustration silently
  replaced the other; without the issue in the hash the same happened between issue A and issue
  B of one document.
- **`source/*.xml`** reparses as the object type it claims. That is asserted in
  `Aeralis.Attachments.Tests`, along with the images being exactly the ICNs the source names,
  and the two issues of a document rendering to distinct files, markup and artwork.
