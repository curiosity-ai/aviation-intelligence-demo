---
name: aeralis-graph-connector
description: How to build and extend the Curiosity.Library data connector that ingests the Aeralis MOD pre-analysis dataset into the graph — the per-input-type handler convention (DTO + schema classes as nested types), the [Node]/[Key]/[Property]/[Timestamp] graph schema, the full edge catalogue, HashCache usage and commit ordering, user creation and deterministic PAWO assignment. Use when writing or changing anything under src/Aeralis.Connector/.
---

# The graph connector — `Aeralis.Connector`

Ingests `data/reference/*.csv` and `data/documents/**/*.json` into a Curiosity
workspace graph, creating **only the links that are already structured in the
source**. Anything that would need NLP to find is left alone — see §6.

Verified against `curiosity-ai/mosaik` and the `Curiosity.Library` package, not
only against the docs. When the two disagree, the repo wins.

---

## 1. Project shape

```
src/Aeralis.Connector/
├── Aeralis.Connector.csproj      net10.0, exe. Curiosity.Library + CsvHelper
├── Program.cs                     arg parsing, Graph.Connect, schema sync, run order
├── ConnectorContext.cs            IGraph + HashCache + dataset root + counters
├── Edges.cs                       every edge name as a const, both directions
├── Schema/                        the [Node] classes, one file per family
└── Ingest/                        one handler per input type (§3)
```

One handler per input type. Each handler is a `static class` whose **nested
types** are its own DTOs and its own graph-schema classes, so a type never
leaks across handlers:

```csharp
internal static class PartNumbersIngest
{
    public const int Version = 1;                     // bump to invalidate HashCache

    private sealed class Row { … }                    // CsvHelper DTO — matches the CSV
    [Node] internal sealed class PartNumber { … }      // graph node — need not match Row

    public static async Task RunAsync(ConnectorContext ctx) { … }
}
```

**The input schema does not have to match the node schema.** Split a row into a
parent plus child nodes wherever the graph reads better that way — IPC items,
SRM repair drawings and PAWO findings are all child nodes minted from a parent
document (§4.3).

---

## 2. The handler contract

Every `RunAsync` follows the same four beats, in this order:

```csharp
public static async Task RunAsync(ConnectorContext ctx)
{
    // 1. LOAD — CsvHelper for CSV, System.Text.Json for JSON
    var rows = ctx.ReadCsv<Row>("part_numbers.csv");

    // 2. LOOP — HashCache decides whether the record needs work at all
    foreach (var row in rows)
    {
        ctx.Cache.IfNotCached(Version, row, r =>
        {
            var node = ctx.Graph.AddOrUpdate(Map(r));
            ctx.Graph.Link(node, Node.FromKey(nameof(AtaChapter), r.Ata6d),
                           Edges.InAtaChapter, Edges.HasPartNumber);
            ctx.Count("PartNumber");
        });
    }

    // 3. COMMIT the graph
    await ctx.Graph.CommitPendingAsync();

    // 4. COMMIT the cache — only after the graph commit returned
    await ctx.Cache.CommitPendingHashesAsync();
}
```

Rules that are not negotiable:

- **Commit ordering.** `CommitPendingHashesAsync` runs *after* a successful
  `CommitPendingAsync`, never before and never if the graph commit threw. A
  hash recorded for work that did not land makes the record invisible forever.
- **Hash the source DTO**, never a shape containing `DateTime.UtcNow`, a
  computed UID, or a counter — every run would miss.
- **`Version` per handler.** Bump the handler's own `Version` const when its
  mapping changes shape (new `[Property]`, an extra `Link`, a changed `[Key]`).
  Handlers version independently; there is no global version.
- **`Node.FromKey(type, key)` for links.** Never fetch a node just to link to
  it. The edge stores by key and activates when both ends exist, which is also
  what lets a handler link forward to a type ingested later in the run.
- **`TryAdd` vs `AddOrUpdate`.** `TryAdd` for immutable reference rows that
  other handlers may have already minted by key (ATA chapters, programs, zones);
  `AddOrUpdate` for anything carrying properties from this handler's source.
- **`AddOrUpdate(schemaObject)` writes the node's *whole* content.** The mapper
  drops `null`s, and nothing else: a `string` left at its `= ""` initialiser, a
  `bool` left `false`, an `int` or `double` left `0`, a `DateTimeOffset` left at
  `default` are all sent, and all overwrite what is there. So a second pass that
  calls it with a partial object — only the two or three fields that pass
  computed — **blanks every other property the first pass wrote**. This is not
  hypothetical: the assignment pass used to do exactly that to
  `PreAnalysisWorkOrder`, clearing the title, the abstract and every section text.

  A node therefore gets **one** `AddOrUpdate` per run, carrying everything. When
  a later pass genuinely has to touch an existing node, use the partial-update
  overload, which sends only the fields named:

  ```csharp
  ctx.Graph.AddOrUpdate(Node.FromKey(nameof(Foo), key), op => op
      .Set(nameof(Foo.Bar), bar));
  ```

  Better still, compute the value before the node is written and pass it in — as
  `AssignmentPass.Plan` now does (§5.2). Relationships never need either: a
  `Link` is additive and cannot clobber a property.

