---
name: curiosity-node-renderers
description: How to render node types in a Curiosity front end — INodeRenderer auto-discovery, a shared renderer base, the card body and footer, LabelField and its silent failures, display names and icon tiles, the tab set, the 360 view, rendering properties as the relationships they really are, and the elaborate renderer for the centrepiece type. Use when writing or changing anything under Renderers/.
---

# Node renderers — how a record looks everywhere

A renderer defines how one node type appears in **every** surface at once: a search result
row, a preview sheet, a full page, a peek panel, a neighbour link, a graph node. Derive from
the product's renderer base, override what differs, and every surface picks it up because the
host calls through your instance.

---

## 1. Auto-discovery — and its two silent failures

Renderers are **not registered**. The app reflects over the loaded assemblies for every
non-abstract `INodeRenderer` implementation and instantiates it. So a renderer joins the app
simply by existing — and two things fail silently:

- **Every renderer needs a public parameter-less constructor.** It supplies its own style info
  to the base rather than receiving one.
- **Shared bases must be `abstract`**, or discovery tries to register the base as a renderer
  for a node type that does not exist.

Name them plainly (`CaseRenderer`, `PartNumberRenderer`); discovery matches the interface,
never the name.

> **Every node type needs a renderer.** A type without one has no label and no icon, and shows
> as a raw UID wherever it is reachable — a neighbour link, a search result, a graph node, a
> property row. That includes the key-only metadata types, which are five lines each. In the
> reference implementation this was the single most common "why does this look broken".

---

## 2. Build a shared base

One abstract base carries everything common: the card, the footer, the tab set, the property
rendering, the modal size. A concrete renderer then states only what is different about its
type, and is typically ten lines:

```csharp
public sealed class CaseRenderer : AppRendererBase
{
    public CaseRenderer() : base(
        new SchemaStyleInfo
        {
            Type        = N.Case.Type,
            DisplayName = "CASE",
            Icon        = UIcons.Screw.ToString(),
            LabelField  = LabelFields.IdAndTitle(N.Case.DocId, N.Case.Title),
        },
        N.Case.Abstract,                                          // the card body
        N.Case.Status, N.Case.Category, N.Case.Programme)         // the card footer
    { }
}
```

---

## 3. The card

### Body

The base draws *every* property as a stack of labelled rows — which for a rich record is
thirty rows inside one row of a result list. Override it to draw **one** property, the
abstract or summary, small and secondary; and return nothing at all when the type has none,
which collapses the row rather than leaving a gap. Reference types usually pass `null`: a part
number has a description, not a summary.

### Footer

The handful of facts that place a record without opening it: where it stands (status), what it
is about (category), which partition it belongs to, plus the type's own one or two
discriminators.

- These are **node properties, not edge walks**, deliberately. A result list draws dozens of
  cards at once; a footer that queried the graph would be dozens of round-trips deep before
  the first row appeared. The values named are the same ones the edges were built from, so the
  footer says what the graph says, synchronously.
- Append, do not replace: the base has already put the timestamp there, and the "set" call
  *clears*.
- **Drop values over ~48 characters** rather than letting prose push the line off the card.
  Check a candidate field against real data before adding it — a field that holds a sentence
  on some records is not a footer field.
- Show the value alone, unlabelled, with the property name as a tooltip. Unlabelled is the
  point at a glance; the tooltip is for the one reader in ten who has to ask which of two
  similar codes they are looking at.

### The icon tile

The type's letters on a coloured tile. Put it on the **modal** as well as the card — a record
opened from a card carrying its mark into a sheet that does not is the one place the reader is
looking hardest and the mark is missing.

The tile is about 34px and **does not auto-size its text**, so it needs its own short name,
separate from the display name:

- up to five characters, four preferred;
- at most two words separated by `'\n'`, each under the same limit — the newline is a wrap
  opportunity, and each word lands on its own line;
- use established abbreviations (`A/C`, `DWG`);
- a two-line tile needs a step-smaller text size than a one-line one;
- where **one node type holds things a reader must tell apart** — a category node that is a
  2-, 4- or 6-digit level — make the tile text a function of the node, not of the type.

### The modal

Override the preview size. The base opens at 80×80 % of the viewport, which is right for a
handful of properties; a tabbed view with a graph, a faceted search and a split screen inside
a tab is not usable at that size — 92×90 is. **Keep the base's "a workspace-configured
renderer wins" branch** when you override.

---

## 4. `LabelField` — a field *name*, and it fails silently

`LabelField` is **not** a caption. It is a list of *node field names* separated by `|`, and the
label is the first one that has a value. When none do — including when the field simply does
not exist on that type — the label falls back to `"<uid> [Display Name]"`, with no error
anywhere. **A typo turns every label of that type into a raw UID.**

So: write it as `N.<Type>.<Field>`, never as a literal, and name a fallback after any field
that can legitimately be empty:

```csharp
LabelField = N.Item.Nomenclature + "|" + N.Item.PartNumber + "|" + N.Item.ItemId,
```

**A label needing more than one field at once uses the interpolated form** — a leading `$`
then `{Field}` placeholders, with an optional `{Field:format}`:

