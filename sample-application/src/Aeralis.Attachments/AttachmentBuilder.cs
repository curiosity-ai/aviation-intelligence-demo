using System.Security.Cryptography;
using System.Xml;
using SkiaSharp;
using Aeralis.Attachments.Csdb;
using Aeralis.Attachments.Pdf;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using Aeralis.Illustrations;
using Aeralis.Presentation;

namespace Aeralis.Attachments;

/// <summary>What kind of file an attachment is.</summary>
public enum AttachmentKind
{
    /// <summary>The document rendered as an S1000D publication.</summary>
    Publication,

    /// <summary>The document rendered as a scan of a printed publication, text still searchable.</summary>
    ScannedPublication,

    /// <summary>A figure laid out as the full manual page it is printed on.</summary>
    IllustrationPage,

    /// <summary>
    /// One annotated view placed inside a publication — the file the CSDB object's
    /// <c>graphic</c> element names by its ICN.
    /// </summary>
    Illustration,
}

/// <summary>One generated file, ready to be written out or uploaded.</summary>
/// <param name="FileName">The name it is stored under. Unique across the dataset.</param>
/// <param name="MediaType">Its content type.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Title">A human-readable title, for the attachment's own node.</param>
/// <param name="Bytes">The file.</param>
public sealed record Attachment(string FileName, string MediaType, AttachmentKind Kind, string Title, byte[] Bytes)
{
    /// <summary>SHA-256 of the content, so an unchanged file is not uploaded twice.</summary>
    public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Bytes));
}

/// <summary>
/// What a document was rendered *from*: the CSDB object the presentation layer was handed, and
/// the illustrations it referenced.
/// </summary>
/// <remarks>
/// The published PDF is the output; this is the input, and keeping it is what makes the output
/// reproducible and reviewable. An author who disagrees with a rendered page needs to see the
/// markup that produced it, and the ICN files it named, without re-running anything.
/// </remarks>
/// <param name="SourceId">Its identity: the document type, identifier and issue together.</param>
/// <param name="DocType">The dataset document type it was projected from.</param>
/// <param name="DocId">The document it was projected from.</param>
/// <param name="Issue">Which issue of that document.</param>
/// <param name="ObjectType">The S1000D CSDB object type it was projected to.</param>
/// <param name="Schema">The S1000D schema that object belongs to, without the suffix.</param>
/// <param name="DataModuleCode">The printed data module code.</param>
/// <param name="Xml">The source itself, indented.</param>
/// <param name="Images">The illustrations the source references, by ICN.</param>
public sealed record CsdbSource(
    string SourceId,
    string DocType,
    string DocId,
    string Issue,
    string ObjectType,
    string Schema,
    string DataModuleCode,
    string Xml,
    IReadOnlyList<Attachment> Images)
{
    /// <summary>The file name the source is written under.</summary>
    public string FileName => $"{DocId}-{Issue}.xml";
}

/// <summary>Everything one document produced.</summary>
/// <param name="Files">
/// The published files, which hang off the document itself — the PDF, or the manual page a
/// figure is printed on.
/// </param>
/// <param name="Source">
/// What it was rendered from, or null for a figure, which is a drawing and has no CSDB object
/// behind it.
/// </param>
public sealed record DocumentRendering(IReadOnlyList<Attachment> Files, CsdbSource? Source)
{
    /// <summary>Nothing was produced — the aircraft has no published drawing to work from.</summary>
    public static DocumentRendering None { get; } = new([], null);
}

