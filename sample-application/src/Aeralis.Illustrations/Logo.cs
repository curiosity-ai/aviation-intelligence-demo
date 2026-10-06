using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace Aeralis.Illustrations;

/// <summary>Which of Aeralis' marks to use.</summary>
public enum LogoEra
{
    /// <summary>
    /// The mark adopted with the Zephyr and carried through the Solano and Vantor years —
    /// on every publication issued before the Meridian entered service.
    /// </summary>
    Heritage,

    /// <summary>The current mark, adopted with the Meridian programme.</summary>
    Modern,
}

/// <summary>
/// The Aeralis marks, drawn in one colour.
/// </summary>
/// <remarks>
/// <para>
/// Which mark a page carries follows the aircraft's own entry into service, so a page for an
/// AUSTER-20 gets the mark Aeralis used when that aircraft was new and a page for a
/// MERIDIAN-46 gets the current one. The winglet variants take their own introduction, not
/// the base type's — a wingletted ZEPHYR-22 is a 2012 aeroplane even though the ZEPHYR-22 is
/// a 1988 one.
/// </para>
/// <para>
/// Both marks are emblems with no lettering of their own, which is why there is no wordmark
/// here and nothing to split: <c>tools/prepare_logos.py</c> drops the exported background
/// plate, crops the viewBox to the artwork and forces the ink to a single colour. The sources
/// are kept in <c>Logos/</c>. One colour rather than the mark's own palette because these sit
/// on hairline line drawings, where the colour would be the only one on the page.
/// </para>
/// <para>
/// Kept internal: the public surface for a mark is <see cref="AeralisMark"/>, which hands out
/// images rather than a Skia drawing call.
/// </para>
/// </remarks>
internal static class Logo
{
    /// <summary>
    /// A mark and the box it should be seen through. The two are separate because the crop is
    /// carried on the root viewBox, and Svg.Skia renders a picture's whole content regardless
    /// of the root viewport — so the crop has to be applied as a clip at draw time.
    /// </summary>
    private sealed record Mark(SKPicture Picture, SKRect View);

    private static readonly Dictionary<string, Mark?> Cache = [];

    /// <summary>
    /// Entry into service, per variant. Only the era boundary matters, but the years are kept
    /// exact so the mapping can be checked rather than taken on trust.
    /// </summary>
    private static readonly Dictionary<AircraftType, int> Introduced = new()
    {
        [AircraftType.Cirrane11] = 2016,
        [AircraftType.Cirrane13] = 2016,
        [AircraftType.Auster20] = 1984,
        [AircraftType.Auster24] = 1986,
        [AircraftType.Zephyr18] = 2003,
        [AircraftType.Zephyr20] = 1996,
        [AircraftType.Zephyr20Winglet] = 2013,
        [AircraftType.Zephyr22] = 1988,
        [AircraftType.Zephyr22Winglet] = 2012,
        [AircraftType.Zephyr24] = 1994,
        [AircraftType.Zephyr24Winglet] = 2013,
        [AircraftType.Solano30] = 1998,
        [AircraftType.Solano30E] = 1998,
        [AircraftType.Solano30F] = 2010,
        [AircraftType.Solano34] = 1994,
        [AircraftType.Solano38] = 2020,
        [AircraftType.Solano39] = 2018,
        [AircraftType.Vantor62] = 1993,
        [AircraftType.Vantor64] = 1993,
        [AircraftType.Vantor66] = 2003,
        [AircraftType.Vantor68] = 2002,
        [AircraftType.Meridian42] = 2015,
        [AircraftType.Meridian46] = 2018,
        [AircraftType.Titan88] = 2007,
    };

    /// <summary>The year the Meridian entered service, and with it the current mark.</summary>
    private const int ModernMarkFrom = 2015;

    /// <summary>The year this variant entered service.</summary>
    public static int EntryIntoService(AircraftType aircraft) => Introduced.GetValueOrDefault(aircraft, 2017);

    /// <summary>The mark Aeralis was using when this aircraft entered service.</summary>
    public static LogoEra EraFor(AircraftType aircraft) =>
        EntryIntoService(aircraft) < ModernMarkFrom ? LogoEra.Heritage : LogoEra.Modern;

    /// <summary>
    /// Draws a mark at the given height, its left edge at <paramref name="left"/> and centred
    /// on <paramref name="centreY"/>. Returns the width it took, or zero if it is missing.
    /// </summary>
    public static float Draw(SKCanvas canvas, LogoEra era, float left, float centreY, float height)
    {
        if (Load(era) is not { } mark || mark.View.Height <= 0) return 0;

        var scale = height / mark.View.Height;

        canvas.Save();
        canvas.Translate(left, centreY - height / 2);
        canvas.Scale(scale);
        canvas.Translate(-mark.View.Left, -mark.View.Top);
        canvas.ClipRect(mark.View);
        canvas.DrawPicture(mark.Picture);
        canvas.Restore();

        return mark.View.Width * scale;
    }

    /// <summary>The width a mark occupies at a given height, without drawing it.</summary>
    public static float Width(LogoEra era, float height)
    {
        if (Load(era) is not { } mark || mark.View.Height <= 0) return 0;
        return mark.View.Width * (height / mark.View.Height);
    }

    private static Mark? Load(LogoEra era)
    {
        var name = $"logo-{era.ToString().ToLowerInvariant()}";

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            var resource = $"Aeralis.Illustrations.Resources.{name}.svg";
            Mark? mark = null;

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream is not null)
                {
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);

                    var svg = new SKSvg();
                    buffer.Position = 0;
                    var picture = svg.Load(buffer);

                    buffer.Position = 0;
                    var view = ViewBox(buffer) ?? picture?.CullRect;

                    if (picture is not null && view is not null) mark = new Mark(picture, view.Value);
                }
            }

            Cache[name] = mark;
            return mark;
        }
    }

    /// <summary>The root viewBox, which is the crop the preparation script wrote.</summary>
    private static SKRect? ViewBox(Stream stream)
    {
        try
        {
            var box = XDocument.Load(stream).Root?.Attribute("viewBox")?.Value;
            if (string.IsNullOrWhiteSpace(box)) return null;

            var parts = box.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return null;

            var values = parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            return new SKRect(values[0], values[1], values[0] + values[2], values[1] + values[3]);
        }
        catch
        {
            return null;
        }
    }
}
