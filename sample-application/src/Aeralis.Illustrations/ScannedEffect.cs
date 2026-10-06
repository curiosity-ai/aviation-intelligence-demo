using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// Makes a rendered page look like a scan of a printed one.
/// </summary>
/// <remarks>
/// <para>
/// Documentation of this age is rarely held as clean digital artwork. What a technical data
/// system actually receives is a scan of paper, and a demo that only ever shows crisp vector
/// pages skips the part that makes the problem interesting — so the effect is on by default for
/// aircraft old enough that their manuals would have been printed rather than published.
/// </para>
/// <para>
/// The steps are the ones that leave the marks people recognise: the sheet sits slightly askew
/// on the platen, the optics lose a little sharpness, the paper is not white and not evenly
/// lit, dust on the glass shows as specks, and the result is stored as JPEG, which quantises
/// the greys and rings faintly around every line of text.
/// </para>
/// <para>
/// It works on a finished raster and knows nothing about what drew it, so a rendered
/// publication is scanned the same way a drawn page is.
/// </para>
/// </remarks>
public static class ScannedEffect
{
    /// <summary>Applies the effect, returning a new bitmap. <paramref name="amount"/> is 0 to 1.</summary>
    public static SKBitmap Apply(SKBitmap page, int seed, float amount = 1f)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var random = new Random(seed);

        var scanned = Expose(page, random, amount);
        Soil(scanned, random, amount);
        return Compress(scanned, amount);
    }

    /// <summary>
    /// Lays the sheet on the platen — slightly rotated and offset — and takes the picture:
    /// greyed, softened, and lifted off pure black and white the way a scanner's response does.
    /// </summary>
    private static SKBitmap Expose(SKBitmap page, Random random, float amount)
    {
        var result = new SKBitmap(page.Width, page.Height);
        using var canvas = new SKCanvas(result);

        // Paper, not white.
        canvas.Clear(new SKColor(0xF6, 0xF4, 0xEF));

        var angle = (float)(random.NextDouble() * 2 - 1) * 1.1f * amount;
        var shiftX = (float)(random.NextDouble() * 2 - 1) * page.Width * 0.006f * amount;
        var shiftY = (float)(random.NextDouble() * 2 - 1) * page.Height * 0.006f * amount;

        // A sheet never lies flat on the glass, so the image drifts sideways down the page.
        var amplitude = page.Width * 0.0032f * amount;
        var period = page.Height / (float)(1.1 + random.NextDouble() * 0.8);
        var phase = (float)(random.NextDouble() * Math.PI * 2);

        // Grey, slightly flattened: black goes to charcoal and white loses a shade, which is
        // what stops a scan from looking like clean vector output.
        var lift = 0.035f * amount;
        var gain = 1f - 0.07f * amount;
        // Skia's colour matrix works on 0..1 channels, so the offsets in the fifth column are
        // fractions of full scale, not 0..255 values.
        var matrix = new[]
        {
            0.299f * gain, 0.587f * gain, 0.114f * gain, 0, lift,
            0.299f * gain, 0.587f * gain, 0.114f * gain, 0, lift,
            0.299f * gain, 0.587f * gain, 0.114f * gain, 0, lift * 0.96f,
            0, 0, 0, 1, 0,
        };

        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateColorMatrix(matrix),
            ImageFilter = amount > 0.05f ? SKImageFilter.CreateBlur(0.55f * amount, 0.55f * amount) : null,
        };

        canvas.Save();
        canvas.RotateDegrees(angle, page.Width / 2f, page.Height / 2f);
        canvas.Translate(shiftX, shiftY);

        // Drawn as overlapping strips, each nudged along the curve, which bends the page
        // without needing a mesh — the offset changes slowly enough that the seams do not show.
        const int strip = 4;
        for (var y = 0; y < page.Height; y += strip)
        {
            var height = Math.Min(strip, page.Height - y);
            var dx = amplitude * MathF.Sin(y / period * MathF.PI * 2 + phase);

            var source = new SKRect(0, y, page.Width, y + height);
            var destination = new SKRect(dx, y, page.Width + dx, y + height + 0.6f);
            canvas.DrawBitmap(page, source, destination, paint);
        }

        canvas.Restore();

        return result;
    }

    /// <summary>Paper grain, uneven lighting, and dust on the glass.</summary>
    private static void Soil(SKBitmap scanned, Random random, float amount)
    {
        using var canvas = new SKCanvas(scanned);
        int w = scanned.Width, h = scanned.Height;
        var scale = Math.Min(w, h);

        // Grain, built at quarter size and scaled up so it reads as paper rather than as
        // per-pixel static.
        int nw = Math.Max(1, w / 4), nh = Math.Max(1, h / 4);
        using (var noise = new SKBitmap(nw, nh))
        {
            for (var y = 0; y < nh; y++)
                for (var x = 0; x < nw; x++)
                {
                    var v = (byte)(255 - random.Next(0, (int)(11 * amount) + 1));
                    noise.SetPixel(x, y, new SKColor(v, v, v));
                }

            using var paint = new SKPaint { BlendMode = SKBlendMode.Multiply, IsAntialias = true };
            canvas.DrawBitmap(noise, new SKRect(0, 0, w, h), paint);
        }

        // Lighting that falls off towards the edges, as a lid never quite closes flat.
        using (var shade = new SKPaint { BlendMode = SKBlendMode.Multiply })
        {
            var edge = (byte)(255 - 15 * amount);
            shade.Shader = SKShader.CreateRadialGradient(
                new SKPoint(w * 0.5f, h * 0.48f),
                MathF.Sqrt(w * w + h * h) * 0.62f,
                [SKColors.White, new SKColor(edge, edge, edge)],
                [0.55f, 1f],
                SKShaderTileMode.Clamp);

            canvas.DrawRect(new SKRect(0, 0, w, h), shade);
        }

        // Dust and toner specks.
        var specks = (int)(120 * amount);
        for (var i = 0; i < specks; i++)
        {
            var grey = (byte)random.Next(40, 150);
            using var paint = new SKPaint
            {
                Color = new SKColor(grey, grey, grey, (byte)random.Next(24, 90)),
                IsAntialias = true,
            };

            var x = (float)random.NextDouble() * w;
            var y = (float)random.NextDouble() * h;
            canvas.DrawCircle(x, y, (float)random.NextDouble() * scale * 0.0016f + 0.4f, paint);
        }

        // A couple of faint streaks along the direction of travel.
        var streaks = (int)(3 * amount);
        for (var i = 0; i < streaks; i++)
        {
            var grey = (byte)random.Next(150, 210);
            using var paint = new SKPaint
            {
                Color = new SKColor(grey, grey, grey, (byte)random.Next(14, 40)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)random.NextDouble() * 2.2f + 0.6f,
                IsAntialias = true,
            };

            var x = (float)random.NextDouble() * w;
            canvas.DrawLine(x, 0, x + (float)(random.NextDouble() * 6 - 3), h, paint);
        }
    }

    /// <summary>
    /// Stores the result as JPEG and reads it back, which is how a scan reaches a document
    /// system and where its blocking and ringing come from.
    /// </summary>
    private static SKBitmap Compress(SKBitmap scanned, float amount)
    {
        var quality = (int)Math.Round(92 - 30 * amount);

        using var image = SKImage.FromBitmap(scanned);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 30, 100));

        var decoded = SKBitmap.Decode(data);
        if (decoded is null) return scanned;

        scanned.Dispose();
        return decoded;
    }
}
