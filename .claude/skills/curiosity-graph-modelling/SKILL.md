---
name: curiosity-graph-modelling
description: How to decide, field by field, whether something becomes a node property, a node plus an edge, an NER-linked entity, an index, or nothing at all — and how to model identity, revisions, child records and shared vocabularies in a Curiosity graph. Use when designing the schema, when adding a data type, or whenever a field is not doing what the UI or the retrieval needs.
---

# Graph modelling — property, node, edge, entity, index

This is the central design skill. Everything downstream — what a user can filter by, what
a similarity signal can key on, what the assistant can traverse, what a renderer can link —
is decided here, and it is expensive to change afterwards.

## The one-line rule

> **A string can be searched and it can be faceted within its own type. It cannot be
> traversed, and it cannot be shared across types.**

Two consequences drive most of this document:

- **Property facets exist**, on string, enum and time fields, and they are the right tool for
  a dimension that only ever narrows **one** node type. But a property facet can only filter
  that one type: `Modification.Status` and `ServiceBulletin.Status` are two separate facets
  over two separate lists. A **graph facet** — the node on the other end of an edge — filters
  every type that links to it at once, which is what you want for any dimension more than one
  type carries. In a mixed corpus that is nearly all of them.
- The "shares a neighbour with the seed" similarity filter — the mechanism that turns graph
  structure into retrieval signal — only sees **neighbours**. A record whose category lives
  only in a property is invisible to a same-category signal, whatever facets exist over it.

So: **a dimension carried by one type alone can stay a property facet; anything carried by
more than one type, or keyed on by a signal, gets a node and an edge — and keeps the
property.** The property is what renders in a details grid without a traversal, what the text
index reads, and what the UI falls back to when an edge is absent.

---

## 1. The decision table

For every field of every incoming record, pick one:

| Choice | When | Cost | What it buys |
| --- | --- | --- | --- |
| **Property only** | A measurement, a free-text paragraph, a one-off value nobody groups by: `WeightKg`, `Manhours`, `DescriptionText` | none | renders, indexes, sorts |
| **Property + property facet** | A bounded vocabulary, a date, or an enum that **only one node type carries** and nobody joins on | an index setting | narrowing a list of that one type |
| **Property + node + edge** | A value from a bounded vocabulary carried by **more than one type**, or an identifier of something that exists in its own right: status, category, department, keyword, chapter, part number | a node type, an edge pair, ingest time | cross-type facets, traversal, similarity signals, a page per value |
| **NER-linked** (spotter + linking) | The value appears **inside prose**, and there is a master list of valid values | index build time, background linking | `_Mentions` / `_AppearsIn` edges the connector could not have written |
| **Index only** | Long free text that must be searched or embedded | index build + storage | full-text search, embeddings, similarity |
| **Nothing** | Internal plumbing, source-system bookkeeping, values nobody will ever ask about | — | — |

These combine. The typical interesting field is **property + node + edge + index**, and the
typical interesting *text* field is **property + index + NER linking**.

### The five questions that decide it

Ask them in order; the first "yes" that fires settles the choice.

1. **Would a user narrow a list by this — across more than one node type?** → node + edge.
   (Only ever within one type, and never joined on? A property facet is enough.)
2. **Would "show me everything else with this value" be a useful page?** → node + edge.
3. **Do two records being the same on this field make them related?** → node + edge (it is a
   similarity signal, and signals need neighbours).
4. **Does this value appear inside other records' prose?** → also a spotter + linking rule.
5. **Would someone search for words in it?** → index it.

If none fire, it is a property, and that is fine. Most fields are properties.

---

## 2. Multi-valued fields are the clearest case of all

A list flattened into `string.Join(", ", …)` is searchable and **completely inert**. Nothing
can ask "which other records carry this one value". Every multi-valued field that names
things is a node type plus one edge per value:

- a record's keywords → `Keyword` nodes;
- the systems a document covers → `AircraftSystem` nodes;
- the fault codes a procedure diagnoses → `FaultCode` nodes;
- the harnesses a wiring sheet routes → `Harness` nodes.

In the reference implementation these were the highest-value promotions of the whole
project, because they connect **across types that share no vocabulary**: a system name is
the only path from a flight-crew limitation to the wiring of the system it limits.

The same reasoning applies to a **single**-valued field that names something already
modelled: if `NextHigherAssembly` holds a part number and `PartNumber` is a node type, link
it — do not leave it as text beside a graph that already contains the answer.

---

## 3. Shared vocabularies — one node type, keyed by value, differentiated by edge

Several types will have a field meaning "what state is this in": a document's `Status`, an
order's `OrderStatus`, a derived `WorkflowStatus`, an item's `Lifecycle`. They overlap —
`CANCELLED`, `ON_HOLD`, `IN_PROGRESS`, `REJECTED` occur in several.

