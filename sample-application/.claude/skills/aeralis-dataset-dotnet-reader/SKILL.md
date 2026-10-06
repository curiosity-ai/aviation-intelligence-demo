---
name: aeralis-dataset-dotnet-reader
description: The C# reader for the Aeralis MOD pre-analysis demo dataset — the Aeralis.Dataset class library (System.Text.Json models for every document type, CsvHelper models for every reference CSV) and the Aeralis.DatasetCheck console app that parses the whole dataset, verifies the schema round-trips, and prints per-type file counts and megabytes read. Use when adding a data type, changing a schema, or running the .NET schema check.
---

# .NET reader — `Aeralis.Dataset` + `Aeralis.DatasetCheck`

The C# projects are the **executable schema**. If the dataset deserialises
cleanly into these types with no unmapped JSON members and no vocabulary
violations, the schema is correct. This complements `tools/validate.py`, which
checks *referential* integrity; the .NET check verifies *structural* fidelity.

```
src/
├── Aeralis.slnx
├── Aeralis.Dataset/          class library — one class per data type
│   ├── Aeralis.Dataset.csproj
│   ├── Enums.cs               controlled vocabularies as string-backed enums
│   ├── Documents/             TechDocument + 9 type_data payloads
│   ├── Reference/             9 CsvHelper record types
│   └── DatasetLoader.cs       loads everything, reports bytes + counts
└── Aeralis.DatasetCheck/     console app
    ├── Aeralis.DatasetCheck.csproj
    └── Program.cs
```

Run:

```bash
dotnet run --project src/Aeralis.DatasetCheck -- --root .
dotnet run --project src/Aeralis.DatasetCheck -- --root . --strict --sample 3
```

`--strict` turns unmapped JSON members and vocabulary misses into a non-zero
exit code. `--sample N` prints N parsed examples per type.

---

## 1. Document model

`TechDocument` mirrors the envelope in `aeralis-dataset-conventions` §6
exactly, with `[JsonPropertyName]` on every member.

```csharp
public sealed class TechDocument
{
    [JsonPropertyName("doc_id")]        public string DocId { get; set; } = "";
    [JsonPropertyName("doc_type")]      public DocType DocType { get; set; }
    [JsonPropertyName("title")]         public string Title { get; set; } = "";
    [JsonPropertyName("program")]       public ProgramLetter Program { get; set; }
    [JsonPropertyName("program_name")]  public string? ProgramName { get; set; }
    [JsonPropertyName("revision")]      public string? Revision { get; set; }
    [JsonPropertyName("status")]        public string? Status { get; set; }
    [JsonPropertyName("language")]      public string? Language { get; set; }
    [JsonPropertyName("ata")]           public AtaRef? Ata { get; set; }
    [JsonPropertyName("secondary_ata")] public List<string> SecondaryAta { get; set; } = [];
    [JsonPropertyName("applicability")] public Applicability? Applicability { get; set; }
    [JsonPropertyName("embodiment")]    public Embodiment? Embodiment { get; set; }
    [JsonPropertyName("dates")]         public DocumentDates? Dates { get; set; }
    [JsonPropertyName("abstract")]      public string Abstract { get; set; } = "";
    [JsonPropertyName("sections")]      public Dictionary<string, string> Sections { get; set; } = [];
    [JsonPropertyName("entities")]      public EntityRefs? Entities { get; set; }
    [JsonPropertyName("references")]    public List<DocumentReference> References { get; set; } = [];
    [JsonPropertyName("keywords")]      public List<string> Keywords { get; set; } = [];
    [JsonPropertyName("originator")]    public Originator? Originator { get; set; }
    [JsonPropertyName("type_data")]     public JsonElement TypeData { get; set; }
    [JsonPropertyName("_meta")]         public DocumentMeta? Meta { get; set; }

    [JsonExtensionData]                 public Dictionary<string, JsonElement>? Unmapped { get; set; }
}
```

`type_data` stays a `JsonElement` on the envelope and is re-deserialised into
the strongly-typed payload chosen by `DocType`:

| `DocType` | `type_data` class |
| --- | --- |
| `MOD` | `ModData` |
| `SB` | `ServiceBulletinData` |
| `IO` | `InternalOrderData` |
| `PAWO` | `PreAnalysisWorkOrderData` |
| `AMM` | `AmmTaskData` |
| `IPC` | `IpcFigureData` |
| `SRM` | `SrmRepairData` |
| `TSM` | `TsmProcedureData` |
| `WDM` | `WdmSheetData` |

