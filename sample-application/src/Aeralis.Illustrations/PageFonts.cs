using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>
/// The embedded typefaces pages are set in, for callers that draw their own page and want it
/// to match — and to look the same on every machine.
/// </summary>
/// <remarks>
/// Nothing here reads a font from the host. A page drawn with an installed font renders
/// differently wherever the font is missing or a different version, which for a checked-in
/// snapshot is the same as not being reproducible at all.
/// </remarks>
public static class PageFonts
{
    /// <summary>The body face: Gudea for a modern page, Courier Prime for a typewritten one.</summary>
    public static SKTypeface Regular(TextStyle style) => Fonts.Regular(style);

    /// <summary>The bold body face.</summary>
    public static SKTypeface Bold(TextStyle style) => Fonts.Bold(style);

    /// <summary>The italic body face.</summary>
    public static SKTypeface Italic(TextStyle style) => Fonts.Italic(style);
}