/// <summary>
/// Builds the attachments for a document: the illustrations, the publication they go into, and
/// the scan of it where the aircraft is old enough that a scan is what would exist.
/// </summary>
/// <remarks>
/// <para>
/// Which document gets what:
/// </para>
/// <list type="bullet">
/// <item>
/// An <b>IPC figure</b> is a figure. It gets the drawing laid out as the A4 manual page it is
/// printed on — heading, figure caption, effectivity box, document number — and nothing else,
/// because the page <em>is</em> the artefact.
/// </item>
/// <item>
/// Every <b>other document type</b> gets the S1000D publication it would be issued as, rendered
/// to PDF with one or two annotated illustrations placed in its content.
/// </item>
/// <item>
/// A publication for an aircraft that entered service <b>before 2000</b> is additionally
/// rebuilt as a searchable scan, and the clean PDF is not kept: what a technical data system
/// holds for a manual of that age is the scan.
/// </item>
/// </list>
/// <para>
/// Everything is deterministic. Illustration randomness, callout choice and the scan's grain
/// are all seeded from the document's own identifier, so a document produces the same drawing
/// and the same scan on every run and on every machine. The PDF bytes themselves are not
/// promised to be identical — the renderer stamps its own metadata — but their content is.
/// </para>
/// </remarks>
public sealed class AttachmentBuilder
{
    /// <summary>Width the in-publication illustrations are drawn at, in pixels.</summary>
    private const int FigureWidth = 1500;

    private readonly IllustrationPlanner _planner;
    private readonly string _workDirectory;
    private readonly Lazy<string?> _logoModern;
    private readonly Dictionary<LogoEra, string> _logos = [];
    private readonly Lock _logoLock = new();

