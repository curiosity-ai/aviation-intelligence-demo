---
name: curiosity-use-case-discovery
description: Interrogate a developer or customer to gather everything needed to design a Curiosity application before any code is written — the decision the app supports, the data types and their real samples, identifiers, relationships, retrieval strategy, users, workflow state and success criteria. Produces a written design brief that the connector, schema, endpoints and front-end are all built from. Use at the very start of a new Curiosity use case, or when an existing one is being reshaped.
---

# Discovery — interrogating for a new Curiosity use case

Your job in this skill is **not** to write code. It is to leave the conversation holding a
written brief complete enough that `curiosity-solution-blueprint` can be executed without
coming back for more. Every question below exists because getting it wrong later cost a
rewrite in the reference implementation.

## How to run the interview

- **Ask in rounds, not in one wall.** Six rounds, in the order below. Each round's answers
  change which questions in the next round matter.
- **Never accept an abstraction where a sample would do.** "We have maintenance documents"
  is not an answer. One real record, with its real field names and real values, is.
- **Ask for the smallest artefact that settles a question.** A file, a screenshot, a
  paragraph of a real record — not a specification.
- **Propose an answer when you can.** "I'd model `status` as a shared node keyed by value —
  is a document's status the same vocabulary as an order's?" gets a better answer than
  "how should we model status?".
- **Write the brief as you go**, and read it back at the end of each round. Contradictions
  surface when the customer hears their own answers in one place.
- **Record what you were told you cannot have.** A missing field is a design constraint,
  not a gap to fill by inventing.

---

## Round 1 — The decision

Everything else is downstream of this. A Curiosity application is not a place to put
documents; it is a place where someone takes a decision faster because the corpus is
navigable.

1. **What is the name of the application, and the one-sentence description a user would
   read on the login screen?**
2. **Who is the user?** Job title, and what they are measured on.
3. **What decision or task does the app support, end to end?** Walk it as a story: what
   arrives, what the user does, what they produce, who consumes it.
4. **What does that person do today instead?** Which systems, which searches, how long,
   and what goes wrong. *The failure mode of the current process is the demo.*
5. **What is the unit of work?** The thing a user opens, works on, and closes — a case, a
   work order, a claim, an analysis. This becomes the centrepiece node type and the
   application's most elaborate screen.
6. **What does "done" look like on one unit of work?** A verdict, a report, a set of
   confirmed links, an approval? *This tells you what the app must let a user **write**,
   which is almost always underspecified.*
7. **What would make a user say the app is wrong?** A missed connection? A false one? A
   stale answer?
8. **Is this a demo, a pilot or production?** It changes data volume, synthetic vs real,
   and how much of the workflow has to actually persist.

> **Trap.** If the answers to 3–6 describe *storing* and *retrieving*, stop and dig. Curiosity
> earns its keep on **connecting** — if nobody needs to find the non-obvious relationship, a
> search box would do.

---

## Round 2 — The data

Ask per data type, and do not stop at the ones they volunteer. The reference implementation
has ten document types and fifteen reference tables; the customer named four.

For **each** type:

1. **Name it as the business names it**, plus its abbreviation. (`MOD`, `SB`, `AMM`.
   Abbreviations become the display names and the card tiles — collect them now.)
2. **A real sample.** One record, whole, with real values. Not a schema — a sample. Ask for
   three if the type varies a lot.
3. **Where does it live and how does it arrive?** File export, database, API, SharePoint,
   PDF. Batch or continuous. Who owns it.
4. **How many are there**, and how fast does it grow?
5. **What is its stable identifier?** Is it unique across the whole corpus or only within
   its type? Is it stable across revisions?
6. **Does it have versions/revisions/issues?** If yes: is the history needed, or only the
   current state? *This is a schema fork — see `curiosity-graph-modelling` §6. Do not
   discover it late.*
7. **Which fields are free text?** Which are long enough to embed usefully (roughly: more
   than a sentence)? Which are controlled vocabularies?
8. **Which fields are dates**, and which of them mean "created", "published", "effective"?
   One becomes the node's timestamp.
9. **Is there a rendered artefact** — a PDF, a drawing, a scanned page — that *is* the
   document to its readers? If so, the app must show that, not a field dump.

Then across types:

10. **Which types share a vocabulary?** Two types with a `status` field drawing on the same
    words want one shared node type.
11. **Is there a partition nothing may cross?** Programme, tenant, region, legal entity.
    *This becomes a hard filter in every query and a rule in every similarity scenario.*

> **Deliverable of this round:** a table of types × (count, identifier, versioned?, free-text
> fields, rendered artefact, source system), and one real sample per type saved to the repo.

---

## Round 3 — The connections

This is the round that decides whether the application is worth building. Push hard.

1. **List the identifiers that appear inside the text**, not only in fields: part numbers,
   case numbers, drawing references, SKUs, contract ids, statute references, ticker symbols.
   For each: **what does it look like** (give three examples and the pattern), and **is there
   a master list of valid values?**
   *A master list makes it a spotter and an exact link; no master list makes it a regex or
   nothing at all.*
2. **For each pair of types: is there a stated relationship in the data?** A field that
   holds the other's id. These become connector edges.
3. **Which relationships does nobody record, but everyone knows about?** *These are the ones
   the application discovers, and they are the whole product.* For each, ask **how** a human
   spots it today: shared identifier? similar wording? same location/system/customer? same
   time window?
4. **Give me two records that are related and share no words.** Then **two that share words
   but are not related.** If the customer cannot produce either, the retrieval story is
   guesswork — say so.
