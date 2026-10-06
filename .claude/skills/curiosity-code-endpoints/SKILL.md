---
name: curiosity-code-endpoints
description: How to write Curiosity server-side code endpoints — the definitions file format and attribute header, AccessMode and ReadOnly, the injected N/E/Q()/Graph identifiers, shared DTO libraries via ImportEndpoint, returning values not JSON strings, traversing instead of re-looking-up, and the UID-everywhere wire discipline. Use when adding or changing anything under code/endpoints.
---

# Code endpoints — the server side of the application

An endpoint is a C# script stored in the workspace, addressed by path, and executed in a
scope that hands it the graph, a query builder, the request body and the current user. It is
where every query, projection and user-decision write lives.

---

## 1. The file format

One file per endpoint, at `code/endpoints/<path>.cs` in the definitions folder. Attribute
lines first, then a **blank line**, then the code — no class, no method, no `using`s.

```csharp
[endpoint: Curiosity.Endpoints.Path("app/pawo/confirmed-impacts")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

var req     = Body.FromJson<TD.CaseRequest>();
var caseUID = req?.CaseUID ?? default(UID128);

if (caseUID == default(UID128))                    return BadRequest("caseUID is required");
if (!Graph.TryGet(caseUID, out var node)
    || node.Type != N.Case.Type)                   return NotFound($"Case '{caseUID}' not found.");

var uids = Q().StartAt(caseUID).Out(edgeType: E.ConfirmedImpact).AsUIDEnumerable().ToArray();

return new TD.ConfirmedImpactsDto { Uids = uids };
```

- **`Path`** is how the endpoint is resolved. It must match the constant the front end calls
  it by, exactly.
- **`AccessMode`** is one of `Unrestricted`, `AllUsers`, `AccessControlled`, `AdminOnly`.
- **`ReadOnly`** marks an endpoint that only queries. The runtime then hands it a read-only
  scope and a write attempt fails loudly. **Every write endpoint must NOT carry it** — this
  is a run-time failure, not a compile-time one, so it is easy to ship.

Available identifiers inside the body: `Body`, `Graph`, `Q()` / `Query()`, `CurrentUser`,
`Logger`, `CancellationToken`, the AI scopes, and a secret accessor.

---

## 2. `N` and `E` are injected — never write a string literal

The workspace prepends generated `N` and `E` helper classes to every endpoint script before
compiling it. So it is `N.Case.Type`, `N.Case.Status`, `E.InCategory` — **including for the
types the graph owns**, e.g. the user type's user-name field and the attachment edges.

A literal `"Status"` that no longer matches the schema returns nothing, forever, in silence.
`N.Case.Status` fails the import instead.

The corollary is an ordering rule: an endpoint naming a node type the connector has not
created will not compile. **Ingest before importing endpoints.**

---

## 3. Everything crossing the wire is a `UID128`

Requests carry `CaseUID`, `TargetUID`, `UserUID`. Responses carry `Uid`, `CategoryUID`,
`InboxUIDs`. Two reasons, both load-bearing:

- **The endpoint stops guessing.** Confirming a decision against a document identifier meant
  trying each of eight candidate node types in turn to work out what the string referred to. A
  UID says.
- **The response is directly renderable.** A list of UIDs goes straight into a neighbour-links
  component or a search area scoped to those UIDs, and **every row is drawn by that node
  type's own registered renderer** — so no title is ever copied through the wire to go stale,
  and a mixed-type list needs no special handling.

Use the `UID128` type itself in the DTOs on **both** sides. In the front end a `UID128` is a
JS string at run time; server-side it is a struct whose converter reads and writes the same
plain string. Identical bytes on the wire, and a compile error when a UID reaches the wrong
parameter.

---

## 4. DTOs — declared twice, identically

Put the shared DTO types in a library endpoint (`app/_lib/helpers`), marked
`AccessMode("AdminOnly")` so it fails closed and cannot be called as an API. Import it into
every endpoint with `//ImportEndpoint("app/_lib/helpers")`, which appends the imported source.

Declare **the same classes again, identically**, in the front end's `Api/Dto.cs`. Both ends
serialise with the same default resolver, so the two declarations literally *are* the wire
format — and a field renamed on one side stops compiling instead of deserialising to null.

> Add a field to one, add it to the other. This is the discipline that replaces a shared
> assembly you cannot have across the Transpose boundary.

Also put in the library anything several endpoints agree on: the list of node types that
count as candidates, the status vocabulary, shared read helpers.

