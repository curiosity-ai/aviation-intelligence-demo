using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// A coarse mask of where a drawing's aircraft actually is, in view fractions.
/// </summary>
/// <remarks>
/// <para>
/// The section outlines in <see cref="SectionMap"/> are rectangles and quads covering roughly
/// the right part of the aircraft, which means they also cover white space beside it — the box
/// around a wing includes the air beyond the tip. Sampling inside the outline alone would
/// sometimes put an arrow in empty space.
/// </para>
/// <para>
/// The drawings are outlines, not filled shapes, so testing for ink under the point does not
/// work either: most of the fuselage interior is white. Instead the mask is filled in from the
/// extent of the ink — a cell counts as aircraft when it lies between the topmost and
/// bottommost ink in its column <em>and</em> between the leftmost and rightmost ink in its row.
/// That is a cheap stand-in for the filled silhouette and errs towards including slightly too
/// much rather than pushing arrows off the airframe.
/// </para>
/// <para>
/// Ink is found on a raster several times finer than the mask, because these drawings are
/// hairlines: rasterised straight to the mask's own resolution they anti-alias to almost
/// nothing and the mask comes back empty, which silently turns the silhouette test into no
/// test at all.
/// </para>
/// </remarks>
internal sealed class Silhouette
{
    private const int Grid = 300;
    private const int Supersample = 4;

    /// <summary>Anything not almost pure white counts, since hairlines render pale.</summary>
    private const byte InkThreshold = 250;

    private readonly bool[,] _filled;

    private Silhouette(bool[,] filled) => _filled = filled;

    /// <summary>Share of the mask counted as aircraft. Near zero means the measurement failed.</summary>
    public double Coverage { get; private init; }

    /// <summary>True when a point in view fractions falls on the aircraft.</summary>
    public bool Contains(Frac point)
    {
        var x = (int)(point.U * Grid);
        var y = (int)(point.V * Grid);
        return x >= 0 && y >= 0 && x < Grid && y < Grid && _filled[x, y];
    }

    public static Silhouette Measure(SKPicture picture, SKRect cull)
    {
        const int raster = Grid * Supersample;

        using var bitmap = new SKBitmap(raster, raster, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(raster / Math.Max(cull.Width, 1e-6f), raster / Math.Max(cull.Height, 1e-6f));
            canvas.Translate(-cull.Left, -cull.Top);
            canvas.DrawPicture(picture);
        }

        var ink = new bool[Grid, Grid];
        for (var y = 0; y < raster; y++)
            for (var x = 0; x < raster; x++)
                if (bitmap.GetPixel(x, y).Red < InkThreshold)
                    ink[x / Supersample, y / Supersample] = true;

        var columns = new (int Min, int Max)[Grid];
        var rows = new (int Min, int Max)[Grid];
        for (var i = 0; i < Grid; i++) { columns[i] = (int.MaxValue, int.MinValue); rows[i] = (int.MaxValue, int.MinValue); }

        for (var x = 0; x < Grid; x++)
            for (var y = 0; y < Grid; y++)
            {
                if (!ink[x, y]) continue;
                columns[x] = (Math.Min(columns[x].Min, y), Math.Max(columns[x].Max, y));
                rows[y] = (Math.Min(rows[y].Min, x), Math.Max(rows[y].Max, x));
            }

        var filled = new bool[Grid, Grid];
        var count = 0;

        for (var x = 0; x < Grid; x++)
        {
            if (columns[x].Min > columns[x].Max) continue;

            for (var y = columns[x].Min; y <= columns[x].Max; y++)
            {
                if (rows[y].Min > rows[y].Max) continue;
                if (x < rows[y].Min || x > rows[y].Max) continue;
                filled[x, y] = true;
                count++;
            }
        }

        return new Silhouette(filled) { Coverage = (double)count / (Grid * Grid) };
    }
}
