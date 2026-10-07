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

## 7. Verified wiring (mosaik main, October 2026)

Checked against a live server while building the aviation news desk. The files are what the
definitions import reads; export after importing and commit what the exporter writes.

**The chain for one text field** - all five must exist, or nothing is linked:

```
config/indexes/others/index-parser.<Type>.<UID>.json   FieldToDocumentIndex: parses the field
config/pipeline-mapping.json                           Type 0 entry: (<Type>, <Field>) → pipeline UID
nlp/pipelines/<Name>.<UID>.json                        base models + your spotters
nlp/spotters/<Type>.<Field>.<UID>.json                 _NlpSpotterFromNode
nlp/linking/index-linking.<Type>.<Field>.json          array of mappings; the importer creates the
                                                       DocumentToGraphIndex if it is missing
```

- **Parser index settings** are `DenormalizeHtmlBeforeParsing`, `FieldName`, `MaximumLength`,
  `NodeType`. An older export also carries `MaximumBatchSize`; today's import rejects the file
  with "Setting MaximumBatchSize not found in SettingsHolder".
- **`pipeline-mapping.json` replaces the whole mapping**, search and file pipelines included
  (`Type` 0 = node type and field, 1 = search, 2 = files). The workspace's default pipelines have
  UIDs that differ per workspace, so commit them with the mapping that names them - otherwise a
  fresh workspace's search parsing points at pipelines that do not exist.
- **The exporter has the last word on the format.** It assigns a parser index its own
  `UniqueExportID` (a hand-minted one is not kept), orders linking rules by entity type, and
  writes a node-type mapping's `Language` as `"ab"` (the field is unused for `Type` 0). Import
  your hand-written files once, export, and commit the export.
- **Base models** `B8rsc5Pqjad4teno7TBVCV` and `8tpTBbfHyygTUYgS9RPzri` (the language-agnostic
  data-parsing pipeline's) are built-ins with the same UIDs everywhere; start your own pipeline
  from them and append your spotters. A pipeline of your own, mapped to your fields, leaves the
  workspace's defaults untouched.
- **The importer adds the built-in `_Abbreviation` linking** to an index it creates, and then
  warns that your file does not list it. Keep it (it is free vocabulary) and let the export write
  it back.
- **A spotter's `IgnoreCase` applies to all of its terms, aliases included.** An alias's own
  `ignoreCase` (in `Graph.AddAlias`) only identifies the alias node. An alias in `Language.Any`
  is used by a spotter of any language.
- **Spotting on the key, with `IncludeAlias`, makes the aliases the term list.** Build the
  spotter from a key that never occurs in prose (a Wikidata id, an ICAO code) and write every
  term you want found as an alias. Then a common-word name ("Emirates") stays the node's name
  without becoming a term, and removing a term is `RemoveAlias` - keep a ledger of what you wrote.
- **Scripts get `E._Mentions` / `E._AppearsIn`**; the front end's generated `E` does not include
  internal edges, so emit them into your own `E` from the connector.

**After the terms change**, the spotter models are cached: refresh each one
(`POST /api/nlp/model/refresh?modelUID=<spotter uid>`), then recreate the parser index of every
field you link (`GET /api/index/list/FieldToDocumentIndex` for the `IndexUID`,
`POST /api/index/recreate?nodeType=<Type>&indexUID=<uid>`) so existing records are parsed again.
`POST /api/nlp/model/parse/?modelUID=<spotter uid>` with a sentence as the body shows what one
spotter finds - the quickest test of a term rule.

**Measure precision on real text before trusting it.** The first pass over 1,556 real articles
linked "GEO" (an orbit) to Georgetown's airport, "Middle East" to an airline, a Roman numeral
to a business jet and three unrelated aircraft to "Falcon". Run `top_mentions`-style counts,
look at the top 30, and fix the rule that produced each bad one - never the row.

---

## 8. Checklist

- [ ] Every identifier from the discovery brief that appears in prose has a spotter
- [ ] Every spotter is backed by a real master list; `AddIfNotFound` is `false` everywhere
- [ ] `IgnoreNumbers` / `MinimumLength` / `IgnoreCase` set per identifier shape, not copied
- [ ] Every spotter is registered in the pipeline
- [ ] Linking rules cover every prose field of every record type — generated, not hand-listed
- [ ] Record-identifier spotters of every type are linked into every other type's prose
- [ ] Each prose section is indexed as its own field, with sensible boosts
- [ ] The ground-truth cases name their discovery mechanism, and both mechanisms are exercised
- [ ] The front end shows NLP edges alongside structured ones and never filters them out
