# Proposal — reorganising the PAWO view around the job it exists for

**Status:** part-implemented — see §5 for what has landed since this was written.
**Scope:** `src/Aeralis.FrontEnd/src/Renderers/PreAnalysisWorkOrderRenderer.cs`, and the
endpoints in `src/Aeralis.Endpoints/code/endpoints/aeralis/` it would need.

---

## 1. What the screen is for

From [`README.md`](../README.md) §1.2: pre-analysis is a **search-and-impact problem, not
a design problem**. An engineer holds one modification and has to answer, against the
whole corpus:

> what already exists that this MOD will conflict with, depend on, supersede, or invalidate?

and then hand over three things: a **list of confirmed impacts**, a **written
justification**, and a **recommendation** (launch / freeze / reject).

So the work is a loop, run dozens of times per work order:

```
read the change  →  see a candidate  →  judge it against the change  →  confirm or dismiss
      ▲                                                                        │
      └────────────────────────  until nothing new comes back  ◀───────────────┘
                                          │
                                          ▼
                        write it up  →  answer the rules  →  recommend
```

Everything below follows from one observation: **the screen currently cuts that loop
across tab boundaries**, so the engineer runs it by navigating instead of by reading.

---

## 2. What is wrong today

Today there are seven tabs: Consolidated Summary, Analysis, History & Sources,
Rules & IPC, Issue Comparison, 360 View, Graph.

### 2.1 The loop is split across two tabs

`Analysis` is where candidates are found and confirmed. `Consolidated Summary` is where
confirmed impacts are listed, and where the coverage matrix and the "chapters you have
not looked at" card live. Those are the *same action* and its *feedback*: confirming
something in tab 2 changes three cards in tab 1 that the engineer cannot see while doing
it. The one signal that tells you when to stop — is anything still suggested that is not
yet covered — is behind the tab you are not on.

### 2.2 The thing being analysed is at the bottom of a column

`RenderModificationDossier` sits third in the right-hand column of the summary tab, under
the impact matrix and the suggested-ATA card, in a scrolling stack. But *every* judgement
on this screen is "does this candidate touch **this** change" — the dossier is the
reference the whole session is read against, and it is the thing you have to scroll to.

### 2.3 The same entities are rendered in two tabs

Part numbers, FINs and drawings are `NeighborsLinks` cards in the right column of
`History & Sources` **and** part numbers, zones and FINs are a card in the left column of
`Rules & IPC`. Two tabs, overlapping content, neither complete.

### 2.4 `Rules & IPC` is two unrelated things in one tab

Engineering rules are a **compliance checklist producing an output** (which rules were
proposed, with what justification). IPC figures, parts and zones are **entity context you
read while judging a candidate**. They share a tab because they had nowhere else to go.

### 2.5 `Issue Comparison` is a stub

`RenderIssueComparison` builds a dropdown of hard-coded `Revision A / B / C` and a
`TextBlock("Select a previous issue to compare")` that nothing ever changes. The dataset
*does* carry real issue history — every document file is an array of issues, oldest first
(`CLAUDE.md` §0.4) and the connector stores superseded issues — so this tab is a promise
the data can already keep, wired to nothing.

### 2.6 The decision is not on the screen

`Recommendation` is a node property. It appears in the card footer and in the Overview
property grid. The one output the whole work order exists to produce has no control
anywhere in the view — you can write the report, but you cannot say what you concluded,
and there is no "submit for review".

### 2.7 Nothing says how far along you are

There is a count of confirmed impacts (a `TextBlock` above each similarity list) and a
`ContextCard` when suggested chapters are uncovered. Neither answers "am I done": chapters
covered vs suggested, rules answered vs applicable, and whether the report has been
written are the three things a reviewer would check, and none of them is stated.

---

## 3. Proposed structure

**Three working tabs, two exploration tabs, and a header that is always there.**

| | today | proposed |
| --- | --- | --- |
| 1 | Consolidated Summary | **Analyse** — read, find, confirm, all on one screen |
| 2 | Analysis | **Report & sign-off** — the deliverable, and the decision |
| 3 | History & Sources | **Context** — provenance, entities, issue history |
| 4 | Rules & IPC | *(rules move into 2, entities into 3)* |
| 5 | Issue Comparison | *(moves into 3, wired to real issues)* |
| 6 | 360 View | **360 View** — unchanged |
| 7 | Graph | **Graph** — unchanged |

### 3.0 A persistent header, above the tabs

One strip that never scrolls away, because it is the identity of the thing and the state
of the work:

