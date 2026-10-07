#!/usr/bin/env python3
"""Builds the entity vocabularies the workspace spots inside articles.

    python3 tools/build_reference_vocabularies.py            # writes data/reference/*.csv

Four master lists, each from a public reference source - never hand-authored:

  organisations.csv  Wikidata: airlines, aerospace / space / defence companies, launch providers,
                     aviation authorities, space agencies, and every manufacturer the aircraft and
                     engine lists name
  aircraft.csv       Wikidata: aircraft families and models (first flight 1950 or later)
  engines.csv        Wikidata: the engines those aircraft are "powered by", plus engine families
  launch_vehicles.csv Wikidata: rocket models, series and families
  airports.csv       OurAirports: large airports with scheduled service

Each row's Terms are what the workspace spots in text - the name included only when it passes
the same rules as the aliases. What a spotter finds is only as good as its terms, so they are
filtered by rule:
a term shorter than 3 characters, purely numeric, not in Latin script, a Roman numeral, a lower-case phrase ("air defense"), a generic industry word ("Aero"), a common English word
(google-10000-english), a phrase made only of the 2,000 most common words with no industry word
in it ("Middle East"), an airline's own ICAO code, or a one-word nickname of an aircraft that
first flew before 1990 ("Fury"), or a product's one-word name that begins an organisation's name
("Pegasus", "Condor") is dropped. A term two entities claim goes to the one whose own name it is,
else to the one with more Wikipedia articles (an airport counts as having none) - and a name two
entities both carry as their own ("Falcon") names neither, and so does an acronym two entities
share unless one has three times the other's coverage. Organisations also get their name without
its legal form ("Northrop Grumman Corporation" → "Northrop Grumman"). The rules are applied
the same way on every run, so the same sources give the same files.

Wikidata is CC0; OurAirports is public domain. Both are credited in data/reference/README.md.
"""

import csv
import io
import json
import re
import sys
import time
import unicodedata
import urllib.parse
import urllib.request
from collections import defaultdict
from pathlib import Path

ROOT       = Path(__file__).resolve().parent.parent
OUT        = ROOT / "data" / "reference"
USER_AGENT = "aviation-intelligence-demo/1.0 (https://github.com/curiosity-ai/aviation-intelligence-demo)"
SPARQL     = "https://query.wikidata.org/sparql"
AIRPORTS   = "https://davidmegginson.github.io/ourairports-data/airports.csv"
COMMON     = "https://raw.githubusercontent.com/first20hours/google-10000-english/master/google-10000-english-no-swears.txt"

