using System.Reflection;
using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>Which family a page is set in.</summary>
public enum TextStyle
{
    /// <summary>A proportional sans, for pages of the phototypeset era onwards.</summary>
    Modern,

    /// <summary>
    /// A typewriter face. Maintenance documentation of the seventies and eighties was set on
    /// monospaced output devices, and a page for an aircraft of that vintage reads wrong in
    /// anything else.
    /// </summary>
    Typewriter,
}

/// <summary>
/// The typefaces pages are set in, loaded from embedded resources so output does not depend on
/// what happens to be installed on the machine.
/// </summary>
/// <remarks>
/// Gudea and Courier Prime are both under the SIL Open Font License; the licences are in
/// <c>Resources/licences/</c>. Aeralis has no display face of its own to ship here, so the
/// type designation in the page heading is set in the same family as the rest of the page.
/// </remarks>
internal static class Fonts
{
    private static readonly Dictionary<string, SKTypeface> Cache = [];

    public static SKTypeface Regular(TextStyle style) =>
        Load(style == TextStyle.Typewriter ? "CourierPrime-Regular.ttf" : "Gudea-Regular.ttf");

    public static SKTypeface Bold(TextStyle style) =>
        Load(style == TextStyle.Typewriter ? "CourierPrime-Bold.ttf" : "Gudea-Bold.ttf");

    public static SKTypeface Italic(TextStyle style) =>
        Load(style == TextStyle.Typewriter ? "CourierPrime-Italic.ttf" : "Gudea-Italic.ttf");

    /// <summary>
    /// The face the aircraft designation is set in, in the page heading. A publisher would use
    /// its own display face here; there is none to ship, so the page's own bold is used and the
    /// heading holds together on weight rather than on family.
    /// </summary>
    public static SKTypeface Heading => Load("Gudea-Bold.ttf");

    private static SKTypeface Load(string file)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(file, out var cached)) return cached;

            var resource = $"Aeralis.Illustrations.Resources.{file}";

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"embedded font '{resource}' is missing");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;

            var typeface = SKTypeface.FromStream(buffer)
                ?? throw new InvalidOperationException($"embedded font '{resource}' could not be read");

            Cache[file] = typeface;
            return typeface;
        }
    }
}
