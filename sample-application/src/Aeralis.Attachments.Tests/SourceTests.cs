using System.Xml;
using Aeralis.Attachments.Csdb;
using Aeralis.Dataset;
using Aeralis.Presentation;

namespace Aeralis.Attachments.Tests;

/// <summary>
/// What a publication was rendered from is kept alongside it: the CSDB object as text, and the
/// illustrations it names. This is what makes a rendered page explainable without re-running
/// anything, so it has to be complete — the same markup that produced the PDF, and every ICN
/// the markup references.
/// </summary>
public class SourceTests : IDisposable
{
    private readonly string _work =
        Path.Combine(Path.GetTempPath(), "aeralis-source-tests", Guid.NewGuid().ToString("N"));

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

    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void EveryPublicationKeepsItsSource(string docId)
    {
        var loaded = Dataset.ById(docId);
        var rendering = Builder.Build(loaded);

        if (loaded.Envelope.DocType == DocType.IPC)
        {
            Assert.Null(rendering.Source);
            return;
        }

        var source = Assert.IsType<CsdbSource>(rendering.Source);

        Assert.Equal($"{loaded.Envelope.DocType}:{loaded.Envelope.DocId}:{loaded.Issue}",
            source.SourceId);
        Assert.Equal(loaded.Envelope.DocId, source.DocId);
        Assert.Equal(loaded.Issue, source.Issue);
        Assert.Equal(CsdbProjection.ObjectTypeFor(loaded.Envelope.DocType).ToString(), source.ObjectType);
        Assert.StartsWith("DMC-", source.DataModuleCode);
        Assert.NotEmpty(source.Xml);

        // The declaration has to match how the text is stored and saved, or a reader that
        // honours it decodes the file wrongly.
        Assert.Contains("encoding=\"utf-8\"", source.Xml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The kept source has to be the markup that was actually rendered — text that merely looks
    /// like it would be worse than nothing, because it would be trusted.
    /// </summary>
    [Fact]
    public void TheSourceReparsesAsTheObjectThatWasRendered()
    {
        var loaded = Dataset.Documents.First(d => d.Envelope.DocType == DocType.SB);
        var source = Assert.IsType<CsdbSource>(Builder.Build(loaded).Source);

        var reparsed = new XmlDocument();
        reparsed.LoadXml(source.Xml);

        Assert.Equal(CsdbObjectType.ServiceBulletin, S1000DPresentation.DetectObjectType(reparsed));
        Assert.Equal(loaded.Envelope.Title,
            reparsed.GetElementsByTagName("infoName").OfType<XmlElement>().First().InnerText);
    }

    /// <summary>
    /// Every ICN the source names must be one of the images kept with it, and the other way
    /// round: a graphic with no file renders as a placeholder frame, and a file no graphic
    /// names is dead weight in the graph.
    /// </summary>
    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void TheImagesAreExactlyTheOnesTheSourceNames(string docId)
    {
        var loaded = Dataset.ById(docId);
        if (loaded.Envelope.DocType == DocType.IPC) return;

        var source = Assert.IsType<CsdbSource>(Builder.Build(loaded).Source);

        var reparsed = new XmlDocument();
        reparsed.LoadXml(source.Xml);

        var named = reparsed.GetElementsByTagName("graphic").OfType<XmlElement>()
            .Select(g => g.GetAttribute("infoEntityIdent") + ".png")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var kept = source.Images.Select(i => i.FileName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(named, kept);
        Assert.All(source.Images, i => Assert.Equal(AttachmentKind.Illustration, i.Kind));
        Assert.All(source.Images, i => Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], i.Bytes[..4]));
    }

    /// <summary>
    /// An information control number identifies a graphic across the whole CSDB, so two
    /// documents must never name the same one.
    /// </summary>
    /// <remarks>
    /// The regression this guards: the model and system fields of an ICN come from the ATA
    /// chapter, and a modification and its pre-analysis work order always share a chapter. With
    /// only a figure index after them, both named the same ICN and the second file written
    /// replaced the first.
    /// </remarks>
    [Fact]
    public void TwoDocumentsOnOneChapterNameDifferentIllustrations()
    {
        var mod = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.MOD && d.Envelope.Program == ProgramLetter.M);
        var pawo = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.PAWO && d.Envelope.Ata?.Ata6d == mod.Envelope.Ata?.Ata6d);

        var builder = Builder;
        var first = Assert.IsType<CsdbSource>(builder.Build(mod).Source);
        var second = Assert.IsType<CsdbSource>(builder.Build(pawo).Source);

        Assert.Equal(first.DocId is not null, second.DocId is not null);
        Assert.NotEmpty(first.Images);
        Assert.NotEmpty(second.Images);
        Assert.Empty(first.Images.Select(i => i.FileName)
            .Intersect(second.Images.Select(i => i.FileName), StringComparer.Ordinal));
    }

    /// <summary>
    /// Two issues of one document are two publications. Each was rendered from its own markup
    /// and must name its own artwork.
    /// </summary>
    /// <remarks>
    /// The regression this guards is the same collision as between two documents, one level
    /// down: with the ICN keyed on the document alone, both issues named the same graphic and
    /// the second file written replaced the first — so the superseded issue silently displayed
    /// the current issue's illustration.
    /// </remarks>
    [Fact]
    public void TwoIssuesOfOneDocumentAreRenderedSeparately()
    {
        var history = Dataset.Issues
            .Where(i => i.Envelope.DocType != DocType.IPC)
            .GroupBy(i => i.Envelope.DocId)
            .First(g => g.Count() > 1)
            .ToList();

        var builder = Builder;
        var sources = history.Select(i => Assert.IsType<CsdbSource>(builder.Build(i).Source)).ToList();

        // Distinct identities, distinct markup, distinct artwork.
        Assert.Equal(sources.Count, sources.Select(s => s.SourceId).Distinct().Count());
        Assert.Equal(sources.Count, sources.Select(s => s.Xml).Distinct().Count());

        var names = sources.SelectMany(s => s.Images).Select(i => i.FileName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());

        // And the published file: the current issue keeps the document's own name, a
        // superseded one carries its letter, so neither overwrites the other.
        var files = history.SelectMany(i => builder.Build(i).Files).Select(f => f.FileName).ToList();
        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains($"{history[0].Envelope.DocId}.pdf", files);
    }

    /// <summary>
    /// A document for an aircraft with a published sheet carries at least one illustration —
    /// the whole point of the projection is that a publication is not a wall of text.
    /// </summary>
    [Fact]
    public void APublicationCarriesItsIllustrations()
    {
        var loaded = Dataset.Documents.First(d =>
            d.Envelope.DocType == DocType.MOD && d.Envelope.Program == ProgramLetter.M);

        var source = Assert.IsType<CsdbSource>(Builder.Build(loaded).Source);

        Assert.NotEmpty(source.Images);
        Assert.Equal(source.Images.Count, source.Images.Select(i => i.FileName).Distinct().Count());
    }
}
