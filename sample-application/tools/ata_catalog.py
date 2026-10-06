"""ATA 2D/4D/6D catalogue for the Aeralis technical-data demo dataset.

Curated, synthetic, and deliberately plausible rather than authoritative.
Structure::

    ATA_2D = {
        "53": (title, domain, {
            "21": (title, [6d_title, 6d_title, ...]),
        }),
    }

6D codes are assigned from ``SIX_D_SUFFIXES`` in order, so ``53-21`` with three
sub-subjects yields ``53-21-00``, ``53-21-11`` and ``53-21-12``.
"""

SIX_D_SUFFIXES = ["00", "11", "12", "13", "21", "22", "31", "32", "41", "42"]

# Chapters that are structural; used for SRM eligibility and part descriptions.
STRUCTURE_CHAPTERS = {"51", "52", "53", "54", "55", "56", "57"}

ATA_2D = {
    "20": ("Standard Practices — Airframe", "STANDARD_PRACTICES", {
        "10": ("Safety Practices", ["General", "Grounding and Bonding", "Personnel Protection"]),
        "20": ("Electrical Standard Practices", ["General", "Wire Splicing and Termination", "Connector Assembly"]),
        "30": ("Fastener Standard Practices", ["General", "Bolted Joints", "Riveted Joints", "Torque Values"]),
        "40": ("Sealing and Bonding", ["General", "Sealant Application", "Adhesive Bonding"]),
    }),
    "21": ("Air Conditioning", "SYSTEMS", {
        "20": ("Distribution", ["General", "Cabin Air Distribution", "Cockpit Air Distribution", "Recirculation"]),
        "30": ("Pressurization Control", ["General", "Outflow Valve Control", "Cabin Pressure Indicating"]),
        "40": ("Heating", ["General", "Cargo Compartment Heating", "Floor Heating"]),
        "50": ("Cooling", ["General", "Air Cycle Machine", "Heat Exchangers"]),
        "60": ("Temperature Control", ["General", "Trim Air System", "Zone Controller"]),
    }),
    "22": ("Auto Flight", "AVIONICS", {
        "10": ("Autopilot", ["General", "Flight Guidance Computer", "Servo Control"]),
        "30": ("Auto Throttle", ["General", "Thrust Control Computer", "Throttle Actuation"]),
        "80": ("Flight Management Computing", ["General", "Flight Management Computer", "Navigation Database Loading"]),
    }),
    "23": ("Communications", "AVIONICS", {
        "10": ("HF Communication", ["General", "HF Transceiver", "HF Antenna Coupler"]),
        "20": ("VHF Communication", ["General", "VHF Transceiver", "VHF Antenna"]),
        "50": ("Audio Integrating", ["General", "Audio Management Unit", "Flight Interphone"]),
        "70": ("Datalink", ["General", "ATC Datalink Router", "SATCOM Data Unit"]),
    }),
    "24": ("Electrical Power", "ELECTRICAL", {
        "20": ("AC Generation", ["General", "Integrated Drive Generator", "Generator Control Unit"]),
        "30": ("DC Generation", ["General", "Transformer Rectifier Unit", "Battery Charge Limiter"]),
        "40": ("External Power", ["General", "External Power Receptacle", "External Power Contactor"]),
        "50": ("AC Distribution", ["General", "Primary Distribution Centre", "Secondary Distribution Box"]),
        "60": ("DC Distribution", ["General", "DC Bus Bar Assembly", "Essential Bus Transfer"]),
    }),
    "25": ("Equipment and Furnishings", "CABIN", {
        "10": ("Flight Compartment", ["General", "Pilot Seats", "Observer Seat Installation"]),
        "20": ("Passenger Compartment", ["General", "Passenger Seats", "Overhead Stowage Compartments", "Cabin Dividers"]),
        "30": ("Galley", ["General", "Galley Structure", "Galley Inserts and Trolleys"]),
        "50": ("Cargo Compartment", ["General", "Cargo Lining Panels", "Cargo Loading System", "Cargo Nets and Restraints"]),
        "60": ("Emergency Equipment", ["General", "Life Vest Stowage", "Escape Slide Installation"]),
    }),
    "26": ("Fire Protection", "SYSTEMS", {
        "10": ("Detection", ["General", "Engine Fire Detection Loops", "Cargo Smoke Detection", "APU Fire Detection"]),
        "20": ("Extinguishing", ["General", "Engine Fire Bottles", "Cargo Extinguishing Distribution"]),
    }),
    "27": ("Flight Controls", "SYSTEMS", {
        "10": ("Aileron and Tab", ["General", "Aileron Actuation", "Aileron Position Sensing"]),
        "20": ("Rudder", ["General", "Rudder Actuation", "Yaw Damper Coupling"]),
        "30": ("Elevator and Tab", ["General", "Elevator Actuation", "Trimmable Horizontal Stabilizer Actuator"]),
        "50": ("Flaps", ["General", "Flap Drive System", "Flap Position Sensing", "Flap Track and Carriage"]),
        "80": ("Slats", ["General", "Slat Drive System", "Slat Position Sensing"]),
        "90": ("Flight Control Computing", ["General", "Flight Control Primary Computer", "Flight Control Secondary Computer"]),
    }),
    "28": ("Fuel", "SYSTEMS", {
        "10": ("Storage", ["General", "Wing Tank Structure", "Centre Tank Structure", "Tank Access Panels"]),
        "20": ("Distribution", ["General", "Boost Pumps", "Crossfeed Valves", "Transfer System"]),
        "40": ("Indicating", ["General", "Fuel Quantity Probes", "Fuel Quantity Computer"]),
    }),
    "29": ("Hydraulic Power", "SYSTEMS", {
        "10": ("Main System", ["General", "Engine Driven Pump", "Reservoir Assembly", "Accumulator"]),
        "20": ("Auxiliary System", ["General", "Electric Motor Pump", "Ram Air Turbine"]),
        "30": ("Indicating", ["General", "Pressure Transmitters", "Quantity Indicating"]),
    }),
    "30": ("Ice and Rain Protection", "SYSTEMS", {
        "10": ("Airfoil Anti-Ice", ["General", "Wing Anti-Ice Valves", "Slat Piccolo Ducts"]),
        "30": ("Pitot and Static Heat", ["General", "Probe Heat Computer", "Probe Heating Elements"]),
        "40": ("Windows and Windshields", ["General", "Windshield Heating Control", "Wiper System"]),
    }),
    "31": ("Indicating and Recording", "AVIONICS", {
        "30": ("Recorders", ["General", "Flight Data Recorder", "Cockpit Voice Recorder"]),
        "50": ("Central Warning", ["General", "Flight Warning Computer", "Aural Warning Generation"]),
        "60": ("Display Systems", ["General", "Display Unit Installation", "Display Management Computer"]),
    }),
    "32": ("Landing Gear", "SYSTEMS", {
        "10": ("Main Gear", ["General", "Main Gear Leg Assembly", "Side Stay and Lock Links"]),
        "20": ("Nose Gear", ["General", "Nose Gear Leg Assembly", "Steering Actuation"]),
        "30": ("Extension and Retraction", ["General", "Landing Gear Control Interface Unit", "Uplock and Downlock"]),
        "40": ("Wheels and Brakes", ["General", "Brake Assembly", "Brake Control Unit", "Tyre Pressure Indicating"]),
    }),
    "33": ("Lights", "CABIN", {
        "10": ("Flight Compartment Lighting", ["General", "Dome Lights", "Instrument Panel Lighting"]),
        "20": ("Passenger Compartment Lighting", ["General", "Ceiling and Wall Wash Lights", "Reading Lights", "Emergency Lighting"]),
        "40": ("Exterior Lighting", ["General", "Landing Lights", "Navigation and Anti-Collision Lights"]),
    }),
    "34": ("Navigation", "AVIONICS", {
        "10": ("Air Data", ["General", "Air Data Module", "Pitot Static Probes"]),
        "20": ("Attitude and Direction", ["General", "Inertial Reference Unit", "Standby Attitude Indicator"]),
        "40": ("Independent Position Determining", ["General", "Weather Radar Transceiver", "Radio Altimeter"]),
        "50": ("Dependent Position Determining", ["General", "Multi-Mode Receiver", "GPS Antenna Installation"]),
    }),
    "35": ("Oxygen", "SYSTEMS", {
        "10": ("Crew Oxygen", ["General", "Crew Oxygen Cylinder", "Crew Mask Regulator"]),
        "20": ("Passenger Oxygen", ["General", "Chemical Oxygen Generators", "Passenger Service Unit Masks"]),
    }),
    "36": ("Pneumatic", "SYSTEMS", {
        "10": ("Distribution", ["General", "Bleed Ducting", "Isolation Valves"]),
        "20": ("Indicating", ["General", "Bleed Monitoring Computer", "Duct Pressure Sensing"]),
    }),
    "38": ("Water and Waste", "CABIN", {
        "10": ("Potable Water", ["General", "Water Tank Assembly", "Water Distribution Lines"]),
        "30": ("Waste Disposal", ["General", "Vacuum Toilet Assembly", "Waste Tank and Drain"]),
    }),
    "42": ("Integrated Modular Avionics", "AVIONICS", {
        "10": ("Core Processing", ["General", "Core Processing Input Output Module", "Module Rack Installation"]),
        "20": ("Network Components", ["General", "AFDX Switch", "Network Cabling"]),
        "40": ("Software Loading", ["General", "Data Loading Configuration", "Onboard Loadable Software Management"]),
    }),
    "44": ("Cabin Systems", "CABIN", {
        "10": ("Cabin Core System", ["General", "Cabin Intercommunication Data System Director", "Cabin Attendant Panel"]),
        "20": ("Inflight Entertainment", ["General", "Seat Display Units", "Head End Server", "IFE Power Supply"]),
        "30": ("External Communication", ["General", "Onboard Connectivity Server", "Wireless Access Points"]),
    }),
    "45": ("Central Maintenance System", "AVIONICS", {
        "20": ("Onboard Maintenance", ["General", "Central Maintenance Computer", "Maintenance Terminal"]),
        "45": ("Data Loading", ["General", "Portable Data Loader Interface", "Data Load Configuration"]),
    }),
    "46": ("Information Systems", "AVIONICS", {
        "20": ("Flight Deck Information", ["General", "Electronic Flight Bag Unit", "EFB Mount and Power"]),
        "40": ("Maintenance Information", ["General", "Aircraft Condition Monitoring Function", "Data Server Module"]),
    }),
    "49": ("Airborne Auxiliary Power", "POWERPLANT", {
        "10": ("Power Plant", ["General", "APU Installation", "APU Mount Assembly"]),
        "50": ("Air Supply", ["General", "APU Load Compressor", "Surge Control Valve"]),
        "60": ("Controls", ["General", "Electronic Control Box", "APU Start Sequencing"]),
    }),
    "51": ("Standard Practices — Structures", "STRUCTURE", {
        "10": ("Investigation and Cleanup", ["General", "Damage Investigation", "Surface Cleaning"]),
        "20": ("Processes", ["General", "Surface Treatment", "Corrosion Removal"]),
        "70": ("Repairs", ["General", "Metallic Repair Practices", "Composite Repair Practices"]),
    }),
    "52": ("Doors", "STRUCTURE", {
        "10": ("Passenger and Crew Doors", ["General", "Door Structure", "Door Hinge and Arm", "Door Seals"]),
        "30": ("Cargo Doors", ["General", "Cargo Door Structure", "Cargo Door Actuation", "Cargo Door Latching"]),
        "40": ("Service Doors", ["General", "Service Door Structure", "Access Panel Installation"]),
        "70": ("Door Warning", ["General", "Door Proximity Sensing", "Door Warning Computing"]),
        "80": ("Landing Gear Doors", ["General", "Main Gear Door Structure", "Nose Gear Door Structure"]),
    }),
    "53": ("Fuselage", "STRUCTURE", {
        "10": ("Fuselage Section 11/12 — Nose", ["General", "Frames", "Skin Panels", "Stringers"]),
        "21": ("Fuselage Section 13 — Forward", ["General", "Frames", "Skin Panels", "Door Surround Structure"]),
        "22": ("Fuselage Section 14 — Centre", ["General", "Frames", "Keel Beam", "Centre Wing Box Junction"]),
        "31": ("Fuselage Section 15/16 — Aft", ["General", "Frames", "Skin Panels", "Pressure Bulkhead"]),
        "40": ("Fuselage Section 19 — Tail Cone", ["General", "Frames and Skin", "APU Firewall"]),
        "50": ("Floor Structure", ["General", "Floor Beams", "Seat Tracks", "Cargo Floor Panels"]),
    }),
    "54": ("Nacelles and Pylons", "STRUCTURE", {
        "10": ("Nacelle Structure", ["General", "Inlet Cowl", "Fan Cowl Doors", "Thrust Reverser Structure"]),
        "50": ("Pylon Structure", ["General", "Pylon Primary Structure", "Engine Mount Fittings", "Pylon Fairings"]),
    }),
    "55": ("Stabilizers", "STRUCTURE", {
        "10": ("Horizontal Stabilizer", ["General", "Stabilizer Box Structure", "Attachment Fittings"]),
        "30": ("Vertical Stabilizer", ["General", "Fin Box Structure", "Fin Attachment Fittings"]),
        "40": ("Rudder Structure", ["General", "Rudder Panel Assembly", "Hinge Fittings"]),
    }),
    "56": ("Windows", "STRUCTURE", {
        "10": ("Flight Compartment Windows", ["General", "Windshield Assembly", "Side Window Assembly"]),
        "20": ("Passenger Compartment Windows", ["General", "Cabin Window Assembly", "Window Frame Structure"]),
    }),
    "57": ("Wings", "STRUCTURE", {
        "10": ("Wing Box Structure", ["General", "Upper Cover Panels", "Lower Cover Panels", "Ribs and Spars"]),
        "20": ("Wing Leading Edge", ["General", "Fixed Leading Edge Structure", "Slat Structure"]),
        "30": ("Wing Trailing Edge", ["General", "Fixed Trailing Edge Structure", "Flap Support Structure"]),
        "50": ("Wing Tip", ["General", "Winglet Structure", "Wing Tip Fairing"]),
    }),
    "71": ("Power Plant", "POWERPLANT", {
        "00": ("Power Plant General", ["General", "Engine Build Up", "Engine Change Procedures"]),
        "10": ("Cowling", ["General", "Cowl Door Structure", "Cowl Latches"]),
        "20": ("Mounts", ["General", "Forward Engine Mount", "Aft Engine Mount"]),
    }),
    "73": ("Engine Fuel and Control", "POWERPLANT", {
        "10": ("Distribution", ["General", "Fuel Metering Unit", "Fuel Nozzles"]),
        "20": ("Controlling", ["General", "Engine Electronic Control Unit", "Control Harness"]),
    }),
    "76": ("Engine Controls", "POWERPLANT", {
        "10": ("Power Control", ["General", "Thrust Lever Assembly", "Throttle Control Unit"]),
        "20": ("Emergency Shutdown", ["General", "Fire Handle Assembly", "Shutdown Relays"]),
    }),
    "78": ("Exhaust", "POWERPLANT", {
        "10": ("Collector and Nozzle", ["General", "Exhaust Nozzle Assembly", "Centre Body"]),
        "30": ("Thrust Reverser", ["General", "Translating Sleeve", "Reverser Actuation", "Cascade Boxes"]),
    }),
    "79": ("Oil", "POWERPLANT", {
        "20": ("Distribution", ["General", "Oil Pump Assembly", "Oil Filter Housing"]),
        "30": ("Indicating", ["General", "Oil Pressure Transmitter", "Oil Quantity Sensing"]),
    }),
    "80": ("Starting", "POWERPLANT", {
        "10": ("Cranking", ["General", "Air Starter Motor", "Start Valve"]),
    }),
    "92": ("Electrical Power Wiring", "ELECTRICAL", {
        "11": ("Flight Compartment Wiring", ["General", "Cockpit Panel Harnesses", "Overhead Panel Wiring"]),
        "21": ("Forward Fuselage Wiring", ["General", "Avionics Bay Harnesses", "Cargo Compartment Wiring", "Door Warning Wiring"]),
        "31": ("Centre Fuselage Wiring", ["General", "Wing Junction Harnesses", "Landing Gear Bay Wiring"]),
        "41": ("Aft Fuselage Wiring", ["General", "Aft Cargo Wiring", "Empennage Harnesses"]),
        "51": ("Wing Wiring", ["General", "Leading Edge Harnesses", "Trailing Edge Harnesses"]),
    }),
}