- the MOD under analysis, as a link, with its ATA and programme — a `DetailsGrid` in the
  new **stacked** mode (`DetailsGrid(columns: 4).Stacked()`), which is exactly the
  label-over-value record header it was added for;
- a **`Banner`** when something is wrong or waiting: urgent and overdue (`.Danger()`),
  suggested chapters not covered (`.Warning()`, with `Action("Show them")` selecting them
  in the basket), rules unanswered (`.Primary()`). One strip, one tone, dismissible —
  the same component the landing page now uses for urgent actions;
- three progress numbers as `Metric` tiles with icon tiles and a `ContributionBar`:
  **impacts confirmed**, **chapters covered / suggested**, **rules answered / applicable**.

That last row is the "am I done" answer from §2.7, and it is the natural home for the
component work that just landed in Tesserae.

### 3.1 Tab 1 — **Analyse**: the workbench

Three columns, left to right, in the order the loop runs. This replaces both
`RenderSummary` and `RenderAnalysis`.

```
┌── the change ──────────┬── candidates ─────────────────┬── the basket ───────────┐
│                        │  [ affected | previous | by   │  Confirmed impacts      │
│  MOD dossier PDF       │    entity ]  facets, search   │   grouped by ATA / type │
│  (the real viewer,     │                               │                         │
│   searchable)          │  ▸ candidate row              │  Coverage strip         │
│                        │      score breakdown          │   ATA × doc type        │
│  ── key facts ──       │      [Confirm] [Dismiss]      │                         │
│  reason for change     │  ▸ candidate row              │  Suggested, not covered │
│  effects, class        │  ▸ …                          │   → banner + chips      │
└────────────────────────┴───────────────────────────────┴─────────────────────────┘
```

- **Left** is the dossier, permanently. This is the change in §2.2, moved to where it is
  read from. *(This half is already done — see §5.)*
- **Centre** is where the time goes: the two existing similarity lists become segments of
  one list rather than two `SearchArea`s side by side, plus a third segment — **by
  entity** — for the exact-match half of the retrieval story the README describes (shared
  PN, FIN, drawing, zone, software). Today the demo shows the semantic half in a list and
  leaves the entity half to the 360 view; both are the pitch.
- **Right** is the basket: what has been confirmed, what it adds up to, and what is still
  missing. Confirming in the centre lands here immediately — same screen, no navigation.
  The impact matrix stops being a table of counts and becomes a coverage strip that can be
  clicked to filter the basket.

Everything in the right column exists today; it is one column over and one tab closer.

### 3.2 Tab 2 — **Report & sign-off**: the deliverable

Read top to bottom, and it is what a reviewer opens on its own:

1. **Applicability** — programme / type / model / MSNs (today's card, unchanged).
2. **Confirmed impacts, as they will be published** — grouped, ordered, exportable.
3. **Rules** — moved out of `Rules & IPC`. Each applicable rule with its `Propose` button
   and a justification field. Unanswered rules count in the header banner.
4. **Report & justification** — the `TextArea` and `Draft with AI`, unchanged, except the
   draft now has the confirmed impacts *and* the rules to draw on.
5. **Recommendation and submit** — a `ChoiceGroup` of Launch / Freeze / Reject with a
   mandatory rationale, and the button that moves `WorkflowStatus`. This is §2.6, and it
   needs one new endpoint (`aeralis/pawo/submit`).

### 3.3 Tab 3 — **Context**: what surrounds the change

Absorbs `History & Sources`, the entity half of `Rules & IPC` (§2.3, §2.4) and
`Issue Comparison` (§2.5):

- **Provenance** — the existing `Plan` lifecycle, IO → MOD → PAWO → WO → SB, each step
  linking to its node.
- **Entity inventory** — *one* place for part numbers, FINs, drawings, zones, software,
  and the manual content that references them, each row carrying its where-used count so
  a shared part is visible without opening it. One card per entity kind, no duplication.
- **Issue history** — the MOD's real issues from the graph, oldest to newest, with a
  side-by-side diff of the selected issue against the current one. Replaces the
  `Revision A / B / C` stub with data the dataset already has.

### 3.4 Tabs 4 and 5 — 360 View and Graph

Unchanged. They are exploration, deliberately open-ended, and correctly centred on the
subject modification rather than on the work order.

---

## 4. What this needs that does not exist yet

| # | What | Where |
| --- | --- | --- |
| 1 | `aeralis/pawo/progress` — confirmed / covered / answered, for the header tiles | new endpoint |
| 2 | `aeralis/pawo/submit` — set recommendation + workflow status | new endpoint |
| 3 | `aeralis/similarity/by-entity` — candidates sharing a PN / FIN / drawing / zone | new endpoint |
| 4 | ~~`aeralis/mod/issues` — the MOD's issue history, for the comparison~~ | **done** |
| 5 | ~~Dismiss alongside Confirm~~ | **done** — `pawo/set-impact-decision` + `pawo/impact-decisions` |
| 6 | Rule justification text stored on the `AppliesRule` edge | extends `rules/add-to-pawo` |

Items 1, 2 and 5 are the ones that change what the demo *shows*; 3 and 4 are the ones that
make the retrieval story and the issue story complete. None of them needs a schema change
to the dataset.

---

## 5. What has landed so far

Not the reorganisation itself — the tabs are still the seven of §2 — but several of the
findings above are fixed inside them.

**The dossier is the real PDF (§2.2).** `RenderModificationDossier` embeds the workspace's
own `_FileEntry` view — scrollable, searchable, zoomable, with its outline — by asking the
registered file renderer for the body it would put in a preview sheet:

```csharp
var row = await new _FileEntryRenderer(showHeader: false, showFooter: false, enableHotkeys: false)
    .PreviewAsync(file, null);

return row is object ? await row.GetModalContentAsync() : null;
```

It is also now the *whole* right-hand column of the Overview tab rather than the third card
down it, which is most of what §2.2 asked for. The same helper renders the issue comparison's
two PDFs.

**Confirm and reject, over a selection (§3.1 centre).** Both candidate lists are
multi-select and every row carries `Confirm impact` / `Reject impact` / `Clear decision`,
batched into one request. `RejectedImpact` is a new edge: a rejection was previously the
*absence* of a confirmation, which is indistinguishable from a candidate nobody had looked
at — so the sweep could not be paused. Rows now badge which answer they got.

**The coverage table is one table (§3.1 right).** The impact matrix and the suggested-ATA
card were merged into one row-per-chapter table, with the suggested-but-uncovered chapters
sorted to the top and badged. The banner counting them is gone: they are at the top of the
table, which is where an unanswered thing belongs.

**The entity duplication is halved (§2.3, §2.4).** `Rules & IPC` is now rules on the left
and one faceted search area — *Related IPC* — on the right, covering IPC figures, items,
part numbers, zones and FINs. The chip cards that overlapped are gone. `History & Sources`
still lists part numbers, FINs and drawings separately; merging those is §3.3 and has not
been done.

**The issue comparison works (§2.5).** Both sides are real documents: the node's own card
over its whole published PDF, with the picker filled from the modification's actual issue
history and defaulting to the newest previous issue. It needed an issue renderer to exist
at all — without one the node had no label field and drew as a raw UID. All ten issue types
have one now, so any `HasIssue` traversal anywhere reads as documents rather than UIDs.

**Applicability renders (§3.2 in passing).** It was two `NeighborsLinks` calls each asking
for several node types at once over no particular edge — a form that sends the types to the
server as one `";"`-joined string, and that gave a card with no labels to say which
dimension was empty. It is one labelled row per dimension now, over the edge that actually
carries it (`InProgram`, `AppliesToType`, `AppliesToModel`, `AppliesToMsn`), each showing
"—" when genuinely empty. Most work orders have no MSN list, so that row is empty by
design and has to read as such.

**The written report is gone from the Overview.** §3.2 puts it in the sign-off tab; until
that tab exists it is simply not on the overview, where it pushed the coverage table and
the confirmed impacts off the top of a scrolling column.

### Still outstanding

Everything structural: the persistent header (§3.0), merging Overview and Analysis into one
workbench (§3.1), the sign-off tab with the recommendation (§3.2, §2.6), the Context tab
(§3.3), the progress numbers (§2.7), and the by-entity candidate segment. Endpoints 1, 2
and 3 of §4 are still unwritten; 4, 5 and 6 have landed.

## 6. Suggested order of work

1. The persistent header — identity, banners, three progress tiles. Visible immediately,
   needs only endpoint (1).
2. Merge Summary + Analysis into **Analyse**. The single biggest change, and the one that
   fixes §2.1. No new endpoints for the first two segments.
3. **Report & sign-off** — move the rules across, add recommendation + submit. Endpoint (2).
4. **Context** — merge the three tabs, deduplicate the entity cards. Endpoint (4) for the
   issue comparison.
5. The by-entity candidate segment. Endpoint (3).

Steps 1–2 are worth doing on their own: they are where the demo is watched.
