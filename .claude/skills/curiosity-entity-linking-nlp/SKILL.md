---
name: curiosity-entity-linking-nlp
description: How entity recognition and linking work in a Curiosity workspace — spotters built from node fields, index-linking rules that write _Mentions/_AppearsIn edges, NLP pipelines, and which fields deserve which. Use when deciding what should be found inside free text, when mentions are not appearing, or when configuring the NLP layer of a new use case.
---

# Entity linking — turning prose into edges

The connector writes the relationships the **source states**. This layer writes the
relationships the **text implies**. Between them they are the whole retrieval story, and the
demo only lands if the second layer finds things the first could not.

Everything here lives in the workspace definitions (`nlp/spotters/`, `nlp/linking/`,
`nlp/pipelines/`) and is version-controlled with the rest of the workspace — see
`curiosity-workspace-configuration`.

---

## 1. The three pieces

```
        ┌─────────────────────────────────────────┐
        │ SPOTTER                                 │  "these strings exist"
        │ built from a node type + field:         │  every PartNumber.Pn value
        │ every value of that field becomes a     │  becomes findable in text
        │ term the tokenizer can spot             │
        └────────────────┬────────────────────────┘
                         │ referenced by
        ┌────────────────▼────────────────────────┐
        │ PIPELINE                                │  "run these spotters"
        │ an ordered set of models/spotters, per  │
        │ language, that text is parsed through   │
        └────────────────┬────────────────────────┘
                         │ consumed by
        ┌────────────────▼────────────────────────┐
        │ LINKING RULE                            │  "when spotted in THIS
        │ (entity type, entity node type,         │   field of THIS node type,
        │  data node type, data field,            │   write THESE edges"
        │  edge to entity, edge to node)          │
        └─────────────────────────────────────────┘
                         │
                         ▼
                _Mentions  /  _AppearsIn
```

**A spotter alone does nothing visible.** It makes a term recognisable; the linking rule is
what turns a recognised term into an edge. Both must exist, and the field must be indexed.

---

## 2. Spotters — one per (node type, field)

A spotter built from a node field takes every value of that field across the graph and makes
it spottable in text. Configuration that matters:

| Setting | What it decides | How to choose |
| --- | --- | --- |
| `NodeType` / `FieldName` | which values become terms | the field holding the identifier, not the title |
| `IgnoreCase` | case-insensitive matching | `true` for names, usually `true` for identifiers too |
| `IgnoreNumbers` | whether a purely numeric token can match | **`true` for identifiers that are alphanumeric-with-structure; `false` for identifiers made of digits and separators** |
| `MinimumLength` | shortest string that may match | raise it until short values stop producing noise — 5 is a sane floor |
| `IncludeAlias` | also spot known aliases of the node | `true` |
| `Exceptions` | values never to spot | the false-friend list; grows in tuning |

Two rules learned the hard way:

- **Only build a spotter where there is a master list.** The whole value of this mechanism is
  that a spotted term resolves to *a node that exists*. Without a catalogue, you are matching
  a pattern and inventing entities.
- **Never let the spotter create nodes.** Linking rules take an `AddIfNotFound` flag; keep it
  `false`. A mention that resolves to nothing should stay plain text. Turning it on fills the
  graph with typo-nodes that then poison facets and similarity.

Choose spotters from the discovery brief's "identifiers that appear inside text" list. In the
reference implementation those are: part numbers, functional item numbers, drawing numbers,
harness references, access panels, kit numbers, repair-drawing references, and **the document
identifier of every record type** — that last group is what makes "this bulletin names that
maintenance task" a link rather than a coincidence.

---

## 3. Linking rules — the field-level decision

One rule per **(entity type × data node type × data field)**. That combinatorial shape is the
point: you are choosing, per text field, which vocabularies are worth resolving in it.

```json
{
  "EntityType":      "PartNumber.Pn",
  "EntityNodeType":  "PartNumber",
  "EdgeTypeToEntity":"_Mentions",
  "EdgeTypeToNode":  "_AppearsIn",
  "DataNodeType":    "Modification",
  "DataNodeField":   "Abstract",
  "IgnoreCase":      false,
  "AddIfNotFound":   false
}
```

Guidance:

- **Every prose field of every record type gets the full set of rules**, unless you have a
  reason not to. Under-linking is the common failure: an impact that lives in the effects
  paragraph and nowhere else simply never surfaces. The reference implementation runs the
  same ~18 entity types against every text field of every record type and revision type —
  several hundred rules, generated rather than hand-written.
