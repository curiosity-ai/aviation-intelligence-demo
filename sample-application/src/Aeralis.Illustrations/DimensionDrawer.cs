using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// Draws overall dimension lines around a view, in the style the manuals use: witness lines out
/// from the extremities, a dimension line with a tick at each end, and the measurement in feet
/// over metres.
/// </summary>
internal static class DimensionDrawer
{
    private const float TickLength = 5f;

    /// <summary>
    /// Returns the areas its labels occupy, so callouts placed afterwards can keep clear of
    /// them instead of covering the measurements with a label plate.
    /// </summary>
    public static List<SKRect> Draw(SKCanvas canvas, SKRect drawing, Dimensions dimensions, ViewKind view, float scale, TextStyle style)
    {
        var (across, _, up, _) = dimensions.ForView(view);
        var occupied = new List<SKRect>();

        var stroke = new SKPaint { Color = new SKColor(0x11, 0x11, 0x11), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.8f, scale * 0.0011f) };
        var fill = new SKPaint { Color = new SKColor(0x11, 0x11, 0x11), IsAntialias = true, Style = SKPaintStyle.Fill };
        var font = new SKFont(Fonts.Regular(style), Math.Clamp(scale * 0.019f, 7f, 18f));

        using (stroke)
        using (fill)
        using (font)
        {
            var offset = scale * 0.055f;

            // Across the top of the drawing.
            var y = drawing.Top - offset;
            Witness(canvas, new SKPoint(drawing.Left, drawing.Top), new SKPoint(drawing.Left, y), stroke);
            Witness(canvas, new SKPoint(drawing.Right, drawing.Top), new SKPoint(drawing.Right, y), stroke);
            Line(canvas, new SKPoint(drawing.Left, y), new SKPoint(drawing.Right, y), stroke);
            occupied.Add(Label(canvas, across, new SKPoint(drawing.MidX, y - font.Size * 0.55f), font, fill, Anchor.Above));

            // Down the right-hand side.
            var x = drawing.Right + offset;
            Witness(canvas, new SKPoint(drawing.Right, drawing.Top), new SKPoint(x, drawing.Top), stroke);
            Witness(canvas, new SKPoint(drawing.Right, drawing.Bottom), new SKPoint(x, drawing.Bottom), stroke);
            Line(canvas, new SKPoint(x, drawing.Top), new SKPoint(x, drawing.Bottom), stroke);
            occupied.Add(Label(canvas, up, new SKPoint(x + font.Size * 0.6f, drawing.MidY), font, fill, Anchor.Beside));
        }

        return occupied;
    }

    /// <summary>Extends a thin witness line a little past the dimension line, as drafting does.</summary>
    private static void Witness(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint stroke)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f) return;

        canvas.DrawLine(from, new SKPoint(to.X + dx / length * TickLength, to.Y + dy / length * TickLength), stroke);
    }

    /// <summary>The dimension line itself, with an arrowhead turned inward at each end.</summary>
    private static void Line(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint stroke)
    {
        canvas.DrawLine(from, to, stroke);
        Arrow(canvas, from, to, stroke);
        Arrow(canvas, to, from, stroke);
    }

    private static void Arrow(SKCanvas canvas, SKPoint tip, SKPoint towards, SKPaint stroke)
    {
        var dx = towards.X - tip.X;
        var dy = towards.Y - tip.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f) return;

        float ux = dx / length, uy = dy / length;
        const float size = 7f;
        var basePoint = new SKPoint(tip.X + ux * size, tip.Y + uy * size);

        canvas.DrawLine(tip, new SKPoint(basePoint.X - uy * size * 0.35f, basePoint.Y + ux * size * 0.35f), stroke);
        canvas.DrawLine(tip, new SKPoint(basePoint.X + uy * size * 0.35f, basePoint.Y - ux * size * 0.35f), stroke);
    }

    /// <summary>How a measurement sits relative to its dimension line.</summary>
    private enum Anchor
    {
        /// <summary>Both lines stacked clear above a horizontal dimension line.</summary>
        Above,

        /// <summary>Both lines beside a vertical dimension line, centred on it.</summary>
        Beside,
    }

    /// <summary>
    /// Writes a measurement as feet over metres and returns the area it covers. Neither line is
    /// allowed to sit on the dimension line: above a horizontal one both baselines are lifted
    /// clear, and beside a vertical one the text starts to the right of it.
    /// </summary>
    private static SKRect Label(SKCanvas canvas, double millimetres, SKPoint at, SKFont font, SKPaint fill, Anchor anchor)
    {
        var (feet, metres) = Dimensions.Format(millimetres);
        var lineHeight = font.Size * 1.15f;
        var width = Math.Max(font.MeasureText(feet), font.MeasureText(metres));

        if (anchor == Anchor.Above)
        {
            var lower = at.Y - font.Size * 0.25f;
            canvas.DrawText(feet, at.X, lower - lineHeight, SKTextAlign.Center, font, fill);
            canvas.DrawText(metres, at.X, lower, SKTextAlign.Center, font, fill);
            return new SKRect(at.X - width / 2, lower - lineHeight - font.Size, at.X + width / 2, lower + font.Size * 0.3f);
        }

        var middle = at.Y + font.Size * 0.35f;
        canvas.DrawText(feet, at.X, middle - lineHeight * 0.5f, SKTextAlign.Left, font, fill);
        canvas.DrawText(metres, at.X, middle + lineHeight * 0.5f, SKTextAlign.Left, font, fill);
        return new SKRect(at.X, middle - lineHeight - font.Size * 0.5f, at.X + width, middle + lineHeight * 0.5f + font.Size * 0.3f);
    }
}
