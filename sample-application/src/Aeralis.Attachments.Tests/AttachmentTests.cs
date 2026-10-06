using System.Text;
using Aeralis.Dataset;
using UglyToad.PdfPig;

namespace Aeralis.Attachments.Tests;

/// <summary>
/// What a document actually produces: the right kind of file, deterministically, and — for the
/// aircraft old enough to be held as scans — a scan whose text is still there.
/// </summary>
public class AttachmentTests : IDisposable
{
    private readonly string _work =
        Path.Combine(Path.GetTempPath(), "aeralis-attachments-tests", Guid.NewGuid().ToString("N"));

    private AttachmentBuilder Builder => new(Dataset.Reference, _work);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AFigureBecomesAManualPage()
    {
        // Program M: the MERIDIAN, which has a three-view drawing.
        var figure = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.IPC && d.Envelope.Program == ProgramLetter.M);

        var rendering = Builder.Build(figure);

        var page = Assert.Single(rendering.Files);
        Assert.Equal(AttachmentKind.IllustrationPage, page.Kind);
        Assert.Equal("image/png", page.MediaType);
        Assert.Equal($"{figure.Envelope.DocId}.png", page.FileName);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], page.Bytes[..4]);

        // A figure is a drawing: there is no CSDB object behind it to keep.
        Assert.Null(rendering.Source);
    }

    [Fact]
    public void ADocumentBecomesAPublication()
    {
        var document = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.AMM && d.Envelope.Program == ProgramLetter.M);

        var attachment = Assert.Single(Builder.Build(document).Files);

        Assert.Equal(AttachmentKind.Publication, attachment.Kind);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(attachment.Bytes, 0, 5));

        using var pdf = PdfDocument.Open(attachment.Bytes);
        Assert.True(pdf.NumberOfPages >= 1);
    }

    [Fact]
    public void APrintEraDocumentBecomesASearchableScan()
    {
        // Program Z is the ZEPHYR family, in service from 1988: its manuals were printed, so
        // what a technical data system holds is a scan of one.
        //
        // A *superseded* issue, though — not the document as it stands. Whatever the current
        // issue looked like when it was first printed, what you open today is the digital
        // publication; the paper in the archive is the issue that was replaced. So this asks
        // the dataset for issues rather than documents, and takes one that is not the latest.
        // See IllustrationPlanner.IsScanned, which is where that rule lives.
        var document = Dataset.Issues.First(d =>
            !d.IsLatest && d.Envelope.DocType == DocType.AMM
                        && d.Envelope.Program == ProgramLetter.Z);

        var attachment = Assert.Single(Builder.Build(document).Files);
        Assert.Equal(AttachmentKind.ScannedPublication, attachment.Kind);

        // A superseded issue is qualified by its letter, so it can sit beside the current one.
        Assert.Equal($"{document.Envelope.DocId}-{document.Issue}.pdf", attachment.FileName);

        using var pdf = PdfDocument.Open(attachment.Bytes);
        Assert.True(pdf.NumberOfPages >= 1);

        var first = pdf.GetPage(1);

        // The scan itself: one image, covering the page.
        var image = Assert.Single(first.GetImages());
        Assert.True(image.WidthInSamples > 1000, "the page image is not a scan-resolution raster");

        // And the words behind it. A title is the safest thing to look for — it is on page one
        // of every publication and is not hyphenated or split across lines.
        var words = first.GetWords().Select(w => w.Text).ToList();
        Assert.NotEmpty(words);
        Assert.Contains(document.Envelope.Title.Split(' ')[0], words);
    }

    /// <summary>
    /// Every document renders to something, with one exception that is deliberate: a figure is
    /// nothing but its drawing, so a figure for an aircraft Aeralis publishes no sheet for
    /// produces no attachment rather than a page with a hole in it. A publication is not
    /// affected — it renders without an aircraft figure.
    /// </summary>
    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void EveryTypeProducesSomething(string docId)
    {
        var loaded = Dataset.ById(docId);
        var attachments = Builder.Build(loaded).Files;

        var drawable = AircraftMapping.Primary(loaded.Envelope.Applicability?.AircraftTypes ?? []) is not null;
        if (loaded.Envelope.DocType == DocType.IPC && !drawable)
        {
            Assert.Empty(attachments);
            return;
        }

        Assert.NotEmpty(attachments);
        Assert.All(attachments, a => Assert.True(a.Bytes.Length > 4096,
            $"{a.FileName} is {a.Bytes.Length} bytes, which is too small to be a rendered document"));
    }

    /// <summary>
    /// The generation is seeded from the document's own identifier, so the same document must
    /// render to the same drawing twice.
    /// </summary>
    /// <remarks>
    /// The illustration is what is asserted on, not the PDF: a PDF carries its own creation
    /// metadata and will differ byte for byte between two runs even when every mark on the page
    /// is identical. A PNG carries nothing but the pixels.
    /// </remarks>
    [Fact]
    public void TheSameDocumentDrawsTheSamePictureTwice()
    {
        var figure = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.IPC && d.Envelope.Program == ProgramLetter.M);

        var first = Assert.Single(Builder.Build(figure).Files);
        var second = Assert.Single(Builder.Build(figure).Files);

        Assert.Equal(first.Sha256, second.Sha256);
    }

    /// <summary>
    /// And two different documents must not: a constant seed would be just as reproducible and
    /// would make every drawing the same one.
    /// </summary>
    [Fact]
    public void TwoDocumentsDrawDifferentPictures()
    {
        var figures = Dataset.Documents
            .Where(d => d.Envelope.DocType == DocType.IPC && d.Envelope.Program == ProgramLetter.M)
            .Take(2)
            .ToList();

        var builder = Builder;
        var first = Assert.Single(builder.Build(figures[0]).Files);
        var second = Assert.Single(builder.Build(figures[1]).Files);

        Assert.NotEqual(first.Sha256, second.Sha256);
    }
}