# --------------------------------------------------------------------------
# Airframe placement — where an illustration callout for this chapter points.
# --------------------------------------------------------------------------
# ``src/Aeralis.Illustrations`` draws the Aeralis three-view sheets and needs a
# section of the airframe plus the view it is best seen on. ``zones.csv`` is the
# primary source; this table is the fallback for a document that lists no zone.
# Values must spell an ``AircraftSection`` / ``ViewKind`` enum member exactly
# (``src/Aeralis.Illustrations/Enums.cs``). 4D and 6D rows inherit their 2D
# chapter's placement.
AIRFRAME_SECTION_2D = {
    "20": ("Fuselage", "Side"),                 # standard practices — airframe-wide
    "21": ("Fuselage", "Side"),                 # packs bay and cabin distribution
    "22": ("Cockpit", "Side"),                  # auto flight
    "23": ("Cockpit", "Side"),                  # communications
    "24": ("ForwardFuselage", "Side"),          # generation and distribution centres
    "25": ("Fuselage", "Side"),                 # equipment and furnishings
    "26": ("Fuselage", "Side"),                 # detection and extinguishing loops
    "27": ("LeftWing", "Plan"),                 # ailerons, flaps, slats
    "28": ("LeftWing", "Plan"),                 # wing tanks
    "29": ("Fuselage", "Side"),                 # hydraulic bay
    "30": ("LeftWing", "Plan"),                 # wing and slat anti-ice
    "31": ("Cockpit", "Side"),                  # indicating and recording
    "32": ("MainLandingGear", "Front"),         # landing gear
    "33": ("Fuselage", "Side"),                 # cabin and exterior lighting
    "34": ("Cockpit", "Side"),                  # navigation
    "35": ("Fuselage", "Side"),                 # crew and passenger oxygen
    "36": ("Fuselage", "Side"),                 # bleed ducting
    "38": ("AftFuselage", "Side"),              # water and waste
    "42": ("ForwardFuselage", "Side"),          # IMA racks in the avionics bay
    "44": ("Fuselage", "Side"),                 # cabin systems
    "45": ("Cockpit", "Side"),                  # central maintenance
    "46": ("Cockpit", "Side"),                  # information systems
    "49": ("Tail", "Side"),                     # auxiliary power unit
    "51": ("Fuselage", "Side"),                 # standard practices — structures
    "52": ("ForwardFuselage", "Side"),          # doors
    "53": ("Fuselage", "Side"),                 # fuselage
    "54": ("LeftEngine", "Side"),               # nacelles and pylons
    "55": ("HorizontalStabilizer", "Plan"),     # stabilizers
    "56": ("ForwardFuselage", "Side"),          # windshields and cabin windows
    "57": ("LeftWing", "Plan"),                 # wings
    "71": ("LeftEngine", "Side"),               # power plant
    "73": ("LeftEngine", "Side"),               # engine fuel and control
    "76": ("LeftEngine", "Side"),               # engine controls
    "78": ("LeftEngine", "Side"),               # exhaust and thrust reverser
    "79": ("LeftEngine", "Side"),               # oil
    "80": ("LeftEngine", "Side"),               # starting
    "92": ("Fuselage", "Side"),                 # wiring
}

