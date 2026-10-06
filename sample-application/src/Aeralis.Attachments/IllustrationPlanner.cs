using System.Globalization;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using Aeralis.Dataset.Reference;
using Aeralis.Illustrations;

namespace Aeralis.Attachments;

/// <summary>One illustration to draw for a document.</summary>
/// <param name="Icn">The information control number the data module references it by.</param>
/// <param name="Caption">The figure title printed under it.</param>
/// <param name="View">Which of the three views to draw.</param>
/// <param name="Annotations">The callouts, already aimed.</param>
/// <param name="ShowDimensions">Whether to add the overall dimension lines.</param>
public sealed record FigurePlan(
    string Icn,
    string Caption,
    ViewKind View,
    IReadOnlyList<Annotation> Annotations,
    bool ShowDimensions);

/// <summary>What a document's attachments look like, before anything is drawn.</summary>
/// <param name="Aircraft">The type to draw, or null if none is published for it.</param>
/// <param name="Figures">The figures to place in the rendered publication.</param>
/// <param name="Scanned">Whether the finished pages are rendered as a scan of a printed page.</param>
public sealed record IllustrationPlan(AircraftType? Aircraft, IReadOnlyList<FigurePlan> Figures, bool Scanned);

/// <summary>
/// Decides what to draw for a document: which aircraft, which views, and what each callout
/// points at and says.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a function of the document, so a document plans the same way on every
/// run. Where a choice is genuinely free — which of five zones to call out when only three
/// fit — it is drawn from a generator seeded on the document's own id.
/// </para>
/// <para>
/// Callouts are aimed from the document's own <c>entities.zones</c>, because a zone is the one
/// field that says *where on the aircraft* the document is about. A FIN installed in that zone
/// makes the better label — "9469HM, sensor, cargo temperature" says more than the zone name —
/// so the label is taken from the FIN when the document lists one that lives there.
/// </para>
/// </remarks>
public sealed class IllustrationPlanner
{
    /// <summary>Callouts on one drawing. More than three and the labels start to crowd.</summary>
    private const int MaxAnnotationsPerFigure = 3;

    /// <summary>Figures in one publication. Two views is what a location figure pair looks like.</summary>
    private const int MaxFigures = 2;

    private readonly AirframeCatalog _airframe;
    private readonly Dictionary<string, FinRow> _fins;
    private readonly Dictionary<string, PartNumberRow> _parts;

    /// <summary>Aircraft in the registry per type — which type a family document is drawn as.</summary>
    private readonly Dictionary<string, int> _fleet;