---

## 3. Run order

Order matters only for readability and for counters — `Node.FromKey` means a
link never blocks on its target existing. Keep it dependency-shaped anyway:

```
 1. ProgramsIngest         P C R N L + aircraft types + models (derived, no source file)
 2. AtaChaptersIngest      ata_chapters.csv       → AtaChapter, parent/child chain
 3. ZonesIngest            zones.csv              → Zone, AccessPanel
 4. SuppliersIngest        suppliers.csv
 5. OperatorsIngest        operators.csv
 6. PartNumbersIngest      part_numbers.csv
 7. FinsIngest             fins.csv
 8. DrawingsIngest         drawings.csv
 9. SoftwareIngest         software.csv
10. MsnIngest              msn_registry.csv
11. RulesIngest            rules.csv              → Rule (engineering rules dataset)
12. UsersIngest            users.csv              → _User via CreateUserAsync (§5)
13. DocumentsIngest        documents/**/*.json    → all 9 document types + child nodes
14. IssuesIngest           documents/**/*.json    → one node per issue of each (§4.5)
15. AssignmentPass         deterministic PAWO assignment + workflow state (§5)
16. AttachmentsIngest      documents/**/*.json    → the rendered file per issue (§5.4)
```

`DocumentsIngest` is one handler with a `switch` on `doc_type`, because every
document shares the envelope; the per-type payload work lives in a nested
`static class` per type inside it.

---

## 4. Graph schema

### 4.1 Reference nodes

| Node | `[Key]` | `[Property]` | `[Timestamp]` |
| --- | --- | --- | --- |
| `Program` | `Letter` | `Name` | — |
| `AircraftType` | `Name` | `ProgramLetter` | — |
| `AircraftModel` | `Name` | `AircraftType`, `ProgramLetter` | — |
| `AtaChapter` | `Code` | `Level`, `Title`, `Domain` | — |
| `PartNumber` | `PartNumber` | `Description`, `PnType`, `Lifecycle`, `Uom`, `MassKg`, `UnitPriceEur` | — |
| `Fin` | `Fin` | `Description`, `System`, `Lru` | — |
| `Drawing` | `DrawingNumber` | `Title`, `DrawingType`, `Issue`, `CadSystem` | `IssueDate` |
| `SoftwareLoad` | `SoftwarePn` | `SoftwareName`, `System`, `Version`, `LoadType`, `CertificationStatus` | — |
| `Zone` | `Zone` | `ZoneName`, `MajorZone`, `MajorZoneName` | — |
| `AccessPanel` | `Panel` | `Zone` | — |
| `Msn` | `Msn` | `AircraftType`, `Model`, `LineNumber`, `EngineType`, `Registration`, `Status` | `DeliveryDate` |
| `Operator` | `OperatorCode` | `OperatorName`, `Iata`, `Icao`, `Country`, `Region` | — |
| `Supplier` | `SupplierCode` | `SupplierName`, `Country`, `CageCode` | — |
| `Rule` | `RuleId` | `Title`, `Text`, `Category`, `Severity`, `Ata2d`, `ProgramLetter`, `Source` | — |

### 4.2 Document nodes

All nine carry the same envelope-derived members plus their own payload
properties. `[Key]` is always `DocId`; `[Timestamp]` is always `Released` when
set, otherwise `Created`.

Shared: `DocId`, `Title`, `Abstract`, `Program`, `Revision`, `Status`,
`Ata2d`, `Ata4d`, `Ata6d`, `Keywords`, plus the type's `sections` flattened
into one `[Property]` each (`ReasonText`, `DescriptionText`, …) so the search
index and the similarity engine see them as separate fields.

