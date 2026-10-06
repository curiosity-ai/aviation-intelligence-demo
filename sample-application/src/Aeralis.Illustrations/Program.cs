using Aeralis.Illustrations;

if (args is ["diag", ..])
{
    Diagnostics.Run(args.Skip(1).ToArray());
    return;
}

var outDir = args.Length > 0 ? args[0] : "illustrations";

// Plain annotated views, one callout each.
(AircraftType Aircraft, ViewKind View, AircraftSection Section, string Text, double? Position)[] singles =
[
    (AircraftType.Zephyr22, ViewKind.Side, AircraftSection.LeftWing, "MOD 27-4411\nSlat track can inspection", 0.25),
    (AircraftType.Meridian46, ViewKind.Plan, AircraftSection.LeftWing, "Wing tip\nwinglet bolt", 0.9),
    (AircraftType.Titan88, ViewKind.Front, AircraftSection.LeftEngine, "Engine 3 pylon", 0.4),
    (AircraftType.Solano34, ViewKind.Side, AircraftSection.Fuselage, "Section 19 frame", 0.85),
    // Plan is the only view the CIRRANE sheets carry.
    (AircraftType.Cirrane13, ViewKind.Plan, AircraftSection.LeftWing, "Outboard flap\ntrack fairing", 0.55),
];

foreach (var (aircraft, view, section, text, position) in singles)
{
    AircraftIllustrator.Save(
        new IllustrationRequest
        {
            Aircraft = aircraft,
            View = view,
            Annotations = [new Annotation(section, text, position)],
            Seed = 42,
        },
        Path.Combine(outDir, $"view-{aircraft}-{view}-{section}.png".ToLowerInvariant()));
}

// Several callouts at once, with dimension lines.
AircraftIllustrator.Save(
    new IllustrationRequest
    {
        Aircraft = AircraftType.Zephyr22,
        View = ViewKind.Side,
        ShowDimensions = true,
        Seed = 3,
        Annotations =
        [
            new Annotation(AircraftSection.Nose, "Radome", 0.4),
            new Annotation(AircraftSection.LeftWing, "Slat track can", 0.35),
            new Annotation(AircraftSection.VerticalStabilizer, "VTP root fitting", 0.25),
            new Annotation(AircraftSection.MainLandingGear, "MLG bogie", 0.5),
        ],
    },
    Path.Combine(outDir, "view-multi-dimensions.png"));

// A4 manual pages, portrait and landscape.
AircraftIllustrator.SaveDocumentPage(
    new DocumentPageRequest
    {
        Aircraft = AircraftType.Zephyr22,
        View = ViewKind.Side,
        DocumentType = DocumentKind.AircraftMaintenanceManual,
        DocumentNumber = "06-10-00",
        Title = "Dimensions and Areas",
        FigureNumber = "Figure 001",
        Heading = "ZEPHYR-18/ZEPHYR-20/ZEPHYR-22/ZEPHYR-24",
        Effectivity = "201-225, 551-599",
        CustomerCode = "SROS",
        PageNumber = "Page 2",
        IssueDate = "May 01/01",
        ShowDimensions = true,
        Seed = 11,
        Annotations =
        [
            new Annotation(AircraftSection.LeftWing, "Slat track can", 0.3),
            new Annotation(AircraftSection.Nose, "Radome latch", 0.5),
        ],
    },
    Path.Combine(outDir, "page-portrait.png"));

AircraftIllustrator.SaveDocumentPage(
    new DocumentPageRequest
    {
        Aircraft = AircraftType.Meridian46,
        View = ViewKind.Plan,
        Orientation = PageOrientation.Landscape,
        DocumentType = DocumentKind.StructuralRepairManual,
        DocumentNumber = "53-21-11",
        Title = "Fwd Cargo Door Surround Structure",
        FigureNumber = "Figure 014",
        Effectivity = "ALL",
        CustomerCode = "MSN 0031-0104",
        PageNumber = "Page 7",
        IssueDate = "Aug 01/26",
        ShowDimensions = true,
        Seed = 5,
        Annotations =
        [
            new Annotation(AircraftSection.LeftWing, "Wing tip device", 0.92),
            new Annotation(AircraftSection.Fuselage, "Fwd cargo door surround", 0.3),
            new Annotation(AircraftSection.HorizontalStabilizer, "THS actuator", 0.4),
        ],
    },
    Path.Combine(outDir, "page-landscape.png"));

// A plan-view-only type. ShowDimensions is set and draws nothing, because a plan view cannot
// say how tall the aircraft is and a partial set of dimensions is worse than none.
AircraftIllustrator.SaveDocumentPage(
    new DocumentPageRequest
    {
        Aircraft = AircraftType.Cirrane11,
        View = ViewKind.Plan,
        DocumentType = DocumentKind.IllustratedPartsCatalog,
        DocumentNumber = "57-40-11",
        Title = "Wing Leading Edge Slat Assembly",
        FigureNumber = "Figure 002",
        Effectivity = "ALL",
        PageNumber = "Page 3",
        IssueDate = "Aug 01/26",
        ShowDimensions = true,
        Seed = 17,
        Annotations =
        [
            new Annotation(AircraftSection.Nose, "Radome", 0.5),
            new Annotation(AircraftSection.RightEngine, "PYR-1500G nacelle", 0.5),
        ],
    },
    Path.Combine(outDir, "page-a220-plan.png"));

// One page per logo era, to check each mark is picked from the aircraft's own vintage.
(AircraftType Aircraft, string Number, string Title)[] eras =
[
    (AircraftType.Auster20, "53-10-00", "Fuselage Station Diagram"),
    (AircraftType.Titan88, "52-10-00", "Passenger Door Locations"),
    (AircraftType.Meridian42, "57-20-00", "Wing Box Structure"),
    (AircraftType.Solano39, "27-51-00", "Flap Surfaces"),
];

foreach (var (aircraft, number, title) in eras)
{
    AircraftIllustrator.SaveDocumentPage(
        new DocumentPageRequest
        {
            Aircraft = aircraft,
            View = ViewKind.Side,
            DocumentType = DocumentKind.AircraftMaintenanceManual,
            DocumentNumber = number,
            Title = title,
            ShowDimensions = true,
            Seed = 9,
            Annotations = [new Annotation(AircraftSection.Fuselage, "Datum", 0.45)],
        },
        Path.Combine(outDir, $"era-{aircraft}.png".ToLowerInvariant()));
}

// The same page with and without the scan, to show what the effect does.
foreach (var scanned in new[] { true, false })
{
    AircraftIllustrator.SaveDocumentPage(
        new DocumentPageRequest
        {
            Aircraft = AircraftType.Zephyr22,
            View = ViewKind.Side,
            DocumentType = DocumentKind.AircraftMaintenanceManual,
            DocumentNumber = "57-40-00",
            Title = "Slat Track Can Inspection",
            Effectivity = "001-119",
            CustomerCode = "AFRA",
            ShowDimensions = true,
            Scanned = scanned,
            Seed = 4,
            Annotations =
            [
                new Annotation(AircraftSection.LeftWing, "Slat track can", 0.3),
                new Annotation(AircraftSection.NoseLandingGear, "NLG bay", 0.5),
            ],
        },
        Path.Combine(outDir, scanned ? "scan-on.png" : "scan-off.png"));
}

Console.WriteLine($"written to {outDir}");
