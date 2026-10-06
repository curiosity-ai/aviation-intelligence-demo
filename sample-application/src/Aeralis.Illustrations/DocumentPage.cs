using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// Lays an annotated view out as a manual page: the type heading and manual name at the top,
/// the figure and its caption in the middle, and the effectivity, document number, page number
/// and date along the bottom.
/// </summary>
/// <remarks>
/// The arrangement follows a real Aeralis manual page — heading centred at the top, the figure
/// caption and figure number centred under the drawing, a revision bar in the left margin, the
/// document reference set large at the bottom right, and the print line centred at the very
/// bottom.
/// </remarks>
internal static class DocumentPage
{
    /// <summary>A4 in millimetres.</summary>
    private const double A4Short = 210.0;
    private const double A4Long = 297.0;

    /// <summary>Page margin in millimetres.</summary>
    private const float MarginMm = 14f;

    public static SKBitmap Draw(DocumentPageRequest request)
    {
        if (request.Dpi < 36) throw new ArgumentOutOfRangeException(nameof(request), "dpi must be at least 36");

        var portrait = request.Orientation == PageOrientation.Portrait;
        var widthMm = portrait ? A4Short : A4Long;
        var heightMm = portrait ? A4Long : A4Short;

        var pxPerMm = request.Dpi / 25.4f;
        var width = (int)Math.Round(widthMm * pxPerMm);
        var height = (int)Math.Round(heightMm * pxPerMm);

        var bitmap = new SKBitmap(width, height);
        var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var margin = MarginMm * pxPerMm;
        var page = new SKRect(margin, margin, width - margin, height - margin);

        using var ink = new SKPaint { Color = new SKColor(0x11, 0x11, 0x11), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var rule = new SKPaint { Color = new SKColor(0x11, 0x11, 0x11), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, pxPerMm * 0.25f) };

        var unit = pxPerMm;
        var headerBottom = DrawHeader(canvas, request, page, unit, ink);
        var footerTop = DrawFooter(canvas, request, page, unit, ink, rule);
        var captionTop = DrawCaption(canvas, request, page, footerTop, unit, ink);
        DrawRevisionBar(canvas, request, page, captionTop, unit, ink);

        DrawFigure(canvas, request, new SKRect(page.Left + unit * 6, headerBottom + unit * 4, page.Right - unit * 2, captionTop - unit * 3));

        canvas.Dispose();

        // Applied last, over the finished page: a scan is a photograph of the printed result,
        // not something any one element on the page knows about.
        return request.IsScanned
            ? ScannedEffect.Apply(bitmap, request.Seed ?? 0, request.ScanAmount)
            : bitmap;
    }

    /// <summary>The type designation and manual name, centred at the top.</summary>
    private static float DrawHeader(SKCanvas canvas, DocumentPageRequest request, SKRect page, float unit, SKPaint ink)
    {
        var heading = request.Heading ?? AircraftIllustrator.Designation(request.Aircraft);

        using var typeFont = new SKFont(Fonts.Heading, unit * 7.5f);
        using var manualFont = new SKFont(Fonts.Bold(request.ResolvedTextStyle), unit * 3.6f);

        var y = page.Top + unit * 6;

        // The mark matching the aircraft's own era.
        var era = request.LogoEra ?? Logo.EraFor(request.Aircraft);

        // Measured off the designation itself rather than off the font size, which includes
        // room for descenders the designation has none of.
        var textWidth = typeFont.MeasureText(heading, out var designation);

        // The mark is a shape, not a word, so it is given a little more than the designation's
        // cap height — set to the lettering's own height it looks shrunken beside it.
        var logoHeight = typeFont.Size * 0.86f;
        var gap = typeFont.Size * 0.24f;
        var logoWidth = Logo.Width(era, logoHeight);
        var left = page.MidX - (logoWidth + gap + textWidth) / 2;

        // Centred on the designation's ink, so both sit level with the lettering.
        if (logoWidth > 0) Logo.Draw(canvas, era, left, y + designation.MidY, logoHeight);
        canvas.DrawText(heading, left + logoWidth + gap, y, SKTextAlign.Left, typeFont, ink);

        y += unit * 4.6f;
        canvas.DrawText(ManualName(request.DocumentType), page.MidX, y, SKTextAlign.Center, manualFont, ink);

        return y;
    }

    /// <summary>The figure caption and figure number, centred above the footer.</summary>
    private static float DrawCaption(SKCanvas canvas, DocumentPageRequest request, SKRect page, float footerTop, float unit, SKPaint ink)
    {
        using var font = new SKFont(Fonts.Regular(request.ResolvedTextStyle), unit * 3.4f);

        var figureY = footerTop - unit * 6;
        var titleY = figureY - unit * 4.6f;

        canvas.DrawText(request.Title, page.MidX, titleY, SKTextAlign.Center, font, ink);
        canvas.DrawText(request.FigureNumber, page.MidX, figureY, SKTextAlign.Center, font, ink);

        return titleY - unit * 4f;
    }

