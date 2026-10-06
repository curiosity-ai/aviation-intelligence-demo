"""Shared document schema: envelope shape, ID patterns and controlled vocabularies.

The single Python-side source of truth for what a document must look like.
Mirrors ``.claude/skills/aeralis-dataset-conventions``. The C# models in
``src/Aeralis.Dataset`` mirror the same contract independently — if the two
ever disagree, one of them is wrong and the skill decides which.
"""
from __future__ import annotations

import re

PROGRAMS = ["M", "C", "T", "Z", "S"]

PROGRAM_META = {
    "M": {"name": "MERIDIAN Family", "sb_prefix": "MRD", "mod_min": 160000, "mod_max": 169999,
          "types": ["MERIDIAN-42", "MERIDIAN-46", "MERIDIAN-42F"],
          "models": ["MRD-42K1", "MRD-46K1", "MRD-42FK1"]},
    "C": {"name": "CIRRANE", "sb_prefix": "CRN", "mod_min": 110000, "mod_max": 119999,
          "types": ["CIRRANE-11", "CIRRANE-13"],
          "models": ["CRN-11G1", "CRN-13G1"]},
    "T": {"name": "TITAN", "sb_prefix": "TTN", "mod_min": 140000, "mod_max": 149999,
          "types": ["TITAN-88"],
          "models": ["TTN-88W1", "TTN-88W2", "TTN-88E1"]},
    "Z": {"name": "ZEPHYR Family", "sb_prefix": "ZPH", "mod_min": 150000, "mod_max": 159999,
          "types": ["ZEPHYR-18", "ZEPHYR-20", "ZEPHYR-22", "ZEPHYR-24"],
          "models": ["ZPH-18C1", "ZPH-18C2", "ZPH-20C2", "ZPH-20C5", "ZPH-20N3",
                     "ZPH-22C4", "ZPH-22V2", "ZPH-22N1", "ZPH-22N7",
                     "ZPH-24C1", "ZPH-24V1", "ZPH-24N3", "ZPH-24N7"]},
    "S": {"name": "SOLANO Family", "sb_prefix": "SLN", "mod_min": 130000, "mod_max": 139999,
          "types": ["SOLANO-30", "SOLANO-34", "SOLANO-38", "SOLANO-39", "VANTOR-64", "VANTOR-68"],
          "models": ["SLN-30R3", "SLN-30V3", "SLN-34R3", "SLN-34V3", "SLN-38R1",
                     "SLN-39R1", "VTR-64C3", "VTR-68R2"]},
}

DOC_TYPES = ["IO", "MOD", "PAWO", "SB", "AMM", "IPC", "SRM", "TSM", "WDM", "FCOM"]

# ---------------------------------------------------------------------------
# identifier patterns
# ---------------------------------------------------------------------------

ID_PATTERNS = {
    "IO":   re.compile(r"^IO-(?P<prog>[MCTZS])-\d{6}$"),
    "MOD":  re.compile(r"^MOD-(?P<num>\d{5,6})$"),
    "PAWO": re.compile(r"^PAWO-(?P<num>\d{5,6})-\d{2}$"),
    "SB":   re.compile(r"^(?P<prefix>MRD|CRN|TTN|ZPH|SLN)-(?P<ata2>\d{2})-\d{4}$"),
    "AMM":  re.compile(r"^AMM-(?P<prog>[MCTZS])-(?P<ata6>\d{2}-\d{2}-\d{2})-\d{3}-\d{3}-[A-Z]$"),
    "IPC":  re.compile(r"^IPC-(?P<prog>[MCTZS])-(?P<ata6>\d{2}-\d{2}-\d{2})-FIG-\d{2}$"),
    "SRM":  re.compile(r"^SRM-(?P<prog>[MCTZS])-(?P<ata6>\d{2}-\d{2}-\d{2})-\d{3}$"),
    "TSM":  re.compile(r"^TSM-(?P<prog>[MCTZS])-(?P<ata6>\d{2}-\d{2}-\d{2})-\d{3}-\d{3}$"),
    "WDM":  re.compile(r"^WDM-(?P<prog>[MCTZS])-(?P<ata4>\d{2}-\d{2})(-\d{2})?$"),
    "FCOM": re.compile(r"^FCOM-(?P<prog>[MCTZS])-(?P<section>DSC|PRO|LIM|PER)"
                       r"-(?P<ata4>\d{2}-\d{2})-\d{2}$"),
}

