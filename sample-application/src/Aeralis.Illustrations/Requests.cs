namespace Aeralis.Illustrations;

/// <summary>Which way round the page is.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// The manual a page belongs to. The name is printed under the header, in the same place the
/// real manuals put it.
/// </summary>
public enum DocumentKind
{
    AircraftMaintenanceManual,
    IllustratedPartsCatalog,
    StructuralRepairManual,
    TroubleShootingManual,
    WiringDiagramManual,
    FlightCrewOperatingManual,
    ServiceBulletin,
    ModificationDossier,
}

/// <summary>
/// Everything needed to draw one annotated view.
/// </summary>
public class IllustrationRequest
{
    /// <summary>Which aircraft's drawing to use.</summary>
    public required AircraftType Aircraft { get; init; }

    /// <summary>Which of the three views to draw.</summary>
    public required ViewKind View { get; init; }

    /// <summary>The callouts to draw. May be empty for a plain view.</summary>
    public IReadOnlyList<Annotation> Annotations { get; init; } = [];

    /// <summary>
    /// Adds engineering dimension lines for the aircraft's overall size on this view, measured
    /// from the source drawings rather than quoted from a datasheet.
    /// </summary>
    public bool ShowDimensions { get; init; }

    /// <summary>Fixes the random targets so a request can be reproduced. Null varies each call.</summary>
    public int? Seed { get; init; }

    /// <summary>Output width in pixels. The height follows from the drawing's proportions.</summary>
    public int Width { get; init; } = 1600;

    /// <summary>
    /// Which family to set text in. Null takes the default for this kind of request.
    /// </summary>
    public TextStyle? TextStyle { get; init; }

    /// <summary>The family actually used, once any default has been applied.</summary>
    internal TextStyle ResolvedTextStyle => TextStyle ?? DefaultTextStyle;

    /// <summary>The family to use when none was asked for.</summary>
    private protected virtual TextStyle DefaultTextStyle => Aeralis.Illustrations.TextStyle.Modern;
}

/// <summary>
/// An <see cref="IllustrationRequest"/> laid out as a manual page, with the header, figure
/// caption and footer blocks a real Aeralis manual page carries.
/// </summary>
public sealed class DocumentPageRequest : IllustrationRequest
{
    /// <summary>The manual this page belongs to.</summary>
    public required DocumentKind DocumentType { get; init; }

    /// <summary>The document reference, printed large in the footer — for example "06-10-00".</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The figure caption printed under the drawing.</summary>
    public required string Title { get; init; }

    /// <summary>Figure reference printed under the caption.</summary>
    public string FigureNumber { get; init; } = "Figure 001";

    /// <summary>Effectivity line printed in the footer box.</summary>
    public string Effectivity { get; init; } = "ALL";

    /// <summary>Operator or customer code printed under the effectivity.</summary>
    public string? CustomerCode { get; init; }

    /// <summary>Page number printed at the bottom right.</summary>
    public string PageNumber { get; init; } = "Page 1";

    /// <summary>Issue date printed under the page number.</summary>
    public string IssueDate { get; init; } = "May 01/01";

    /// <summary>
    /// The heading printed at the top, which on a real page lists the types the manual covers —
    /// for example "ZEPHYR-20/ZEPHYR-22/ZEPHYR-24". Defaults to the aircraft's own designation.
    /// </summary>
    public string? Heading { get; init; }

    /// <summary>Which way round the page is.</summary>
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>
    /// Which Aeralis mark to print. Defaults to the one in use when this aircraft entered
    /// service, so a page reads as being of its aircraft's era.
    /// </summary>
    public LogoEra? LogoEra { get; init; }

    /// <summary>Output resolution. A4 at 150 dpi is 1240 x 1754 pixels.</summary>
    public int Dpi { get; init; } = 150;

    /// <summary>
    /// Renders the page as a scan of a printed one rather than as clean artwork. Null turns it
    /// on for aircraft that entered service before 2000, whose manuals would have been printed
    /// and later scanned rather than published digitally.
    /// </summary>
    public bool? Scanned { get; init; }

    /// <summary>How heavily the scan shows, from 0 to 1.</summary>
    public float ScanAmount { get; init; } = 1f;

    /// <summary>
    /// Prints the company mark at the foot of the page as well as in the heading. Null turns
    /// it on for the heritage era, whose printed pages carried it at both ends; the current
    /// identity says nothing in the footer the heading has not already said.
    /// </summary>
    public bool? ShowFooterMark { get; init; }

    /// <summary>Whether the footer mark is printed, once the default has been applied.</summary>
    internal bool HasFooterMark =>
        ShowFooterMark ?? (LogoEra ?? Illustrations.Logo.EraFor(Aircraft)) == Illustrations.LogoEra.Heritage;

    /// <summary>Whether this page is rendered as a scan, once the default has been applied.</summary>
    internal bool IsScanned => Scanned ?? IsPrintEra;

    /// <summary>
    /// Pages for pre-2000 aircraft default to a typewriter face: manuals of that period were
    /// produced on monospaced output devices, and setting one in a modern sans gives away that
    /// it was never a printed page.
    /// </summary>
    private protected override TextStyle DefaultTextStyle =>
        IsPrintEra ? Aeralis.Illustrations.TextStyle.Typewriter : Aeralis.Illustrations.TextStyle.Modern;

    private bool IsPrintEra => Logo.EntryIntoService(Aircraft) < 2000;
}