    /// <summary>
    /// The change bar and the sideways reference string down the left margin, both of which a
    /// real page carries.
    /// </summary>
    private static void DrawRevisionBar(SKCanvas canvas, DocumentPageRequest request, SKRect page, float captionTop, float unit, SKPaint ink)
    {
        using var font = new SKFont(Fonts.Regular(request.ResolvedTextStyle), unit * 2.6f);

        canvas.DrawText("R", page.Left, captionTop + unit * 4f, SKTextAlign.Left, font, ink);

        var reference = $"{request.DocumentNumber.Replace('-', ' ')} {request.PageNumber.Replace("Page ", "")}";

        canvas.Save();
        canvas.Translate(page.Left + unit * 1.5f, page.MidY + unit * 22);
        canvas.RotateDegrees(-90);
        canvas.DrawText(reference, 0, 0, SKTextAlign.Left, font, ink);
        canvas.Restore();
    }

    /// <summary>The effectivity box, the document number, the page and date, and the print line.</summary>
    private static float DrawFooter(SKCanvas canvas, DocumentPageRequest request, SKRect page, float unit, SKPaint ink, SKPaint rule)
    {
        using var boxFont = new SKFont(Fonts.Regular(request.ResolvedTextStyle), unit * 3.0f);
        using var numberFont = new SKFont(Fonts.Bold(request.ResolvedTextStyle), unit * 8f);
        using var smallFont = new SKFont(Fonts.Regular(request.ResolvedTextStyle), unit * 2.9f);

        var printY = page.Bottom;
        canvas.DrawText("Printed in the European Union", page.MidX, printY, SKTextAlign.Center, smallFont, ink);

        // The company mark, set small at the foot on the left, where the print line leaves room.
        if (request.HasFooterMark)
        {
            var era = request.LogoEra ?? Logo.EraFor(request.Aircraft);
            Logo.Draw(canvas, era, page.Left, printY - unit, unit * 4.2f);
        }

        var boxBottom = printY - unit * 6;
        var boxTop = boxBottom - unit * 14;
        var boxLeft = page.Left + unit * 8;
        var boxRight = boxLeft + unit * 68;

        // The effectivity box is open on the right, as it is on the printed pages.
        canvas.DrawLine(boxLeft, boxTop, boxRight, boxTop, rule);
        canvas.DrawLine(boxLeft, boxTop, boxLeft, boxBottom, rule);
        canvas.DrawLine(boxLeft, boxBottom, boxRight, boxBottom, rule);

        canvas.DrawText($"EFF :    {request.Effectivity}", boxLeft + unit * 3, boxTop + unit * 5, SKTextAlign.Left, boxFont, ink);
        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
            canvas.DrawText(request.CustomerCode, boxLeft + unit * 3, boxBottom - unit * 2.5f, SKTextAlign.Left, boxFont, ink);

        // The document reference, set large, with the page and date under it.
        canvas.DrawText(Spaced(request.DocumentNumber), page.Right - unit * 2, boxTop + unit * 7, SKTextAlign.Right, numberFont, ink);
        canvas.DrawText(request.PageNumber, page.Right - unit * 2, boxBottom - unit * 3, SKTextAlign.Right, smallFont, ink);
        canvas.DrawText(request.IssueDate, page.Right - unit * 2, boxBottom, SKTextAlign.Right, smallFont, ink);

        return boxTop;
    }

    /// <summary>Fits the drawing into the space left between header and caption.</summary>
    private static void DrawFigure(SKCanvas canvas, DocumentPageRequest request, SKRect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;

        var drawing = AircraftIllustrator.Load(request.Aircraft, request.View);

        // Leave room inside the panel for callout labels and dimension lines.
        var inset = Math.Min(area.Width, area.Height) * (request.ShowDimensions ? 0.16f : 0.10f);
        var usable = new SKRect(area.Left + inset, area.Top + inset, area.Right - inset, area.Bottom - inset);

        var scale = Math.Min(usable.Width / drawing.Width, usable.Height / drawing.Height);
        float w = drawing.Width * scale, h = drawing.Height * scale;

        // Sit the figure towards the top of the panel, as the printed pages do, rather than
        // floating it in the middle of whatever space is left.
        var top = usable.Top + Math.Min(usable.Height - h, Math.Min(area.Width, area.Height) * 0.06f);
        var target = new SKRect(usable.MidX - w / 2, top, usable.MidX + w / 2, top + h);

        AircraftIllustrator.Compose(canvas, request, drawing, target, area);
    }

    /// <summary>Separates a document number with the mid dots the manuals print, "06·10·00".</summary>
    private static string Spaced(string documentNumber) => documentNumber.Replace("-", "·");

    private static string ManualName(DocumentKind kind) => kind switch
    {
        DocumentKind.AircraftMaintenanceManual => "AIRCRAFT MAINTENANCE MANUAL",
        DocumentKind.IllustratedPartsCatalog => "ILLUSTRATED PARTS CATALOG",
        DocumentKind.StructuralRepairManual => "STRUCTURAL REPAIR MANUAL",
        DocumentKind.TroubleShootingManual => "TROUBLE SHOOTING MANUAL",
        DocumentKind.WiringDiagramManual => "WIRING DIAGRAM MANUAL",
        DocumentKind.FlightCrewOperatingManual => "FLIGHT CREW OPERATING MANUAL",
        DocumentKind.ServiceBulletin => "SERVICE BULLETIN",
        DocumentKind.ModificationDossier => "MODIFICATION DOSSIER",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
