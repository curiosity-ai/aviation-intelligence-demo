---
name: curiosity-dashboards-and-lists
description: Building the home page and the work lists of a Curiosity application — replacing the home view, notice banners, metric tiles that open the list they counted, live-computed performance charts, one segment description shared by every surface, and SearchArea as the universal list. Use when building the dashboard, an inbox page, or any list of records in the front end.
---

# Dashboards and lists — the pages that are not a record

Two surfaces beyond the renderers: the **home page**, which says what is waiting for this
person, and the **work lists**, which are the same five questions asked from three places.

Everything on both is computed live from the graph. Nothing is hard-coded and nothing is
pre-aggregated — if a metric cannot be computed from the data, it does not go on the page.

---

## 1. Replacing the home view

```csharp
settings.HomeView = state => new DashboardView(state);
```

The home page renders **as** the selected user (`Viewer`, see `curiosity-frontend-setup` §6),
because a demo has no real login but every number on the page is per-person.

---

## 2. The order of the page

```
   [ notice strip ]        what is actually waiting, in words
   [ 4 counter tiles ]     the same thing in digits — each opens its list
   [ my performance ]      three live numbers about this person's throughput
   [ work lists ]          a segmented pivot, one segment per counter
```

### The notice strip

Say it in words before you say it in digits. A banner: urgent items still open (danger, flame
icon, an "Urgent" badge) or — when nothing is urgent — items with no owner (primary). Its
action selects the matching segment of the list below, so **the number always has a list
behind it**.

**Nothing waiting means no strip at all.** A banner that is always there reads as chrome.

### Counter tiles

One card per number, each a metric leading with an icon tile and drawn value-first, so the
digits read first and the words under them only say what was counted.

- **Colour is what the number means** — red is late, amber is nobody's yet — so the row is read
  by colour before it is read by digit.
- **Fetch the summary once per user** and let the greeting, the banner and all four tiles await
  the same task. Otherwise the sentence and the digits can disagree.
- **The whole card is the click target.** The number is the thing being clicked and it is the
  biggest thing on the card.
- **Every card opens the list it counted.** A number on a dashboard is half a question whose
  other half already exists as a segment of a list page.

### My performance

Three numbers, each drawn as what it actually is:

| Number | Drawn as |
| --- | --- |
| Completed per period | A metric whose **value** is the total over the window and whose **chart** is a sparkline of the periods, with the last period against the one before it in the header. **Not** a grid of twelve rows: the only question anyone asks of a series is whether it is going up |
| Average duration per unit | A counter tile |
| Outcome split | One metric whose value is the success share and whose chart is a contribution bar (max 100) of the outcomes, with the period's total in the header |

All computed server-side from the workflow status, the completion date and the recorded effort
— see `curiosity-code-endpoints` §7.

---

## 3. One description of the segments, read by three surfaces

The counter cards, the sidebar entries and the pivot tabs are three renderings of one list of
segments. Describe them **once** — id, label, icon, colour — in a single place, and have all
three read it.

```csharp
foreach (var segment in WorkLists.Segments) { … }
```

It lives in its own file because more than one surface shows it and they have to agree: every
counter card and the banner send the reader from one to the other.

**Two segments people conflate, and both belong on the page:** "in my inbox" is *mine and not
finished*; "mine" is *everything ever assigned to me*. Both numbers are on the home page, so
both need a segment — a card whose number says 7 opening a list of 34 is a card that lied.

---

## 4. `SearchArea` is the universal list

Every list of records in the application is a search area. You never write a table of records:
the search area gives you the registered renderer per row, facets, selection, commands,
highlighting and paging for free, and it stays correct when a new node type appears.

Three ways to scope it:

```csharp
// (a) a query — "everything matching this traversal"
SearchArea().WithFacets().OnSearch(sr => sr.WithTargetQuery(API.Query.StartAt(uid).Out(…).TakeAll()));

// (b) an explicit set — "exactly these, which something already counted"
SearchArea().NoSearchBox().SearchFor(new SearchRequest().WithTargetUIDs(uids));

// (c) a similarity engine — "ranked candidates, with scores"
SearchArea().WithFacets().WithSimilarityEngine(_ => Api.SimilarityAsync(path, seedUID));
```

**Use (b) wherever a count came from an endpoint.** The endpoint returns the UID array it
counted; opening the number then shows exactly that set, and the tile and the list cannot
diverge. Use (a) for open-ended lists where a count was not involved.

Other things worth knowing:

- `.WithExcludeUIDs(…)` — always exclude the seed from a similarity list, and the record the
  seed is about. See `curiosity-similarity-scenarios` §4.
- `.Renderer(r => r.WithSelection().CustomizeResult(…))` — multi-select plus per-row badges and
  commands. Register a command with a **unique id** so a twenty-row selection offers one
  command, not twenty identical ones.
- `.TriggerSearch()` — the way to repaint after a decision changes, because a row's badge is
  read while the row is being built.
- A single node rendered as its own card is just (b) with one UID and a fixed height — which
  keeps that type's icon, label, footer and commands instead of re-implementing them.

---

## 5. The list page

One page, several segments, distinguished by a query parameter — see
`curiosity-frontend-setup` §5. `inbox`, `urgent`, `mine`, `unassigned`, `all` was the right
set for the reference implementation and is a good default.

Scope `mine` and `all` with a query; scope the rest with the **UID arrays the summary
endpoint counted**.

---

## 6. Checklist

- [ ] Home view replaced; the page renders as the selected viewer
- [ ] Words before digits: a notice strip that disappears when nothing is waiting
- [ ] One summary fetch per user, awaited by the greeting, the banner and every tile
- [ ] Every counter opens the list it counted, via the shared segment description
- [ ] Segments described once and read by cards, sidebar and pivot
- [ ] Series drawn as a sparkline, shares drawn as a contribution bar — not as grids
- [ ] Every metric computed live from the graph; nothing hard-coded
- [ ] Every list is a `SearchArea`; counted lists use the endpoint's own UID array
- [ ] Multi-select commands registered with a unique id and batched

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`DashboardView.cs`](examples/DashboardView.cs) | Notice strip, counter tiles that open their list, and the three performance numbers each drawn as what it is |
| [`WorkLists.cs`](examples/WorkLists.cs) | One segment description read by cards, sidebar and pivot; query-scoped vs UID-scoped lists |
| [`Viewer.cs`](examples/Viewer.cs) | "Who am I rendering as", and the searchable-list picker |