```csharp
LabelField = "$ATA {Code} — {Title}",     // reads as both
LabelField = "${Value} ({Kind})",         // for a node keyed "{Kind}:{Value}"
```

**Every type with an identifier *and* a title should use it.** The `|` form takes the first
field with a value, so `Title|DocId` shows the title and never the identifier — the wrong way
round for technical content, where the identifier alone does not say what the record is and
the title alone does not say which of four similar ones it is. Compose it through one helper:

```csharp
LabelField = LabelFields.IdAndTitle(N.AmmTask.DocId, N.AmmTask.Title),   // "${DocId} > {Title}"
```

Compose it, never type it as a literal: placeholders are field names, a name that does not
exist renders as empty, and nothing fails. Built from `N.<Type>.<Field>`, a rename is a build
error. A missing value leaves no hole — the interpolator trims separators off both ends.

**Two traps, both live in the reference implementation:**

- a field that exists but holds a *paragraph* is technically valid and useless — a finding
  labelled by its rationale produced link text a screen wide;
- a type with no renderer has no `LabelField` at all.

---

## 5. `DisplayName` is the abbreviation, upper-case

`MOD`, `SB`, `AMM`, `PAWO`, `ATA`, `PN`. These are the names on the documents and in every
conversation about them — nobody asks for "the Modification" — and the type name is read
hundreds of times a session in facets, result rows and graph nodes, where the short form is
what keeps a mixed list scannable.

Only where the discipline **has** an abbreviation. `Drawing`, `Zone`, `Operator`, `Supplier`
keep their words; nothing is contracted just to be short.

Two traps: do not encode a *level* in the type name when one type holds several levels (an
`AtaChapter` is `ATA`, and a `Level` property says which); and do not use an abbreviation that
is only true of part of the data.

Every renderer also sets an **`Icon`**. Without one the base derives a colour from the type
name, which tells types apart but says nothing about what they are — and these are read side
by side in mixed lists, where a wrench against a tail fin is the difference between scanning
and reading. Take names from the icon enum so a wrong one is a build error rather than a blank
square.

---

## 6. Tabs

Every type gets the same base set, and the base builds it:

```
Overview   the properties grid                    (§7)
Consult    the published file, where there is one (§8)
Graph      the neighbourhood, using the base's own graph segment helper
360        everything connected, in one faceted search area (§9)
[relationship tabs, per edge group]
```

Do not build a graph explorer by hand inside a deferred block — the base's graph segment
already handles the mount-then-load-then-fit sequence.

---

## 7. A property that is a reference renders as the thing it references

Override the properties grid and ask a **property-link table** about every property before
drawing it. Where the property is really a reference, draw the neighbours across that edge
instead of the string.

This is most of the details grid, not an edge case: a category code, a partition letter, a
keyword list, a related record id, a status, a classification. As text, every one is a dead end
— you can read "53" and not ask what else is in it.

```csharp
var link  = PropertyLinks.For(NodeType, propertyName);
var value = GetValueForProperty(node, propertyName);

if (link is object)
    return new NeighborsLinks(node.UID, link.NodeTypes, link.EdgeType,
                              wrap: true, maxItemsToShow: 12,
                              showIfNone: () => PlainValue(value));
```

Three things make it safe:

- **The property stays on the node.** It is what the search index reads, and it is the
  fallback (`showIfNone`) when the edge is absent — so a type that has the property but not the
  edge degrades to plain text rather than to a blank row.
- **The per-type table wins over the shared/envelope one.** Two types will use a shared
  property's *name* for something else and reach it over a different edge. Matching the shared
  table first silently sends both down an edge that does not exist for them.
- **Every entry is written `N.<Type>.<Field>` → `E.<Edge>`**, so a rename on either side is a
  build error.

The mapping **cannot be derived** — that a given property is reached by a given edge, and that
`Status` means one vocabulary rather than another, is a decision the connector made. Write it
out once, in one table.

> Adding a property to this table is the *second* half of promoting a string to a
> relationship in the connector. Without it the new node is reachable only from the 360 view.

Long values still need the base's split: a paragraph gets its own scrollable box rather than
sitting on a row of labels.

---

## 8. The published file

Where a record has an attached artefact, show **the real viewer**, not a picture of its pages.
Ask the workspace's own file renderer for the body it would put in a preview sheet, and place
that body in your tab:

```csharp
var files = await API.Query.StartAt(ownerUID).Out(N._FileEntry.Type, E.HasAttachment).TakeAll().GetUIDsAsync();
var file  = await API.Nodes.GetAsync(files.UIDs[0]);

var row   = await new _FileEntryRenderer(showHeader: false, showFooter: false, enableHotkeys: false)
                      .PreviewAsync(file, null);

var body  = row is object ? await row.GetModalContentAsync() : null;
```

The three flags are what make it a body rather than a sheet: no header and no footer, because
the card already says what this is and the tab is not a modal; no hotkeys, because the PDF's
find shortcut must not take it from the page it is embedded in. Turning the footer off is also
what puts the download button in the viewer's own toolbar.

