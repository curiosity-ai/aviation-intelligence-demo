using System.IO.Compression;
using System.Reflection;
using SkiaSharp;
using Svg.Skia;

namespace Aeralis.Illustrations;

/// <summary>A callout to draw on a view: some text pointing at a part of the aircraft.</summary>
/// <param name="Section">Which part of the airframe to point at.</param>
/// <param name="Text">The callout text. Newlines start a new line of the label.</param>
/// <param name="Position">
/// Where along the section to aim, from 0 to 1. 0 is the forward or inboard end — the nose for
/// the fuselage, the root for a wing — and 1 the aft or outboard end. Null picks anywhere in
/// the section.
/// </param>
public sealed record Annotation(AircraftSection Section, string Text, double? Position = null);

/// <summary>
/// Draws Aeralis three-view drawings with technical callouts pointing at named parts of the
/// airframe, either on their own or laid out as a manual page.
/// </summary>
/// <remarks>
/// The drawings come from <c>tools/gen_aircraft_views.py</c> as SVG, embedded here gzipped. Section outlines
/// live in <see cref="SectionMap"/> and are deliberately rough; each target is drawn from the
/// intersection of the outline with the silhouette measured from the drawing itself, so
/// callouts land on the aircraft rather than in white space next to it.
/// </remarks>
public static class AircraftIllustrator
{
    /// <summary>
    /// Clear band kept around the drawing for labels, as a fraction of the requested width. It
    /// is applied as the same number of pixels on all four sides, so a side view — which is
    /// three times as wide as it is tall — still gets a usable band above and below rather than
    /// a sliver.
    /// </summary>
    private const float Margin = 0.11f;

    /// <summary>Wider band, for when dimension lines need room outside the drawing.</summary>
    private const float DimensionMargin = 0.17f;

    private static readonly Dictionary<AircraftType, string> ResourceKeys = new()
    {
        [AircraftType.Cirrane11] = "CIRRANE-11",
        [AircraftType.Cirrane13] = "CIRRANE-13",
        [AircraftType.Auster20] = "AUSTER-20",
        [AircraftType.Auster24] = "AUSTER-24",
        [AircraftType.Zephyr18] = "ZEPHYR-18",
        [AircraftType.Zephyr20] = "ZEPHYR-20",
        [AircraftType.Zephyr20Winglet] = "ZEPHYR-20-winglet",
        [AircraftType.Zephyr22] = "ZEPHYR-22",
        [AircraftType.Zephyr22Winglet] = "ZEPHYR-22-winglet",
        [AircraftType.Zephyr24] = "ZEPHYR-24",
        [AircraftType.Zephyr24Winglet] = "ZEPHYR-24-winglet",
        [AircraftType.Solano30E] = "SOLANO-30E",
        [AircraftType.Solano30] = "SOLANO-30",
        [AircraftType.Solano30F] = "SOLANO-30F",
        [AircraftType.Solano34] = "SOLANO-34",
        [AircraftType.Solano38] = "SOLANO-38",
        [AircraftType.Solano39] = "SOLANO-39",
        [AircraftType.Vantor62] = "VANTOR-62",
        [AircraftType.Vantor64] = "VANTOR-64",
        [AircraftType.Vantor66] = "VANTOR-66",
        [AircraftType.Vantor68] = "VANTOR-68",
        [AircraftType.Meridian42] = "MERIDIAN-42",
        [AircraftType.Meridian46] = "MERIDIAN-46",
        [AircraftType.Titan88] = "TITAN-88",
    };

    private static readonly Dictionary<string, Drawing> Cache = [];

    private static readonly ViewKind[] ThreeViews = [ViewKind.Plan, ViewKind.Side, ViewKind.Front];

    private static readonly ViewKind[] PlanOnly = [ViewKind.Plan];

    /// <summary>The views a published sheet exists for. Every type has all three except the CIRRANEs.</summary>
    /// <remarks>
    /// Aeralis publishes the CIRRANE-11 and CIRRANE-13 general arrangements as a plan view alone,
    /// where every other type gets a three-view sheet. There is no side or front drawing of
    /// either to fall back on, so a caller that offers a choice of view should offer only what
    /// this returns; asking <see cref="Draw(IllustrationRequest)"/> for a view a type does not
    /// have throws.
    /// </remarks>
    public static IReadOnlyList<ViewKind> AvailableViews(AircraftType aircraft) =>
        aircraft is AircraftType.Cirrane11 or AircraftType.Cirrane13 ? PlanOnly : ThreeViews;

    /// <summary>Draws an annotated view.</summary>
    public static SKBitmap Draw(IllustrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Width < 200) throw new ArgumentOutOfRangeException(nameof(request), "width must be at least 200px");

        var drawing = Load(request.Aircraft, request.View);

        var band = request.Width * (request.ShowDimensions ? DimensionMargin : Margin);
        var drawW = request.Width - 2 * band;
        var scale = drawW / drawing.Width;
        var drawH = drawing.Height * scale;
        var height = (int)Math.Round(drawH + 2 * band);

        var bitmap = new SKBitmap(request.Width, Math.Max(height, 1));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        Compose(canvas, request, drawing, new SKRect(band, band, band + drawW, band + drawH),
            new SKRect(0, 0, request.Width, height));