    public IllustrationPlanner(ReferenceData reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _airframe = new AirframeCatalog(reference);
        _fleet = reference.Msns
            .GroupBy(m => m.AircraftType, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        _fins = reference.Fins
            .GroupBy(f => f.Fin, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _parts = reference.PartNumbers
            .GroupBy(p => p.PartNumber, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

/// <summary>Plans the illustrations for one issue of a document.</summary>
    /// <param name="isLatest">
    /// Whether this is the issue the document is currently at. Only a superseded issue is
    /// rendered as a scan — see <see cref="IsScanned"/>.
    /// </param>
    public IllustrationPlan Plan(TechDocument document, bool isLatest)
    {
        ArgumentNullException.ThrowIfNull(document);

        var aircraft = AircraftMapping.Primary(document.Applicability?.AircraftTypes ?? [], _fleet);
        if (aircraft is null)
        {
            return new IllustrationPlan(null, [], false);
        }

        var placements = OnAvailableViews(Placements(document), aircraft.Value);
        var figures = new List<FigurePlan>();

        // One figure per view, in the order the views were first met, so the figure a reader
        // sees first is the one the document's first zone is on.
        foreach (var group in placements.GroupBy(p => p.View).Take(MaxFigures))
        {
            var chosen = Choose(group.ToList(), document.DocId, figures.Count);
            var annotations = chosen
                .Select(p => new Annotation(p.Section, p.Label, p.Position))
                .ToList();

            figures.Add(new FigurePlan(
                Icn: Icn(document, figures.Count + 1),
                Caption: Caption(document, group.Key),
                View: group.Key,
                Annotations: annotations,
                // Dimension lines belong on the first figure only: they describe the aircraft,
                // not the subject, and repeating them on a second view is noise.
                ShowDimensions: figures.Count == 0 && group.Key != ViewKind.Front));
        }

        return new IllustrationPlan(aircraft, figures, IsScanned(aircraft.Value, isLatest));
    }

    /// <summary>
    /// Whether a page is rendered as a scan of a printed one.
    ///
    /// Two things have to be true. The aircraft has to be of the printed era — a manual for a
    /// type that entered service before 2000 was produced on paper — and the issue has to be a
    /// **superseded** one. The issue a document is currently at is the live one: whatever it
    /// looked like when it was first printed, what you open today is the digital publication,
    /// and rendering it as a scan says the current manual is a photocopy. What sits in the
    /// archive as paper is the issue that was replaced.
    ///
    /// This is also what makes the two halves of an issue history read differently — the
    /// superseded issue as a scan of a page from around the aircraft's entry into service, the
    /// current one as clean digital artwork.
    /// </summary>
    private static bool IsScanned(AircraftType aircraft, bool isLatest) =>
        !isLatest && AeralisMark.IsPrintEra(aircraft);

    /// <summary>
    /// The full-page manual illustration a figure document gets in place of a publication —
    /// the drawing laid out as the page it would be printed on.
    /// </summary>
    public DocumentPageRequest? PlanFigurePage(TechDocument document, IpcFigureData? figure,
        bool isLatest)
    {
        var aircraft = AircraftMapping.Primary(document.Applicability?.AircraftTypes ?? [], _fleet);
        if (aircraft is null) return null;

        var placements = OnAvailableViews(Placements(document), aircraft.Value);
        var views = AircraftIllustrator.AvailableViews(aircraft.Value);
        var view = placements.Count > 0 ? placements[0].View : views[0];
        var chosen = Choose(placements.Where(p => p.View == view).ToList(), document.DocId, 0);

        var types = document.Applicability?.AircraftTypes ?? [];

        return new DocumentPageRequest
        {
            Aircraft = aircraft.Value,
            View = view,
            DocumentType = DocumentKind.IllustratedPartsCatalog,
            DocumentNumber = document.Ata?.Ata6d ?? "00-00-00",
            Title = figure?.FigureTitle ?? document.Title.ToUpperInvariant(),
            FigureNumber = FigureNumber(figure),
            Heading = AircraftMapping.Heading(types, aircraft.Value),
            Effectivity = Effectivity(document),
            PageNumber = "Page 1",
            IssueDate = IssueDate(document),
            Orientation = PageOrientation.Portrait,
            // Explicit rather than left to the request's own default, which keys only on the
            // aircraft's era and would scan the current issue too.
            Scanned = IsScanned(aircraft.Value, isLatest),
            ShowDimensions = false,
            Annotations = chosen.Select(p => new Annotation(p.Section, p.Label, p.Position)).ToList(),
            Seed = Seeded.Seed(document.DocId, $"figure-page-{Csdb.CsdbProjection.IssueOf(document)}"),
        };
    }

    // ------------------------------------------------------------------ internals

    /// <summary>
    /// Moves any placement onto a view the aircraft actually has a drawing of.
    /// </summary>
    /// <remarks>
    /// Aeralis publishes the CIRRANE as a plan view alone. A zone's <c>view_hint</c> says which view
    /// shows the feature best across the fleet, and for most zones that is the side — so for
    /// those two types the hint is re-pointed at the plan rather than dropped. The section and
    /// the position along it still hold: the outlines cover every section on every view.
    /// </remarks>
    private static List<Placement> OnAvailableViews(List<Placement> placements, AircraftType aircraft)
    {
        var views = AircraftIllustrator.AvailableViews(aircraft);
        if (views.Count == 3) return placements;

        // Two placements that were distinct only because they were on different views land on
        // top of each other once both are moved to the same one, so the same test that kept
        // them apart in the first place is applied again.
        var seen = new HashSet<(AircraftSection, ViewKind)>();

        return placements
            .Select(p => views.Contains(p.View) ? p : p with { View = views[0] })
            .Where(p => seen.Add((p.Section, p.View)))
            .ToList();
    }

    /// <summary>
    /// Every place on the airframe the document points at, in the order its zones are listed,
    /// one per section-and-view so two zones in the same place do not produce two callouts on
    /// top of each other. Falls back to the ATA chapter when the document lists no zone.
    /// </summary>
    private List<Placement> Placements(TechDocument document)
    {
        var zones = document.Entities?.Zones ?? [];
        var seen = new HashSet<(AircraftSection, ViewKind)>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var placements = new List<Placement>();

        foreach (var zone in zones)
        {
            if (_airframe.ForZone(zone) is not { } placement) continue;
            if (!seen.Add((placement.Section, placement.View))) continue;

            // Two zones can produce the same label — the same FIN or part is the best thing to
            // say about both — and the same words twice on one drawing say nothing the second
            // time. The zone's own name is always distinct, so it is what a repeat falls to.
            var label = Label(document, zone, placement, labels);
            if (!labels.Add(label))
            {
                label = placement.Label;
                if (!labels.Add(label)) continue;
            }

            placements.Add(placement with { Label = label });
        }

        if (placements.Count == 0 &&
            _airframe.ForAta(document.Ata?.Ata6d, document.Ata?.Ata4d, document.Ata?.Ata2d) is { } fromAta)
        {
            placements.Add(fromAta);
        }

        return placements;
    }

    /// <summary>
    /// What a callout says. A FIN the document lists that is installed in this zone names a
    /// piece of equipment and is the most useful thing to point at; otherwise a part the
    /// document lists in the same ATA chapter; otherwise the zone itself.
    /// </summary>
    private string Label(TechDocument document, string zone, Placement placement,
        IReadOnlySet<string> alreadyUsed)
    {
        foreach (var fin in document.Entities?.Fins ?? [])
        {
            if (!_fins.TryGetValue(fin, out var row) || row.Zone != zone) continue;

            var label = $"{fin}\n{Shorten(row.Description)}";
            if (!alreadyUsed.Contains(label)) return label;
        }

        foreach (var part in document.Entities?.PartNumbers ?? [])
        {
            if (!_parts.TryGetValue(part, out var row) || row.Ata6d != document.Ata?.Ata6d) continue;

            var label = $"{Shorten(row.Description)}\n{part}";
            if (!alreadyUsed.Contains(label)) return label;
        }

        return placement.Label;
    }

    /// <summary>
    /// Keeps the callouts that fit. Which ones, when more are available than fit, is drawn
    /// from the document's id so the same document always shows the same set.
    /// </summary>
    private static List<Placement> Choose(List<Placement> available, string docId, int figureIndex)
    {
        if (available.Count <= MaxAnnotationsPerFigure) return available;

        // The first is always kept — it is the document's own first zone. The rest are drawn
        // without replacement from what is left.
        var chosen = new List<Placement> { available[0] };
        var pool = available.Skip(1).ToList();
        var random = Seeded.Random(docId, $"annotations-{figureIndex}");

        while (chosen.Count < MaxAnnotationsPerFigure && pool.Count > 0)
        {
            var index = random.Next(pool.Count);
            chosen.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return chosen;
    }

    /// <summary>A nomenclature short enough to sit beside the aircraft.</summary>
    private static string Shorten(string? description)
    {
        var text = (description ?? "").Trim();
        if (text.Length == 0) return "";

        // Nomenclature is written "BOLT, SHEAR HEAD, STEEL" — the first two terms identify it
        // and the rest qualifies it. Set in title case, because a callout in full capitals
        // beside a line drawing reads as shouting.
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kept = string.Join(", ", parts.Take(2));
        return kept.Length <= 34 ? TitleCase(kept) : TitleCase(parts[0]);
    }

    private static string TitleCase(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();

    private static string Caption(TechDocument document, ViewKind view) =>
        $"{document.Ata?.Title ?? document.Title} — {ViewName(view)}";

    private static string ViewName(ViewKind view) => view switch
    {
        ViewKind.Plan => "location, plan view",
        ViewKind.Front => "location, front view",
        _ => "location, side view",
    };

    /// <summary>
    /// The information control number the data module references the graphic by. S1000D's ICN
    /// form is ICN-(model)-(system)-(variant)-(originator)-(sequence)-(variant)-(issue)-(security).
    /// </summary>
    /// <remarks>
    /// The sequence is a stable hash of the document <em>and its issue</em>, followed by the
    /// figure's index inside that issue — not the index alone. An information control number
    /// identifies a graphic across the whole CSDB, and the index alone is not unique twice
    /// over: the model and system fields come from the ATA chapter, which several documents
    /// share (a modification and its pre-analysis work order always do), and two issues of one
    /// document are two separate publications with their own artwork. Either collision would
    /// have the second file written silently replace the first.
    /// </remarks>
    private static string Icn(TechDocument document, int sequence)
    {
        var model = DataModuleCodeModel(document);
        var system = (document.Ata?.Ata6d ?? "000000").Replace("-", "");
        var issue = Csdb.CsdbProjection.IssueOf(document);
        var origin = Seeded.Seed(document.DocId, $"issue-{issue}")
            .ToString("X8", CultureInfo.InvariantCulture);
        return $"ICN-{model}-A-{system}-A-U8025-{origin}{sequence:00}-A-001-01";
    }

    private static string DataModuleCodeModel(TechDocument document) =>
        Csdb.DataModuleCode.ModelIdentFor(document.Program);

    private static string FigureNumber(IpcFigureData? figure)
    {
        var reference = figure?.Figure ?? "";
        var last = reference.Split('-').LastOrDefault();
        return last is { Length: > 0 } && last.All(char.IsAsciiDigit)
            ? $"Figure {int.Parse(last):000}"
            : "Figure 001";
    }

    /// <summary>The effectivity line a manual page carries in its footer box.</summary>
    private static string Effectivity(TechDocument document)
    {
        var applicability = document.Applicability;
        if (applicability is null) return "ALL";

        if (applicability.Msns.Count > 0)
        {
            return string.Join(", ", applicability.Msns.Take(4))
                   + (applicability.Msns.Count > 4 ? ", …" : "");
        }
        if (applicability.MsnRanges.Count > 0)
        {
            return string.Join(", ", applicability.MsnRanges.Take(3).Select(r => $"{r.From}-{r.To}"));
        }
        return applicability.Scope == ApplicabilityScope.ALL_PROGRAM || applicability.AircraftTypes.Count == 0
            ? "ALL"
            : string.Join(", ", applicability.AircraftTypes);
    }

    /// <summary>The date a manual page prints, in the "May 01/01" form real pages use.</summary>
    private static string IssueDate(TechDocument document)
    {
        var dates = document.Dates;
        var text = dates?.Revised ?? dates?.Released ?? dates?.Created;
        return DateOnly.TryParse(text, out var date)
            ? date.ToString("MMM dd/yy", CultureInfo.InvariantCulture)
            : "Jan 01/20";
    }
}