`TechDocument<TPayload>` is the typed view returned by
`DatasetLoader.LoadDocuments<TPayload>(DocType)`.

**Rule: `[JsonExtensionData]` exists to *report* drift, not to hide it.** Every
payload class carries one, and the checker fails on any member that lands
there — an unmapped member means the C# model and the skills disagree.

---

## 2. Reference model (CsvHelper)

One `sealed class` per CSV, `[Name("column")]`-mapped, with a
`CsvConfiguration` whose `HeaderValidated` / `MissingFieldFound` hooks report
to the checker so a column rename surfaces instead of being swallowed.

| CSV | Record |
| --- | --- |
| `ata_chapters.csv` | `AtaChapter` |
| `part_numbers.csv` | `PartNumberRow` |
| `fins.csv` | `FinRow` |
| `msn_registry.csv` | `MsnRow` |
| `drawings.csv` | `DrawingRow` |
| `software.csv` | `SoftwareRow` |
| `zones.csv` | `ZoneRow` |
| `operators.csv` | `OperatorRow` |
| `suppliers.csv` | `SupplierRow` |

Pipe-separated columns (`programs`, `domains`, `access_panels`,
`fleet_programs`) use `PipeListConverter` and surface as `List<string>`.

`ZoneRow` and `AtaChapter` additionally carry the illustration placement that
`src/Aeralis.Illustrations` uses to aim a callout at the airframe:

| Member | Column | Value |
| --- | --- | --- |
| `AirframeSection` | `airframe_section` | an `AircraftSection` enum name (`ForwardFuselage`, `LeftWing`, …) |
| `ViewHint` | `view_hint` | a `ViewKind` enum name — `Plan`, `Side`, `Front` |
| `SectionPosition` (`ZoneRow` only) | `section_position` | `double` 0.05–0.95 along the section, 0 = forward/inboard |

Both are read as `string`, not as the `Aeralis.Illustrations` enums —
`Aeralis.Dataset` does not reference that project. The vocabularies are
enforced on the Python side by `tools/validate.py --only reference`. The
`double` parses under `CultureInfo.InvariantCulture`, matching the `.` separator
the generator writes.

---

## 3. Enums

Controlled vocabularies from the skills become enums with a
`JsonStringEnumConverter`. Anything unrecognised deserialises to `Unknown`
**and is reported**, so a typo in the data shows up as a vocabulary violation
rather than a crash or a silent default.

Covered: `DocType`, `ProgramLetter`, `EmbodimentStatus`, `ModClass`,
`ChangeCategory`, `ModTrigger`, `CertificationImpact`, `Interchangeability`,
`RiskLevel`, `SbType`, `Compliance`, `DowntimeCategory`, `IoStatus`,
`RequesterType`, `Priority`, `PawoStatus`, `Recommendation`, `FindingType`,
`Severity`, `DiscoveryMode`, `AmmTaskType`, `RepairCategory`, `DamageType`,
`NdtMethod`, `DiagramType`, `RelationType`, `ApplicabilityScope`.

---

## 4. Checker output

```
DOCUMENTS
  Program    IO   MOD  PAWO    SB   AMM   IPC   SRM   TSM   WDM   total
  P           6    30     6    22    30    10     8     8     5     125
  ...
  total      30   150    30   110   150    50    40    40    25     625

  by type: MOD 150 files 2.40 MB · SB 110 files 1.70 MB · ...

SCHEMA
  documents parsed .......... 625 / 625
  unmapped JSON members ..... 0
  vocabulary violations ..... 0

TOTAL
  files read ................ 634
  bytes read ................ 11.42 MB
```

The two required outputs — **file counts per type** and **megabytes read** —
are the `DOCUMENTS` table and the `TOTAL` block.

---

## 5. When you change a schema

1. Update the relevant skill (the spec).
2. Update the C# model.
3. Update `tools/validate.py` if the change is referential.
4. Re-run both gates:
   ```bash
   python3 tools/validate.py
   dotnet run --project src/Aeralis.DatasetCheck -- --root . --strict
   ```

Both must be clean before committing.