        return bitmap;
    }

    /// <summary>Draws an annotated view laid out as a manual page.</summary>
    public static SKBitmap DrawDocumentPage(DocumentPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DocumentPage.Draw(request);
    }

    /// <summary>Draws an annotated view and writes it out as a PNG.</summary>
    public static void Save(IllustrationRequest request, string path) => Write(Draw(request), path);

    /// <summary>Draws a manual page and writes it out as a PNG.</summary>
    public static void SaveDocumentPage(DocumentPageRequest request, string path) => Write(DrawDocumentPage(request), path);

    /// <summary>Convenience overload for the common case of one callout on one view.</summary>
    /// <param name="position">
    /// Where along the section to aim, 0 to 1 — 0 near the nose for the fuselage, or the wing
    /// root for a wing. Null picks anywhere in the section.
    /// </param>
    public static SKBitmap Draw(
        AircraftType aircraft,
        ViewKind view,
        AircraftSection section,
        string text,
        double? position = null,
        int? seed = null,
        int width = 1600) =>
        Draw(new IllustrationRequest
        {
            Aircraft = aircraft,
            View = view,
            Annotations = [new Annotation(section, text, position)],
            Seed = seed,
            Width = width,
        });

    /// <summary>
    /// Draws the view, its dimension lines and its callouts into <paramref name="target"/>.
    /// Shared by the standalone image and by the drawing panel of a manual page.
    /// </summary>
    /// <param name="target">Where the drawing itself goes.</param>
    /// <param name="bounds">The area labels may occupy, which is usually larger.</param>
    internal static void Compose(SKCanvas canvas, IllustrationRequest request, Drawing drawing, SKRect target, SKRect bounds)
    {
        var scale = target.Width / drawing.Width;

        // Everything a label must keep off: the aircraft itself and any dimension lines. Drawn
        // once to the page and again into the placer's mask, from the same closure, so the two
        // cannot drift apart.
        void DrawObstacles(SKCanvas into)
        {
            into.Save();
            into.Translate(target.Left, target.Top);
            into.Scale(scale);
            into.DrawPicture(drawing.Picture);
            into.Restore();

            if (request.ShowDimensions && DimensionsFor(request.Aircraft) is { } dimensions)
                DimensionDrawer.Draw(into, target, dimensions, request.View, Math.Min(bounds.Width, bounds.Height), request.ResolvedTextStyle);
        }

        DrawObstacles(canvas);

        if (request.Annotations.Count == 0) return;

        var placer = LabelPlacer.Build(bounds, DrawObstacles);
        var random = request.Seed is null ? new Random() : new Random(request.Seed.Value);

        foreach (var annotation in request.Annotations)
        {
            var point = Sampler.Pick(SectionMap.Get(request.View, annotation.Section), drawing.Silhouette, annotation.Position, random);
            var at = new SKPoint(target.Left + (float)point.U * target.Width, target.Top + (float)point.V * target.Height);
            Callout.Draw(canvas, at, annotation.Text, bounds, placer, request.ResolvedTextStyle);
        }
    }

    internal static Drawing Load(AircraftType aircraft, ViewKind view)
    {
        if (!ResourceKeys.TryGetValue(aircraft, out var key))
            throw new ArgumentOutOfRangeException(nameof(aircraft), $"no drawing embedded for {aircraft}");

        // Say which views the type does have rather than let it fail later as a missing
        // resource, which reads like a packaging fault instead of an answerable question.
        if (!AvailableViews(aircraft).Contains(view))
            throw new ArgumentOutOfRangeException(nameof(view),
                $"Aeralis publishes no {view.ToString().ToLowerInvariant()} view of the {Designation(aircraft)}; " +
                $"its sheet carries {string.Join(", ", AvailableViews(aircraft))} only");

        var name = $"{key}-{view.ToString().ToLowerInvariant()}";

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            var resource = $"Aeralis.Illustrations.Resources.{name}.svgz";
            using var compressed = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"embedded drawing '{resource}' is missing");
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gzip.CopyTo(buffer);
            buffer.Position = 0;

            var svg = new SKSvg();
            var picture = svg.Load(buffer) ?? throw new InvalidOperationException($"could not parse '{resource}'");

            var rect = picture.CullRect;
            var drawing = new Drawing(picture, rect.Width, rect.Height, Silhouette.Measure(picture, rect));
            Cache[name] = drawing;
            return drawing;
        }
    }

    internal static Dimensions? DimensionsFor(AircraftType aircraft) =>
        Dimensions.For(aircraft, ResourceKeys[aircraft]);

    /// <summary>The type designation as it is printed, for example "SOLANO-30F".</summary>
    public static string Designation(AircraftType aircraft) =>
        ResourceKeys[aircraft].Replace("-winglet", "").ToUpperInvariant();

    /// <summary>Diagnostics for checking that a drawing's silhouette was measured sensibly.</summary>
    internal static (double Coverage, float Width, float Height) Inspect(AircraftType aircraft, ViewKind view)
    {
        var drawing = Load(aircraft, view);
        return (drawing.Silhouette.Coverage, drawing.Width, drawing.Height);
    }

    /// <summary>Where a request would put its target, in view fractions.</summary>
    internal static Frac Target(AircraftType aircraft, ViewKind view, AircraftSection section, double? position, int seed)
    {
        var drawing = Load(aircraft, view);
        return Sampler.Pick(SectionMap.Get(view, section), drawing.Silhouette, position, new Random(seed));
    }

    internal sealed record Drawing(SKPicture Picture, float Width, float Height, Silhouette Silhouette);

    private static void Write(SKBitmap bitmap, string path)
    {
        using (bitmap)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 95);
            using var stream = File.Create(path);
            data.SaveTo(stream);
        }
    }
}
