#!/usr/bin/env python3
"""Seeded generator for the reference (master) CSV data.

Produces everything under ``data/reference/``. Deterministic: re-running yields
byte-identical files. See ``.claude/skills/aeralis-reference-data``.

    python3 tools/gen_reference.py            # generate
    python3 tools/gen_reference.py --check    # verify integrity of what exists
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ata_catalog  # noqa: E402

RANDOM_SEED = 20260807
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "data", "reference")

PROGRAMS = ["M", "C", "T", "Z", "S"]

PROGRAM_INFO = {
    "M": {
        "name": "MERIDIAN Family",
        "types": ["MERIDIAN-42", "MERIDIAN-46", "MERIDIAN-42F"],
        "type_weights": [60, 33, 7],
        "models": {"MERIDIAN-42": ["MRD-42K1"], "MERIDIAN-46": ["MRD-46K1"], "MERIDIAN-42F": ["MRD-42FK1"]},
        "engines": {"MERIDIAN-42": ["AUR-8400"], "MERIDIAN-46": ["AUR-9700"], "MERIDIAN-42F": ["AUR-9700"]},
        "msn_max": 700, "msn_count": 240, "eis_year": 2015,
    },
    "C": {
        "name": "CIRRANE",
        "types": ["CIRRANE-11", "CIRRANE-13"],
        "type_weights": [25, 75],
        "models": {"CIRRANE-11": ["CRN-11G1"], "CIRRANE-13": ["CRN-13G1"]},
        "engines": {"CIRRANE-11": ["PYR-1519G", "PYR-1521G"], "CIRRANE-13": ["PYR-1524G", "PYR-1525G"]},
        "msn_max": 900, "msn_count": 200, "eis_year": 2016,
    },
    "T": {
        "name": "TITAN",
        "types": ["TITAN-88"],
        "type_weights": [100],
        "models": {"TITAN-88": ["TTN-88W1", "TTN-88W2", "TTN-88E1"]},
        "engines": {"TITAN-88": ["AUR-9070", "AUR-9072", "CVX-7270"]},
        "msn_max": 260, "msn_count": 120, "eis_year": 2007,
    },
    "Z": {
        "name": "ZEPHYR Family",
        "types": ["ZEPHYR-18", "ZEPHYR-20", "ZEPHYR-22", "ZEPHYR-24"],
        "type_weights": [4, 20, 46, 30],
        "models": {
            "ZEPHYR-18": ["ZPH-18C1", "ZPH-18C2"],
            "ZEPHYR-20": ["ZPH-20C2", "ZPH-20C5", "ZPH-20N3"],
            "ZEPHYR-22": ["ZPH-22C4", "ZPH-22V2", "ZPH-22N1", "ZPH-22N7"],
            "ZEPHYR-24": ["ZPH-24C1", "ZPH-24V1", "ZPH-24N3", "ZPH-24N7"],
        },
        "engines": {
            "ZEPHYR-18": ["VDK-56B"], "ZEPHYR-20": ["VDK-56B", "VNT-2500A", "VDK-1226"],
            "ZEPHYR-22": ["VDK-56B", "VNT-2500A", "VDK-1226", "PYR-1127G"],
            "ZEPHYR-24": ["VDK-56B", "VNT-2500A", "VDK-1232", "PYR-1133G"],
        },
        "msn_max": 12500, "msn_count": 400, "eis_year": 1990,
    },
    "S": {
        "name": "SOLANO Family",
        "types": ["SOLANO-30", "SOLANO-34", "SOLANO-38", "SOLANO-39", "VANTOR-64", "VANTOR-68"],
        "type_weights": [24, 30, 4, 22, 12, 8],
        "models": {
            "SOLANO-30": ["SLN-30R3", "SLN-30V3"], "SOLANO-34": ["SLN-34R3", "SLN-34V3"],
            "SOLANO-38": ["SLN-38R1"], "SOLANO-39": ["SLN-39R1"],
            "VANTOR-64": ["VTR-64C3"], "VANTOR-68": ["VTR-68R2"],
        },
        "engines": {
            "SOLANO-30": ["AUR-7720", "PYR-4170", "CVX-6080"], "SOLANO-34": ["AUR-7720", "CVX-6080"],
            "SOLANO-38": ["AUR-7000"], "SOLANO-39": ["AUR-7000"],
            "VANTOR-64": ["VDK-56C"], "VANTOR-68": ["AUR-5560"],
        },
        "msn_max": 2100, "msn_count": 240, "eis_year": 1994,
    },
}

# --------------------------------------------------------------------------
# vocabularies
# --------------------------------------------------------------------------

DOMAIN_NOUNS = {
    "STRUCTURE": ["FRAME", "STRINGER", "SKIN PANEL", "DOUBLER", "FITTING", "BRACKET", "CLIP",
                  "WEB", "SPAR CAP", "RIB", "INTERCOSTAL", "ANGLE", "SHEAR TIE", "LUG",
                  "STRAP", "PLATE", "CLEAT", "SPLICE PLATE", "STIFFENER", "GUSSET"],
    "SYSTEMS": ["VALVE", "DUCT", "SENSOR", "ACTUATOR", "PUMP", "FILTER", "HOSE", "MANIFOLD",
                "SWITCH", "TRANSMITTER", "ACCUMULATOR", "REGULATOR", "CLAMP", "PIPE",
                "RESTRICTOR", "SOLENOID", "HEAT EXCHANGER", "NON-RETURN VALVE"],
    "AVIONICS": ["COMPUTER", "MODULE", "RACK", "ANTENNA", "DISPLAY UNIT", "CONTROL PANEL",
                 "TRANSCEIVER", "RECEIVER", "DATA CONCENTRATOR", "CIRCUIT CARD",
                 "INTERFACE UNIT", "MOUNTING TRAY"],
    "CABIN": ["PANEL", "LINING", "STOWAGE BIN", "LIGHT ASSY", "TRIM", "CURTAIN RAIL",
              "GALLEY INSERT", "MONUMENT FITTING", "DECOR PANEL", "SEAT TRACK COVER",
              "PARTITION", "LATCH"],
    "POWERPLANT": ["COWL LATCH", "MOUNT LINK", "HEAT SHIELD", "DUCT", "CLAMP", "NOZZLE",
                   "SEAL", "FIREWALL PANEL", "BRACKET", "SHROUD", "TUBE ASSY"],
    "ELECTRICAL": ["HARNESS", "CONNECTOR", "BACKSHELL", "TERMINAL BLOCK", "GROUND STUD",
                   "CONTACTOR", "RELAY", "CIRCUIT BREAKER", "BUS BAR", "SUPPORT BRACKET",
                   "CABLE ASSY", "SPLICE MODULE"],
    "STANDARD_PRACTICES": ["BOLT", "NUT", "WASHER", "RIVET", "SCREW", "PIN", "COLLAR",
                           "SLEEVE", "GROMMET", "SPACER"],
}

DOMAIN_NOUNS["STRUCTURE_EQUIPMENT"] = []  # filled in below, after the pool is defined

POSITION_QUALIFIERS = ["LH", "RH", "FWD", "AFT", "UPPER", "LOWER", "INBD", "OUTBD", ""]
ASSY_QUALIFIERS = ["ASSY", "SUB-ASSY", "KIT", "", "", ""]

STANDARD_DESCRIPTIONS = [
    "BOLT, HEXAGON HEAD, TITANIUM", "BOLT, SHEAR HEAD, STEEL", "BOLT, TENSION HEAD, ALLOY STEEL",
    "NUT, SELF-LOCKING, ANCHOR", "NUT, BARREL, FLOATING", "WASHER, PLAIN, CORROSION RESISTANT",
    "WASHER, COUNTERSUNK, ALUMINIUM", "RIVET, SOLID, COUNTERSUNK HEAD",
    "RIVET, BLIND, PROTRUDING HEAD", "SCREW, PAN HEAD, CROSS RECESS",
    "SCREW, COUNTERSUNK, HEXAGON SOCKET", "PIN, CLEVIS, CORROSION RESISTANT",
    "COLLAR, SWAGED, ALUMINIUM ALLOY", "SLEEVE, PROTECTIVE, PTFE",
    "GROMMET, EDGING, SILICONE", "SPACER, TUBULAR, ALUMINIUM",
    "CLAMP, LOOP, CUSHIONED", "TIE, CABLE, SELF-LOCKING",
]

FIN_LETTERS = {
    "21": ["HQ", "HM"], "22": ["CE", "CC"], "23": ["RC", "RN"], "24": ["GX", "XU", "PC"],
    "25": ["DL", "DQ"], "26": ["WD", "WF"], "27": ["KS", "CE", "EV"], "28": ["QA", "QT"],
    "29": ["GA", "GG"], "30": ["HA", "WX"], "31": ["FP", "FW"], "32": ["GB", "WS", "GM"],
    "33": ["LP", "RH"], "34": ["FE", "DA", "TR"], "35": ["HD", "HE"], "36": ["PN", "PU"],
    "38": ["WM", "WP"], "42": ["CP", "CQ"], "44": ["RM", "RK"], "45": ["TM", "TN"],
    "46": ["IS", "IU"], "49": ["PA", "PB"], "52": ["WT", "WV"], "56": ["WY"],
    "71": ["EN", "PP"], "73": ["EF"], "76": ["EC"], "78": ["ET"], "79": ["EO"],
    "80": ["ES"], "92": ["VC", "VU"], "20": ["VU"], "51": ["VU"], "53": ["VU"],
    "54": ["PP"], "55": ["VU"], "57": ["VU"],
}

FIN_SYSTEM_NAMES = {
    "21": "Air Conditioning", "22": "Auto Flight", "23": "Communications",
    "24": "Electrical Power", "25": "Equipment and Furnishings", "26": "Fire Protection",
    "27": "Flight Controls", "28": "Fuel", "29": "Hydraulic Power",
    "30": "Ice and Rain Protection", "31": "Indicating and Recording", "32": "Landing Gear",
    "33": "Lights", "34": "Navigation", "35": "Oxygen", "36": "Pneumatic",
    "38": "Water and Waste", "42": "Integrated Modular Avionics", "44": "Cabin Systems",
    "45": "Central Maintenance", "46": "Information Systems", "49": "Auxiliary Power",
    "52": "Door Warning", "56": "Window Heating", "71": "Power Plant",
    "73": "Engine Fuel", "76": "Engine Controls", "78": "Thrust Reverser", "79": "Engine Oil",
    "80": "Engine Starting", "92": "Wiring", "20": "Standard Practices",
    "51": "Structures", "53": "Fuselage", "54": "Nacelles", "55": "Stabilizers", "57": "Wings",
}

# A FIN identifies a function in a location, so its zone must be plausible for
# its ATA chapter. Maps ATA 2D → the major zones the function can live in.
FIN_ZONE_MAJORS = {
    "20": None, "21": ["100", "200"], "22": ["100", "200"], "23": ["100", "200"],
    "24": ["100", "200", "500", "600"], "25": ["200", "900"], "26": ["100", "400"],
    "27": ["300", "500", "600"], "28": ["100", "500", "600"], "29": ["100", "700"],
    "30": ["200", "500", "600"], "31": ["100", "200"], "32": ["700"],
    "33": ["200", "400"], "34": ["100", "200"], "35": ["200", "900"],
    "36": ["100", "400", "500", "600"], "38": ["100", "900"], "42": ["100"],
    "44": ["200", "900"], "45": ["100", "200"], "46": ["100", "200"], "49": ["300"],
    "51": None, "52": ["800"], "53": ["100", "200"], "54": ["400"], "55": ["300"],
    "56": ["200"], "57": ["500", "600"], "71": ["400"], "73": ["400"], "76": ["400"],
    "78": ["400"], "79": ["400"], "80": ["400"], "92": None,
}

# FINs name equipment, never raw structure — even on structural chapters such as
# ATA 52 (door proximity sensors) or 53 (equipment racks).
FIN_NOUN_DOMAINS = {
    "STRUCTURE": "STRUCTURE_EQUIPMENT", "STANDARD_PRACTICES": "ELECTRICAL",
    "POWERPLANT": "POWERPLANT", "SYSTEMS": "SYSTEMS", "AVIONICS": "AVIONICS",
    "CABIN": "CABIN", "ELECTRICAL": "ELECTRICAL",
}

# Equipment that lives on a structural chapter: door warning, panel latching,
# rack installation. Never a frame or a stringer.
STRUCTURE_EQUIPMENT_NOUNS = [
    "PROXIMITY SENSOR", "MICROSWITCH", "POSITION SENSOR", "WARNING SENSOR",
    "LATCH SENSOR", "CONTROL UNIT", "INDICATOR", "ACTUATOR", "LOCK ACTUATOR",
    "HANDLE ASSY", "DAMPER", "LOCKING MECHANISM", "SENSOR TARGET",
]
DOMAIN_NOUNS["STRUCTURE_EQUIPMENT"] = STRUCTURE_EQUIPMENT_NOUNS

MAJOR_ZONES = {
    "100": ("Lower Fuselage", ["Fwd Cargo Compartment, LH", "Fwd Cargo Compartment, RH",
                              "Aft Cargo Compartment, LH", "Aft Cargo Compartment, RH",
                              "Bulk Cargo Compartment", "Avionics Bay, LH", "Avionics Bay, RH",
                              "Belly Fairing, Fwd", "Belly Fairing, Aft", "Keel Beam Area",
                              "Air Conditioning Bay", "Hydraulic Bay"]),
    "200": ("Upper Fuselage", ["Flight Compartment", "Fwd Cabin, LH", "Fwd Cabin, RH",
                               "Centre Cabin, LH", "Centre Cabin, RH", "Aft Cabin, LH",
                               "Aft Cabin, RH", "Overhead Crew Rest", "Ceiling Void, Fwd",
                               "Ceiling Void, Aft", "Fwd Galley Area", "Aft Galley Area"]),
    "300": ("Empennage", ["Vertical Stabilizer, LH", "Vertical Stabilizer, RH",
                          "Horizontal Stabilizer, LH", "Horizontal Stabilizer, RH",
                          "Rudder", "Elevator, LH", "Elevator, RH", "Tail Cone",
                          "APU Compartment", "Aft Pressure Bulkhead Area"]),
    "400": ("Powerplants and Pylons", ["Engine 1 Nacelle", "Engine 2 Nacelle",
                                       "Engine 1 Pylon", "Engine 2 Pylon",
                                       "Engine 1 Fan Cowl", "Engine 2 Fan Cowl",
                                       "Engine 1 Thrust Reverser", "Engine 2 Thrust Reverser"]),
    "500": ("Left Wing", ["Left Wing Leading Edge", "Left Wing Trailing Edge",
                          "Left Wing Box, Inbd", "Left Wing Box, Outbd", "Left Slat Area",
                          "Left Flap Area", "Left Aileron Area", "Left Winglet",
                          "Left Wing Tank Access"]),
    "600": ("Right Wing", ["Right Wing Leading Edge", "Right Wing Trailing Edge",
                           "Right Wing Box, Inbd", "Right Wing Box, Outbd", "Right Slat Area",
                           "Right Flap Area", "Right Aileron Area", "Right Winglet",
                           "Right Wing Tank Access"]),
    "700": ("Landing Gear", ["Nose Gear Bay", "Left Main Gear Bay", "Right Main Gear Bay",
                             "Nose Gear Doors", "Left Main Gear Doors", "Right Main Gear Doors",
                             "Centre Gear Bay"]),
    "800": ("Doors", ["Passenger Door 1 LH", "Passenger Door 1 RH", "Passenger Door 2 LH",
                      "Passenger Door 2 RH", "Passenger Door 3 LH", "Passenger Door 3 RH",
                      "Passenger Door 4 LH", "Passenger Door 4 RH", "Fwd Cargo Door",
                      "Aft Cargo Door", "Bulk Cargo Door", "Service Door, Fwd"]),
    "900": ("Cabin Monuments", ["Fwd Lavatory, LH", "Fwd Lavatory, RH", "Mid Lavatory",
                                "Aft Lavatory, LH", "Aft Lavatory, RH", "Fwd Galley",
                                "Mid Galley", "Aft Galley", "Crew Rest Compartment",
                                "Stowage Area, Fwd"]),
}

SUPPLIER_STEMS = [
    ("Hartcliff Technologies", "United Kingdom", "HYDRAULICS|ACTUATION"),
    ("Vallonex Aerosystems", "France", "AVIONICS|DISPLAYS"),
    ("Norsteg Industries", "Sweden", "STRUCTURES|MACHINING"),
    ("Kaelmar Precision", "Germany", "FASTENERS|MACHINING"),
    ("Ardentia Controls", "United States", "FLIGHT_CONTROLS|SENSORS"),
    ("Belforte Systems", "Italy", "ELECTRICAL|POWER"),
    ("Cindermoor Aviation", "United Kingdom", "CABIN|INTERIORS"),
    ("Delvarra Engineering", "Spain", "STRUCTURES|COMPOSITES"),
    ("Ellenhorst Aerospace", "Germany", "PNEUMATICS|AIR_MANAGEMENT"),
    ("Fjordane Avionics", "Norway", "AVIONICS|NAVIGATION"),
    ("Granvale Components", "United States", "FASTENERS|STANDARD_PARTS"),
    ("Hessmark Aerotech", "Netherlands", "FUEL|PUMPS"),
    ("Iberacon Sistemas", "Spain", "ELECTRICAL|WIRING"),
    ("Jarnovik Mekanik", "Czech Republic", "LANDING_GEAR|MACHINING"),
    ("Kestrelane Group", "Canada", "CABIN|SEATING"),
    ("Lumendra Optics", "Japan", "LIGHTING|DISPLAYS"),
    ("Montclare Aerospace", "United States", "ENGINE_ACCESSORIES"),
    ("Nydalen Technic", "Denmark", "SENSORS|INSTRUMENTATION"),
    ("Orvantis Materials", "France", "MATERIALS|SEALANTS"),
    ("Pellworth Dynamics", "United Kingdom", "ACTUATION|MECHANISMS"),
    ("Quintarra Electronics", "Ireland", "AVIONICS|COMPUTING"),
    ("Rossheim Metalltechnik", "Austria", "STRUCTURES|FORGINGS"),
    ("Saltvik Aerospace", "Finland", "COMPOSITES|PANELS"),
    ("Trentmoor Fluid Systems", "United Kingdom", "HYDRAULICS|FLUIDS"),
    ("Umbrafex Cabin", "Portugal", "CABIN|MONUMENTS"),
    ("Verdanne Aviation", "Belgium", "OXYGEN|SAFETY"),
    ("Westerbrink Systems", "Netherlands", "FIRE_PROTECTION"),
    ("Xanther Precision", "Switzerland", "FASTENERS|PRECISION"),
    ("Ystadt Aerocomp", "Sweden", "COMPOSITES|STRUCTURES"),
    ("Zelmara Controls", "Poland", "CONTROLS|ELECTRONICS"),
    ("Ashgrove Technic", "United States", "WIRING|HARNESSES"),
    ("Brennholt Systeme", "Germany", "AIR_MANAGEMENT|DUCTS"),
    ("Caldenmar Aviation", "Canada", "LANDING_GEAR|BRAKES"),
    ("Dunmarrow Aerospace", "Ireland", "STRUCTURES|SHEET_METAL"),
    ("Escaldes Aviónica", "Andorra", "AVIONICS|SOFTWARE"),
    ("Farrowgate Systems", "United Kingdom", "IFE|CONNECTIVITY"),
    ("Girondel Aéro", "France", "ENGINE_ACCESSORIES|FUEL"),
    ("Holmvang Industri", "Norway", "MATERIALS|COATINGS"),
    ("Innsdale Components", "United States", "STANDARD_PARTS"),
    ("Jorvalen Tech", "Estonia", "SENSORS|PROXIMITY"),
]

OPERATOR_STEMS = [
    ("Skyline International Airways", "SIA", "SL", "SKL", "Singapore", "APAC", "9V"),
    ("Nordwind Continental", "NWC", "NW", "NWC", "Germany", "EMEA", "D"),
    ("Cedar Gulf Airways", "CGA", "CG", "CDG", "United Arab Emirates", "MEA", "A6"),
    ("Andes Cargo Lines", "ACL", "AZ", "ANC", "Chile", "LATAM", "CC"),
    ("Halcyon Atlantic", "HAT", "HY", "HCA", "Ireland", "EMEA", "EI"),
    ("Meridian Pacific Air", "MPA", "MP", "MRP", "Australia", "APAC", "VH"),
    ("Baltic Crown Airlines", "BCA", "BK", "BCR", "Latvia", "EMEA", "YL"),
    ("Karoo Skyways", "KSW", "KO", "KRO", "South Africa", "MEA", "ZS"),
    ("Solstice Air", "SOL", "SO", "SLS", "Iceland", "EMEA", "TF"),
    ("Aurora Northern", "AUN", "AU", "AUN", "Canada", "AMER", "C"),
    ("Verdant Isles Airways", "VIA", "VI", "VDI", "Philippines", "APAC", "RP"),
    ("Corvale Airlines", "CVL", "CV", "CVL", "France", "EMEA", "F"),
    ("Trenmark Air Transport", "TMK", "TM", "TMK", "United Kingdom", "EMEA", "G"),
    ("Sundara Airways", "SDA", "SD", "SDA", "Indonesia", "APAC", "PK"),
    ("Orinoco Express Air", "OEA", "OE", "ORX", "Colombia", "LATAM", "HK"),
    ("Firth Regional", "FRR", "FR", "FTH", "United Kingdom", "EMEA", "G"),
    ("Levantine Airways", "LVA", "LV", "LVN", "Lebanon", "MEA", "OD"),
    ("Pampas Air Lines", "PAL", "PZ", "PMP", "Argentina", "LATAM", "LV"),
    ("Kirinya Air", "KRA", "KY", "KRA", "Kenya", "MEA", "5Y"),
    ("Tundra Jet", "TJT", "TJ", "TDR", "Norway", "EMEA", "LN"),
    ("Sablewing Aviation", "SBW", "SB", "SBW", "United States", "AMER", "N"),
    ("Marisol Airlines", "MSL", "MS", "MSL", "Spain", "EMEA", "EC"),
    ("Highvale Air", "HVA", "HV", "HVL", "Switzerland", "EMEA", "HB"),
    ("Zephyr Coastal", "ZCT", "ZC", "ZPC", "Portugal", "EMEA", "CS"),
    ("Anatolia Skies", "ATS", "AK", "ANS", "Türkiye", "EMEA", "TC"),
    ("Great Basin Airways", "GBA", "GB", "GBA", "United States", "AMER", "N"),
    ("Coral Sea Air", "CSA", "CS", "CRS", "Fiji", "APAC", "DQ"),
    ("Vistula Airlines", "VSL", "VS", "VST", "Poland", "EMEA", "SP"),
    ("Rhodian Air", "RDA", "RD", "RDA", "Greece", "EMEA", "SX"),
    ("Ambergris Airways", "AMB", "AM", "AMB", "Belize", "LATAM", "V3"),
    ("Nihon Crest Air", "NCA", "NC", "NCA", "Japan", "APAC", "JA"),
    ("Sierra Verde Airlines", "SVA", "SV", "SVD", "Costa Rica", "LATAM", "TI"),
    ("Elbrus Air", "ELB", "EL", "ELB", "Georgia", "EMEA", "4L"),
    ("Golden Steppe Airways", "GSA", "GS", "GSP", "Kazakhstan", "EMEA", "UP"),
    ("Riverine Cargo", "RVC", "RV", "RVC", "Netherlands", "EMEA", "PH"),
    ("Blue Meridian Air", "BMA", "BM", "BMD", "United Kingdom", "EMEA", "G"),
    ("Cascadia Wings", "CDW", "CW", "CDW", "Canada", "AMER", "C"),
    ("Serapis Airlines", "SRP", "SP", "SRP", "Egypt", "MEA", "SU"),
    ("Tasman Link", "TSL", "TL", "TSL", "New Zealand", "APAC", "ZK"),
    ("Windward Republic Air", "WRA", "WR", "WRA", "Dominican Republic", "LATAM", "HI"),
    ("Ostara Airways", "OST", "OS", "OST", "Austria", "EMEA", "OE"),
    ("Cobalt Straits Air", "CBS", "CB", "CBS", "Malaysia", "APAC", "9M"),
    ("Lindenau Luftfahrt", "LDL", "LD", "LDL", "Germany", "EMEA", "D"),
    ("Pyrenean Air", "PYR", "PY", "PYR", "Spain", "EMEA", "EC"),
    ("Norvik Cargo Lines", "NVC", "NV", "NVC", "Sweden", "EMEA", "SE"),
    ("Gulfstream Levant", "GSL", "GL", "GSL", "Jordan", "MEA", "JY"),
    ("Amaranth Airways", "AMA", "AA", "AMA", "India", "APAC", "VT"),
    ("Brisance Air", "BRS", "BR", "BRS", "France", "EMEA", "F"),
    ("Chinook Pacific", "CKP", "CK", "CKP", "United States", "AMER", "N"),
    ("Danube Regional", "DNR", "DN", "DNR", "Hungary", "EMEA", "HA"),
    ("Emerald Coast Air", "ECA", "EC", "ECA", "Ireland", "EMEA", "EI"),
    ("Falkirk Airways", "FKA", "FK", "FKA", "United Kingdom", "EMEA", "G"),
    ("Grandvale Air Lines", "GVA", "GV", "GVA", "Brazil", "LATAM", "PR"),
    ("Hokuriku Air Transport", "HKA", "HK", "HKA", "Japan", "APAC", "JA"),
    ("Ionian Wings", "IOW", "IW", "IOW", "Greece", "EMEA", "SX"),
    ("Jutland Air", "JTA", "JT", "JTA", "Denmark", "EMEA", "OY"),
    ("Kalahari Express", "KLE", "KL", "KLE", "Botswana", "MEA", "A2"),
    ("Loire Atlantique Air", "LAA", "LA", "LAA", "France", "EMEA", "F"),
    ("Mistral Airways", "MTA", "MT", "MTA", "Italy", "EMEA", "I"),
    ("Novaterra Airlines", "NVT", "NT", "NVT", "Peru", "LATAM", "OB"),
]

SOFTWARE_SYSTEMS = [
    ("FCGU", "Flight Control Guidance Unit", "27-90-11"),
    ("FMGC", "Flight Management Guidance Computer", "22-80-11"),
    ("CIDS", "Cabin Intercommunication Data System Director", "44-10-11"),
    ("CMC", "Central Maintenance Computer", "45-20-11"),
    ("FWC", "Flight Warning Computer", "31-50-11"),
    ("BSCU", "Brake and Steering Control Unit", "32-40-12"),
    ("SFCC", "Slat Flap Control Computer", "27-50-11"),
    ("FQIC", "Fuel Quantity Indicating Computer", "28-40-12"),
    ("BMC", "Bleed Monitoring Computer", "36-20-11"),
    ("ADM", "Air Data Module", "34-10-11"),
    ("IRU", "Inertial Reference Unit", "34-20-11"),
    ("EFB", "Electronic Flight Bag Unit", "46-20-11"),
    ("CPIOM", "Core Processing Input Output Module", "42-10-11"),
    ("GCU", "Generator Control Unit", "24-20-12"),
    ("ECB", "APU Electronic Control Box", "49-60-11"),
    ("DMC", "Display Management Computer", "31-60-12"),
    ("WXR", "Weather Radar Transceiver", "34-40-11"),
    ("MMR", "Multi-Mode Receiver", "34-50-11"),
    ("IFES", "Inflight Entertainment Head End Server", "44-20-12"),
    ("OCS", "Onboard Connectivity Server", "44-30-11"),
    ("ACMF", "Aircraft Condition Monitoring Function", "46-40-11"),
    ("EEC", "Engine Electronic Control Unit", "73-20-11"),
    ("PHC", "Probe Heat Computer", "30-30-11"),
    ("LGCIU", "Landing Gear Control Interface Unit", "32-30-11"),
    ("ZC", "Air Conditioning Zone Controller", "21-60-12"),
]

SOFTWARE_FUNCTIONS = ["OPS", "CFG", "NAV", "DB", "BITE", "APP", "BOOT", "PARAM"]

DRAWING_TYPES = ["ASSEMBLY", "DETAIL", "INSTALLATION", "SCHEMATIC", "WIRING", "TOOLING"]
CAD_SYSTEMS = ["CATIA_V5", "CATIA_V6", "3DX", "LEGACY_2D"]

DATASET_END = dt.date(2026, 8, 7)

# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------


def weighted(rng, items, weights):
    return rng.choices(items, weights=weights, k=1)[0]


def write_csv(name, header, rows):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)
    return path, len(rows)


def read_csv(name):
    path = os.path.join(OUT, name)
    with open(path, newline="", encoding="utf-8") as fh:
        return list(csv.DictReader(fh))


def program_set(rng, primary, share_chance=0.12):
    """Return a pipe-joined program list, occasionally multi-program."""
    if rng.random() < share_chance:
        others = [p for p in PROGRAMS if p != primary]
        extra = rng.sample(others, rng.choice([1, 1, 2, 3]))
        return "|".join(p for p in PROGRAMS if p == primary or p in extra)
    return primary


def ata_digits(code):
    return code.replace("-", "")


# --------------------------------------------------------------------------
# generators
# --------------------------------------------------------------------------


def gen_ata():
    rows = [[c, lvl, parent, title, domain, progs, section, view]
            for c, lvl, parent, title, domain, progs, section, view
            in ata_catalog.iter_rows()]
    rows.sort(key=lambda r: (r[0]))
    return write_csv("ata_chapters.csv",
                     ["code", "level", "parent", "title", "domain", "programs",
                      "airframe_section", "view_hint"], rows)


ZONE_DETAILS = ["Fwd Section", "Aft Section", "Upper Area", "Lower Area", "Inbd Area",
                "Outbd Area", "Access Area", "Equipment Rack Area", "Stowage Area"]

# --------------------------------------------------------------------------
# Airframe placement of a zone — where an illustration callout points.
# --------------------------------------------------------------------------
# ``src/Aeralis.Illustrations`` aims a callout with three values: an airframe
# section, the view it reads best on, and a position 0..1 along that section
# (0 = forward / inboard end, 1 = aft / outboard end). The section and view
# names must spell an ``AircraftSection`` / ``ViewKind`` enum member exactly —
# see ``src/Aeralis.Illustrations/Enums.cs`` and ``SectionMap.cs`` for the
# outline each section covers on each view.
#
# Keyed by sub-zone name, i.e. the first half of ``zone_name``; every name in
# ``MAJOR_ZONES`` must appear here. Purely derived from the name, never from
# the RNG, so adding a column cannot shift any other generated value.
ZONE_AIRFRAME = {
    # 100 — Lower Fuselage
    "Fwd Cargo Compartment, LH": ("ForwardFuselage", "Side", 0.35),
    "Fwd Cargo Compartment, RH": ("ForwardFuselage", "Side", 0.35),
    "Aft Cargo Compartment, LH": ("AftFuselage", "Side", 0.30),
    "Aft Cargo Compartment, RH": ("AftFuselage", "Side", 0.30),
    "Bulk Cargo Compartment": ("AftFuselage", "Side", 0.55),
    "Avionics Bay, LH": ("ForwardFuselage", "Side", 0.15),
    "Avionics Bay, RH": ("ForwardFuselage", "Side", 0.15),
    "Belly Fairing, Fwd": ("Fuselage", "Side", 0.45),
    "Belly Fairing, Aft": ("Fuselage", "Side", 0.60),
    "Keel Beam Area": ("Fuselage", "Side", 0.52),
    "Air Conditioning Bay": ("Fuselage", "Side", 0.48),
    "Hydraulic Bay": ("Fuselage", "Side", 0.55),
    # 200 — Upper Fuselage
    "Flight Compartment": ("Cockpit", "Side", 0.10),
    "Fwd Cabin, LH": ("ForwardFuselage", "Side", 0.45),
    "Fwd Cabin, RH": ("ForwardFuselage", "Side", 0.45),
    "Centre Cabin, LH": ("Fuselage", "Side", 0.50),
    "Centre Cabin, RH": ("Fuselage", "Side", 0.50),
    "Aft Cabin, LH": ("AftFuselage", "Side", 0.65),
    "Aft Cabin, RH": ("AftFuselage", "Side", 0.65),
    "Overhead Crew Rest": ("Fuselage", "Side", 0.60),
    "Ceiling Void, Fwd": ("ForwardFuselage", "Side", 0.55),
    "Ceiling Void, Aft": ("AftFuselage", "Side", 0.45),
    "Fwd Galley Area": ("ForwardFuselage", "Side", 0.25),
    "Aft Galley Area": ("AftFuselage", "Side", 0.75),
    # 300 — Empennage
    "Vertical Stabilizer, LH": ("VerticalStabilizer", "Side", 0.45),
    "Vertical Stabilizer, RH": ("VerticalStabilizer", "Side", 0.45),
    "Horizontal Stabilizer, LH": ("HorizontalStabilizer", "Plan", 0.45),
    "Horizontal Stabilizer, RH": ("HorizontalStabilizer", "Plan", 0.45),
    "Rudder": ("VerticalStabilizer", "Side", 0.70),
    "Elevator, LH": ("HorizontalStabilizer", "Plan", 0.60),
    "Elevator, RH": ("HorizontalStabilizer", "Plan", 0.60),
    "Tail Cone": ("Tail", "Side", 0.12),
    "APU Compartment": ("Tail", "Side", 0.20),
    "Aft Pressure Bulkhead Area": ("AftFuselage", "Side", 0.85),
    # 400 — Powerplants and Pylons (engine 1 is port, engine 2 starboard)
    "Engine 1 Nacelle": ("LeftEngine", "Plan", 0.45),
    "Engine 2 Nacelle": ("RightEngine", "Plan", 0.45),
    "Engine 1 Pylon": ("LeftEngine", "Plan", 0.10),
    "Engine 2 Pylon": ("RightEngine", "Plan", 0.10),
    "Engine 1 Fan Cowl": ("LeftEngine", "Side", 0.30),
    "Engine 2 Fan Cowl": ("RightEngine", "Side", 0.30),
    "Engine 1 Thrust Reverser": ("LeftEngine", "Side", 0.70),
    "Engine 2 Thrust Reverser": ("RightEngine", "Side", 0.70),
    # 500 — Left Wing
    "Left Wing Leading Edge": ("LeftWing", "Plan", 0.45),
    "Left Wing Trailing Edge": ("LeftWing", "Plan", 0.50),
    "Left Wing Box, Inbd": ("LeftWing", "Plan", 0.25),
    "Left Wing Box, Outbd": ("LeftWing", "Plan", 0.70),
    "Left Slat Area": ("LeftWing", "Plan", 0.55),
    "Left Flap Area": ("LeftWing", "Plan", 0.35),
    "Left Aileron Area": ("LeftWing", "Plan", 0.80),
    "Left Winglet": ("LeftWing", "Front", 0.95),
    "Left Wing Tank Access": ("LeftWing", "Plan", 0.40),
    # 600 — Right Wing
    "Right Wing Leading Edge": ("RightWing", "Plan", 0.45),
    "Right Wing Trailing Edge": ("RightWing", "Plan", 0.50),
    "Right Wing Box, Inbd": ("RightWing", "Plan", 0.25),
    "Right Wing Box, Outbd": ("RightWing", "Plan", 0.70),
    "Right Slat Area": ("RightWing", "Plan", 0.55),
    "Right Flap Area": ("RightWing", "Plan", 0.35),
    "Right Aileron Area": ("RightWing", "Plan", 0.80),
    "Right Winglet": ("RightWing", "Front", 0.95),
    "Right Wing Tank Access": ("RightWing", "Plan", 0.40),
    # 700 — Landing Gear
    "Nose Gear Bay": ("NoseLandingGear", "Side", 0.40),
    "Left Main Gear Bay": ("MainLandingGear", "Front", 0.40),
    "Right Main Gear Bay": ("MainLandingGear", "Front", 0.40),
    "Nose Gear Doors": ("NoseLandingGear", "Side", 0.70),
    "Left Main Gear Doors": ("MainLandingGear", "Front", 0.70),
    "Right Main Gear Doors": ("MainLandingGear", "Front", 0.70),
    "Centre Gear Bay": ("MainLandingGear", "Side", 0.50),
    # 800 — Doors (1 to 4 run nose to tail)
    "Passenger Door 1 LH": ("ForwardFuselage", "Side", 0.20),
    "Passenger Door 1 RH": ("ForwardFuselage", "Side", 0.20),
    "Passenger Door 2 LH": ("ForwardFuselage", "Side", 0.80),
    "Passenger Door 2 RH": ("ForwardFuselage", "Side", 0.80),
    "Passenger Door 3 LH": ("AftFuselage", "Side", 0.30),
    "Passenger Door 3 RH": ("AftFuselage", "Side", 0.30),
    "Passenger Door 4 LH": ("AftFuselage", "Side", 0.70),
    "Passenger Door 4 RH": ("AftFuselage", "Side", 0.70),
    "Fwd Cargo Door": ("ForwardFuselage", "Side", 0.60),
    "Aft Cargo Door": ("AftFuselage", "Side", 0.40),
    "Bulk Cargo Door": ("AftFuselage", "Side", 0.60),
    "Service Door, Fwd": ("ForwardFuselage", "Side", 0.15),
    # 900 — Cabin Monuments
    "Fwd Lavatory, LH": ("ForwardFuselage", "Side", 0.30),
    "Fwd Lavatory, RH": ("ForwardFuselage", "Side", 0.30),
    "Mid Lavatory": ("Fuselage", "Side", 0.50),
    "Aft Lavatory, LH": ("AftFuselage", "Side", 0.75),
    "Aft Lavatory, RH": ("AftFuselage", "Side", 0.75),
    "Fwd Galley": ("ForwardFuselage", "Side", 0.20),
    "Mid Galley": ("Fuselage", "Side", 0.55),
    "Aft Galley": ("AftFuselage", "Side", 0.80),
    "Crew Rest Compartment": ("AftFuselage", "Side", 0.35),
    "Stowage Area, Fwd": ("ForwardFuselage", "Side", 0.35),
}

# How the detail suffix of a zone name nudges the position along its section.
# "Upper Area" / "Lower Area" are vertical, so they leave the longitudinal or
# spanwise station alone.
ZONE_DETAIL_SHIFT = {
    "Fwd Section": -0.15,
    "Aft Section": +0.15,
    "Upper Area": 0.0,
    "Lower Area": 0.0,
    "Inbd Area": -0.12,
    "Outbd Area": +0.12,
    "Access Area": +0.05,
    "Equipment Rack Area": -0.05,
    "Stowage Area": +0.08,
}

POSITION_MIN, POSITION_MAX = 0.05, 0.95

_ZONE_SUB_NAMES = [s for _major, (_name, subs) in MAJOR_ZONES.items() for s in subs]
_MISSING_ZONE_PLACEMENT = [s for s in _ZONE_SUB_NAMES if s not in ZONE_AIRFRAME]
if _MISSING_ZONE_PLACEMENT:
    raise RuntimeError("ZONE_AIRFRAME is missing an entry for sub-zone(s): "
                       + ", ".join(sorted(set(_MISSING_ZONE_PLACEMENT))))
_EXTRA_ZONE_PLACEMENT = sorted(set(ZONE_AIRFRAME) - set(_ZONE_SUB_NAMES))
if _EXTRA_ZONE_PLACEMENT:
    raise RuntimeError("ZONE_AIRFRAME names a sub-zone absent from MAJOR_ZONES: "
                       + ", ".join(_EXTRA_ZONE_PLACEMENT))
_MISSING_DETAIL_SHIFT = [d for d in ZONE_DETAILS if d not in ZONE_DETAIL_SHIFT]
if _MISSING_DETAIL_SHIFT:
    raise RuntimeError("ZONE_DETAIL_SHIFT is missing an entry for detail(s): "
                       + ", ".join(_MISSING_DETAIL_SHIFT))


def zone_placement(sub_name, detail):
    """``(airframe_section, view_hint, section_position)`` for one zone row."""
    try:
        section, view, base = ZONE_AIRFRAME[sub_name]
    except KeyError:
        raise KeyError(
            f"ZONE_AIRFRAME has no entry for sub-zone {sub_name!r}; every name in "
            f"MAJOR_ZONES must be placed on the airframe") from None
    try:
        shift = ZONE_DETAIL_SHIFT[detail]
    except KeyError:
        raise KeyError(
            f"ZONE_DETAIL_SHIFT has no entry for zone detail {detail!r}") from None
    pos = min(POSITION_MAX, max(POSITION_MIN, base + shift))
    return section, view, f"{pos:.2f}"


def gen_zones(rng):
    """Three-level zoning: major (100) → sub decade (120) → detail zone (128)."""
    rows = []
    for major, (major_name, subs) in MAJOR_ZONES.items():
        base = int(major)
        for idx, sub_name in enumerate(subs[:9]):
            decade = base + (idx + 1) * 10
            details = rng.sample(ZONE_DETAILS, rng.choice([3, 4, 4, 5, 6]))
            for d_idx, detail in enumerate(details, start=1):
                zone = str(decade + d_idx)
                panels = "|".join(
                    f"{zone}{ch}{side}" for ch, side in
                    rng.sample([("A", "L"), ("B", "L"), ("A", "R"), ("B", "R"),
                                ("C", "L"), ("C", "R")], rng.choice([2, 2, 3, 4])))
                if rng.random() < 0.5:
                    progs = "|".join(PROGRAMS)
                else:
                    progs = program_set(rng, rng.choice(PROGRAMS), share_chance=0.7)
                # Derived from the name alone — deliberately not drawn from rng,
                # so the placement columns cannot perturb the random stream.
                section, view, position = zone_placement(sub_name, detail)
                rows.append([zone, f"{sub_name} — {detail}", major, major_name,
                             progs, panels, section, view, position])
    rows.sort(key=lambda r: r[0])
    return write_csv("zones.csv",
                     ["zone", "zone_name", "major_zone", "major_zone_name",
                      "programs", "access_panels", "airframe_section", "view_hint",
                      "section_position"], rows)


def gen_suppliers(rng):
    rows = [["AERA", "Aeralis Operations", "France", "AIRFRAME|INTEGRATION", "F0001"]]
    used = {"AERA"}
    for name, country, domains in SUPPLIER_STEMS:
        code = "".join(w[0] for w in name.split()[:3]).upper()
        if len(code) < 3:
            code = name[:3].upper()
        while code in used:
            code = code + rng.choice("XYZKQ")
            code = code[:4]
        used.add(code)
        cage = rng.choice("KFDGJ") + str(rng.randint(1000, 9999))
        rows.append([code, name, country, domains, cage])
    # pad to 80 with regional divisions of existing suppliers
    idx = 0
    while len(rows) < 80:
        base_name, country, domains = SUPPLIER_STEMS[idx % len(SUPPLIER_STEMS)]
        suffix = ["Deutschland", "France", "UK", "Iberia", "Americas", "Asia"][idx // len(SUPPLIER_STEMS) % 6]
        name = f"{base_name.split()[0]} {suffix}"
        code = (name.split()[0][:2] + suffix[:2]).upper()
        while code in used:
            code = code[:3] + rng.choice("0123456789")
        used.add(code)
        rows.append([code, name, country, domains, rng.choice("KFDGJ") + str(rng.randint(1000, 9999))])
        idx += 1
    rows.sort(key=lambda r: r[0])
    return write_csv("suppliers.csv",
                     ["supplier_code", "supplier_name", "country", "domains", "cage_code"], rows)


def gen_operators(rng):
    rows = []
    for name, code, iata, icao, country, region, reg_prefix in OPERATOR_STEMS:
        fleet = "|".join(sorted(rng.sample(PROGRAMS, rng.choice([1, 2, 2, 3, 3, 4])),
                                key=PROGRAMS.index))
        rows.append([code, name, iata, icao, country, region, fleet, reg_prefix])
    rows.sort(key=lambda r: r[0])
    return write_csv("operators.csv",
                     ["operator_code", "operator_name", "iata", "icao", "country",
                      "region", "fleet_programs", "registration_prefix"], rows)


ATA_WEIGHTS = {"STRUCTURE": 35, "SYSTEMS": 30, "AVIONICS": 8, "CABIN": 7,
               "POWERPLANT": 12, "ELECTRICAL": 6, "STANDARD_PRACTICES": 2}


def pick_ata(rng, six_by_domain):
    domain = weighted(rng, list(ATA_WEIGHTS), list(ATA_WEIGHTS.values()))
    return rng.choice(six_by_domain[domain]), domain


def gen_part_numbers(rng, suppliers, n=6000):
    six = ata_catalog.six_d_codes()
    six_by_domain = {}
    for code, title, domain in six:
        six_by_domain.setdefault(domain, []).append((code, title))
    vendors = [s for s in suppliers if s["supplier_code"] != "AERA"]

    rows = []
    used = set()
    for _ in range(n):
        (code, title), domain = pick_ata(rng, six_by_domain)
        pn_type = weighted(rng, ["AERALIS", "VENDOR", "STANDARD"], [55, 30, 15])
        primary = rng.choice(PROGRAMS)

        if pn_type == "AERALIS":
            pn = "D" + ata_digits(code) + "".join(rng.choice("0123456789") for _ in range(7))
            supplier = "AERA"
            programs = program_set(rng, primary, 0.06)
        elif pn_type == "VENDOR":
            sup = rng.choice(vendors)
            stem = sup["supplier_code"][:rng.choice([2, 3, 3, 4])].upper()
            pn = f"{stem}{rng.randint(1000, 999999)}-{rng.randint(1, 99):02d}"
            supplier = sup["supplier_code"]
            programs = program_set(rng, primary, 0.16)
        else:
            fam = rng.choice(["AGS", "AES", "EN", "LN"])
            digits = rng.randint(1000, 999999)
            suffix = rng.choice([f"-{rng.randint(1, 99):02d}", f"K{rng.randint(1, 9)}",
                                 f"-{rng.randint(1, 9)}{rng.choice('ABCDE')}"])
            pn = f"{fam}{digits}{suffix}"
            supplier = rng.choice(vendors)["supplier_code"]
            programs = program_set(rng, primary, 0.85)

        if pn in used:
            continue
        used.add(pn)

        if pn_type == "STANDARD":
            desc = rng.choice(STANDARD_DESCRIPTIONS)
        else:
            noun = rng.choice(DOMAIN_NOUNS[domain])
            context = title.split("—")[-1].strip().upper()
            if context == "GENERAL":
                context = title.split("—")[0].strip().upper()
            parts = [noun, context]
            pos = rng.choice(POSITION_QUALIFIERS)
            asy = rng.choice(ASSY_QUALIFIERS)
            if asy:
                parts.append(asy)
            if pos:
                parts.append(pos)
            desc = ", ".join(parts)[:60]

        lifecycle = weighted(rng, ["ACTIVE", "OBSOLETE", "SUPERSEDED", "NEW"], [75, 12, 8, 5])
        uom = weighted(rng, ["EA", "M", "KG", "L", "SET"], [90, 3, 3, 2, 2])
        if pn_type == "STANDARD":
            mass = round(rng.uniform(0.001, 0.4), 3)
            price = int(max(1, rng.lognormvariate(2.4, 0.9)))
        elif domain == "STRUCTURE":
            mass = round(rng.uniform(0.05, 34.0), 3)
            price = int(max(20, rng.lognormvariate(7.0, 1.3)))
        else:
            mass = round(rng.uniform(0.02, 18.0), 3)
            price = int(max(10, rng.lognormvariate(6.6, 1.4)))
        rows.append([pn, desc, pn_type, code, code.split("-")[0], programs, supplier,
                     uom, f"{mass:.3f}", price, lifecycle, ""])

    # supersession links: every SUPERSEDED part points at an ACTIVE part of the
    # same ATA 6D and a compatible program set.
    by_ata = {}
    for r in rows:
        if r[10] == "ACTIVE":
            by_ata.setdefault(r[3], []).append(r)
    for r in rows:
        if r[10] != "SUPERSEDED":
            continue
        candidates = [c for c in by_ata.get(r[3], [])
                      if set(r[5].split("|")) & set(c[5].split("|")) and c[0] != r[0]]
        if candidates:
            r[11] = rng.choice(candidates)[0]
        else:
            r[10] = "ACTIVE"

    rows.sort(key=lambda r: r[0])
    return write_csv("part_numbers.csv",
                     ["part_number", "description", "pn_type", "ata_6d", "ata_2d",
                      "programs", "supplier_code", "uom", "mass_kg", "unit_price_eur",
                      "lifecycle", "interchangeable_with"], rows)


def gen_fins(rng, parts, zones, n=2500):
    six = ata_catalog.six_d_codes()
    # FINs live on systems-ish chapters; structures get a few panel/rack FINs.
    eligible = [(c, t, d) for c, t, d in six if c.split("-")[0] in FIN_LETTERS]
    parts_by_ata = {}
    for p in parts:
        parts_by_ata.setdefault(p["ata_6d"], []).append(p)
    zones_by_program = {}
    for z in zones:
        for prog in z["programs"].split("|"):
            zones_by_program.setdefault(prog, []).append(z)

    rows, used = [], set()
    attempts = 0
    while len(rows) < n and attempts < n * 12:
        attempts += 1
        code, title, domain = rng.choice(eligible)
        two = code.split("-")[0]
        if domain == "STRUCTURE" and rng.random() < 0.85:
            continue
        letters = rng.choice(FIN_LETTERS[two])
        num = rng.choice([rng.randint(1, 9), rng.randint(10, 99),
                          rng.randint(100, 999), rng.randint(1000, 9999)])
        tail = rng.choice(["", "", "", str(rng.randint(1, 9))])
        fin = f"{num}{letters}{tail}"
        if fin in used:
            continue

        primary = rng.choice(PROGRAMS)
        # The FIN's default part must exist for the FIN's primary program; the
        # FIN's program set is then narrowed to what that part actually covers.
        pool = [p for p in parts_by_ata.get(code, [])
                if primary in p["programs"].split("|")]
        if not pool:
            continue
        part = rng.choice(pool)
        wanted = set(program_set(rng, primary, 0.18).split("|"))
        programs = "|".join(p for p in PROGRAMS
                            if p in wanted & set(part["programs"].split("|")))

        zpool = zones_by_program.get(primary, [])
        if not zpool:
            continue
        majors = FIN_ZONE_MAJORS.get(two)
        if majors:
            scoped = [z for z in zpool if z["major_zone"] in majors]
            if scoped:
                zpool = scoped
        zone = rng.choice(zpool)["zone"]

        noun = rng.choice(DOMAIN_NOUNS[FIN_NOUN_DOMAINS[domain]])
        context = title.split("—")[-1].strip().upper()
        if context == "GENERAL":
            context = title.split("—")[0].strip().upper()
        pos = rng.choice(POSITION_QUALIFIERS)
        desc = ", ".join(x for x in [noun, context, pos] if x)[:64]

        used.add(fin)
        rows.append([fin, desc, code, two, FIN_SYSTEM_NAMES.get(two, "General"),
                     programs, zone, part["part_number"],
                     "true" if rng.random() < 0.35 else "false"])

    rows.sort(key=lambda r: r[0])
    return write_csv("fins.csv",
                     ["fin", "description", "ata_6d", "ata_2d", "system", "programs",
                      "zone", "default_part_number", "lru"], rows)


def gen_software(rng, fins, n=300):
    fins_by_ata = {}
    for f in fins:
        fins_by_ata.setdefault(f["ata_6d"], []).append(f)
    all_fins = list(fins)

    rows, used = [], set()
    while len(rows) < n:
        stem, system, ata = rng.choice(SOFTWARE_SYSTEMS)
        func = rng.choice(SOFTWARE_FUNCTIONS)
        major, minor = rng.randint(1, 14), rng.randint(0, 9)
        name = f"{stem}_{func}_V{major}.{minor}"
        if name in used:
            continue
        primary = rng.choice(PROGRAMS)
        programs = program_set(rng, primary, 0.10)

        pool = [f for f in fins_by_ata.get(ata, []) if primary in f["programs"].split("|")]
        if not pool:
            pool = [f for f in all_fins
                    if primary in f["programs"].split("|")
                    and f["ata_2d"] == ata.split("-")[0]]
        if not pool:
            pool = [f for f in all_fins if primary in f["programs"].split("|")]
        if not pool:
            continue
        host = rng.choice(pool)

        spn = ("L" + rng.choice("ABCDEFGH") + str(rng.randint(1, 9)) + primary
               + "".join(rng.choice("0123456789ABCDEF") for _ in range(7)))
        used.add(name)
        rows.append([spn, name, system, ata, programs, f"{major}.{minor}",
                     weighted(rng, ["LSAP", "DATABASE", "FIELD_LOADABLE", "EMBEDDED"],
                              [55, 18, 17, 10]),
                     host["fin"],
                     weighted(rng, ["CERTIFIED", "IN_CERTIFICATION", "DEVELOPMENT", "WITHDRAWN"],
                              [72, 12, 10, 6]),
                     ""])

    # version chains within the same system+function+program
    groups = {}
    for r in rows:
        key = (r[1].rsplit("_V", 1)[0], r[4])
        groups.setdefault(key, []).append(r)
    for members in groups.values():
        members.sort(key=lambda r: tuple(int(x) for x in r[5].split(".")))
        for prev, cur in zip(members, members[1:]):
            if rng.random() < 0.75:
                cur[9] = prev[0]

    rows.sort(key=lambda r: r[0])
    return write_csv("software.csv",
                     ["software_pn", "software_name", "system", "ata_6d", "programs",
                      "version", "load_type", "host_fin", "certification_status",
                      "supersedes"], rows)


def gen_drawings(rng, n=1500):
    six = ata_catalog.six_d_codes()
    six_by_domain = {}
    for code, title, domain in six:
        six_by_domain.setdefault(domain, []).append((code, title))
    rows, used = [], set()
    while len(rows) < n:
        (code, title), domain = pick_ata(rng, six_by_domain)
        dn = "D" + ata_digits(code) + f"{rng.randint(0, 99999):05d}"
        if dn in used:
            continue
        used.add(dn)
        primary = rng.choice(PROGRAMS)
        programs = program_set(rng, primary, 0.08)
        context = title.split("—")[-1].strip().upper()
        if context == "GENERAL":
            context = title.split("—")[0].strip().upper()
        dtype = weighted(rng, DRAWING_TYPES, [30, 25, 20, 10, 10, 5])
        suffix = {"ASSEMBLY": "ASSY", "DETAIL": "DETAIL", "INSTALLATION": "INSTL",
                  "SCHEMATIC": "SCHEMATIC", "WIRING": "WIRING", "TOOLING": "TOOLING"}[dtype]
        pos = rng.choice(POSITION_QUALIFIERS)
        dt_title = ", ".join(x for x in [context, suffix, pos] if x)[:70]
        year = rng.randint(2012, 2025)
        issue_date = dt.date(year, rng.randint(1, 12), rng.randint(1, 28))
        cad = weighted(rng, CAD_SYSTEMS, [40, 25, 20, 15])
        rows.append([dn, dt_title, code, programs, dtype,
                     rng.choice("ABCDEFGH"), issue_date.isoformat(), cad])
    rows.sort(key=lambda r: r[0])
    return write_csv("drawings.csv",
                     ["drawing_number", "title", "ata_6d", "programs", "drawing_type",
                      "issue", "issue_date", "cad_system"], rows)


def gen_msns(rng, operators):
    rows, used_regs = [], set()
    for prog in PROGRAMS:
        info = PROGRAM_INFO[prog]
        count = info["msn_count"]
        # spread MSN numbers over the program block, ascending
        step = max(1, info["msn_max"] // count)
        numbers = sorted(set(
            min(info["msn_max"], max(1, i * step + rng.randint(0, max(1, step - 1))))
            for i in range(count)))
        while len(numbers) < count:
            cand = rng.randint(1, info["msn_max"])
            if cand not in numbers:
                numbers.append(cand)
                numbers.sort()
        eligible_ops = [o for o in operators if prog in o["fleet_programs"].split("|")]
        span_years = DATASET_END.year - info["eis_year"]
        for idx, num in enumerate(numbers):
            msn = f"{prog}{num:05d}"
            frac = idx / max(1, count - 1)
            year = info["eis_year"] + int(frac * span_years)
            year = min(year, DATASET_END.year)
            month = rng.randint(1, 12)
            day = rng.randint(1, 28)
            delivery = dt.date(year, month, day)
            if delivery > DATASET_END:
                delivery = DATASET_END - dt.timedelta(days=rng.randint(1, 300))
            atype = weighted(rng, info["types"], info["type_weights"])
            model = rng.choice(info["models"][atype])
            engine = rng.choice(info["engines"][atype])
            op = rng.choice(eligible_ops) if eligible_ops else rng.choice(operators)
            prefix = op["registration_prefix"]
            for _ in range(60):
                letters = "".join(rng.choice("ABCDEFGHIJKLMNOPQRSTUVWXYZ") for _ in range(3))
                reg = f"{prefix}-{letters}" if len(prefix) <= 2 else f"{prefix}{letters}"
                if reg not in used_regs:
                    break
            used_regs.add(reg)
            status = weighted(rng, ["IN_SERVICE", "STORED", "IN_PRODUCTION", "WITHDRAWN"],
                              [86, 7, 5, 2])
            if status == "IN_PRODUCTION" and frac < 0.9:
                status = "IN_SERVICE"
            rows.append([msn, prog, atype, model, num, op["operator_code"], engine,
                         delivery.isoformat(), reg, status])
    rows.sort(key=lambda r: r[0])
    return write_csv("msn_registry.csv",
                     ["msn", "program", "aircraft_type", "model", "line_number",
                      "operator_code", "engine_type", "delivery_date", "registration",
                      "status"], rows)


# --------------------------------------------------------------------------
# users and engineering rules (drive the application, not the documents)
# --------------------------------------------------------------------------

# Pre-analysis engineers. Specialities overlap so most chapters have more than
# one plausible owner, and a few chapters have none — which is what keeps the
# dashboard's "Unassigned Items" tile non-empty.
USERS = [
    ("mlefevre",  "Marc",     "Lefevre",    "Valmont",      "ESZS", "Senior Stress Engineer",        "53|52|57"),
    ("kbrandt",   "Katrin",   "Brandt",     "Neuhafen",       "ESZS", "Structures Pre-Analysis Lead",  "53|51|56"),
    ("rocallpo",  "Rosa",     "Callpo",     "Alcadara",        "EDSA", "Repair Design Engineer",        "51|54|55"),
    ("jtennant",  "James",    "Tennant",    "Ashcombe",        "EDSA", "Wing Structures Engineer",      "57|27|54"),
    ("acostanza", "Alessia",  "Costanza",   "Valmont",      "EVAA", "Systems Integration Engineer",  "21|36|30"),
    ("pnovak",    "Petr",     "Novak",      "Neuhafen",       "EVAA", "Hydraulics and Gear Engineer",  "29|32|"),
    ("shalvorse", "Sigrid",   "Halvorsen",  "Wendmark",        "ELSC", "Electrical Systems Engineer",   "24|92|33"),
    ("dmarchett", "Davide",   "Marchetti",  "Valmont",      "ELSC", "Wiring Integration Engineer",   "92|24|"),
    ("yokafor",   "Yemi",     "Okafor",     "Valmont",      "EGAM", "Avionics Engineer",             "34|31|42"),
    ("lferreira", "Luisa",    "Ferreira",   "Port-Rivage", "EGAM", "Flight Controls Engineer",      "27|22|"),
    ("hnakamura", "Haruki",   "Nakamura",   "Valmont",      "EAAM", "Powerplant Engineer",           "71|78|73"),
    ("cbergstro", "Clara",    "Bergstrom",  "Kingsmoor",     "EAAM", "Fuel Systems Engineer",         "28|47|"),
    ("mvandenb",  "Maaike",   "van den Berg","Vaubranche",       "ETGC", "Cabin Systems Engineer",        "25|44|38"),
    ("tafolabi",  "Tunde",    "Afolabi",    "Mobile",        "ETGC", "Cabin Interiors Engineer",      "25|33|35"),
    ("ekowalska", "Ewa",      "Kowalska",   "Neuhafen",       "EYIS", "In-Service Engineer",           "52|26|"),
    ("bdurand",   "Bruno",    "Durand",     "Valmont",      "EYIS", "Continued Airworthiness Lead",  "53|32|28"),
]


def gen_users(rng):
    rows = []
    for user_name, first, last, site, dept, role, ata in USERS:
        email = f"{first.lower()}.{last.lower().replace(' ', '')}@example-airframer.test"
        programs = "|".join(sorted(rng.sample(PROGRAMS, rng.choice([2, 3, 3, 4, 5])),
                                   key=PROGRAMS.index))
        rows.append([user_name, email, first, last, site, dept, role,
                     "|".join(x for x in ata.split("|") if x), programs])
    rows.sort(key=lambda r: r[0])
    return write_csv("users.csv",
                     ["user_name", "email", "first_name", "last_name", "site",
                      "department", "role", "ata_specialities", "programs"], rows)


# Engineering rules a pre-analysis must check against. Each row is a distinct,
# hand-written rule; the generator only assigns ids and applicability.
RULES = [
    ("DESIGN", "MANDATORY", "53|52|57", "Minimum edge distance on a reworked fastener hole",
     "A reworked fastener hole must retain an edge distance of at least 2.0 times the final fastener diameter to the nearest free edge, and at least 1.5 times to an adjacent hole centre. A repair that cannot hold both limits requires a new load path, not an oversize fastener."),
    ("DESIGN", "MANDATORY", "53|51", "Doubler taper ratio at a load-diffusion joint",
     "The thickness taper of a metallic doubler must not exceed 1 in 10 measured along the primary load direction. A steeper taper concentrates load at the doubler run-out and invalidates the fatigue substantiation of the parent structure."),
    ("DESIGN", "RECOMMENDED", "53|52|56", "Corner radius at a cut-out",
     "Any new cut-out in pressurised skin must have a corner radius of at least 12 mm. Where the cut-out is within two frame bays of an existing door surround, the radius must be justified by a dedicated fatigue analysis."),
    ("DESIGN", "MANDATORY", "51|53|57", "Mixed-material galvanic separation",
     "A titanium or carbon-fibre part in contact with an aluminium alloy substructure requires an interposed insulating layer and wet assembly with sealant. Direct contact is not permitted regardless of the protective treatment applied to either face."),
    ("DESIGN", "RECOMMENDED", "51|54", "Repair stacking limit",
     "No more than two approved repairs may overlap within a single skin bay. A third repair in the same bay requires an engineering assessment of the combined static and fatigue effect, and cannot be released under a standard repair scheme."),
    ("CERTIFICATION", "MANDATORY", "53|55|57", "Damage tolerance re-substantiation trigger",
     "Any modification that changes a principal structural element, its load path, or its inspection accessibility requires the damage tolerance evaluation to be re-run and the resulting inspection threshold and interval to be re-issued."),
    ("CERTIFICATION", "MANDATORY", "27|22|31", "Flight control system change classification",
     "A change affecting flight control laws, actuation authority or the associated monitoring is classified as a major change and requires authority involvement. It cannot be embodied under a minor change classification even when the hardware is unchanged."),
    ("CERTIFICATION", "RECOMMENDED", "24|42|46", "Software load certification evidence",
     "A software load carried by a modification must reference the certified configuration index it belongs to. A load whose certification status is not CERTIFIED may be embodied only under a flight test permit."),
    ("CERTIFICATION", "MANDATORY", "26|35|33", "Emergency equipment relocation",
     "Relocating emergency equipment, escape path lighting or an exit marking requires re-demonstration of the evacuation provisions affected. Equivalent placement is not sufficient evidence on its own."),
    ("SAFETY", "MANDATORY", "28|24|92", "Fuel tank ignition prevention",
     "Any modification introducing wiring, a bracket or a fastener penetration into or within 25 mm of a fuel tank boundary must be assessed against the ignition prevention requirements before release. Bonding resistance must be measured, not inferred."),
    ("SAFETY", "MANDATORY", "26|71|78", "Fire zone penetration",
     "A new penetration of a designated fire zone boundary requires a fireproof or fire-resistant seal qualified for that zone. A modification that adds such a penetration cannot be released without the qualification evidence attached."),
    ("SAFETY", "RECOMMENDED", "32|29", "Landing gear bay clearance",
     "A minimum dynamic clearance of 20 mm must be retained between any new installation in a landing gear bay and the swept envelope of the gear, doors and their linkages, at maximum tyre growth and full oleo extension."),
    ("MAINTAINABILITY", "RECOMMENDED", "53|52|25", "Access retention",
     "A modification must not remove access to an item with a scheduled maintenance task. Where access is reduced, the affected task must be re-timed and the maintenance planning document updated in the same change."),
    ("MAINTAINABILITY", "RECOMMENDED", "24|92|34", "Line-replaceable unit removal path",
     "A line-replaceable unit must remain removable without disturbing an adjacent system installation. A modification that makes removal conditional on removing another unit requires an explicit maintainability concession."),
    ("MAINTAINABILITY", "ADVISORY", "51|53|57", "Inspection method preservation",
     "Where an existing repeat inspection uses eddy current or ultrasonic methods, a modification covering the inspected area must preserve probe access or supply an equivalent alternative method with the same detection capability."),
    ("INTERCHANGEABILITY", "MANDATORY", "53|57|32", "One-way interchangeability declaration",
     "Where a new part can replace the old but not the reverse, the modification must declare ONE_WAY_INTERCHANGEABLE and the illustrated parts data must carry both items with explicit pre-mod and post-mod effectivity."),
    ("INTERCHANGEABILITY", "MANDATORY", "25|44|33", "Mixed configuration in a single zone",
     "Pre-modification and post-modification standards of the same item must not be mixed within one zone unless the modification explicitly permits it. Where mixing is permitted, the permitted combinations must be listed."),
    ("INTERCHANGEABILITY", "RECOMMENDED", "24|92", "Connector keying change",
     "A change to connector keying, contact arrangement or backshell must be accompanied by a change of part number. Retaining the part number across a keying change is not permitted."),
    ("WEIGHT", "RECOMMENDED", "53|57|25", "Weight and balance reporting threshold",
     "A modification whose mass change exceeds 2 kg per aircraft, or which is asymmetric about the aircraft centre line by more than 0.5 kg, must report an updated centre-of-gravity effect."),
    ("WEIGHT", "ADVISORY", "25|44", "Cumulative cabin weight growth",
     "Cabin modifications are assessed cumulatively per aircraft standard. A change taking the running total beyond the agreed cabin weight allowance requires a compensating reduction elsewhere in the same standard."),
    ("DOCUMENTATION", "MANDATORY", "53|52|57", "Manual impact declaration",
     "A modification that changes a part number, an access route, a torque value or an inspection requirement must declare the affected maintenance, parts and repair manual content. An undeclared manual impact blocks release."),
    ("DOCUMENTATION", "MANDATORY", "51|53", "Repair scheme revalidation",
     "Where a modification lands on structure covered by an existing repair scheme, that scheme must be reviewed and either revalidated, restricted or withdrawn as part of the modification."),
    ("DOCUMENTATION", "RECOMMENDED", "92|24", "Wiring diagram currency",
     "A modification adding, removing or rerouting a harness must deliver the revised wiring diagram sheets in the same release. Diagrams may not lag the embodiment of the change."),
    ("PROCESS", "MANDATORY", "53|52|57|27", "Concurrent embodiment declaration",
     "Where two modifications affect the same installation envelope, the embodiment order must be declared on both. An undeclared ordering constraint is treated as a blocking pre-analysis finding."),
    ("PROCESS", "MANDATORY", "52|53", "Effectivity condition on a superseding change",
     "When a modification supersedes another, every service bulletin whose effectivity is expressed as pre-modification of the superseded change must be re-issued or explicitly confirmed as unaffected."),
    ("PROCESS", "RECOMMENDED", "21|36|30", "Supplier change traceability",
     "A change of supplier for a qualified item requires the qualification evidence to be re-established and the previous supplier part number retained in the parts data as a superseded reference."),
    ("PROCESS", "ADVISORY", "34|31|42", "Configuration index alignment",
     "A modification touching an avionics platform must state the target configuration index. Modifications that leave the index unstated cannot be scheduled into a production standard."),
    ("DESIGN", "RECOMMENDED", "29|28|36", "Pipe routing separation",
     "A hydraulic or fuel line must maintain 15 mm separation from any electrical harness, or be separated by a physical barrier. Where separation cannot be held, the harness must be routed above the line, never below it."),
    ("DESIGN", "MANDATORY", "27|57", "Control run friction budget",
     "A modification adding a bracket, seal or guide to a mechanical control run must demonstrate that the total run friction stays inside the qualified budget. Incremental additions are assessed against the cumulative figure."),
    ("MAINTAINABILITY", "RECOMMENDED", "38|25|44", "Drain path preservation",
     "A modification in a lower fuselage or lavatory zone must not obstruct an existing drain path. Where a drain path is altered, the revised path must be shown to clear the same volume in the same attitude range."),
    ("DESIGN", "MANDATORY", "21|36", "Hot bleed duct proximity",
     "A duct carrying bleed air above 200 degrees Celsius must hold 25 mm clearance to any flammable fluid line and be shrouded wherever the clearance falls below 50 mm. A modification that reduces the gap must show the duct burst plume misses the fluid line and its fittings."),
    ("DESIGN", "RECOMMENDED", "21|38", "Condensate fall in distribution ducting",
     "Air conditioning distribution ducting must fall continuously toward a drain point at not less than 1 in 50. A modification that introduces a local low point must add a drain of at least 6 mm bore discharging to an approved overboard path."),
    ("DESIGN", "RECOMMENDED", "30|57", "Heater element routing and overlap",
     "A heater mat or element rerouted by a modification must retain a bend radius of at least ten times the element thickness and must not cross a structural fastener line. Overlapping two elements to close a coverage gap is not permitted in any zone."),
    ("DESIGN", "MANDATORY", "35|25", "Oxygen path cleanliness",
     "Any part introduced into an oxygen path must be delivered oxygen-clean and sealed, with residual hydrocarbon contamination below 5 mg per square metre. A modification may not call up a lubricant, sealant or thread compound absent from the approved oxygen service list."),
    ("DESIGN", "RECOMMENDED", "38|25|44", "Freeze protection on relocated water lines",
     "A potable or waste line routed outside a heated compartment must carry trace heating and insulation sized for the certified cold soak case. A modification that moves a line into an unheated zone must deliver that protection within the same change, not as a follow-on."),
    ("DESIGN", "MANDATORY", "73|71", "Shrouded fuel line in an engine fire zone",
     "A fuel line inside a designated engine fire zone must be of shrouded or double-wall construction with the interspace drained overboard at the lowest point. Replacing such a line with a single-wall equivalent is not acceptable regardless of material or rating."),
    ("DESIGN", "RECOMMENDED", "78|54", "Thrust reverser stroke clearance",
     "A modification inside a thrust reverser or its surrounding fairing must retain 15 mm clearance to the blocker doors and translating sleeve throughout the full deployment stroke, assessed with the maximum accumulated rigging tolerance stacked adversely."),
    ("DESIGN", "MANDATORY", "80|24", "Ignition channel segregation",
     "A starting or ignition circuit modification must keep the two ignition channels segregated over their full run. The channels may not share a connector, a backshell, a conduit or a common bracket at any point between the exciter and the igniter plug."),
    ("DESIGN", "MANDATORY", "23|92", "Antenna bonding resistance",
     "A new or relocated antenna must achieve a bonding resistance below 2.5 milliohms to structure, measured at four points around the base flange after installation. A conductive gasket is not accepted as the primary bonding path."),
    ("DESIGN", "ADVISORY", "23|34", "Co-site separation between antennas",
     "A communications antenna should be installed at least 1.2 m from any navigation antenna working in an adjacent band, or the pairing must be cleared by an electromagnetic compatibility test. A co-site conflict may not be resolved by reducing transmit power."),
    ("PROCESS", "MANDATORY", "36|29|21", "Proof pressure after a pressurised line intervention",
     "Any pressurised duct, line or manifold disturbed by a modification must be proof tested to 1.5 times maximum working pressure before return to service. The test record must be called up by reference in the accomplishment instructions."),
    ("PROCESS", "MANDATORY", "73|76|80", "Engine control rigging revalidation",
     "A modification affecting thrust lever signalling, engine control unit wiring or the start sequence requires the engine control rigging to be re-checked and the idle and maximum stops re-verified on every affected engine position."),
    ("PROCESS", "RECOMMENDED", "80|73", "Dry motoring before first fuel-on start",
     "After a modification to engine fuel metering, the start valve or ignition hardware, a dry motoring run followed by a leak check at maximum motoring pressure must be completed before the first fuel-on start of that engine."),
    ("PROCESS", "RECOMMENDED", "45|42|46", "Reported configuration alignment",
     "A change of equipment identification, part number or software version must be reflected in the central maintenance configuration database within the same production standard. The reported configuration must match the installed configuration at delivery."),
    ("PROCESS", "ADVISORY", "20|51|92", "Named standard practices revision",
     "A modification should name the revision of the standard practices manual its processes are drawn from. Where a process is not contained in that revision, the qualified process specification must be attached to the modification itself."),
    ("PROCESS", "MANDATORY", "92|24|31", "Electrical close-out testing",
     "After a harness modification, continuity and insulation resistance must be measured on every affected circuit and the bonding of each new bracket verified below 10 milliohms. Sampling a subset of circuits is not accepted in place of full testing."),
    ("PROCESS", "MANDATORY", "32|29", "Retraction test after gear or supply work",
     "A modification to landing gear structure, its actuation or the hydraulic supply feeding it must be closed by a full retraction and extension test on jacks, including one alternate extension, before the aircraft returns to service."),
    ("PROCESS", "MANDATORY", "52|53", "Door re-rig and seal check",
     "A modification to a passenger or cargo door structure, seal or hinge must be followed by a re-rig against the door rigging datum and a pressurisation seal check. Shimming out a rigging discrepancy is not an acceptable close-out."),
    ("CERTIFICATION", "RECOMMENDED", "21|25", "Ventilation rate on a cabin reconfiguration",
     "A cabin reconfiguration that changes seat count or monument position must confirm the delivered fresh air flow per occupant stays at or above the certified minimum in every zone, evaluated at the least favourable pack configuration."),
    ("CERTIFICATION", "MANDATORY", "34|31", "Air data source relocation",
     "A pitot or static port moved by more than 25 mm requires the static source error correction to be re-established by flight test. The previous correction table may not be carried over on the basis of geometric similarity."),
    ("CERTIFICATION", "MANDATORY", "56|53", "Flight deck transparency substitution",
     "A flight deck transparency change must retain the certified bird strike capability at the design speed at sea level, and the interlayer heating must reach the qualified temperature distribution within the specified warm-up time."),
    ("CERTIFICATION", "RECOMMENDED", "42|45", "Hosted application resource budgets",
     "Hosting a new application on an integrated modular avionics platform requires the allocated processing, memory and bandwidth budgets to be re-issued, with partitioning evidence showing no change to the resources available to existing applications."),
    ("CERTIFICATION", "RECOMMENDED", "22|34", "Flight guidance annunciation change",
     "A change to flight guidance mode annunciation or its symbology requires a human factors assessment and a simulator evaluation with line crews before the change can be classified. Mode confusion findings must be closed before release."),
    ("CERTIFICATION", "MANDATORY", "49|24", "Auxiliary power start envelope",
     "A modification affecting auxiliary power unit start capability, its electrical supply or its bleed availability must state the effect on the in-flight start envelope. Where the aircraft holds an extended operations approval, that basis must be re-confirmed."),
    ("CERTIFICATION", "MANDATORY", "55|27|57", "Control surface mass balance",
     "A modification adding mass to a control surface, tab or its attachment must re-establish the static balance of that surface inside the qualified moment band and record the measured value. Compensating by removing mass from the opposite surface is not permitted."),
    ("SAFETY", "MANDATORY", "21|26|36", "Overheat detection loop coverage",
     "Where a bleed or pack installation is protected by an overheat detection loop, a modification that displaces, shortens or reroutes the loop must re-demonstrate detection within 5 seconds of a leak at the worst-case position along the duct run."),
    ("SAFETY", "MANDATORY", "35|33|25", "Oxygen container drop envelope",
     "A modification to a ceiling panel, stowage or lighting rail above a seat row must preserve the unobstructed drop envelope of the oxygen mask container. The affected rows must be verified by a physical drop test, not by clearance analysis."),
    ("SAFETY", "MANDATORY", "38|53", "Waste drain mast integrity",
     "A modification affecting a waste drain mast or its heater must preserve the anti-ice function and the leak-free interface at the fuselage penetration. A drain path that can ice over within the certified envelope is not acceptable under any dispatch condition."),
    ("SAFETY", "MANDATORY", "78|76", "Reverser deployment inhibits",
     "Any change to thrust reverser locking, position sensing or its command path must preserve two independent inhibits against inadvertent in-flight deployment. Loss of independence between the inhibits blocks release of the modification."),
    ("SAFETY", "MANDATORY", "79|71", "Oil joint retention in an engine zone",
     "An oil line, fitting or scavenge component introduced in an engine zone must be positively retained by lockwire or an equivalent qualified means, and must be shown not to spray onto a surface hotter than the oil auto-ignition temperature after a single joint failure."),
    ("SAFETY", "MANDATORY", "46|24", "Domain segregation for information systems",
     "An information system exchanging data with the aircraft control domain must do so through an approved one-way gateway. A modification may not create a bidirectional path between an open network and an avionics network under any traffic condition."),
    ("SAFETY", "MANDATORY", "49|54", "Auxiliary power compartment firewall",
     "The auxiliary power unit compartment firewall must retain its 15-minute fire resistance across every penetration. A modification adding a bracket, cable or duct through the firewall must use a qualified fitting and be sealed on the cold side."),
    ("MAINTAINABILITY", "RECOMMENDED", "49|71", "Auxiliary power unit removal path",
     "The auxiliary power unit must remain removable as a single unit through the existing access opening. Equipment added inside the compartment must be shown clear of the removal path with at least 25 mm margin at every point of the extraction."),
    ("MAINTAINABILITY", "RECOMMENDED", "79|49", "Oil servicing access",
     "A modification in an engine or auxiliary power compartment must preserve line access to the oil quantity indication and the replenishment point without opening a second access panel or calling up a higher access platform than the current task specifies."),
    ("MAINTAINABILITY", "RECOMMENDED", "45|31", "Fault message mapping",
     "A modification that replaces a monitored unit must map every existing central maintenance fault message onto the new unit or withdraw the message explicitly. Orphan fault messages are treated as a blocking pre-analysis finding."),
    ("MAINTAINABILITY", "ADVISORY", "33|25", "Cabin lighting unit replacement",
     "A cabin lighting unit should remain replaceable from inside the cabin without removing a ceiling or sidewall panel. Where panel removal becomes necessary, the elapsed task time must be re-stated in the maintenance planning data."),
    ("MAINTAINABILITY", "ADVISORY", "56|25", "Cabin window inspection access",
     "A cabin window reveal or shade modification should leave the pane and its seal inspectable without special tooling. A bonded reveal is acceptable only where a compensating inspection is added to the maintenance programme in the same change."),
    ("MAINTAINABILITY", "RECOMMENDED", "27|55", "Attachment fitting inspection access",
     "A modification on a stabilizer or a control surface must preserve access to the attachment fitting inspection points named in the airworthiness limitations. Access that becomes conditional on surface removal must be re-timed in the maintenance programme."),
    ("DOCUMENTATION", "MANDATORY", "73|76|79|71", "Powerplant data impact",
     "A modification touching engine-mounted equipment must declare its impact on the engine and pylon maintenance data, listing the affected task numbers, torque values and build standard tables. An undeclared powerplant data impact blocks release."),
    ("DOCUMENTATION", "RECOMMENDED", "35|38|25", "Placard and marking revision",
     "A modification that changes the content, position or language of a placard, marking or safety instruction must deliver the revised placard drawing and the updated cabin marking list within the same release."),
    ("DOCUMENTATION", "MANDATORY", "30|21|36", "Dispatch condition declaration",
     "A modification affecting a system carrying a minimum equipment list entry must state whether the dispatch condition, the associated operational procedure or the rectification interval changes. An unstated dispatch impact blocks release."),
    ("DOCUMENTATION", "ADVISORY", "23|46|45", "Datalink configuration table delivery",
     "A modification changing a communications or datalink configuration item should deliver the revised configuration table and the associated flight-deck message list in the same release, identified by the configuration item revision."),
    ("DOCUMENTATION", "RECOMMENDED", "22|31|34", "Flight-deck effect documentation",
     "A modification that changes a flight-deck indication, alert or its associated procedure must deliver the revised flight crew content and alert list in the same release, stating the alert identifier and its inhibit conditions."),
    ("INTERCHANGEABILITY", "MANDATORY", "45|42|46", "Software part number on a behavioural change",
     "A software load whose behaviour changes in any observable way must carry a new software part number. Re-issuing a load under the same part number is not permitted even where the change is limited to configuration data."),
    ("INTERCHANGEABILITY", "RECOMMENDED", "21|30|36", "Flow characteristic on a replacement valve",
     "A replacement valve, duct or heat exchanger must match the flow characteristic of the item it supersedes across the whole regulated range. Where the characteristic differs, the modification must declare the resulting mixed-fleet restriction per aircraft."),
    ("INTERCHANGEABILITY", "RECOMMENDED", "71|73|79", "Engine build standard mixing",
     "Engine-mounted parts belonging to different build standards must not be mixed on one engine unless the modification lists the permitted combinations. That list must be reproduced in the illustrated parts data for every affected figure."),
    ("INTERCHANGEABILITY", "RECOMMENDED", "32|29", "Gear position standard mixing",
     "A landing gear component or its hydraulic actuator introduced at a new standard must be declared interchangeable per gear position. Fitting different standards to the left and right positions is prohibited unless listed as a permitted combination."),
    ("WEIGHT", "MANDATORY", "54|71|78", "Nacelle mass asymmetry",
     "Mass added to a nacelle, pylon fairing or thrust reverser half must be declared per engine position, and a port-to-starboard difference above 1.5 kg must be compensated. Any single addition above 1 kg requires a powerplant vibration survey before release."),
    ("WEIGHT", "ADVISORY", "44|46|23", "Installed mass of a cabin systems fit",
     "A cabin systems installation should report installed mass including harnesses, brackets and cooling provisions, not the unit mass alone. Harness mass is estimated at 45 g per metre per conductor pair unless the run is weighed."),
    ("WEIGHT", "RECOMMENDED", "38|21", "Water tank capacity change reporting",
     "A change to potable water tank capacity or to the number of tanks installed must be reported both as a dry mass and as a full-tank mass, with the effect on the loading limits stated at the extreme fill states."),
]


def gen_rules(rng):
    rows = []
    for idx, (category, severity, ata, title, text) in enumerate(RULES, start=1):
        atas = [a for a in ata.split("|") if a]
        rule_id = f"RUL-{idx:04d}"
        programs = "|".join(PROGRAMS) if rng.random() < 0.6 else "|".join(
            sorted(rng.sample(PROGRAMS, rng.choice([2, 3, 4])), key=PROGRAMS.index))
        source = rng.choice(["TDH-100 Technical Design Handbook",
                             "EPM-22 Engineering Process Manual",
                             "SRH-14 Structural Repair Handbook",
                             "CAW-07 Continued Airworthiness Manual",
                             "SYS-31 Systems Installation Standard"])
        rows.append([rule_id, title, text, category, severity, "|".join(atas),
                     programs, source])
    rows.sort(key=lambda r: r[0])
    return write_csv("rules.csv",
                     ["rule_id", "title", "text", "category", "severity",
                      "ata_2d", "programs", "source"], rows)


# --------------------------------------------------------------------------
# check
# --------------------------------------------------------------------------


def check():
    errors = []
    ata = {r["code"]: r for r in read_csv("ata_chapters.csv")}
    parts = read_csv("part_numbers.csv")
    fins = read_csv("fins.csv")
    software = read_csv("software.csv")
    drawings = read_csv("drawings.csv")
    zones = {r["zone"] for r in read_csv("zones.csv")}
    msns = read_csv("msn_registry.csv")
    operators = {r["operator_code"] for r in read_csv("operators.csv")}
    suppliers = {r["supplier_code"] for r in read_csv("suppliers.csv")}

    part_index = {p["part_number"]: p for p in parts}
    fin_index = {f["fin"]: f for f in fins}

    def level6(code):
        return code in ata and ata[code]["level"] == "6"

    for p in parts:
        if not level6(p["ata_6d"]):
            errors.append(f"part {p['part_number']}: unknown ATA 6D {p['ata_6d']}")
        if p["supplier_code"] not in suppliers:
            errors.append(f"part {p['part_number']}: unknown supplier {p['supplier_code']}")
        if p["interchangeable_with"] and p["interchangeable_with"] not in part_index:
            errors.append(f"part {p['part_number']}: dangling interchangeable_with")
    for f in fins:
        if not level6(f["ata_6d"]):
            errors.append(f"fin {f['fin']}: unknown ATA 6D {f['ata_6d']}")
        if f["default_part_number"] not in part_index:
            errors.append(f"fin {f['fin']}: unknown default_part_number")
        elif not (set(f["programs"].split("|"))
                  & set(part_index[f["default_part_number"]]["programs"].split("|"))):
            errors.append(f"fin {f['fin']}: default part not program-compatible")
        if f["zone"] not in zones:
            errors.append(f"fin {f['fin']}: unknown zone {f['zone']}")
    for s in software:
        if s["host_fin"] not in fin_index:
            errors.append(f"software {s['software_pn']}: unknown host_fin {s['host_fin']}")
        if s["supersedes"] and s["supersedes"] not in {x["software_pn"] for x in software}:
            errors.append(f"software {s['software_pn']}: dangling supersedes")
    for d in drawings:
        if not level6(d["ata_6d"]):
            errors.append(f"drawing {d['drawing_number']}: unknown ATA 6D")
    for m in msns:
        if m["operator_code"] not in operators:
            errors.append(f"msn {m['msn']}: unknown operator")
        if m["msn"][0] != m["program"]:
            errors.append(f"msn {m['msn']}: program letter mismatch")

    for key, rows_ in (("part_number", parts), ("fin", fins), ("msn", msns),
                       ("drawing_number", drawings), ("software_pn", software)):
        seen = set()
        for r in rows_:
            if r[key] in seen:
                errors.append(f"duplicate {key}: {r[key]}")
            seen.add(r[key])

    for prog in PROGRAMS:
        np_ = sum(1 for p in parts if prog in p["programs"].split("|"))
        nf = sum(1 for f in fins if prog in f["programs"].split("|"))
        nd = sum(1 for d in drawings if prog in d["programs"].split("|"))
        ns = sum(1 for s in software if prog in s["programs"].split("|"))
        nm = sum(1 for m in msns if m["program"] == prog)
        print(f"  {prog}: {np_:5d} parts  {nf:5d} fins  {nd:5d} drawings  "
              f"{ns:4d} software  {nm:4d} msns")
        if np_ < 400:
            errors.append(f"program {prog}: only {np_} parts (need >= 400)")
        if nf < 200:
            errors.append(f"program {prog}: only {nf} fins (need >= 200)")

    if errors:
        print(f"\n{len(errors)} error(s):")
        for e in errors[:40]:
            print("  -", e)
        return 1
    print("\nreference data OK")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="verify existing reference data")
    args = ap.parse_args()
    if args.check:
        return check()

    rng = random.Random(RANDOM_SEED)
    results = []
    results.append(gen_ata())
    results.append(gen_zones(random.Random(RANDOM_SEED + 1)))
    results.append(gen_suppliers(random.Random(RANDOM_SEED + 2)))
    results.append(gen_operators(random.Random(RANDOM_SEED + 3)))
    suppliers = read_csv("suppliers.csv")
    results.append(gen_part_numbers(random.Random(RANDOM_SEED + 4), suppliers))
    parts = read_csv("part_numbers.csv")
    zones = read_csv("zones.csv")
    results.append(gen_fins(random.Random(RANDOM_SEED + 5), parts, zones))
    fins = read_csv("fins.csv")
    results.append(gen_software(random.Random(RANDOM_SEED + 6), fins))
    results.append(gen_drawings(random.Random(RANDOM_SEED + 7)))
    operators = read_csv("operators.csv")
    results.append(gen_msns(random.Random(RANDOM_SEED + 8), operators))
    results.append(gen_users(random.Random(RANDOM_SEED + 9)))
    results.append(gen_rules(random.Random(RANDOM_SEED + 10)))

    total = 0
    for path, count in results:
        total += count
        print(f"  {os.path.basename(path):24s} {count:6d} rows")
    print(f"  {'TOTAL':24s} {total:6d} rows")
    del rng
    return check()


if __name__ == "__main__":
    sys.exit(main())