FIGURE_RE = re.compile(r"^FIG-\d{2}-\d{2}-\d{2}-\d{2}$")
REPAIR_DRAWING_RE = re.compile(r"^RD-\d{2}-\d{2}-\d{2}-\d{3}$")
MSN_RE = re.compile(r"^[MCTZS]\d{5}$")
ATA6_RE = re.compile(r"^\d{2}-\d{2}-\d{2}$")
ATA4_RE = re.compile(r"^\d{2}-\d{2}$")

SB_PREFIX_TO_PROGRAM = {m["sb_prefix"]: p for p, m in PROGRAM_META.items()}


def program_of_doc_id(doc_id: str, doc_type: str):
    """Derive the program letter from an identifier, or None if not encoded."""
    m = ID_PATTERNS[doc_type].match(doc_id)
    if not m:
        return None
    groups = m.groupdict()
    if "prog" in groups and groups.get("prog"):
        return groups["prog"]
    if doc_type == "SB":
        return SB_PREFIX_TO_PROGRAM.get(groups["prefix"])
    if doc_type in ("MOD", "PAWO"):
        num = int(groups["num"])
        for prog, meta in PROGRAM_META.items():
            if meta["mod_min"] <= num <= meta["mod_max"]:
                return prog
    return None


# ---------------------------------------------------------------------------
# envelope
# ---------------------------------------------------------------------------

ENVELOPE_ORDER = [
    "doc_id", "doc_type", "title", "program", "program_name", "revision", "issue", "status",
    "language", "ata", "secondary_ata", "applicability", "embodiment", "dates",
    "abstract", "sections", "entities", "references", "keywords", "originator",
    "type_data", "_meta",
]

REQUIRED_KEYS = ["doc_id", "doc_type", "title", "program", "ata", "applicability",
                 "dates", "abstract", "sections", "entities", "type_data"]

ENTITY_KEYS = ["part_numbers", "fins", "drawings", "software", "zones", "figures",
               "repair_drawings", "msn_mentions"]

APPLICABILITY_KEYS = ["scope", "aircraft_types", "models", "msns", "msn_ranges",
                      "excluded_msns", "engine_types", "operators", "embodiment_condition"]

EMBODIMENT_CONDITION_KEYS = ["post_mod", "pre_mod", "post_sb", "pre_sb"]

DATE_KEYS = ["created", "released", "revised", "effective"]

# --- issues ----------------------------------------------------------------
#
# A document file holds an *array* of issues, oldest first; the last one is the document as it
# stands. Every element is a complete envelope, so an issue may differ from its predecessor in
# any field — which is the point: an issue exists because something in the document had to
# change.

ISSUE_KEYS = ["letter", "supersedes", "date", "reason", "raised_by_pawo", "raised_by_finding",
              "finding_type"]

#: Issue letters, in order.
ISSUE_LETTERS = "ABCDEFGH"

#: Types whose revision is numbered rather than lettered. A service bulletin is at issue 01.
NUMERIC_REVISION_TYPES = {"SB"}


def revision_for(doc_type, index):
    """The revision token a type uses for its ``index``-th issue, counting from 1."""
    if doc_type in NUMERIC_REVISION_TYPES:
        return f"{index:02d}"
    return ISSUE_LETTERS[index - 1]


def revision_tokens(doc_type, index):
    """Every spelling accepted for the revision of the ``index``-th issue.

    ``-`` is how the dataset spells an original issue that was never lettered, which several
    types use, so it is accepted alongside the canonical token for the first issue only.
    """
    canonical = revision_for(doc_type, index)
    return {canonical, "-"} if index == 1 else {canonical}


