# Reference vocabularies

The master lists the workspace spots inside articles. Generated, never hand-edited:

```bash
python3 tools/build_reference_vocabularies.py
```

| File | Rows | Source | Node type |
| --- | --- | --- | --- |
| `organisations.csv` | airlines, aerospace/space/defence companies, launch providers, aviation authorities, space agencies, and every manufacturer the aircraft and engine lists name | [Wikidata](https://www.wikidata.org) (CC0) | `Organisation` |
| `aircraft.csv` | aircraft families and models, first flight 1950 or later, with ≥ 10 Wikipedia articles | Wikidata | `AircraftType` |
| `engines.csv` | the engines those aircraft are "powered by", plus engine families | Wikidata | `EngineType` |
| `airports.csv` | large airports with scheduled service | [OurAirports](https://ourairports.com/data/) (public domain) | `Airport` |

`ManufacturerIds` and `EngineIds` are Wikidata's own statements (manufacturer P176, powered by P516);
the connector writes them as `AircraftBuiltBy`, `EngineBuiltBy` and `PoweredBy` edges.

## Terms

`Terms` is what the workspace spots in text, written as aliases on the node. The spotters are built
from the key (a Wikidata id or an ICAO code, which never occurs in prose) with aliases included, and
are case-sensitive - so what is found is exactly this column.

The generator filters the label and the aliases by rule, the same way on every run:

- dropped: shorter than 3 characters, purely numeric, not in Latin script;
- dropped: a single word among the 10,000 most common English words - or, for an all-capitals
  acronym, among the 1,000 most common (case-sensitive spotting means "LEAP" never matches "leap");
- a term two entities claim goes to the one whose own name it is, else to the one with more
  Wikipedia articles;
- for aircraft and engines, the name without its manufacturer's leading words is added when the
  rest is a designation ("Airbus A350" → "A350", "CFM International LEAP" → "LEAP");
- for airports, the IATA code, the name without its city and "International Airport", and the
  OurAirports keywords that share a word with the name (the others are other-language city names).

An entity whose name is itself a common word ("Emirates", "Level") stays in the graph and is only
spotted by its longer terms. Wikidata's data quality varies; a false mention is fixed by fixing the
rule here and regenerating, never by editing a row.