| Node | Extra properties |
| --- | --- |
| `Modification` | `ModNumber`, `ModClass`, `ChangeCategory`, `Trigger`, `EmbodimentStatus`, `IsCandidate`, `WeightImpactKg`, `EstimatedManhours`, `EstimatedCostKeur`, `RiskLevel`, `CertificationImpact`, `Interchangeability` |
| `ServiceBulletin` | `SbType`, `Compliance`, `ReasonCode`, `AdLinked`, `AdReference`, `Manhours`, `DowntimeCategory`, `KitNumber`, `WeightChangeKg` |
| `InternalOrder` | `RequestCategory`, `Priority`, `IoStatus`, `RequesterType`, `Funding`, `BudgetKeur`, `AircraftCount`, `ClosureNote` |
| `PreAnalysisWorkOrder` | see §5.2 — the workflow state lives here |
| `AmmTask` | `TaskNumber`, `TaskType`, `Manhours`, `Personnel`, `Steps`, `Warnings`, `Cautions` |
| `IpcFigure` | `Figure`, `FigureTitle`, `SheetCount`, `NextHigherAssembly` |
| `SrmRepair` | `RepairNumber`, `RepairCategory`, `DamageType`, `NdtMethod`, `WeightPenaltyKg`, `StaticStrengthRestoredPct`, `RequiresOemApproval` |
| `TsmProcedure` | `ProcedureNumber`, `FaultSymptom`, `IsolationSteps`, `MelItem`, `Dispatchable` |
| `WdmSheet` | `SheetNumber`, `SheetTitle`, `DiagramType`, `WireCount`, `WireGaugeRange` |
| `FcomEntry` | `Section`, `SectionTitle`, `EntryNumber`, `FlightPhase`, `OperationalImpact`, `CrewAction`, `AffectsQrh`, `AffectsMel`, `Systems` |

### 4.3 Child nodes minted from a document

The source JSON nests these; the graph gets them as their own nodes so they can
be linked, searched and confirmed individually.

| Child node | Minted by | `[Key]` | Why it is a node |
| --- | --- | --- | --- |
| `IpcItem` | `IpcFigure` | `{figure}#{item}` | Carries `PRE MOD-x` / `POST MOD-x` effectivity — the embodiment signal |
| `RepairDrawing` | `SrmRepair` | `RD-…` reference | Referenced by other documents; has its own sheets/title |
| `ExpectedFinding` | `PreAnalysisWorkOrder` | `{pawoId}#{findingId}` | The ground truth: type, severity, discovery mode, rationale, resolution |
| `SbRevision` | `ServiceBulletin` | `{sbId}#{revision}` | Revision history is a timeline, not a blob |
| `MsnRangeApplicability` | any document | `{docId}#{from}-{to}` | Lets a range be traversed without exploding it into MSNs |

### 4.3b Metadata dimensions — everything filterable is a node

A string can be searched; it cannot be traversed. Two things follow, and both are
why the connector links metadata instead of only storing it:

- the search UI can only offer a **related facet** on something that sits on the
  other end of an edge;
- `IQuery.HasCommonNeighborsWith(seed, nodeType, edgeType)` — the "shares a
  neighbour with the seed" filter the similarity signals are built from
  (`Mosaik/Graph/src/Queries/IQuery.cs:648`) — only sees *neighbours*. A document
  whose ATA 2D lives in a property is invisible to a same-chapter signal.

So every dimension a user might filter by, or a signal might key on, gets a node
**and** keeps its property. The property is what renders in a details grid
without a traversal.

| Node | `[Key]` | Source |
| --- | --- | --- |
| `EngineType` | `Name` | `applicability.engine_types` and `msn_registry.engine_type` |
| `Department` | `Code` | `originator.department`, PAWO `assigned.lead_department` |
| `Site` | `Name` | `originator.site`, PAWO `assigned.lead_site` |
| `AtaDomain` | `Name` | `ata_chapters.domain` |
| `MajorZone` | `Code` | `zones.major_zone` |
| `Keyword` | `Text` | `keywords[]`, lower-cased |
| `AircraftSystem` | `Name` | `fins.system`, `software.system`, FCOM `type_data.systems[]` |
| `FaultCode` | `Code` | TSM `type_data.fault_codes[]` |
| `MelItem` | `Item` | TSM `type_data.dispatch.mel_item` |
| `Harness` | `Reference` | WDM `type_data.harnesses[]` |
| `AirworthinessDirective` | `Reference` | SB `type_data.ad_reference` |
| `ServiceKit` | `KitNumber` | SB `type_data.material.kit_number` |
| `ModNumber` | `Number` | MOD `type_data.mod_number`, and the number in a PAWO's subject |
| `Status` | `{Value}` | every status-like field — see below |
| `Classification` | `{Kind}:{Value}` | the rest of the controlled-vocabulary long tail — see below |

**A multi-valued field flattened into a string is the clearest case of all.**
`Systems`, `FaultCodes` and `Harnesses` were `string.Join(", ", …)` on the node:
searchable, but the individual values were gone as far as traversal is concerned,
so nothing could ask "which other sheet draws harness 1214W". Each is now also a
node. Two of them pay off across document *types* the demo otherwise cannot
connect — `AircraftSystem` is reached from a FIN, a software load and an FCOM
entry, which is the only path from a flight-crew limitation to the wiring of the
system it limits; `MelItem` joins a troubleshooting procedure's dispatch note to
the crew entry carrying the same relief.