# Organisation kinds, in precedence order: the first that applies names the organisation.
ORG_CLASSES = [
    ("Aviation authority", "?x wdt:P31 wd:Q1875618 ."),
    ("Space agency",       "?x wdt:P31 wd:Q17505024 . ?x wikibase:sitelinks ?s . FILTER(?s >= 8)"),
    ("Armed force",        "VALUES ?c { wd:Q781132 wd:Q61883 wd:Q4508 wd:Q1274955 wd:Q772547 } ?x wdt:P31 ?c . ?x wikibase:sitelinks ?s . FILTER(?s >= 15) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
    ("Airline",            "?x wdt:P31/wdt:P279* wd:Q46970 . ?x wikibase:sitelinks ?s . FILTER(?s >= 10) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
    ("Launch provider",    "?x wdt:P31 wd:Q11753232 . ?x wikibase:sitelinks ?s . FILTER(?s >= 3) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
    ("Manufacturer",       "?x wdt:P31 wd:Q936518 . ?x wikibase:sitelinks ?s . FILTER(?s >= 5) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
    ("Defence contractor", "?x wdt:P31 wd:Q1934969 . ?x wikibase:sitelinks ?s . FILTER(?s >= 8) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
    ("Aerospace company",  "VALUES ?ind { wd:Q3477363 wd:Q1434048 wd:Q1875606 wd:Q2729047 wd:Q639280 wd:Q392933 } ?x wdt:P452 ?ind . ?x wikibase:sitelinks ?s . FILTER(?s >= 8) FILTER NOT EXISTS { ?x wdt:P576 [] }"),
]

AIRCRAFT_QUERY = """
SELECT ?x ?first ?s (GROUP_CONCAT(DISTINCT STRAFTER(STR(?m), "entity/"); separator="|") AS ?mfr)
                    (GROUP_CONCAT(DISTINCT STRAFTER(STR(?e), "entity/"); separator="|") AS ?eng) WHERE {
  VALUES ?c { wd:Q15056993 wd:Q15056995 }
  ?x wdt:P31 ?c ; wikibase:sitelinks ?s .
  FILTER(?s >= 10)
  OPTIONAL { ?x wdt:P606 ?first }
  FILTER(!BOUND(?first) || YEAR(?first) >= 1950)
  OPTIONAL { ?x wdt:P176 ?m }
  OPTIONAL { ?x wdt:P516 ?e }
} GROUP BY ?x ?first ?s
"""

LAUNCH_VEHICLE_QUERY = """
SELECT ?x ?s (GROUP_CONCAT(DISTINCT STRAFTER(STR(?m), "entity/"); separator="|") AS ?mfr) WHERE {
  VALUES ?c { wd:Q110055303 wd:Q111722634 wd:Q109542585 }
  ?x wdt:P31 ?c ; wikibase:sitelinks ?s .
  FILTER(?s >= 5)
  OPTIONAL { ?x wdt:P176 ?m }
} GROUP BY ?x ?s
"""

# Words that make a phrase of common words specific to this industry: "American Airlines" is a
# name, "Middle East" is not.
INDUSTRY_WORDS = {"air", "airlines", "airline", "airways", "aviation", "aerospace", "aircraft", "airport",
                  "agency", "administration", "authority", "launch", "rocket"}

ENGINE_FAMILY_QUERY = """
SELECT ?x ?s WHERE { ?x wdt:P31 wd:Q15057020 ; wikibase:sitelinks ?s . FILTER(?s >= 3) }
"""


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    common = load_common_words()

    print("aircraft…")
    aircraft = {}
    for row in sparql(AIRCRAFT_QUERY):
        qid = qid_of(row["x"])
        if qid in aircraft and int(aircraft[qid]["s"]) >= int(row["s"]):
            continue
        aircraft[qid] = {"s": row["s"], "first": row.get("first", "")[:4], "mfr": split(row.get("mfr")), "eng": split(row.get("eng"))}

    print("engines…")
    engines = {}
    for qid in sorted({e for a in aircraft.values() for e in a["eng"]}):
        engines[qid] = {"s": "0"}
    for row in sparql(ENGINE_FAMILY_QUERY):
        engines.setdefault(qid_of(row["x"]), {"s": row["s"]})
    for qid, mfrs in values_of(list(engines), "wdt:P176").items():
        engines[qid]["mfr"] = mfrs

    print("launch vehicles…")
    rockets = {}
    for row in sparql(LAUNCH_VEHICLE_QUERY):
        qid = qid_of(row["x"])
        if qid in aircraft or (qid in rockets and int(rockets[qid]["s"]) >= int(row["s"])):
            continue
        rockets[qid] = {"s": row["s"], "mfr": split(row.get("mfr"))}

    print("organisations…")
    orgs = {}
    for kind, pattern in ORG_CLASSES:
        for row in sparql(f"SELECT DISTINCT ?x WHERE {{ {pattern} }}"):
            orgs.setdefault(qid_of(row["x"]), {"kind": kind})
    # "Manufacturer" on Wikidata is sometimes a state or a country (a licence-built type); only an
    # organisation counts.
    named = sorted({m for a in aircraft.values() for m in a["mfr"]} | {m for e in engines.values() for m in e.get("mfr", [])}
                   | {m for r in rockets.values() for m in r["mfr"]})
    for qid in organisations_among(named):
        orgs.setdefault(qid, {"kind": "Manufacturer"})

    print("labels and aliases…")
    names    = labels_and_aliases(list(orgs) + list(aircraft) + list(engines) + list(rockets))
    weight   = sitelinks(list(orgs) + list(aircraft) + list(engines) + list(rockets))
    country  = {q: c for q, c in first_label_of(list(orgs), "wdt:P17").items()}
    icao     = {q: v[0] for q, v in values_of(list(orgs), "wdt:P230", literal=True).items()}

    # An entity without an English label cannot be spotted in English text.
    orgs     = {q: v for q, v in orgs.items()     if q in names}
    aircraft = {q: v for q, v in aircraft.items() if q in names}
    engines  = {q: v for q, v in engines.items()  if q in names}
    rockets  = {q: v for q, v in rockets.items()  if q in names}

    print("airports…")
    airports = load_airports()

    # Terms per entity, before the cross-entity ambiguity rule.
    terms = {}
    for q in orgs:
        terms[("org", q)] = candidate_terms(with_corporate_suffix_dropped(names[q]), prefixes=[])
    for q, a in aircraft.items():
        terms[("aircraft", q)] = candidate_terms(names[q], prefixes=[names[m][0] for m in a["mfr"] if m in names])
    for q, e in engines.items():
        terms[("engine", q)] = candidate_terms(names[q], prefixes=[names[m][0] for m in e.get("mfr", []) if m in names])
    for q, r in rockets.items():
        terms[("rocket", q)] = candidate_terms(names[q], prefixes=[names[m][0] for m in r["mfr"] if m in names])
    places = {normal(a["municipality"]) for a in airports} | {normal(a["country"]) for a in airports}
    for a in airports:
        terms[("airport", a["ident"])] = candidate_terms(airport_names(a, places), prefixes=[])

    airports_by_icao = {a["ident"]: a for a in airports}

    # The first word of a multi-word organisation name ("Pegasus Airlines", "Condor Flugdienst").
    org_first_words = {names[q][0].split()[0].lower() for q in orgs if len(names[q][0].split()) > 1}

    # Each term goes to one entity: the one it names outright, else the most-referenced one.
    def rank(key, t):
        own   = normal(terms[key][0]) == normal(t)
        links = 0 if key[0] == "airport" else int(weight.get(key[1], 0))
        return (0 if own else 1, -links, key)

    owner = {}
    for key, ts in terms.items():
        for t in ts:
            n = normal(t)
            if n not in owner or rank(key, t) < rank(owner[n], t):
                owner[n] = key

    own_names = defaultdict(set)
    for key, ts in terms.items():
        own_names[normal(ts[0])].add(key)
    for n, keys in own_names.items():
        if len(keys) > 1:
            owner[n] = None

    # An acronym two entities share goes to a clear front-runner (three times the Wikipedia
    # coverage of the next: "RAF" is the Royal Air Force), and otherwise names neither ("CSA").
    acronym_claims = defaultdict(set)
    for key, ts in terms.items():
        for t in ts:
            if is_acronym(t):
                acronym_claims[t].add(key)
    for t, keys in acronym_claims.items():
        if len(keys) > 1:
            ranked = sorted(keys, key=lambda k: -(0 if k[0] == "airport" else int(weight.get(k[1], 0))))
            first, second = (int(weight.get(k[1], 0)) if k[0] != "airport" else 0 for k in ranked[:2])
            owner[normal(t)] = ranked[0] if first >= 3 * max(second, 1) else None

    # The name is a term like any other: "Emirates" is a common word, so the airline is spotted as
    # "Emirates Group" and not as every mention of the country.
    def keep(key, label):
        out = []
        for t in terms[key]:
            if owner[normal(t)] != key:
                continue
            if not acceptable(t, common):
                continue
            # An airline's ICAO code (CCA, ISR, FIN) is how a flight plan names it, not a newsroom -
            # and as an acronym it is usually something else.
            if key[0] == "org" and t == icao.get(key[1]):
                continue
            # A product's one-word name that is also how a company is known names neither:
            # "Pegasus" in airline news is the airline, not the rocket.
            if key[0] in ("aircraft", "engine", "rocket") and " " not in t and t.lower() in org_first_words:
                continue
            # A bare nickname of an old type ("Fury", 1954) is somebody else's newer one.
            if key[0] == "aircraft" and " " not in t and not re.search(r"\d", t) and t != label and (aircraft[key[1]]["first"] or "0") < "1990":
                continue
            # An IATA code is three capitals; most three-letter words written in capitals are
            # something else ("GEO" is an orbit before it is Georgetown), so the whole list applies.
            if key[0] == "airport" and t == airports_by_icao[key[1]]["iata"] and t.lower() in common:
                continue
            out.append(t)
        return out

    write(OUT / "organisations.csv", ["Id", "Name", "Kind", "Country", "IcaoCode", "Terms"], sorted(
        [q, names[q][0], v["kind"], country.get(q, ""), icao.get(q, ""), "|".join(keep(("org", q), names[q][0]))]
        for q, v in orgs.items()))

    write(OUT / "aircraft.csv", ["Id", "Name", "FirstFlight", "ManufacturerIds", "EngineIds", "Terms"], sorted(
        [q, names[q][0], v["first"], "|".join(m for m in v["mfr"] if m in orgs), "|".join(e for e in v["eng"] if e in engines),
         "|".join(keep(("aircraft", q), names[q][0]))]
        for q, v in aircraft.items()))

    write(OUT / "engines.csv", ["Id", "Name", "ManufacturerIds", "Terms"], sorted(
        [q, names[q][0], "|".join(m for m in v.get("mfr", []) if m in orgs), "|".join(keep(("engine", q), names[q][0]))]
        for q, v in engines.items()))

    write(OUT / "launch_vehicles.csv", ["Id", "Name", "ManufacturerIds", "Terms"], sorted(
        [q, names[q][0], "|".join(m for m in v["mfr"] if m in orgs), "|".join(keep(("rocket", q), names[q][0]))]
        for q, v in rockets.items()))

    write(OUT / "airports.csv", ["IcaoCode", "Name", "IataCode", "Municipality", "Country", "Terms"], sorted(
        [a["ident"], a["name"], a["iata"], a["municipality"], a["country"], "|".join(keep(("airport", a["ident"]), a["name"]))]
        for a in airports))

    print(f"{len(orgs)} organisations, {len(aircraft)} aircraft, {len(engines)} engines, {len(rockets)} launch vehicles, {len(airports)} airports")


# ── terms ────────────────────────────────────────────────────────────────────

CORPORATE_SUFFIX = re.compile(r",?\s+(Corporation|Corp\.?|Incorporated|Inc\.?|plc|PLC|SE|AG|N\.V\.|NV|S\.A\.|SA|S\.p\.A\.|Ltd\.?|Limited|LLC|Company|Co\.|Holdings|Group)$")


def with_corporate_suffix_dropped(names):
    """Nobody writes "Northrop Grumman Corporation": the name without its legal form is a term too."""
    out = list(names)
    for n in names:
        short = CORPORATE_SUFFIX.sub("", n).strip()
        if short and short != n and len(short.split()) >= 1:
            out.append(short)
    return out


def candidate_terms(names, prefixes):
    """The label and aliases, cleaned, plus each with its manufacturer's name taken off the front."""
    out = []
    for n in names:
        n = re.sub(r"\s*\([^)]*\)", "", n).strip()
        n = re.sub(r"\s+(family|series)$", "", n, flags=re.I).strip()
        if n:
            out.append(n)
        for p in prefixes:
            # The words the name shares with the front of its manufacturer's name come off:
            # "Airbus A350" made by "Airbus Commercial Aircraft" → "A350", "CFM International LEAP"
            # → "LEAP". Only a remainder that is a designation (a digit, or two capitals) is kept,
            # so "Boeing Business Jet" does not become "Business Jet".
            words, shared = n.split(), 0
            for a, b in zip(words, p.split()):
                if a.lower() != b.lower():
                    break
                shared += 1
            rest = " ".join(words[shared:])
            designation = re.search(r"\d", rest) or (sum(c.isupper() for c in rest) >= 2 and not re.fullmatch(r"[IVXLC]+", rest))
            if 0 < shared < len(words) and designation:
                out.append(rest)
    seen, unique = set(), []
    for t in out:
        if t not in seen:
            seen.add(t)
            unique.append(t)
    return unique


GENERIC = {"airport", "international", "intl", "regional", "national", "airfield", "aerodrome"}


def airport_names(a, places):
    """Name, IATA code, the name with its city and "International Airport" taken off, and the
    OurAirports keywords that share a word with the name (the others are other-language city names)."""
    name   = a["name"]
    tokens = {w.lower() for w in re.findall(r"[\w']+", name)} - GENERIC
    out    = [name]
    if a["iata"]:
        out.append(a["iata"])
    core = name
    if a["municipality"] and core.lower().startswith(a["municipality"].lower() + " "):
        core = core[len(a["municipality"]) + 1:]
    words = core.split()
    while words and words[-1].lower().strip(".") in GENERIC:
        words.pop()
    core = " ".join(words)
    if core and normal(core) not in places and core.lower() not in GENERIC:
        out += [core + " Airport", core]
    for k in a["keywords"]:
        if normal(k) not in places and tokens & {w.lower() for w in re.findall(r"[\w']+", k)}:
            out.append(k)
    return out


def is_acronym(term):
    return 2 < len(term) <= 5 and term.isupper() and term.isalpha()


GENERIC_SINGLE = {"aero", "air", "avia", "jet", "jets", "space", "aviation", "aerospace", "airlines", "airways", "airline", "aircraft", "rocket", "launch"}


def acceptable(term, common):
    if len(term) < 3:
        return False
    if term.lower() in GENERIC_SINGLE:
        return False
    if re.fullmatch(r"[\d\s.,\-/]+", term):
        return False
    if any(unicodedata.category(c).startswith("L") and not is_latin(c) for c in term):
        return False
    if re.fullmatch(r"[IVXLC]+", term):
        return False
    words = term.split()
    # A name is capitalised somewhere; a phrase in lower case ("air defense") is a common noun.
    # One-word lower-case brands ("flydubai") are left to the common-word check below.
    if len(words) > 1 and not any(c.isupper() for c in term):
        return False
    lowered = [w.lower().strip(".,'") for w in term.replace("-", " ").split()]
    if len(lowered) > 1 and all(common.get(w, 10**6) < 2000 for w in lowered) and not set(lowered) & INDUSTRY_WORDS:
        return False
    # Spotting is case-sensitive, so an acronym only collides with a word written in capitals
    # (a shouted headline): those are checked against the 1,000 most common words, everything
    # else against all 10,000.
    if len(words) == 1 and common.get(term.lower(), 10**6) < (1000 if term.isupper() else 10**5):
        return False
    return True


def is_latin(c):
    try:
        return "LATIN" in unicodedata.name(c)
    except ValueError:
        return False


def normal(t):
    return re.sub(r"\s+", " ", t.strip().lower())


def load_common_words():
    """word → frequency rank."""
    with urllib.request.urlopen(request(COMMON), timeout=60) as r:
        words = [w.strip().lower() for w in r.read().decode("utf-8").splitlines() if w.strip()]
    return {w: rank for rank, w in reversed(list(enumerate(words)))}


# ── Wikidata ─────────────────────────────────────────────────────────────────

def sparql(query, attempts=4):
    data = urllib.parse.urlencode({"query": query}).encode()
    for i in range(attempts):
        try:
            req = urllib.request.Request(SPARQL, data=data, headers={"User-Agent": USER_AGENT, "Accept": "application/sparql-results+json"})
            with urllib.request.urlopen(req, timeout=180) as r:
                result = json.load(r)
            return [{k: v["value"] for k, v in b.items()} for b in result["results"]["bindings"]]
        except Exception as e:  # noqa: BLE001 - the query service throttles; retry with back-off
            if i == attempts - 1:
                raise
            print(f"  retry after {e}", file=sys.stderr)
            time.sleep(5 * 2 ** i)


def batches(items, size=300):
    for i in range(0, len(items), size):
        yield items[i:i + size]


def labels_and_aliases(qids):
    """qid → [English label, English aliases…]; items without an English label are left out."""
    labels, aliases = {}, defaultdict(list)
    for batch in batches(sorted(set(qids))):
        values = " ".join("wd:" + q for q in batch)
        for row in sparql(f'SELECT ?x ?l WHERE {{ VALUES ?x {{ {values} }} ?x rdfs:label ?l FILTER(lang(?l) = "en") }}'):
            labels[qid_of(row["x"])] = row["l"]
        # Aliases, and the short name (P1813) - which is where "ESA" lives when no alias says so.
        for row in sparql(f'SELECT ?x ?a WHERE {{ VALUES ?x {{ {values} }} {{ ?x skos:altLabel ?a }} UNION {{ ?x wdt:P1813 ?a }} FILTER(lang(?a) = "en") }}'):
            aliases[qid_of(row["x"])].append(row["a"])
    return {q: [l] + sorted(set(aliases[q])) for q, l in labels.items()}


def sitelinks(qids):
    out = {}
    for batch in batches(sorted(set(qids))):
        values = " ".join("wd:" + q for q in batch)
        for row in sparql(f"SELECT ?x ?s WHERE {{ VALUES ?x {{ {values} }} ?x wikibase:sitelinks ?s }}"):
            out[qid_of(row["x"])] = row["s"]
    return out


def organisations_among(qids):
    out = set()
    for batch in batches(qids, 100):
        values = " ".join("wd:" + q for q in batch)
        for row in sparql(f"SELECT DISTINCT ?x WHERE {{ VALUES ?x {{ {values} }} ?x wdt:P31/wdt:P279* wd:Q43229 FILTER NOT EXISTS {{ ?x wdt:P31/wdt:P279* wd:Q56061 }} }}"):
            out.add(qid_of(row["x"]))
    return sorted(out)


def values_of(qids, prop, literal=False):
    out = defaultdict(list)
    for batch in batches(sorted(set(qids))):
        values = " ".join("wd:" + q for q in batch)
        for row in sparql(f"SELECT ?x ?v WHERE {{ VALUES ?x {{ {values} }} ?x {prop} ?v }}"):
            out[qid_of(row["x"])].append(row["v"] if literal else qid_of(row["v"]))
    return {q: sorted(set(v)) for q, v in out.items()}


def first_label_of(qids, prop):
    out = {}
    for batch in batches(sorted(set(qids))):
        values = " ".join("wd:" + q for q in batch)
        rows = sparql(f'SELECT ?x ?l WHERE {{ VALUES ?x {{ {values} }} ?x {prop} ?v . ?v rdfs:label ?l FILTER(lang(?l) = "en") }} ORDER BY ?x ?l')
        for row in rows:
            out.setdefault(qid_of(row["x"]), row["l"])
    return out


def qid_of(uri):
    return uri.rsplit("/", 1)[-1]


def split(value):
    return sorted(v for v in (value or "").split("|") if v)


# ── OurAirports ──────────────────────────────────────────────────────────────

def load_airports():
    with urllib.request.urlopen(request(AIRPORTS), timeout=120) as r:
        rows = list(csv.DictReader(io.StringIO(r.read().decode("utf-8"))))
    with urllib.request.urlopen(request("https://davidmegginson.github.io/ourairports-data/countries.csv"), timeout=60) as r:
        countries = {c["code"]: c["name"] for c in csv.DictReader(io.StringIO(r.read().decode("utf-8")))}
    out = []
    for a in rows:
        if a["type"] != "large_airport" or a["scheduled_service"] != "yes" or not a["icao_code"]:
            continue
        out.append({
            "ident":        a["icao_code"],
            "name":         a["name"],
            "iata":         a["iata_code"],
            "municipality": a["municipality"],
            "country":      countries.get(a["iso_country"], a["iso_country"]),
            "keywords":     [k.strip() for k in a["keywords"].split(",") if k.strip()],
        })
    return out


def request(url):
    return urllib.request.Request(url, headers={"User-Agent": USER_AGENT})


def write(path, header, rows):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)
    print(f"  wrote {path.relative_to(ROOT)} ({len(rows)} rows)")


if __name__ == "__main__":
    main()