_MISSING_AIRFRAME = sorted(set(ATA_2D) - set(AIRFRAME_SECTION_2D))
if _MISSING_AIRFRAME:
    raise RuntimeError(
        "AIRFRAME_SECTION_2D is missing an entry for ATA 2D chapter(s): "
        + ", ".join(_MISSING_AIRFRAME))
_EXTRA_AIRFRAME = sorted(set(AIRFRAME_SECTION_2D) - set(ATA_2D))
if _EXTRA_AIRFRAME:
    raise RuntimeError(
        "AIRFRAME_SECTION_2D has an entry for unknown ATA 2D chapter(s): "
        + ", ".join(_EXTRA_AIRFRAME))


def airframe_of(code):
    """``(airframe_section, view_hint)`` for any 2D/4D/6D code."""
    two = code.split("-")[0]
    try:
        return AIRFRAME_SECTION_2D[two]
    except KeyError:
        raise KeyError(f"no AIRFRAME_SECTION_2D entry for ATA 2D chapter {two!r}") from None


# Programs each 2D chapter applies to. A few chapters are program-restricted so
# that the reference data is not uniformly available everywhere.
CHAPTER_PROGRAM_OVERRIDES = {
    "42": "M|C|T|S",   # IMA — not on the earliest ZEPHYR classic standard
    "46": "M|C|T|Z",
    "44": "M|C|T|Z|S",
    "78": "M|T|Z|S",   # CIRRANE thrust reverser handled under vendor documentation
}
ALL_PROGRAMS = "M|C|T|Z|S"