5. **What does a user filter by** when they narrow a list? Every answer is a node type and a
   facet, not a property. Collect the whole list.
6. **What would a user want to see on one screen that today lives in five systems?**

---

## Round 4 — People, ownership and workflow

The reference implementation's dashboard, inbox and assistant all exist because of this
round, and all of it was discovered late.

1. **Who are the users, and how do they differ from each other?** Skills, specialities,
   sites, departments, roles. *These become nodes and edges from the user, not strings.*
2. **How does work reach a person?** Assignment, self-service pickup, a queue, a rota.
   Automatic or manual. Can it be delegated? Reassigned?
3. **What states does a unit of work move through?** Give the exact vocabulary. Which are
   terminal? Which mean "mine and not finished" (that is the inbox) versus "mine ever" (that
   is a different list, and both will be on the home page)?
4. **What makes something urgent?** A due date, a severity, a flag. Be precise — this becomes
   a computed property and a metric tile.
5. **What does a manager or the user themself want to know about their own throughput?**
   Volume per period, time per unit, outcome split. *All of it must be computed live from
   the graph; if the data cannot support the metric, do not put the tile on the page.*
6. **Is there real authentication, or does the demo need to switch perspective between
   users?** A demo with no login still needs "render as this user".
7. **Who may see what?** Whole-corpus visibility, per-team, per-record? This decides
   `AccessMode` on every endpoint and the workspace's access-group configuration.

---

## Round 5 — What the AI has to do

Distinguish four layers explicitly; customers conflate them and the conflation produces an
assistant that hallucinates identifiers.

1. **Retrieval** — what should a search or a similarity list surface, ranked how? Which
   signals should count and how much? (Feeds `curiosity-similarity-scenarios`.)
2. **Reading** — which questions are answered by reading one record in full? *That is a
   `consult` reader, one per node type.* Ask which node types a user would attach to a
   conversation.
3. **Judging** — which questions need a multi-document verdict with evidence? *That is an
   agent.* Get the exact shape of the answer they want: verdict first? severity? quotations?
   what has to happen next?
4. **Operating** — which questions are about their own workload rather than the corpus?
   *That is a custom AI tool reading the same edges the dashboard reads.*

Also:

5. **What must the assistant refuse?** Anything safety-, legally- or contractually-
   determining. Get the sentence they want it to say.
6. **What is the vocabulary an answer is written in?** If answers are full of identifiers,
   the chat view should parse replies against the corpus so each one becomes a link.
7. **Is there a house style for the written output** the user produces? Collect a real
   example of a good one.

---

## Round 6 — Ground truth and success

Without this round you cannot tell a working retrieval story from a plausible one.

1. **Can you give me ten real cases with the right answer already known?** A unit of work
   plus the records that turned out to be relevant, and — as valuable — records that look
   relevant and are not.
2. **For each, why was it relevant?** Shared identifier, shared wording, shared location.
   *Record the discovery mechanism per case; it is what tells you whether your signals
   work.* (In the reference implementation this is the `expected_findings` block with a
   `discovery` field of `SEMANTIC` / `ENTITY`.)
3. **What is the demo narrative?** One walkthrough, five minutes, that ends in a moment where
   the app finds something a person would have missed. Name the exact records it uses.
4. **How will we know it works?** Recall against the ten cases? Time to complete one unit?
   User verdict?
5. **What is out of scope for v1?** Write it down; it is the most useful sentence in the brief.

---

## The brief

Write it into the repository as `docs/use-case.md` before writing code. Sections:

```
1.  The application       name, one-liner, user, the decision it supports
2.  The unit of work      what a user opens, works on and closes; what "done" means
3.  Data types            table: name, abbrev, count, identifier, versioned?, source,
                          free-text fields, rendered artefact
4.  Reference/master data table: name, row count, key, what it is used for
5.  Partitions            what nothing may cross
6.  Identifiers in text   pattern + master list availability, per identifier
7.  Stated relationships  type → type, the field that states it   (→ connector edges)
8.  Discovered relations  type → type, how a human spots it        (→ retrieval, NOT edges)
9.  Filter dimensions     every value a user narrows by            (→ nodes + facets)
10. Users & workflow      roles, specialities, assignment, states, urgency, metrics
11. AI layers             retrieval / consult / agents / operational tools; refusals
12. Ground truth          the ten cases with their discovery mechanism
13. Demo narrative        the five-minute walkthrough, with real record ids
14. Out of scope
15. Open questions        what you were told nobody knows
```

Then, and only then, open `curiosity-solution-blueprint`.

---

## Red flags to raise before building

Say these out loud to the customer; each one changes the shape of the project.

| Signal | What it means |
| --- | --- |
| No identifier appears inside free text | Entity linking will contribute nothing; the story is text similarity alone. |
| No free text longer than a sentence | Embeddings will contribute nothing; the story is entity linking alone. |
| Nobody can produce two related records sharing no words | The "semantic discovery" claim is unevidenced. Reduce the claim or get better data. |
| Every relationship is already recorded in the source | Ingest it and be honest: this is a navigation product, not a discovery one. |
| The unit of work has no state and no owner | There is no dashboard, no inbox and no metrics. Do not promise them. |
| "We'll get you real data later" | Design against the sample you have and say which decisions are provisional. Synthetic data built to the same shape is better than waiting. |
| The corpus is one type with one text field | Fine — but the graph adds little. Consider whether search alone answers it. |
