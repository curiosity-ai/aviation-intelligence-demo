using SkiaSharp;
using Aeralis.Illustrations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;

namespace Aeralis.Attachments.Pdf;

/// <summary>
/// Draws a page of a PDF onto a bitmap, so the scanned-page effect has something to work on.
/// </summary>
/// <remarks>
/// <para>
/// There is no PDF rasteriser in the stack — FOP.Sharp writes PDFs, PdfPig reads them, and
/// neither draws one. What PdfPig does give is the page taken apart: every glyph with its
/// baseline and size, every vector path with its commands and paint, every embedded image with
/// its placement. That is enough to draw the page again with Skia, and it is exact for the
/// pages this project produces — text, rules, table borders and illustrations, which is all
/// the presentation stylesheets emit. It is not a general PDF renderer and does not try to be:
/// no shading patterns, no transparency groups, no clipping.
/// </para>
/// <para>
/// Glyphs are placed one at a time from their own baseline positions, so the substituted
/// typeface does not have to match the metrics of the one the PDF was written with — the
/// layout has already happened and every character carries where it landed.
/// </para>
/// </remarks>
public static class PdfPageRasterizer
{
    /// <summary>A PDF point is 1/72 inch.</summary>
    private const double PointsPerInch = 72.0;

    /// <summary>Renders one page at the given resolution.</summary>
    public static SKBitmap Render(Page page, double dpi)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 36);

        var scale = dpi / PointsPerInch;
        var width = Math.Max(1, (int)Math.Round(page.Width * scale));
        var height = Math.Max(1, (int)Math.Round(page.Height * scale));

        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var frame = new PageFrame(page.Height, scale);

        // Paths first, then images, then text: that is the order the presentation stylesheets
        // stack them — a shaded table cell behind its rules, an illustration behind nothing,
        // and text over both.
        DrawPaths(canvas, page, frame);
        DrawImages(canvas, page, frame);
        DrawText(canvas, page, frame);

        return bitmap;
    }

    /// <summary>Maps PDF user space (origin bottom left, points) onto the bitmap.</summary>
    private readonly record struct PageFrame(double PageHeight, double Scale)
    {
        public float X(double x) => (float)(x * Scale);
        public float Y(double y) => (float)((PageHeight - y) * Scale);
        public float Length(double value) => (float)(value * Scale);
        public SKPoint Point(PdfPoint point) => new(X(point.X), Y(point.Y));
    }

    // ------------------------------------------------------------------------- paths

    private static void DrawPaths(SKCanvas canvas, Page page, PageFrame frame)
    {
        foreach (var path in page.Paths)
        {
            if (path.IsClipping) continue;

            using var geometry = ToSkPath(path, frame,
                path.FillingRule == FillingRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding);
            if (geometry.IsEmpty) continue;

            if (path.IsFilled)
            {
                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = ToColor(path.FillColor, SKColors.Black),
                };
                canvas.DrawPath(geometry, fill);
            }

            if (path.IsStroked)
            {
                using var stroke = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    Color = ToColor(path.StrokeColor, SKColors.Black),
                    // A zero-width line in PDF is the thinnest the device can draw, which on
                    // a raster page is one pixel.
                    StrokeWidth = Math.Max(frame.Length(path.LineWidth), 0.9f),
                    StrokeCap = path.LineCapStyle == UglyToad.PdfPig.Graphics.Core.LineCapStyle.Round
                        ? SKStrokeCap.Round
                        : SKStrokeCap.Butt,
                };
                canvas.DrawPath(geometry, stroke);
            }
        }
    }

    private static SKPath ToSkPath(PdfPath path, PageFrame frame, SKPathFillType fillType)
    {
        var geometry = new SKPathBuilder { FillType = fillType };

        foreach (var subpath in path)
        {
            var started = false;

            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        geometry.MoveTo(frame.Point(move.Location));
                        started = true;
                        break;

                    case PdfSubpath.Line line:
                        if (!started)
                        {
                            geometry.MoveTo(frame.Point(line.From));
                            started = true;
                        }
                        geometry.LineTo(frame.Point(line.To));
                        break;

                    case PdfSubpath.BezierCurve curve:
                        if (!started)
                        {
                            geometry.MoveTo(frame.Point(curve.StartPoint));
                            started = true;
                        }
                        // The curve's own flattening, which keeps the control points out of a
                        // conversion that would have to guess whether they are quadratic.
                        foreach (var segment in curve.ToLines(16))
                        {
                            geometry.LineTo(frame.Point(segment.To));
                        }
                        break;

                    case PdfSubpath.Close:
                        geometry.Close();
                        break;
                }
            }
        }

        return geometry.Detach();
    }

    private static SKColor ToColor(IColor? color, SKColor fallback)
    {
        if (color is null) return fallback;
        var (r, g, b) = color.ToRGBValues();
        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // ------------------------------------------------------------------------ images

    private static void DrawImages(SKCanvas canvas, Page page, PageFrame frame)
    {
        foreach (var image in page.GetImages())
        {
            using var bitmap = Decode(image);
            if (bitmap is null) continue;

            var bounds = image.BoundingBox;
            var destination = new SKRect(
                frame.X(bounds.Left), frame.Y(bounds.Top),
                frame.X(bounds.Right), frame.Y(bounds.Bottom));

            canvas.DrawBitmap(bitmap, destination,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
    }

    private static SKBitmap? Decode(IPdfImage image)
    {
        // TryGetPng handles the colour-space and filter combinations PdfPig can turn back into
        // a picture; the raw bytes are the fall-back for the ones it passes through, which for
        // an illustration written by this pipeline is a JPEG or a PNG either way.
        if (image.TryGetPng(out var png) && png is { Length: > 0 })
        {
            var decoded = SKBitmap.Decode(png);
            if (decoded is not null) return decoded;
        }

        if (image.TryGetBytesAsMemory(out var memory) && memory.Length > 0)
        {
            return SKBitmap.Decode(memory.Span);
        }

        return SKBitmap.Decode(image.RawBytes);
    }

    // -------------------------------------------------------------------------- text

    private static void DrawText(SKCanvas canvas, Page page, PageFrame frame)
    {
        using var ink = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        var typefaces = new Dictionary<string, SKTypeface>(StringComparer.Ordinal);

        // A page holds a few thousand letters and a handful of distinct face-and-size pairs.
        // Building an SKFont per letter is most of the time this method takes.
        var fonts = new Dictionary<(string Font, int Size), SKFont>();

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrEmpty(letter.Value) || letter.Value == " ") continue;
            if (letter.RenderingMode == TextRenderingMode.Neither) continue;

            var pixels = frame.Length(letter.PointSize);
            var key = (letter.FontName ?? "", (int)Math.Round(pixels * 16));
            if (!fonts.TryGetValue(key, out var font))
            {
                fonts[key] = font = new SKFont(Typeface(typefaces, letter.FontName), pixels);
            }

            ink.Color = ToColor(letter.Color, SKColors.Black);

            var baseline = frame.Point(letter.StartBaseLine);

            if (letter.TextOrientation == TextOrientation.Horizontal)
            {
                canvas.DrawText(letter.Value, baseline.X, baseline.Y, SKTextAlign.Left, font, ink);
                continue;
            }

            // Anything not set along the page — a rotated table header, say — is drawn through
            // the rotation its baseline describes.
            var end = frame.Point(letter.EndBaseLine);
            var degrees = (float)(Math.Atan2(end.Y - baseline.Y, end.X - baseline.X) * 180 / Math.PI);

            canvas.Save();
            canvas.Translate(baseline.X, baseline.Y);
            canvas.RotateDegrees(degrees);
            canvas.DrawText(letter.Value, 0, 0, SKTextAlign.Left, font, ink);
            canvas.Restore();
        }

        foreach (var font in fonts.Values) font.Dispose();
    }

    /// <summary>
    /// The face to draw a PDF font with. The PDF names a base-14 font; the embedded families
    /// stand in for them so a page rasterises the same way wherever it runs.
    /// </summary>
    private static SKTypeface Typeface(Dictionary<string, SKTypeface> cache, string? fontName)
    {
        var name = fontName ?? "";
        if (cache.TryGetValue(name, out var cached)) return cached;

        var style = name.Contains("Courier", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
            ? TextStyle.Typewriter
            : TextStyle.Modern;

        var bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                     || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

        var typeface = bold
            ? PageFonts.Bold(style)
            : italic
                ? PageFonts.Italic(style)
                : PageFonts.Regular(style);

        cache[name] = typeface;
        return typeface;
    }
}