def issue_index(letter):
    """The 1-based position of an issue letter, or None if it is not one."""
    return ISSUE_LETTERS.index(letter) + 1 if letter in ISSUE_LETTERS else None


def default_issue():
    return {"letter": "A", "supersedes": None, "date": None, "reason": "Original issue.",
            "raised_by_pawo": None, "raised_by_finding": None, "finding_type": None}


def default_document():
    return {
        "doc_id": "", "doc_type": "", "title": "", "program": "", "program_name": "",
        "revision": "-", "issue": None, "status": "", "language": "en",
        "ata": {"ata_2d": "", "ata_4d": "", "ata_6d": "", "title": ""},
        "secondary_ata": [],
        "applicability": {
            "scope": "ALL_PROGRAM", "aircraft_types": [], "models": [], "msns": [],
            "msn_ranges": [], "excluded_msns": [], "engine_types": [], "operators": [],
            "embodiment_condition": {"post_mod": [], "pre_mod": [], "post_sb": [], "pre_sb": []},
        },
        "embodiment": None,
        "dates": {"created": None, "released": None, "revised": None, "effective": None},
        "abstract": "", "sections": {},
        "entities": {k: [] for k in ENTITY_KEYS},
        "references": [], "keywords": [], "originator": None, "type_data": {},
        "_meta": {"synthetic": True, "generator": "authored", "dataset_version": "1.0"},
    }


# ---------------------------------------------------------------------------
# controlled vocabularies
# ---------------------------------------------------------------------------