Because the connector attaches per revision *and* hangs the current one off the record, the
same walk gives a revision the file of that revision and a record the file of its latest —
without either renderer knowing which it is looking at.

Decide whether a type has a file from the generated `N.DocumentTypes`, not from a hand-kept
list, so a new record type arrives with its tab already on.

---

## 9. The 360 tab

One search area, deliberately filtered **broadly**, so the user sees every kind of object that
touches this node — including the NLP-derived ones.

```csharp
SearchArea()
   .WithFacets()
   .OnSearch(sr => sr.WithTargetQuery(API.Query.StartAt(node.UID).Out(nodeTypes: types).TakeAll()));
```

- **Pass no edge-type filter.** Structured edges and `_Mentions` / `_AppearsIn` both belong
  here; the whole point is that the user does not have to know which mechanism found a
  connection.
- The type list differs per renderer — a part number shows the records that use it; a
  centrepiece record shows everything. Abstract the shell into one component and let each
  renderer pass its own list, defaulting to the generated all-types set.
- A variant that starts **one hop away** is useful for a record whose subject is another record
  — express the hop inside the target query, so it stays one server call and stays a traversal.

---

## 10. Revision renderers

If you modelled revisions (`curiosity-graph-modelling` §6), each revision node type needs its
own renderer, or every history traversal draws raw UIDs. Put everything shared in one abstract
base; each type then states four things: node type, display name, tile text, icon.

- **Tile text and display name are separate here.** The tile shows the *document's*
  abbreviation (`AMM`), while the display name stays the readable `AMM issue` for facets and
  modal headers.
- **The revision letter is a badge, not a footer entry** — which revision you are looking at is
  the reason the node exists separately from its record, and the badge is read before the title.
- Nothing extra is needed to keep a revision visually distinct from its record: the base
  derives the tile colour from the type name, so they tint differently on their own.
- All revision types declare the same fields under the same names, so one property-link helper
  answers all of them before the per-type table.

---

## 11. The centrepiece renderer

The unit of work gets an elaborate renderer instead of the base tab set. What the reference
implementation learned building one:

- **No inner tab strips.** A pivot inside a tab puts two levels of navigation in front of the
  user *and* hides the half of a comparison they are meant to be reading against. Where a tab
  has two sides, it is a **single** split view.
- **Left is the analysis as it stands; right is the thing being analysed.** Consistently, on
  every tab that has two sides.
- **Merge a table and its commentary into one list.** A coverage matrix in one card and a
  suggestions list in another meant holding one in your head to read the other; one list, one
  row per item, carrying both what was found and what is still suggested — with the
  uncovered ones sorted to the top and badged — replaced both plus the banner that counted the
  gap.
- **The written output does not belong on the reading screen.** It is a product, not something
  read while deciding, and at the bottom of a column it makes that column scroll past the
  things that are.
- **Ask for one node type over one *named* edge per row** in an applicability/summary block.
  Passing several types with no edge gives you no way to say which dimension came back empty.
  And **always pass a "none" fallback** — an empty row must read as "—" rather than collapse,
  or the whole card looks like it failed to render.
- **Hand a dropdown all of its items in one call.** The items call *clears* before adding, so
  calling it once per item in a loop leaves a dropdown holding only the last one.
- **Multi-select plus batched commands** on any list where the job is deciding about dozens of
  rows. Register a command with a **unique id** so the same command across twenty selected
  rows collapses into one entry rather than twenty identical ones.
- **Load all decision state in one call before the first row draws**, and re-run the search to
  repaint badges after a change — a row's badge is read while the row is being built.

---

## 12. Checklist

- [ ] Every node type has a renderer, including key-only metadata types
- [ ] Every renderer has a public parameter-less constructor; every shared base is `abstract`
- [ ] `LabelField` composed from `N.<Type>.<Field>`, with a fallback, never a literal
- [ ] Identifier-and-title types use the interpolated id-and-title helper
- [ ] `DisplayName` is the discipline's abbreviation, upper-case, where one exists
- [ ] Every renderer sets an `Icon`; the tile short name fits ~4–5 characters
- [ ] Card body is one property; footer is the few facts that place the record
- [ ] Preview modal resized; the workspace-configured-renderer branch preserved
- [ ] Every reference-like property is in the property-link table
- [ ] 360 areas filter broadly and never exclude `_Mentions` / `_AppearsIn`
- [ ] No pivot inside a tab

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`AppRendererBase.cs`](examples/AppRendererBase.cs) | The whole shared base: card, footer, icon tile, modal size, tab set, property-as-relationship rendering, the published-file embed |
| [`Renderers.cs`](examples/Renderers.cs) | Concrete renderers — a unit of work, a reference type with a narrowed 360, a multi-level type with a per-node tile, and the key-only metadata types |
| [`LabelFields.cs`](examples/LabelFields.cs) | Composing `LabelField` so a rename is a build error |
| [`PropertyLinks.cs`](examples/PropertyLinks.cs) | The per-type-wins-over-envelope table |
| [`View360.cs`](examples/View360.cs) | The broad 360 area, and the one-hop variant |