Model them as **one `Status` node type keyed by the value alone**, and carry *which field the
link came from* on the **edge** (`HasDocumentStatus`, `HasOrderStatus`, `HasWorkflowStatus`).

- One `ON_HOLD` node that everything on hold hangs off — "everything on hold" is one node,
  not a query per type.
- "This record's own status" is still one traversal with one answer, because the edge names
  the field.

Two rules make it safe:

- **Canonicalise the key — and keep it readable.** A shared node is only shared if every
  producer spells the state the same way, so run every value through one canonicaliser. A
  derived vocabulary spelled `InProgress` while the authored one says `IN_PROGRESS` silently
  splits every state in two.

  But **the key is what a user sees**: it is the facet label, the chip on a card, the title of
  the node's own page. So prefer a canonical form that is already user-friendly — *"In
  progress"*, *"On hold"* — rather than a machine shape. The reference implementation
  canonicalised to `SCREAMING_SNAKE`, which is correct and ugly, and the ugliness is on screen
  everywhere.

  Where the source vocabulary genuinely is machine-shaped and you want it preserved, the
  alternative is a **`[Property]` holding the display value** beside the key, plus a renderer
  whose `LabelField` names that property — so the graph joins on the canonical key and every
  surface reads the friendly one. Decide which of the two you are doing **before** ingesting;
  changing the key later re-mints every node and orphans every edge.
- **Keep the field sets disjoint, and check it at startup.** If a name appears in two
  different vocabularies' lists, one edge would return two different node types.

**Where to draw the line between `Status` and a general `Classification`:** a value belongs
in the status family when it is *a state the thing is in and can move out of*. `Priority`,
`Severity`, `RiskLevel`, `Compliance` are **judgements about** the thing, not where it
stands — they go in the classification family.

**One `Classification` node type, keyed `{Kind}:{Value}`, but one edge pair *per* vocabulary.**
Sharing the node type is right — they are all "how this record is classified", each with a
handful of values. Sharing a single `HasClassification` edge is wrong: traversing it returns
every vocabulary at once, so a renderer has no way to ask for *this record's risk level*.
Derive the pair from the kind (`Has{Kind}` / `{Kind}Of`), look it up in the edge catalogue,
and **throw at startup if it is missing** — otherwise you write thousands of links over an
edge type the schema never registered.

**A node type with only a key is still worth having.** A node holding nothing but a change
number is where the whole change gathers: the record that defines it and every analysis of it
both point at it. As an integer on each record it was a fact you could read and not follow.

---

## 4. Hierarchies — link every level, not just the leaf

If a classification is hierarchical (chapter → section → subject; category → subcategory),
link the record to **every level**, not only the deepest one, even though the levels are
chained to each other.

A parent chain makes the top level *reachable* from the leaf, but a three-hop walk is not a
common neighbour — so a "same top-level category" similarity signal finds nothing. In the
reference implementation this is `InAtaChapter` (6-digit) + `InAta4d` + `InAta2d`, all three
written for every document, in addition to the `ChildOfAta` chain between the chapters.

---

## 5. What the connector must NOT link

**Create an edge only where the source states the relationship in a structured field.**

| Do link | Do not link |
| --- | --- |
| `entities.part_numbers[]` → the part | a part number that only appears in prose |
| `references[].id` → that record | two records that merely read alike |
| `applicability.ids[]` → those items | an item inferred from a range at query time |
| a ground-truth pointer, clearly marked as such | the relationship the search is meant to rediscover |

This is not tidiness. If the application's claim is "we surface connections nobody
catalogued", and the connector wrote an edge for that same connection out of the source
data, the demo proves nothing — the app is reading back its own input.

NLP-derived relationships arrive as a **separate, additive layer** (`_Mentions` /
`_AppearsIn`, from the workspace pipeline — see `curiosity-entity-linking-nlp`). The
connector never writes those, and the front end must never filter them out of a 360-degree
view: the user should not have to know which mechanism found a connection.

**Ground truth is the one apparent exception, and it is not one.** If the corpus carries
"the answers" for evaluation, model them as their own node type on their own edge
(`HasFinding` → `FindingTargets`), clearly *not* the edge the application writes when a user
confirms something. A user's confirmed decision is a distinct edge created only by the write
endpoint — which is exactly what makes it meaningful to compare against the ground truth.

---

## 6. Revisions — decide before you write the `[Node]` class

If a record has versions, you have three options, and picking late is expensive.