---

## 5. Return a value, not a JSON string

The controller serialises whatever the script returns. Returning `x.ToJson()` sends it as
`text/plain` and — worse — lets the returned shape drift from the DTO the front end
deserialises.

This matters most for similarity: **return the engine's own result object unchanged**. That
is the type the front-end search area consumes, and a hand-built projection of it
deserialises into an empty result with no error anywhere. See
`curiosity-similarity-scenarios`.

---

## 6. Traverse, do not re-look-up

The graph is the point. Where a property *names* something that is also on the other end of an
edge, use the edge:

```csharp
// the subject of a case
var subject = Q().StartAt(caseUID).Out(N.Change.Type, E.CaseOf).AsUIDEnumerable().FirstOrDefault();

// what a user owns — not by comparing a name
var mine    = Q().StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsUIDEnumerable();

// rules that bind this case, via its category
var rules   = Q().StartAt(caseUID).Out(N.Category.Type, E.InCategory)
                                  .Out(N.Rule.Type,     E.HasRule).AsUIDEnumerable();
```

This is what guarantees that a dashboard count and the list behind it cannot disagree: both
are the same traversal. A property-based version of either is a second copy of the fact.

---

## 7. The endpoint set — the shapes that recur

Every application of this kind ends up with roughly these:

| Shape | Read-only | Notes |
| --- | --- | --- |
| `dashboard/summary` | yes | The counts behind the metric tiles — **and the UID arrays behind each count**, so the tile and the list it opens are the same set |
| `dashboard/performance` | yes | Throughput per period, average duration, outcome split — all computed live |
| `dashboard/users` | yes | The people the app can render as; each dimension returned **twice**, as UIDs (for rendering) and as text (for client-side search) |
| `<unit>/decisions` | yes | Everything already decided, in **one** call, so rows can be badged before the first one draws |
| `<unit>/set-decision` | **no** | One decision for a whole selection, batched |
| `<unit>/matrix` | yes | A cross-tab projection for the summary screen |
| `<unit>/draft-report` | **no** | An AI-drafted written output |
| `similarity/*` | yes | Similarity scenarios; return the engine's result unchanged |
| `rules/applicable` + `rules/add` | yes / **no** | Applicable guidance, and proposing one against the unit |
| `<record>/revisions` | yes | Revision history, newest first, for a comparison screen |

Two design notes that were worth the trouble:

- **Return UID arrays next to every count.** A count whose list is recomputed by a different
  query somewhere else is a count that will eventually lie.
- **Record the negative decision too.** If "confirmed" is an edge and "rejected" is the absence
  of one, a dismissed candidate is indistinguishable from one nobody has looked at — which
  makes a long review impossible to pause and resume. Model both; make them mutually exclusive
  by having the write clear the other.

---

## 8. Errors

- `BadRequest(...)` for a missing or malformed parameter.
- `NotFound(...)` for a UID that does not resolve, or resolves to the wrong node type — check
  the type, not just the existence.
- Distinguish **401** ("we cannot tell who you are") from **403** ("we know, and you may not")
  where you write status codes directly. Clients branch on the difference: a 401 can log a
  valid user out.

---

## 9. Checklist

- [ ] Attribute header first, blank line, then code
- [ ] `Path` matches the front-end constant exactly
- [ ] `AccessMode` set deliberately; the shared library is `AdminOnly`
- [ ] Write endpoints are **not** marked `ReadOnly`
- [ ] No node type or field name written as a string literal
- [ ] Every identifier crossing the wire is a `UID128`
- [ ] DTOs declared identically in the library and in the front end
- [ ] Returns a value, never a JSON string; similarity returns the engine's own result
- [ ] Relationships traversed, not re-derived from properties
- [ ] Counts ship the UIDs they counted
- [ ] Imported after an ingest has created the schema

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`_lib.helpers.cs`](examples/_lib.helpers.cs) | The shared `AdminOnly` DTO library — requests, responses, shared constants and reads |
| [`dashboard.summary.cs`](examples/dashboard.summary.cs) | A read-only endpoint: traversal over lookup, and shipping the UIDs behind every count |
| [`case.set-decision.cs`](examples/case.set-decision.cs) | A **write** endpoint: no `ReadOnly` marking, batched, mutually-exclusive edges |

The similarity endpoint template lives with its own skill, in [`../curiosity-similarity-scenarios/examples/`](../curiosity-similarity-scenarios/examples/).