- **Include the record-identifier spotters of every other record type.** Cross-references
  written in prose are the highest-signal mentions in the corpus.
- **`IgnoreCase: false` for structured identifiers**, `true` for names, organisations,
  abbreviations. A case-insensitive identifier match produces false positives against ordinary
  words.
- **Do not link the field the entity was built from to itself.** A part number's own
  `Pn` field mentioning part numbers is noise.
- **Abbreviations and contacts/organisations come for free** — the built-in entity types are
  worth adding to prose fields; they cost nothing and make an answer's vocabulary navigable.
- The **file-content** node type deserves rules too where documents are attached: that is how a
  mention inside the published PDF becomes an edge.

---

## 4. Pipelines

A pipeline is an ordered set of models and spotters, per language, that text is parsed
through. In practice you have one data-parsing pipeline per language, and you add each new
spotter to it — a spotter that is not in a pipeline never runs.

The pipeline UID is also useful in the front end: a chat view can parse assistant replies
against it so that identifiers in an answer become links to the records they name (see
`curiosity-chat-experience`).

**Checklist when a mention is not appearing** — walk it in this order:

1. Is the **field indexed** at all?
2. Does a **spotter** exist for that (node type, field)?
3. Is the spotter **in the pipeline**?
4. Is there a **linking rule** for this (entity, data node type, data field) triple?
5. Does `MinimumLength` / `IgnoreNumbers` exclude this particular value?
6. Have the **nodes been ingested** — a spotter built before the catalogue exists has nothing
   in it, and needs rebuilding.
7. Has the **background linking finished**? It is asynchronous and lags a large ingest.

---

## 5. Text indexes — what is searchable and embeddable

An index is declared per (node type, field). The settings that matter in practice:

| Setting | Effect |
| --- | --- |
| `Searchable` | the field participates in full-text search |
| `SearchBoostFactor` | relative weight — raise for titles and identifiers, leave at 1 for body prose |
| `ProducesFacets` | the field becomes a **property facet** — string, enum or time. It filters *this node type only*, so it is right for a dimension one type carries alone; for anything more than one type carries, prefer a **graph facet** (node + edge), which filters them all at once. See `curiosity-graph-modelling` §1 |
| `IndexLemma`, `IndexAliases`, `IndexStopWords` | linguistic normalisation |
| `Fuzziness` | typo tolerance; leave at the default for identifiers |
| `Language` | `Any` for a mixed or identifier-heavy corpus |
| `Mode` | when indexing happens relative to node availability |

Rules:

- **Index each prose section as its own field**, not concatenated. That is why the connector
  flattens source sections into one property each: it lets you boost a title over a body, and
  it lets a similarity signal read a specific section.
- **Index the identifier fields**, with a boost. Users search by identifier constantly.
- **Do not index bookkeeping.** Every index costs build time and memory.
- **Index the revision types too** if users search history — and accept that it roughly
  doubles the index count.

---

## 6. What this layer is for — and how to keep it honest

The application's claim is that it finds connections nobody catalogued. That claim is only
testable if:

- the corpus contains pairs that are **related and share no vocabulary**, discoverable only
  by a shared entity;
- and pairs that **share no entity at all**, discoverable only by text similarity;
- and the ground truth records **which mechanism** was supposed to find each one.

If your corpus is synthetic, plant both kinds deliberately and label them. If it is real, get
the customer to label ten cases during discovery (`curiosity-use-case-discovery` round 6).
Without that labelling you cannot tell a working entity layer from a coincidence.

---

## 7. Checklist

- [ ] Every identifier from the discovery brief that appears in prose has a spotter
- [ ] Every spotter is backed by a real master list; `AddIfNotFound` is `false` everywhere
- [ ] `IgnoreNumbers` / `MinimumLength` / `IgnoreCase` set per identifier shape, not copied
- [ ] Every spotter is registered in the pipeline
- [ ] Linking rules cover every prose field of every record type — generated, not hand-listed
- [ ] Record-identifier spotters of every type are linked into every other type's prose
- [ ] Each prose section is indexed as its own field, with sensible boosts
- [ ] The ground-truth cases name their discovery mechanism, and both mechanisms are exercised
- [ ] The front end shows NLP edges alongside structured ones and never filters them out
