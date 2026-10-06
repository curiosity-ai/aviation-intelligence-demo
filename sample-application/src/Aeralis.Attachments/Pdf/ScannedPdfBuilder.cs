using System.Text;
using SkiaSharp;
using Aeralis.Illustrations;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Aeralis.Attachments.Pdf;

/// <summary>
/// Turns a cleanly rendered PDF into what a technical data system actually holds for a document
/// of that age: a scan of the printed page, with the text still there behind it.
/// </summary>
/// <remarks>
/// <para>
/// Three steps per page. The page is rasterised (<see cref="PdfPageRasterizer"/>), the scan is
/// applied to the raster (<see cref="ScannedEffect"/> — skew, optical softening, paper grain,
/// dust, JPEG blocking), and a new page is written holding that image at full bleed with the
/// original text drawn over it in the invisible rendering mode.
/// </para>
/// <para>
/// The invisible text is the point. A scan on its own is a picture: it cannot be searched,
/// indexed, embedded or quoted, and a demo whose corpus is half pictures demos nothing. Writing
/// the words back at the coordinates they were laid out at is what an OCR pass produces, except
/// that here the "OCR" is exact — the text never left the pipeline. Selecting a line in a viewer
/// highlights it where it appears on the scan.
/// </para>
/// </remarks>
public static class ScannedPdfBuilder
{
    /// <summary>Resolution the page is scanned at. 200 dpi is what an office scanner gives.</summary>
    public const double DefaultDpi = 200;

    /// <summary>JPEG quality for the page images. A scan is a JPEG, and this is why it looks like one.</summary>
    private const int JpegQuality = 72;

    /// <summary>
    /// Rebuilds <paramref name="pdf"/> as a searchable scan.
    /// </summary>
    /// <param name="pdf">The cleanly rendered PDF.</param>
    /// <param name="seed">Fixes the scan's randomness, so a document scans the same way every run.</param>
    /// <param name="amount">How heavily the scan shows, 0 to 1.</param>
    /// <param name="dpi">Resolution to rasterise at.</param>
    public static byte[] Build(byte[] pdf, int seed, float amount = 1f, double dpi = DefaultDpi)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        using var source = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true });
        using var builder = new PdfDocumentBuilder();

        // Helvetica is a metric-only standard font — no glyph data is embedded and none is
        // needed, because nothing of it is ever painted. It exists so the words have a font
        // to be written with.
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (var number = 1; number <= source.NumberOfPages; number++)
        {
            var page = source.GetPage(number);
            var target = builder.AddPage(page.Width, page.Height);

            AddScan(target, page, seed + number, amount, dpi);
            AddInvisibleText(target, page, font);
        }

        return builder.Build();
    }

    /// <summary>Whether a rendered PDF is worth scanning — an empty one is not.</summary>
    public static bool HasPages(byte[] pdf)
    {
        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true });
            return document.NumberOfPages > 0;
        }
        catch (PdfDocumentFormatException)
        {
            return false;
        }
    }

    private static void AddScan(PdfPageBuilder target, Page page, int seed, float amount, double dpi)
    {
        using var clean = PdfPageRasterizer.Render(page, dpi);
        using var scanned = ScannedEffect.Apply(clean, seed, amount);
        using var image = SKImage.FromBitmap(scanned);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        // Full bleed: the scan is the page, edge to edge, including the paper around the text.
        target.AddJpeg(data.ToArray(), new PdfRectangle(0, 0, page.Width, page.Height));
    }

    /// <summary>
    /// Writes the page's words back over the scan in the invisible rendering mode, word by word
    /// at the position each was laid out at.
    /// </summary>
    /// <remarks>
    /// Word by word rather than letter by letter: a viewer joins adjacent glyphs into a
    /// selection run, and one text-showing operator per word is both a tenth of the operators
    /// and what makes a double-click select a word rather than a character.
    /// </remarks>
    private static void AddInvisibleText(PdfPageBuilder target, Page page, PdfDocumentBuilder.AddedFont font)
    {
        target.SetTextRenderingMode(TextRenderingMode.Neither);

        foreach (var word in page.GetWords())
        {
            var text = Printable(word.Text);
            if (text.Length == 0) continue;

            var box = word.BoundingBox;
            var size = Math.Max(1, box.Height);
            if (double.IsNaN(size) || double.IsInfinity(size)) continue;

            try
            {
                target.AddText(text, size, new PdfPoint(box.BottomLeft.X, box.BottomLeft.Y), font);
            }
            catch (InvalidOperationException)
            {
                // A character the standard font has no width for. The word is dropped rather
                // than the page; there is no glyph to show for it either way.
            }
            catch (NotSupportedException)
            {
            }
        }
    }

    /// <summary>
    /// The word reduced to what a WinAnsi standard font can write. Anything outside it — an em
    /// dash, a degree sign, a non-breaking space — is folded to its nearest plain equivalent so
    /// the word stays searchable instead of being dropped whole.
    /// </summary>
    private static string Printable(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            var replacement = c switch
            {
                '—' or '–' or '−' => "-",
                '‘' or '’' => "'",
                '“' or '”' => "\"",
                ' ' or ' ' or ' ' => " ",
                '…' => "...",
                '·' or '•' => ".",
                '±' => "+/-",
                _ => c >= ' ' && c <= 'ÿ' ? c.ToString() : "",
            };
            builder.Append(replacement);
        }

        return builder.ToString().Trim();
    }
}