def iter_rows():
    """Yield ``(code, level, parent, title, domain, programs, section, view)``.

    ``section`` / ``view`` are the airframe placement inherited from the row's
    2D chapter (see :data:`AIRFRAME_SECTION_2D`).
    """
    for two, (two_title, domain, fours) in sorted(ATA_2D.items()):
        programs = CHAPTER_PROGRAM_OVERRIDES.get(two, ALL_PROGRAMS)
        section, view = AIRFRAME_SECTION_2D[two]
        yield (two, 2, "", two_title, domain, programs, section, view)
        for four_suffix, (four_title, six_titles) in sorted(fours.items()):
            four = f"{two}-{four_suffix}"
            yield (four, 4, two, four_title, domain, programs, section, view)
            for idx, six_title in enumerate(six_titles):
                six = f"{four}-{SIX_D_SUFFIXES[idx]}"
                full = f"{four_title} — {six_title}" if six_title != "General" else f"{four_title} — General"
                yield (six, 6, four, full, domain, programs, section, view)


def six_d_codes(program=None):
    """All level-6 codes, optionally filtered to a program letter."""
    return [
        (code, title, domain)
        for code, level, _parent, title, domain, programs, _section, _view in iter_rows()
        if level == 6 and (program is None or program in programs.split("|"))
    ]


def domain_of(code):
    two = code.split("-")[0]
    entry = ATA_2D.get(two)
    return entry[1] if entry else "SYSTEMS"


if __name__ == "__main__":
    rows = list(iter_rows())
    levels = {}
    for _c, level, *_rest in rows:
        levels[level] = levels.get(level, 0) + 1
    print(f"{len(rows)} rows: " + ", ".join(f"level {k}: {v}" for k, v in sorted(levels.items())))