The same reasoning applies to a single-valued field that names something which
already exists as a node: an IPC figure's `NextHigherAssembly` is a part number
(88 of 98 of them resolve), and an applicability range's `from` / `to` are
aircraft. Both are now linked (`NextHigherAssembly`, `RangeFromMsn` /
`RangeToMsn`) as well as stored.

**ATA is linked at all three levels.** `InAtaChapter` (6D), `InAta4d`, `InAta2d`
— not just the leaf. The `ChildOfAta` chain makes 2D *reachable* from 6D, but a
three-hop walk is not a common neighbour, so a same-2D signal would find nothing.

**`Status` is keyed by the value, and that is the difference that matters.** Nine
fields across the dataset are the same idea under different names — a document's
`status`, a MOD's embodiment status, an IO's `io_status`, a PAWO's `pawo_status`, the
derived `WorkflowStatus`, a closed PAWO's `outcome`, a part's `lifecycle`, a software
load's `certification_status`, an aircraft's `status` — and they draw on one
overlapping vocabulary: `CANCELLED`, `ON_HOLD`, `IN_PROGRESS`, `SUPERSEDED`,
`WITHDRAWN` and `REJECTED` each occur in two or three of them. So the key is the value
alone: one `ON_HOLD` node an internal order and a pre-analysis both hang off, and
"everything on hold" is one node rather than a query per document type. Which field a
link came from is carried by the **edge** (`HasIoStatus`, `HasPawoStatus`, …), so
"this document's own status" is still one traversal with one answer.

`MetaLinks.SetStatus(ctx, node, StatusField.X, value)` is the call, and
`MetaLinks.StatusField` is the list. A field belongs there when its value is a *state
the thing is in and can move out of* — which is why `Recommendation`, `Priority`,
`Severity`, `Compliance` and `RiskLevel` stay classifications: they are judgements
about the thing, not where it stands. Two rules keep the sharing real:

- **The key is canonicalised to `SCREAMING_SNAKE`** (`CanonicalStatus`). A shared node
  is only shared if everyone spells the state the same way. The derived workflow
  vocabulary used to be `InProgress` / `OnHold` / `Validated` and split three states
  across two nodes each; it is now spelled like every authored vocabulary, and the
  canonicaliser is the guard that stops the next one doing it again.
- **`StatusField` and `Kind` are checked disjoint at startup.** A name in both would
  put two node types on one edge, so traversing it would return a `Status` sometimes
  and a `Classification` other times.

The one place the shared key conflates rather than joins is `IN_SERVICE` /
`IN_PRODUCTION`: a modification is embodied in service, an aircraft is flying. Same
word, different subject, deliberately one node — the edge tells them apart.

**`Classification` is one node type, not thirty-eight — but one edge pair *per*
vocabulary.** Embodiment status, compliance, risk level, damage type and the rest
are each a single enum with a handful of values and are all "how this document is
classified", so they share a node type keyed `{Kind}:{Value}`. They do not share
an edge. A single `HasClassification` made every vocabulary one undifferentiated
facet, and left a renderer no way to ask for *this document's risk level* —
traversing it returns all of them at once.

So `MetaLinks.Classify` derives the pair from the kind (`Has{Kind}` / `{Kind}Of`)
and looks it up in `Edges`, throwing at startup if either name is missing. Adding
a vocabulary is a `Kind` constant, two `Edges` constants and a `Classify(...)`
call — and forgetting the constants fails the run instead of writing thousands of
links over an edge type the schema never registered.

**A node type with only a key is still worth having.** `ModNumber` holds nothing
but the number, and that is the point: `MOD-166204` and every `PAWO-166204-nn`
analysing it carry it, so the node is where the whole change gathers. As an `int`
on each document it was a fact you could read and not follow.

**Mint once, link always.** `ConnectorContext.Mint(nodeType, key)` returns true
the first time a key is seen in a run. Metadata nodes are a few dozen values
shared by tens of thousands of rows — calling `TryAdd` per row makes the pending
buffer do work proportional to *rows × values* and turns a 3-second run into a
multi-minute one. Mint on first sight, then link by key every time.

### 4.5 Issues

A document file is an array of issues, oldest first, and the last one is the document as it
stands. `DocumentsIngest` maps that current state, keyed by identifier alone — which is what
everything else in the graph links to. `IssuesIngest` maps **every** issue, including the
current one, into a parallel node type per document type:

| | |
| --- | --- |
| Node | `ModificationIssue`, `ServiceBulletinIssue`, `AmmTaskIssue`, … one per document type |
| `[Key]` | `IssueId` — `{DocId}:{Letter}`, e.g. `MOD-160142:B` |
| `[Property]` | `DocId`, `IssueLetter`, `Supersedes`, `IssueReason`, `RaisedByPawo`, `RaisedByFinding`, `FindingType`, `IsLatest`, plus the envelope and the section prose of its document counterpart |

- **Why a type per type and not one `DocumentIssue`.** Curiosity reads the schema off the
  concrete `[Node]` class, so a shared base would be invisible to it, and a single type would
  have to carry the union of every type's prose fields.
- **Envelope and prose only.** The type-specific measurements — man-hours, mass, risk level —
  stay on the document node, which is the state anything else reasons about. What differs
  between issues, and what a reader comparing two of them looks at, is the text.
- **`IssueRaisedBy` is the point.** A pre-analysis claims it found an impact on a document;
  the issue linked to that finding is the evidence the impact was acted on, and the issue
  before it is what the finding was complaining about. Without it the ground truth is
  unfalsifiable.
- The issue the document is currently at carries `IsLatest`, and its rendered file is attached
  to both the issue and the document (§5.4).

### 4.4 Edge catalogue

Bi-directional, declared in `Edges.cs` as `const string` pairs and registered
with `CreateEdgeSchemaAsync(typeof(Edges))`.

