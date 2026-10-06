---
name: curiosity-similarity-scenarios
description: How to build ranked candidate lists with the Curiosity similarity engine — scenarios, text and graph signals, weights, fusion, negative signals, hard rules, two-hop projections, and wiring the result into a front-end search area. Use when writing a "what else is related to this" endpoint, tuning a ranking, or explaining why a result surfaced.
---

# Similarity scenarios — ranked candidates, with reasons

This is where the application earns its claim. A similarity scenario fuses several
independent signals into one ranked list, keeps the **per-signal attribution** so the UI can
say *why* a result surfaced, and applies hard filters after fusion.

Write scenarios. Do not hand-roll a loop that scores candidates — you lose the attribution,
the candidate management and the front-end integration all at once.

---

## 1. The shape

```csharp
var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(400)
        .MaxCandidatesPerSignal(150)
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    .AddSignal("SimilarText", s => s
        .Describe("Embedding similarity over title, abstract and description")
        .Weight(1.0f)
        .Limit(150)
        .FromAsync(async ctx => await ctx.Graph.Query()
            .StartAtSimilarTextAsync(seedText, count: 150,
                                     nodeTypes: TD.CandidateTypes, applyCutoff: false)))

    .AddSignal("SharedPart", s => s
        .Describe("Shares a part number with the change")
        .Weight(0.9f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(TD.CandidateTypes)
            .HasCommonNeighborsWith(seedUID, N.PartNumber.Type, E.UsesPart)))

    .AddNegativeSignal("AlreadyConfirmed", s => s
        .Describe("Already confirmed")
        .From(ctx => ctx.Graph.Query()
            .StartAt(seedOwnerUID)
            .Out(TD.CandidateTypes, null, new[] { E.ConfirmedImpact })))

    .Fuse(Fusion.Sum)
    .FuseFinal(Fusion.Discount)

    .AddRule("SamePartition", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates).IsRelatedTo(partitionUID).AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

return result;                                   // the engine's own result, unchanged
```

**Return the engine's result object unchanged.** It is the type the front-end search area
consumes; a hand-built `{ source, hits }` projection of it deserialises into an empty result
with no error anywhere.

---

## 2. Signals

A signal is one independent reason two things might be related. Give each a **name**, a
**`Describe`** (it is what the UI shows the user) and a **weight**.

### Text signals

Start from the seed's own prose — usually title + abstract + the main description, joined.
This is the strongest single signal and the **only** one that can reach a record sharing no
identifier at all.

- Build the seed text from the fields you deliberately indexed separately, so you can choose
  which ones participate.
- `applyCutoff: false` when the scenario will fuse and filter anyway — let the engine see the
  tail.

### Graph signals

`HasCommonNeighborsWith(seedUID, nodeType, edgeType)` — "shares a neighbour of this type over
this edge with the seed". **This is why metadata is modelled as nodes.** A category that lives
in a property is invisible here.

One signal per shared dimension:

| Signal | Typical weight | Reasoning |
| --- | --- | --- |
| Shares a precise entity (a part, a component, a contract) | 0.9–1.0 | Two records touching one identified thing are related whatever they say |
| Same deepest category level | 0.8 | Precise |
| Same middle level | 0.45 | |
| Same top level | 0.2 | Weak but real |
| Shares a location/zone | 0.35 | Physical proximity is a reason to look even across systems |
| Shares a system/domain | 0.3 | Reaches across types with no common vocabulary |

**Split a hierarchy into one signal per level with descending weights**, rather than one
signal with a branch inside it. The weights then order themselves and the attribution says
which level matched.

### Negative signals

Something the user has already dealt with should be **demoted, not hidden** — the list stays
a complete picture, and the user can see what they decided. Use `AddNegativeSignal` plus a
discounting final fusion, not a rule.

### Rules

A rule is applied after fusion and is **absolute**. Use it only for things that are genuinely
never allowed: partition isolation, scope boundaries, deleted records. Anything with
exceptions is a weight, not a rule.

---

## 3. Two-hop scenarios — score, then project

"Find similar *cases*" is often really "find similar *records*, then walk to the cases that
analysed them". Do it in that order:

1. score the records with a full scenario;
2. traverse to the related objects;
3. score each by the **best** score among the records that reached it.

Scores survive **narrowing** (`Where`, `Except`, `IsRelatedTo`) but not **expansion** (`Out`),
so a two-hop signal contributes by rank rather than by score. That is the right weight for an
inference anyway — say so in a comment rather than pretending otherwise.

The same pattern gives you **aggregate scoring**: take the per-record score dictionary, resolve
each record's category, and sum the scores per category. That produces "which areas does this
change probably touch" from "which records does it probably touch", and it is one of the most
convincing things the demo does — because it surfaces an area that the structured data does
not already say is impacted.

---

## 4. Wiring it to the UI

The front-end search area consumes the scenario result directly, so scores and per-signal
attribution render themselves:

```csharp
SearchArea()
   .WithFacets()
   .WithSimilarityEngine(_ => Api.SimilarityAsync(endpointPath, seedUID))
   .OnSearch(sr => sr.WithExcludeUIDs(excluded))
   .Renderer(r => r.WithSelection().CustomizeResult(result => decisions.Customize(result, onChanged)))
```

**Always exclude the seed, and the thing the seed is about.** A node is the closest match in
the corpus to itself, so without an exclusion the seed ranks first in its own list of related
things — and the record it analyses ranks near it, which is the *change* rather than something
the change affects. Both are rows nobody can act on, in the two places the best real
candidates should be.

Compute the exclusion set **once** and hand the same task to every list on the screen, so two
lists cannot end up excluding different things.

---

## 5. Tuning

- **Start with all weights at 1.0 and one signal at a time.** Add a signal, look at the top
  twenty, decide whether it helped. Weight-tuning before signal selection is guesswork.
- **Score against the ground-truth cases** from the discovery brief. Recall at 10 and at 20 on
  ten known cases tells you more than any amount of reading result lists.
- **Check both discovery mechanisms separately.** If your entity-only pairs are being found
  and your semantic-only pairs are not, the text index or the field selection is wrong — no
  amount of weight tuning will fix it.
- **Track progress** on long scenarios and relay the message; a candidate list that appears
  after eight silent seconds reads as broken.
- **Cap candidates** (`MaxCandidates`, `MaxCandidatesPerSignal`) so a hot signal cannot flood
  the pool.

---

## 6. Checklist

- [ ] Written as a scenario, not a hand-rolled loop
- [ ] Every signal has a name, a `Describe` and a deliberate weight
- [ ] Text signal built from the fields you indexed separately
- [ ] One graph signal per shared dimension; hierarchies split by level
- [ ] Already-decided candidates are a **negative signal**, not a rule
- [ ] Only absolute constraints are rules
- [ ] The endpoint returns the engine's own result object, unchanged
- [ ] The seed and its subject are excluded from every list, from one shared computation
- [ ] Recall measured against ground truth, per discovery mechanism

---

## Reference implementation

A complete, commented scenario in
[`examples/similarity.affected-documents.cs`](examples/similarity.affected-documents.cs): text
signal, per-dimension graph signals, a hierarchy split by level, a negative signal for
already-decided candidates, fusion, and a hard partition rule — returning the engine's own result
object unchanged.
