using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// The Aeralis marks as standalone images, for callers that put one on a page this project
/// does not draw — the running header of a rendered S1000D publication, for instance.
/// </summary>
/// <remarks>
/// A page drawn by <see cref="AircraftIllustrator"/> places its own mark, picked from the
/// aircraft's era. Anything rendered outside this project has to be handed the mark as a
/// file, so the same choice and the same artwork are available here: same era mapping, same
/// single-colour ink.
/// </remarks>
public static class AeralisMark
{
    /// <summary>The mark Aeralis was using when this aircraft entered service.</summary>
    public static LogoEra EraFor(AircraftType aircraft) => Logo.EraFor(aircraft);

    /// <summary>The year this variant entered service.</summary>
    public static int EntryIntoService(AircraftType aircraft) => Logo.EntryIntoService(aircraft);

    /// <summary>
    /// Whether pages for this aircraft are of the printed era — the same test that decides
    /// whether <see cref="DocumentPageRequest.Scanned"/> defaults on.
    /// </summary>
    public static bool IsPrintEra(AircraftType aircraft) => Logo.EntryIntoService(aircraft) < 2000;

    /// <summary>
    /// Renders a mark as a PNG, black on opaque white, at the given height in pixels. The
    /// width follows from the mark's own proportions.
    /// </summary>
    /// <remarks>
    /// Opaque white rather than transparent: the mark is going onto a printed page, and a
    /// renderer that flattens an alpha channel badly turns the black ink grey.
    /// </remarks>
    /// <returns>The PNG bytes, or an empty array if the mark is not available.</returns>
    public static byte[] RenderPng(LogoEra era, int heightPx)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(heightPx, 4);

        var width = Logo.Width(era, heightPx);
        if (width <= 0) return [];

        using var bitmap = new SKBitmap((int)Math.Ceiling(width), heightPx);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            Logo.Draw(canvas, era, 0, heightPx / 2f, heightPx);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