| Forward | Reverse | From → To |
| --- | --- | --- |
| `InProgram` | `HasDocument` | any → `Program` |
| `AppliesToType` | `HasApplicableDocument` | document → `AircraftType` |
| `AppliesToModel` | `HasApplicableDocument` | document → `AircraftModel` |
| `AppliesToMsn` | `HasApplicableDocument` | document → `Msn` |
| `ExcludesMsn` | `ExcludedByDocument` | document → `Msn` |
| `AppliesToOperator` | `HasApplicableDocument` | document → `Operator` |
| `InAtaChapter` | `HasAtaDocument` | any → `AtaChapter` (the `ata_6d` leaf) |
| `InAta4d` | `HasAta4dDocument` | any → `AtaChapter` (4D) |
| `InAta2d` | `HasAta2dDocument` | any → `AtaChapter` (2D) |
| `InAtaDomain` | `HasAtaChapter` | `AtaChapter` → `AtaDomain` |
| `AppliesToEngine` | `HasEngineDocument` | document/`Msn` → `EngineType` |
| `FromDepartment` | `OriginatedDocument` | document → `Department` |
| `AtSite` | `SiteOfDocument` | document → `Site` |
| `InMajorZone` | `HasZone` | `Zone`/`Fin` → `MajorZone` |
| `HasKeyword` | `KeywordOf` | document → `Keyword` |
| `Has{Field}` | `{Field}Of` | any → `Status` — one pair per status field, derived from `MetaLinks.StatusField` |
| `Has{Kind}` | `{Kind}Of` | any → `Classification` — one pair per vocabulary, derived from `MetaLinks.Kind` |
| `UnderDirective` | `MandatedBulletin` | `ServiceBulletin` → `AirworthinessDirective` |
| `UsesKit` | `KitFor` | `ServiceBulletin` → `ServiceKit` |
| `HasModNumber` | `ModNumberOf` | `Modification`/`PreAnalysisWorkOrder` → `ModNumber` |
| `AlsoImpactsAta` | `AlsoImpactedBy` | document → `AtaChapter` (`secondary_ata`) |
| `ChildOfAta` | `HasAtaChild` | `AtaChapter` → `AtaChapter` |
| `UsesPart` | `UsedByDocument` | document → `PartNumber` |
| `AddsPart` / `RemovesPart` / `ModifiesPart` | `AddedBy` / `RemovedBy` / `ModifiedBy` | `Modification` → `PartNumber` |
| `UsesFin` | `UsedByDocument` | document → `Fin` |
| `UsesDrawing` | `UsedByDocument` | document → `Drawing` |
| `UsesSoftware` | `UsedByDocument` | document → `SoftwareLoad` |
| `InZone` | `HasDocument` | document → `Zone` |
| `MentionsMsn` | `MentionedByDocument` | document → `Msn` |
| `SupersedesMod` | `SupersededByMod` | `Modification` → `Modification` |
| `EmbodiedBy` | `Embodies` | `Modification` ↔ `ServiceBulletin` |
| `RequiresMod` | `PrerequisiteOf` | document → `Modification` |
| `ConcurrentWith` | `ConcurrentWith` | document → document |
| `Impacts` | `ImpactedBy` | `Modification`/`ServiceBulletin` → manual document |
| `DocumentedIn` | `Documents` | document → manual document |
| `ReferencesDoc` | `ReferencedByDoc` | document → document (generic) |
| `RaisedBy` | `Created` | `Modification` ↔ `InternalOrder` |
| `PreAnalysisOf` | `HasPreAnalysis` | `PreAnalysisWorkOrder` → `Modification` |
| `FromOrder` | `HasWorkOrder` | `PreAnalysisWorkOrder` → `InternalOrder` |
| `PostModCondition` / `PreModCondition` | `PostModConditionOf` / `PreModConditionOf` | document → `Modification` |
| `HasFinding` | `FindingOf` | `PreAnalysisWorkOrder` → `ExpectedFinding` |
| `FindingTargets` | `TargetedByFinding` | `ExpectedFinding` → document |
| `ConfirmedImpact` | `ConfirmedImpactOf` | `PreAnalysisWorkOrder` → document |
| `HasItem` | `ItemOf` | `IpcFigure` → `IpcItem` |
| `ItemPart` | `ListedInIpcItem` | `IpcItem` → `PartNumber` |
| `EffectivityPreMod` / `EffectivityPostMod` | `PreModEffectivityOf` / `PostModEffectivityOf` | `IpcItem` → `Modification` |
| `HasRepairDrawing` | `RepairDrawingOf` | `SrmRepair` → `RepairDrawing` |
| `HasRevision` | `RevisionOf` | `ServiceBulletin` → `SbRevision` |
| `SuppliedBy` | `Supplies` | `PartNumber` → `Supplier` |
| `DefaultPart` | `DefaultPartOf` | `Fin` → `PartNumber` |
| `HostFin` | `HostsSoftware` | `SoftwareLoad` → `Fin` |
| `SupersedesSoftware` | `SupersededBySoftware` | `SoftwareLoad` → `SoftwareLoad` |
| `InZoneLocation` | `HasFin` | `Fin` → `Zone` |
| `OperatedBy` | `Operates` | `Msn` → `Operator` |
| `OfType` | `HasMsn` | `Msn` → `AircraftType` |
| `OfModel` | `HasMsn` | `Msn` → `AircraftModel` |
| `InSystem` | `HasSystemDocument` | `Fin`/`SoftwareLoad`/`FcomEntry` → `AircraftSystem` |
| `ReportsFaultCode` | `DiagnosedBy` | `TsmProcedure` → `FaultCode` |
| `HasMelItem` | `MelItemOf` | `TsmProcedure` → `MelItem` |
| `RoutesHarness` | `RoutedOnSheet` | `WdmSheet` → `Harness` |
| `NextHigherAssembly` | `AssemblyOfFigure` | `IpcFigure` → `PartNumber` |
| `RangeFromMsn` / `RangeToMsn` | `StartsRange` / `EndsRange` | `MsnRangeApplicability` → `Msn` |
| `AssignedTo` | `HasAssignment` | `PreAnalysisWorkOrder` → `_User` — the **only** record of ownership |
| `DelegatedTo` | `HasDelegation` | `PreAnalysisWorkOrder` → `_User` |
| `SpecialisesIn` | `SpecialistIs` | `_User` → `AtaChapter` |
| `WorksAtSite` | `SiteOfUser` | `_User` → `Site` |
| `InDepartment` | `DepartmentOfUser` | `_User` → `Department` |
| `HasUserRole` | `UserRoleOf` | `_User` → `Classification` (kind `UserRole`) |
| `AppliesRule` | `RuleAppliedBy` | `PreAnalysisWorkOrder` → `Rule` |
| `RuleForAta` | `HasRule` | `Rule` → `AtaChapter` |
| `_AttachmentOf` | `_HasAttachment` | `_FileEntry` → issue, document or `DocumentationSource` (§5.4) |
| `SourceOf` | `HasSource` | `DocumentationSource` → the issue it rendered |
| `IssueOfDocument` | `HasIssue` | `{Type}Issue` → document |
| `SupersedesIssue` | `SupersededByIssue` | `{Type}Issue` → the issue before it |
| `IssueRaisedBy` | `RequiredIssue` | `{Type}Issue` → `ExpectedFinding` |

---

## 5. Users, assignment and workflow state

### 5.1 Users

`data/reference/users.csv` drives a `graph.CreateUserAsync(userName, email,
firstName, lastName)` per row, then `SpecialisesIn` edges to the ATA 2D
chapters that user covers. Users are **not** `[Node]` classes — `_User` is an
internal node type the graph owns; the connector holds the returned `Node`.

Because it is graph-owned there is nowhere to add properties, so the remaining
columns are edges to the same nodes the documents are classified by:
`WorksAtSite` → `Site`, `InDepartment` → `Department`, `HasUserRole` →
`Classification` (kind `UserRole`, so it goes through `Classify` like any other). That is what puts an engineer one hop from
the work of their own office, and it is why the dashboard can render their
profile from UIDs instead of from strings the endpoint had to spell out.