VOCAB = {
    "applicability.scope": ["ALL_PROGRAM", "TYPE", "MODEL", "MSN_RANGE", "MSN_LIST", "OPERATOR"],
    "embodiment.status": ["PROPOSAL", "PRE_ANALYSIS", "LAUNCHED", "FROZEN", "IN_PRODUCTION",
                          "IN_SERVICE", "SUPERSEDED", "CANCELLED"],
    "embodiment.points": ["PRODUCTION", "RETROFIT", "SPARES", "NONE"],
    "relation": ["SUPERSEDES", "SUPERSEDED_BY", "EMBODIED_BY", "EMBODIES", "PREREQUISITE_OF",
                 "REQUIRES", "CONCURRENT_WITH", "IMPACTS", "DOCUMENTED_IN", "REFERENCES",
                 "RAISED_BY", "CREATED", "PRE_ANALYSIS_OF", "CONFLICTS_WITH",
                 "REPLACES_PART_IN"],
    "mod.mod_class": ["MAJOR", "MINOR"],
    "mod.change_category": ["STRUCTURE", "SYSTEM", "CABIN", "SOFTWARE", "POWERPLANT",
                            "ELECTRICAL", "AVIONICS", "DOCUMENTATION", "REPAIR"],
    "mod.trigger": ["IN_SERVICE_FINDING", "CUSTOMER_REQUEST", "OBSOLESCENCE", "WEIGHT_SAVING",
                    "RELIABILITY", "AIRWORTHINESS_DIRECTIVE", "CERTIFICATION",
                    "SUPPLIER_CHANGE", "COST_REDUCTION", "FUEL_BURN",
                    "PRODUCTION_IMPROVEMENT", "SAFETY"],
    "mod.certification.impact": ["NO_IMPACT", "MINOR_CHANGE", "MAJOR_CHANGE", "STC"],
    "mod.interchangeability": ["INTERCHANGEABLE", "ONE_WAY_INTERCHANGEABLE",
                               "NOT_INTERCHANGEABLE"],
    "mod.risk_level": ["LOW", "MEDIUM", "HIGH"],
    "sb.sb_type": ["MODIFICATION", "INSPECTION", "REPAIR", "SOFTWARE_LOAD", "CONVERSION",
                   "INFORMATION", "SUPPLIER"],
    "sb.compliance": ["MANDATORY", "RECOMMENDED", "DESIRABLE", "OPTIONAL"],
    "sb.downtime_category": ["LINE", "A_CHECK", "C_CHECK", "D_CHECK", "AOG"],
    "sb.reason_code": ["FATIGUE", "CORROSION", "OBSOLESCENCE", "RELIABILITY", "SAFETY",
                       "PERFORMANCE", "CUSTOMER", "PRODUCTION", "SOFTWARE_DEFECT",
                       "SUPPLIER_QUALITY"],
    "io.requester_type": ["OPERATOR", "LESSOR", "OEM_INTERNAL", "MRO", "AUTHORITY",
                          "SUPPLIER"],
    "io.request_category": ["STRUCTURE", "SYSTEM", "CABIN", "SOFTWARE", "REPAIR",
                            "POWERPLANT", "AVIONICS", "WEIGHT", "OBSOLESCENCE"],
    "io.priority": ["AOG", "HIGH", "MEDIUM", "LOW"],
    "io.io_status": ["RAISED", "UNDER_REVIEW", "CONVERTED_TO_MOD", "REJECTED", "ON_HOLD",
                     "CLOSED"],
    "io.funding": ["CUSTOMER_FUNDED", "OEM_FUNDED", "WARRANTY", "SHARED", "CONTRACTUAL"],
    "pawo.pawo_status": ["RAISED", "IN_PROGRESS", "ON_HOLD", "COMPLETED", "CANCELLED"],
    "pawo.task.method": ["ENTITY", "SEMANTIC", "ENTITY_AND_SEMANTIC", "MANUAL_REVIEW",
                         "SME_INTERVIEW"],
    "pawo.task.status": ["OPEN", "IN_PROGRESS", "DONE", "BLOCKED"],
    "pawo.recommendation": ["PROCEED", "PROCEED_WITH_CONDITIONS", "REWORK_SCOPE",
                            "MERGE_WITH_EXISTING_MOD", "REJECT", "PENDING"],
    "pawo.finding_type": ["DUPLICATE", "OVERLAPPING_SCOPE", "PREREQUISITE", "SUPERSESSION",
                          "EFFECTIVITY_INVALIDATION", "PART_CONFLICT", "FIN_CONFLICT",
                          "SOFTWARE_CONFLICT", "ZONE_CONFLICT", "REPAIR_CONFLICT",
                          "MANUAL_IMPACT", "CERTIFICATION_IMPACT", "CONCURRENCY_REQUIRED"],
    "pawo.severity": ["BLOCKING", "MAJOR", "MINOR", "INFORMATIONAL"],
    "pawo.discovery": ["ENTITY", "SEMANTIC", "BOTH", "APPLICABILITY", "GRAPH_TRAVERSAL"],
    "amm.task_type": ["REMOVAL", "INSTALLATION", "INSPECTION", "REPAIR", "CLEANING", "TEST",
                      "SERVICING", "DEACTIVATION", "ADJUSTMENT"],
    "srm.repair_category": ["TEMPORARY", "INTERIM", "PERMANENT"],
    "srm.damage_type": ["CRACK", "CORROSION", "DENT", "SCRATCH", "PUNCTURE", "DELAMINATION",
                        "LIGHTNING_STRIKE", "IMPACT", "HOLE"],
    "srm.ndt_method": ["VISUAL", "EDDY_CURRENT", "ULTRASONIC", "X_RAY", "DYE_PENETRANT",
                       "THERMOGRAPHY", "TAP_TEST"],
    "wdm.diagram_type": ["WIRING_DIAGRAM", "SCHEMATIC", "EQUIPMENT_LIST", "CONNECTOR_LIST",
                         "GROUND_POINT_LIST"],
    "fcom.section": ["DSC", "PRO", "LIM", "PER"],
    "fcom.operational_impact": ["NONE", "PROCEDURE_CHANGE", "LIMITATION_CHANGE",
                                "PERFORMANCE_CHANGE", "TRAINING_REQUIRED"],
    "fcom.flight_phase": ["PREFLIGHT", "TAXI", "TAKEOFF", "CLIMB", "CRUISE", "DESCENT",
                          "APPROACH", "LANDING", "ALL_PHASES"],
}