| Option | When | Shape |
| --- | --- | --- |
| **Current state only** | history is not part of the job | one node per record, keyed by its identifier |
| **Current state + revision nodes** | users compare revisions, or you must explain *why* something changed | one node per record **plus** a parallel node type per record type, keyed `{Id}:{Revision}` |
| **Revisions as the only nodes** | the record has no meaningful "current" | one node per revision, chained |

The reference implementation uses the middle option and would again. What it learned:

- **A parallel node type per record type, not one shared `Revision` type.** Curiosity reads
  the schema off the concrete node class, so a shared base is invisible to it, and a single
  type would carry the union of every type's text fields.
- **The revision node holds the envelope and the prose; the record node keeps the
  measurements.** What differs between revisions, and what a reader comparing two of them
  looks at, is the text. Everything else reasons about the current state.
- **Chain them** (`SupersedesIssue`), and mark the current one with a boolean.
- **Link the revision to the reason it was raised** where the corpus knows it. That edge —
  from a revision back to the finding that forced it — is what makes a claimed impact
  falsifiable rather than an assertion.
- **Everything else links to the record**, not to a revision. Only the comparison screen and
  the history walk care about revisions.

Budget: revisions roughly double the node types, the renderers, the consult readers and the
index count.

---

## 7. Child records — mint a node when it is individually interesting

Source records nest things: line items, sub-figures, findings, revisions, attachments. Mint a
child node when the child is **individually linkable, searchable or decidable**:

- it carries its own effectivity/condition — the signal a parent cannot express;
- other records reference it by its own identifier;
- a user confirms, rejects or comments on it one at a time.

Key it from its parent (`{parentId}#{childId}`) so it is reconstructible, and link both ways
(`HasItem` / `ItemOf`). Otherwise leave it as properties on the parent.

**The input schema does not have to match the node schema.** Splitting one source record into
a parent plus children — or merging several source files into one node — is normal and often
right. Map for the graph you want to query, not for the file you were given.

---

## 8. Identity

- **Every node type has one stable `[Key]`.** It must be derivable from the source without a
  lookup, because links are made by key rather than by fetching the target.
- **Keys are unique per node type**, so a key only has to be unique within its type.
- Where a natural identifier is not unique across types (an identifier that does not say what
  it is), qualify it: `{Type}:{Id}`, or `{Type}:{Id}:{Revision}` where each revision was
  published in its own right.
- **Never key on something that changes.** A key change is a new node and an orphaned old one.
- Where the *source* has no identifier, derive one deterministically from stable content —
  never from a counter, a timestamp or a hash of something that includes ingestion time.

---

## 9. Users are graph citizens

The user node type is owned by the graph, so you cannot add properties to it. Everything you
want to know about a user is therefore an **edge to the same nodes your records are
classified by**: their site, their department, their role (as a classification), their
specialities (as the same category nodes documents carry).

That is what puts a person one hop from the work of their own office, and it is what lets a
screen render a user's profile from UIDs — each one opening the page for that site, that
department, that speciality — instead of from strings an endpoint had to spell out.

**Ownership is an edge and only an edge.** Do not also store an owner's name as a property:
it is a second copy of the same fact and the two drift the moment either is rewritten. Every
"whose is this?" question is a traversal, which is what guarantees that a count and the list
behind it agree.

---

## 10. Files are nodes too

Where a record has a published artefact — a PDF, a drawing, a scan — upload it and hang it
off the record using the workspace's **own** attachment edge names (the underscore-prefixed
pair). The product already renders that edge as the attachment list and already has a viewer
for the file; anything else gives you edges nothing in the product knows how to show.

Two further rules learned here:

- **Markup is a property, artwork is a file.** Source markup you might want to read or search
  belongs on a node as a property; an image belongs in the attachment list.
- **Attach per revision.** Each revision was published in its own right. Hang each revision's
  file off that revision, and hang the current one off the record as well — then one walk
  gives a revision the file of that revision and a record the file of its latest, without
  either renderer knowing which it is looking at.

---

## 11. Checklist for a new data type

- [ ] `[Key]` is stable, derivable, unique within the type
- [ ] One timestamp field chosen and marked
- [ ] Every controlled-vocabulary field: node + edge, canonicalised key, disjoint field sets
- [ ] Every multi-valued field: node type + one edge per value, never a joined string
- [ ] Hierarchical classifications linked at **every** level
- [ ] Every field naming an already-modelled thing: linked, not just stored
- [ ] Free-text fields identified for indexing and for embedding
- [ ] Identifiers appearing in prose: master list checked, spotter planned
- [ ] Revisions: decided, and the shape written down
- [ ] Child records: minted only where individually interesting
- [ ] No edge written for a relationship the application is supposed to discover
- [ ] Every edge declared in both directions, in one catalogue
- [ ] A renderer exists for the type — including key-only metadata types