Each user has 2–4 ATA 2D specialities and a home site. Specialities overlap so
that more than one user is a plausible owner for most chapters, and a few
chapters have no specialist at all — that is what makes "Unassigned Items" on
the dashboard non-empty.

### 5.2 Deterministic assignment

Assignment is computed in the connector, not stored in the dataset, so it can
be re-tuned without regenerating documents. It must be **deterministic**: seed
every choice from a stable hash of the PAWO's `doc_id`, never from `Random()`
without a seed and never from wall-clock time.

```csharp
static int StableSeed(string docId)                    // FNV-1a over the id
static User PickOwner(string docId, string ata2d, …)   // specialists first, then anyone
```

Rules:
- ~82 % of PAWOs get an `AssignedTo`; the rest stay unassigned on purpose.
- A PAWO whose ATA 2D has a specialist goes to a specialist ~75 % of the time.
- ~12 % of assigned PAWOs also carry `DelegatedTo` a second user.

**The pass is split in two around the document ingest, and both halves matter.**

`AssignmentPass.Plan(ctx, users)` runs **before** `DocumentsIngest`, reads the
documents and the users, and writes nothing. `DocumentsIngest` takes its result
and folds `WorkflowStatus`, `IsUrgent` and `HoursSpent` into the single
`AddOrUpdate` that creates the work-order node — so the node is written once,
complete, and the clobbering trap in §2 never arises. The plan's fingerprint is
part of the document's cache key, so re-tuning the staffing model re-writes the
work orders it moved even though nothing in the dataset changed.

`AssignmentPass.LinkAsync(ctx, plan)` runs **after**, once both ends exist, and
writes the ownership as `AssignedTo` / `DelegatedTo` edges.

**Ownership is an edge and only an edge.** There is deliberately no
`IsAssigned` flag and no `AssignedToUserName` property: a name duplicated beside
the edge is a second copy of the same fact, and the two drift the moment either
is rewritten. Endpoints answer "whose is this?" by traversing `HasAssignment`
from the user — which is also how the dashboard's "mine" list and its count are
guaranteed to agree.

### 5.3 Workflow properties on `PreAnalysisWorkOrder`

The dashboard computes every metric live from these, so they must be populated
for both the open work orders and the historical ones:

| Property | Meaning |
| --- | --- |
| `WorkflowStatus` | `UNASSIGNED` · `ASSIGNED` · `IN_PROGRESS` · `DELEGATED` · `VALIDATED` · `REJECTED` · `ON_HOLD` — spelled like the authored vocabularies so it shares the `Status` nodes they use, and linked over `HasWorkflowStatus` |
| `Outcome` | `Validated` · `Rejected` · `Delegated` · `null` while open |
| `OpenedDate` / `CompletedDate` | drive throughput-per-week and hours-per-PAWO |
| `HoursSpent` | `assigned.spent_manhours`, or a seeded value for historical ones |
| `IsUrgent` | due within 14 days, or carrying a `BLOCKING` finding |
| `DueDate` | from `type_data.due_date` |
| `FindingCount` / `BlockingFindingCount` / `ConfirmedImpactCount` | denormalised for cheap dashboard counts |

Ownership is **not** in this table on purpose — see §5.2. Every property here is
written by `DocumentsIngest` in the node's single `AddOrUpdate`.

**Historical work orders are required.** Five open PAWOs per program is far too
thin to draw a throughput chart or a last-month status split. The dataset
therefore also carries closed pre-analyses against released MODs — see
`aeralis-io-pawo-documents` §6. The connector treats them identically; only
`WorkflowStatus` and `CompletedDate` differ.

---

### 5.4 Attachments and the source they were rendered from

`AttachmentsIngest` renders every document to the file a technical data system would
hold for it and uploads it, so a node in the graph opens as the published document
rather than as JSON. The rendering lives in `Aeralis.Attachments` — read
[its README](../../../src/Aeralis.Attachments/README.md) before changing what a
document produces; this section is only how it reaches the graph.

Two things land, not one. The **output** hangs off the document:

```csharp
var file = await ctx.Graph.UploadFileAsync(stream, attachment.FileName,
                                           SourceName, checkHash: true, silent: true);
ctx.Graph.Link(file, Node.FromKey(nodeType, doc.DocId),
               Edges.AttachmentOf, Edges.HasAttachment);
```

The **input** — the CSDB object the presentation layer was handed — gets its own node,
with the illustrations it names attached to *that*:

```csharp
var node = ctx.Graph.AddOrUpdate(new DocumentationSource { SourceId = "AMM:AMM-M-21-…", Source = xml, … });
ctx.Graph.Link(node, document, Edges.SourceOf, Edges.HasSource);
// then each ICN image is uploaded and linked _AttachmentOf → node
```

| `DocumentationSource` | |
| --- | --- |
| `[Key]` | `SourceId` — `{DocType}:{DocId}:{Issue}`. An identifier alone does not say which type it is (`MRD-53-1042` does not say "SB"), and every issue was rendered from its own markup |
| `[Property]` | `DocType`, `DocId`, `Issue`, `Title`, `ProgramLetter`, `ObjectType`, `Schema`, `DataModuleCode`, `Source`, `SourceLength`, `IllustrationCount`, `Illustrations` |
| `[Timestamp]` | the document's release date |

- **The markup is a property, the artwork is a file.** A property is searchable and
  renders in place, which is what you want of markup you are about to read; an
  illustration is a picture and belongs in the attachment list.
- **A figure has no source.** An IPC document is a drawing, with no CSDB object behind
  it, so it produces a page attachment and no `DocumentationSource`.
- **Rendered per issue.** Each issue was published in its own right, so each has its own
  markup, artwork and PDF. The current issue's file keeps the document's own name
  (`MOD-160142.pdf`) and is attached to both the issue and the document; a superseded one
  carries its letter (`MOD-160142-A.pdf`) and hangs off its issue only.

- **`_HasAttachment` / `_AttachmentOf` are the workspace's own edge names**, not
  ours — hence the underscore. An uploaded file is a `_FileEntry`, a node type the
  graph owns, and the application already renders `_HasAttachment` as the
  attachment list of whatever the file hangs off. Anything else would give us edges
  nothing in the product knows how to show.
- **One source name** (`aeralis-documents`) for the whole corpus, so it
  groups in the workspace and can be replaced wholesale.
- **`UploadFileAsync` returns the node.** There is no key to reconstruct it from, so
  the link is made on the spot, from the returned node — this is the one place a
  handler does not use `Node.FromKey` for both ends.
- **Rendering is parallel, uploading is not.** The pending buffer is not safe to have
  several threads inside; rendering, which is where the time goes, runs across cores
  behind a semaphore that serialises the upload.
- **The HashCache fingerprint is the document's own content**, so an untouched
  document is neither re-rendered nor re-uploaded. A render that throws leaves its
  hash unrecorded and is retried next run, without abandoning the pass.
- **It is the only slow step.** `--no-attachments` skips it, `--attachment-threads N`
  sets the width, `--attachments-out <dir>` also writes every file to disk — which is
  the only way to see the output of a `--dry-run`.

---

## 6. What the connector must NOT link

The whole point of the demo is that the application discovers the rest. Create
an edge **only** when the source states the relationship in a structured field.

| Do link | Do not link |
| --- | --- |
| `entities.part_numbers[]` → `PartNumber` | a part number that only appears in prose |
| `references[].doc_id` → that document | two documents that merely read alike |
| `applicability.msns[]` → `Msn` | an MSN inferred from a range at query time |
| `expected_findings[].doc_id` → target | a finding the search is supposed to rediscover |

NLP-derived relationships arrive later as `_AppearsIn` / `_Mentions` edges
produced by the workspace's own pipeline. The connector never writes those, and
the front-end treats them as a separate, additive layer.

`ExpectedFinding` nodes are the one apparent exception, and they are not one:
they are *ground truth for evaluation*, linked as `FindingTargets`, not as a
claim that the impact is confirmed. A confirmed impact is a distinct edge
(`ConfirmedImpact`) written only by the front-end's confirm flow.

---

## 7. Running it

```bash
dotnet run --project src/Aeralis.Connector -- \
    --root . --endpoint "$CURIOSITY_ENDPOINT" --token "$CURIOSITY_TOKEN"

dotnet run --project src/Aeralis.Connector -- --root . --dry-run   # parse + map, no writes
dotnet run --project src/Aeralis.Connector -- --root . --reset-cache
```

`--dry-run` exercises every handler and prints the node/edge counters without
connecting, which is the cheap way to check a mapping change.

The cache lives at `cache/aeralis-connector.db` next to the binary and is
gitignored. Losing it costs one full re-ingest and nothing else.

## 8. Checklist

- [ ] One handler per input type, DTOs and `[Node]` classes nested inside it
- [ ] Every handler bumps its own `Version` when its mapping changes
- [ ] `CommitPendingAsync` before `CommitPendingHashesAsync`, always
- [ ] Links use `Node.FromKey`, never a fetch
- [ ] Every edge registered in `Edges.cs` in both directions
- [ ] No edge that NLP is supposed to discover
- [ ] Assignment reproducible: same dataset ⇒ same owners, statuses and dates
