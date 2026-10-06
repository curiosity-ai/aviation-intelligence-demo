using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// Draws a technical-drawing callout: a dot on the target, a leader out to a shelf clear of the
/// artwork, and the text sitting on the shelf.
/// </summary>
/// <remarks>
/// Where the label goes is <see cref="LabelPlacer"/>'s decision, taken from the free space
/// actually left on the page. This type only draws what it is told to.
/// </remarks>
internal static class Callout
{
    private const float ArrowLength = 14f;
    private const float ArrowWidth = 5.5f;

    public static void Draw(SKCanvas canvas, SKPoint target, string text, SKRect bounds, LabelPlacer placer, TextStyle style)
    {
        var scale = Math.Min(bounds.Width, bounds.Height);
        var fontSize = Math.Clamp(scale * 0.023f, 9f, 22f);

        using var font = new SKFont(Fonts.Regular(style), fontSize);
        using var stroke = new SKPaint { Color = Ink, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, scale * 0.0013f) };
        using var fill = new SKPaint { Color = Ink, IsAntialias = true, Style = SKPaintStyle.Fill };

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var lineHeight = fontSize * 1.3f;
        var textWidth = lines.Max(l => font.MeasureText(l));
        var textHeight = lines.Length * lineHeight;

        var placement = placer.Place(target, textWidth, textHeight, scale * 0.035f);

        canvas.DrawLine(target, placement.Elbow, stroke);
        canvas.DrawLine(placement.Elbow, placement.ShelfEnd, stroke);
        DrawArrowhead(canvas, target, placement.Elbow, fill);
        canvas.DrawCircle(target, Math.Max(1.6f, scale * 0.0022f), fill);

        var baseline = placement.Text.Bottom - fontSize * 0.25f;
        for (var i = 0; i < lines.Length; i++)
            canvas.DrawText(lines[i], placement.Text.Left, baseline - (lines.Length - 1 - i) * lineHeight, SKTextAlign.Left, font, fill);
    }

    private static SKColor Ink => new(0x11, 0x11, 0x11);

    private static void DrawArrowhead(SKCanvas canvas, SKPoint tip, SKPoint from, SKPaint fill)
    {
        float dx = tip.X - from.X, dy = tip.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f) return;

        float ux = dx / length, uy = dy / length;
        var basePoint = new SKPoint(tip.X - ux * ArrowLength, tip.Y - uy * ArrowLength);

        using var head = new SKPathBuilder();
        head.MoveTo(tip);
        head.LineTo(basePoint.X - uy * ArrowWidth, basePoint.Y + ux * ArrowWidth);
        head.LineTo(basePoint.X + uy * ArrowWidth, basePoint.Y - ux * ArrowWidth);
        head.Close();

        using var path = head.Snapshot();
        canvas.DrawPath(path, fill);
    }
}