# ---------------------------------------------------------------------------
# reference-data vocabularies
# ---------------------------------------------------------------------------

# Airframe placement of a zone / ATA chapter, used to aim the illustration
# callouts drawn by ``src/Aeralis.Illustrations``. These mirror the
# ``AircraftSection`` and ``ViewKind`` enums in
# ``src/Aeralis.Illustrations/Enums.cs`` and must stay spelled exactly as the
# C# members are.
AIRFRAME_SECTIONS = [
    "Nose", "Cockpit", "ForwardFuselage", "Fuselage", "AftFuselage",
    "LeftWing", "RightWing", "VerticalStabilizer", "HorizontalStabilizer",
    "Tail", "LeftEngine", "RightEngine", "NoseLandingGear", "MainLandingGear",
]

VIEW_KINDS = ["Plan", "Side", "Front"]

SECTION_POSITION_MIN, SECTION_POSITION_MAX = 0.05, 0.95

# Columns each reference CSV must carry, in order.
REFERENCE_COLUMNS = {
    "zones.csv": ["zone", "zone_name", "major_zone", "major_zone_name", "programs",
                  "access_panels", "airframe_section", "view_hint", "section_position"],
    "ata_chapters.csv": ["code", "level", "parent", "title", "domain", "programs",
                         "airframe_section", "view_hint"],
}

# Document counts per program (see TODO.md phase 3).
EXPECTED_COUNTS = {"IO": 12, "MOD": 60, "PAWO": 52, "SB": 44, "AMM": 60, "IPC": 20,
                   "SRM": 16, "TSM": 16, "WDM": 10, "FCOM": 10}

CANDIDATE_STATUSES = {"PROPOSAL", "PRE_ANALYSIS", "LAUNCHED"}
RELEASED_STATUSES = {"FROZEN", "IN_PRODUCTION", "IN_SERVICE", "SUPERSEDED", "CANCELLED"}

DATASET_START = "2016-01-01"
DATASET_END = "2026-08-07"

# ---------------------------------------------------------------------------
# Entry into service, and the documents whose history reaches back to it
# ---------------------------------------------------------------------------

#: Document types that live as long as the aircraft does. A maintenance task, a parts
#: figure, a repair, a fault-isolation procedure, a wiring sheet and a crew entry are all
#: first issued around the time the type enters service and revised for decades after; the
#: dataset's window opens in 2016, so where it holds a document's *original* issue that
#: issue is dated from the aircraft instead.
#:
#: The change artefacts — MOD, SB, IO, PAWO — are deliberately not here. A modification
#: dossier is dated to the change it describes, and every change in this dataset is
#: current.
LONG_LIVED_DOC_TYPES = {"AMM", "IPC", "SRM", "TSM", "WDM", "FCOM"}

#: How far before a type enters service its manuals are already being written. The floor
#: for `dates.created` on an original issue, and the reason it may precede the type's first
#: delivery.
AUTHORING_LEAD_DAYS = 730


def entry_into_service(msn_rows, aircraft_types, program):
    """The date the earliest of these aircraft types entered service.

    Taken from the fleet rather than from a table of its own: `msn_registry.csv` records a
    delivery date per aircraft, and the first delivery of a type *is* its entry into
    service. Deriving it means the two can never disagree, and that re-generating the
    registry moves the manuals with it.

    ``aircraft_types`` empty means the document applies to the whole programme, so every
    type in it counts.

    Returns an ISO date string, or None when nothing matches.
    """
    wanted = set(aircraft_types or [])
    if not wanted:
        wanted = set((PROGRAM_META.get(program) or {}).get("types") or [])
    if not wanted:
        return None

    delivered = [row["delivery_date"] for row in msn_rows
                 if row.get("aircraft_type") in wanted and row.get("delivery_date")]

    return min(delivered) if delivered else None