    /// <param name="reference">The reference data, for the zone and ATA placement tables.</param>
    /// <param name="workDirectory">
    /// Where the ICN files and the publisher marks are written. The renderer resolves graphics
    /// from a directory on disk, so they have to exist as files while a page is laid out.
    /// </param>
    public AttachmentBuilder(ReferenceData reference, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        _planner = new IllustrationPlanner(reference);
        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);
        _logoModern = new Lazy<string?>(() => LogoFile(LogoEra.Modern));
    }

    /// <summary>Builds everything one issue of a document produces.</summary>
    /// <remarks>
    /// One issue, not one document: a document file holds an array of them and each was
    /// published in its own right, so each has its own markup, artwork and PDF. The issue the
    /// document is currently at keeps the plain file name; a superseded one carries its letter,
    /// so the two sit side by side without one overwriting the other.
    /// </remarks>
    public DocumentRendering Build(LoadedDocument loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var document = loaded.Envelope;

        return document.DocType == DocType.IPC
            ? BuildFigurePage(document, loaded.Payload as IpcFigureData, loaded.IsLatest)
            : BuildPublication(document, loaded.Payload, loaded.IsLatest);
    }

    /// <summary>
    /// What a published file is called. The current issue gets the document's own name, which
    /// is what a reader following a link expects; a superseded issue is qualified by its letter.
    /// </summary>
    private static string FileName(TechDocument document, bool isLatest, string extension) =>
        isLatest
            ? $"{Safe(document.DocId)}.{extension}"
            : $"{Safe(document.DocId)}-{CsdbProjection.IssueOf(document)}.{extension}";

    // ----------------------------------------------------------------- figure pages

    private DocumentRendering BuildFigurePage(TechDocument document, IpcFigureData? figure,
        bool isLatest)
    {
        var request = _planner.PlanFigurePage(document, figure, isLatest);
        if (request is null) return DocumentRendering.None;

        using var page = AircraftIllustrator.DrawDocumentPage(request);
        var bytes = Encode(page, SKEncodedImageFormat.Png, 95);

        // No source: a figure is a drawing. There is no CSDB object behind it to keep.
        return new DocumentRendering(
            [
                new Attachment(
                    FileName: FileName(document, isLatest, "png"),
                    MediaType: "image/png",
                    Kind: AttachmentKind.IllustrationPage,
                    Title: $"{document.DocId} — {figure?.FigureTitle ?? document.Title}",
                    Bytes: bytes),
            ],
            Source: null);
    }

    // ----------------------------------------------------------------- publications

    private DocumentRendering BuildPublication(TechDocument document, object? payload,
        bool isLatest)
    {
        var plan = _planner.Plan(document, isLatest);

        // Each document draws into its own directory. Two documents on the same ATA chapter
        // mint the same ICN — the code is derived from the chapter — so a shared directory
        // would have them overwrite each other's graphics, and worse, race while doing it.
        var graphicsDirectory = Path.Combine(_workDirectory, "icn",
            $"{Safe(document.DocId)}-{CsdbProjection.IssueOf(document)}");
        var graphics = WriteGraphics(document, plan, graphicsDirectory);

        try
        {
            XmlDocument csdb = CsdbProjection.Build(document, payload, plan.Figures);
            var options = Options(document, plan, graphicsDirectory, graphics.Count);

            byte[] pdf;
            using (var rendered = S1000DPresentation.RenderToPdf(csdb, options))
            using (var buffer = new MemoryStream())
            {
                rendered.CopyTo(buffer);
                pdf = buffer.ToArray();
            }

            var source = Source(document, payload, csdb, graphics);

            if (!plan.Scanned || !ScannedPdfBuilder.HasPages(pdf))
            {
                return new DocumentRendering(
                    [
                        new Attachment(FileName(document, isLatest, "pdf"), "application/pdf",
                            AttachmentKind.Publication, PublicationTitle(document), pdf),
                    ],
                    source);
            }

            var scanned = ScannedPdfBuilder.Build(pdf, Seeded.Seed(document.DocId, "scan"));
            return new DocumentRendering(
                [
                    new Attachment(FileName(document, isLatest, "pdf"), "application/pdf",
                        AttachmentKind.ScannedPublication, PublicationTitle(document), scanned),
                ],
                source);
        }
        finally
        {
            try
            {
                if (Directory.Exists(graphicsDirectory)) Directory.Delete(graphicsDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Left behind. It is under the work directory, which is disposable.
            }
        }
    }

    /// <summary>
    /// The CSDB object and the illustrations it names, kept as the record of what the
    /// publication was rendered from.
    /// </summary>
    private static CsdbSource Source(TechDocument document, object? payload, XmlDocument csdb,
        IReadOnlyList<Attachment> images)
    {
        var type = CsdbProjection.ObjectTypeFor(document.DocType);
        var info = CsdbObjectTypes.Info(type);
        var model = DataModuleCode.ModelIdentFor(document.Program);

        return new CsdbSource(
            SourceId: SourceIdFor(document.DocType, document.DocId, CsdbProjection.IssueOf(document)),
            DocType: document.DocType.ToString(),
            DocId: document.DocId,
            Issue: CsdbProjection.IssueOf(document),
            ObjectType: type.ToString(),
            Schema: info.Schema,
            DataModuleCode: DataModuleCode.For(document, model, payload).ToString(),
            Xml: Indent(csdb),
            Images: images);
    }

    /// <summary>
    /// The identity of a source: the document type, the identifier and the issue together. The
    /// identifier alone does not say which type it is — a service bulletin is numbered
    /// <c>MRD-53-1042</c> and nothing in that says "SB" — and a document has one source per
    /// issue, because each issue was rendered from its own markup.
    /// </summary>
    public static string SourceIdFor(DocType docType, string docId, string issue) =>
        $"{docType}:{docId}:{issue}";



    /// <summary>The CSDB object as text, indented — it is kept to be read.</summary>
    private static string Indent(XmlDocument csdb)
    {
        var buffer = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = System.Text.Encoding.UTF8,
        }))
        {
            csdb.Save(writer);
        }
        return buffer.ToString();
    }

    /// <summary>
    /// A <see cref="StringWriter"/> that declares itself UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> writes the encoding of the <em>writer</em> into the declaration,
    /// not the one in its settings, and a string is UTF-16 in memory — so the default writes
    /// <c>encoding="utf-16"</c> onto text that is then stored and saved as UTF-8. The
    /// declaration would be a lie, and any reader that honours it would decode the file wrongly.
    /// </remarks>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }

    /// <summary>
    /// Draws the plan's figures and writes them where the renderer will find them. A
    /// <c>graphic</c> names an ICN, not a file, so the file has to be named for the ICN.
    /// </summary>
    private static List<Attachment> WriteGraphics(TechDocument document, IllustrationPlan plan, string directory)
    {
        var written = new List<Attachment>();
        if (plan.Aircraft is not { } aircraft || plan.Figures.Count == 0) return written;

        Directory.CreateDirectory(directory);

        foreach (var figure in plan.Figures)
        {
            var request = new IllustrationRequest
            {
                Aircraft = aircraft,
                View = figure.View,
                Annotations = figure.Annotations,
                ShowDimensions = figure.ShowDimensions,
                Width = FigureWidth,
                Seed = Seeded.Seed(document.DocId, figure.Icn),
                // The publication carries its own typography; the drawing is set in the modern
                // face either way, because the scan is applied to the finished page and would
                // otherwise be applied to type that already looks scanned.
                TextStyle = Aeralis.Illustrations.TextStyle.Modern,
            };

            using var bitmap = AircraftIllustrator.Draw(request);
            var bytes = Encode(bitmap, SKEncodedImageFormat.Png, 92);

            File.WriteAllBytes(Path.Combine(directory, $"{figure.Icn}.png"), bytes);
            written.Add(new Attachment($"{figure.Icn}.png", "image/png",
                AttachmentKind.Illustration, figure.Caption, bytes));
        }

        return written;
    }

    private PresentationOptions Options(TechDocument document, IllustrationPlan plan,
        string graphicsDirectory, int graphicsCount)
    {
        // The mark of the aircraft's own era, the same choice the illustrator makes for a
        // drawn page — an ZEPHYR-22 publication carries the Aeralis Industrie lock-up and an MERIDIAN
        // one the current wordmark.
        var era = plan.Aircraft is { } aircraft ? AeralisMark.EraFor(aircraft) : LogoEra.Modern;
        var logo = plan.Aircraft is null ? _logoModern.Value : LogoFileCached(era);

        // The monospaced face belongs to the era the publication was output in, not to whether
        // this particular issue is being scanned: an ZEPHYR-22 manual page reads as typewritten
        // whether you are looking at the archived paper copy or at today's issue of it.
        var typewritten = plan.Aircraft is { } printEra && AeralisMark.IsPrintEra(printEra);

        return new PresentationOptions
        {
            Publisher = CsdbProjection.EnterpriseName,
            PublisherLogoPath = logo,
            PublisherLogoHeightMm = era == LogoEra.Modern ? 5.0 : 8.0,
            PublicationTitle = CsdbProjection.PublicationTitleFor(document.DocType),
            FontFamily = typewritten ? "Courier" : "Helvetica",
            FontSizePt = typewritten ? 8.0 : 9,
            GraphicsDirectories = graphicsCount > 0 ? [graphicsDirectory] : [],
            Watermark = Watermark(document),
        };
    }

    /// <summary>
    /// A draft watermark where the document says it is one. Nothing else is watermarked:
    /// a released manual page does not carry one.
    /// </summary>
    private static string? Watermark(TechDocument document) =>
        document.Status?.ToUpperInvariant() switch
        {
            "DRAFT" => "DRAFT",
            "IN_WORK" or "IN WORK" => "IN WORK",
            "CANCELLED" => "CANCELLED",
            "SUPERSEDED" => "SUPERSEDED",
            _ => null,
        };

    private static string PublicationTitle(TechDocument document) =>
        $"{document.DocId} issue {CsdbProjection.IssueOf(document)} — {document.Title}";

    // ------------------------------------------------------------------- publisher mark

    private string? LogoFileCached(LogoEra era)
    {
        lock (_logoLock)
        {
            if (_logos.TryGetValue(era, out var cached)) return cached;

            var file = LogoFile(era);
            if (file is not null) _logos[era] = file;
            return file;
        }
    }

    /// <summary>Writes the era's full lock-up out as a PNG for the header to reference.</summary>
    private string? LogoFile(LogoEra era)
    {
        // 300 px tall covers the 8 mm the header asks for at well over print resolution.
        var png = AeralisMark.RenderPng(era, 300);
        if (png.Length == 0) return null;

        var path = Path.Combine(_workDirectory, $"publisher-{era.ToString().ToLowerInvariant()}.png");
        File.WriteAllBytes(path, png);
        return path;
    }

    // --------------------------------------------------------------------------- misc

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>A document identifier as a file name. Every id in the dataset is already safe.</summary>
    private static string Safe(string docId) =>
        string.Concat(docId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
