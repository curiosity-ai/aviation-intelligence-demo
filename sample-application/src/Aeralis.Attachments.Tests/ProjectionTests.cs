using System.Xml;
using Aeralis.Attachments.Csdb;
using Aeralis.Dataset;
using Aeralis.Presentation;

namespace Aeralis.Attachments.Tests;

/// <summary>
/// The S1000D projection: every document type must come out as an object the presentation
/// layer recognises from its own content and can lay out.
/// </summary>
public class ProjectionTests
{
    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void ProjectsToTheObjectTypeItIsIssuedAs(string docId)
    {
        var loaded = Dataset.ById(docId);
        var expected = CsdbProjection.ObjectTypeFor(loaded.Envelope.DocType);

        XmlDocument csdb = CsdbProjection.Build(loaded.Envelope, loaded.Payload, []);

        // Detected from the object, not from what we said it was: a projection that produces
        // the wrong content element would still render, just through the wrong stylesheet.
        Assert.Equal(expected, S1000DPresentation.DetectObjectType(csdb));
    }

    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void TransformsToWellFormedFo(string docId)
    {
        var loaded = Dataset.ById(docId);
        XmlDocument csdb = CsdbProjection.Build(loaded.Envelope, loaded.Payload, []);

        XmlDocument fo = S1000DPresentation.TransformToFo(csdb);

        Assert.Equal("root", fo.DocumentElement?.LocalName);
        Assert.NotEmpty(fo.GetElementsByTagName("page-sequence", "http://www.w3.org/1999/XSL/Format"));
    }

    [Theory]
    [MemberData(nameof(Dataset.OnePerTypeIds), MemberType = typeof(Dataset))]
    public void CarriesTheDocumentsOwnIdentification(string docId)
    {
        var loaded = Dataset.ById(docId);
        var document = loaded.Envelope;

        XmlDocument csdb = CsdbProjection.Build(document, loaded.Payload, []);

        var code = csdb.GetElementsByTagName("dmCode").OfType<XmlElement>().First();
        var ata6d = (document.Ata?.Ata6d ?? "").Replace("-", "");

        Assert.Equal(ata6d[..2], code.GetAttribute("systemCode"));
        Assert.Equal(ata6d.Substring(2, 1), code.GetAttribute("subSystemCode"));
        Assert.Equal(ata6d.Substring(3, 1), code.GetAttribute("subSubSystemCode"));
        Assert.Equal(ata6d.Substring(4, 2), code.GetAttribute("assyCode"));
        Assert.Equal(DataModuleCode.ModelIdentFor(document.Program), code.GetAttribute("modelIdentCode"));

        var title = csdb.GetElementsByTagName("infoName").OfType<XmlElement>().First();
        Assert.Equal(document.Title, title.InnerText);
    }

    [Fact]
    public void PlacesTheFiguresItIsGiven()
    {
        // A modification: descriptive, and the type the demo is built around.
        var loaded = Dataset.Documents.First(d => d.Envelope.DocType == DocType.MOD);
        var figures = new[]
        {
            new FigurePlan("ICN-TEST-00001", "First figure", Illustrations.ViewKind.Side, [], false),
            new FigurePlan("ICN-TEST-00002", "Second figure", Illustrations.ViewKind.Plan, [], false),
        };

        XmlDocument csdb = CsdbProjection.Build(loaded.Envelope, loaded.Payload, figures);

        var graphics = csdb.GetElementsByTagName("graphic").OfType<XmlElement>()
            .Select(g => g.GetAttribute("infoEntityIdent"))
            .ToList();

        Assert.Equal(["ICN-TEST-00001", "ICN-TEST-00002"], graphics);
    }

    [Fact]
    public void EveryDocumentTypeIsProjected()
    {
        // A type with no projection would silently fall through to the descriptive one, so
        // the mapping is asserted rather than left to the switch's default.
        Assert.Equal(10, Dataset.OnePerType.Count);
        foreach (var type in Enum.GetValues<DocType>().Where(t => t != DocType.Unknown))
        {
            Assert.Contains(Dataset.OnePerType, d => d.Envelope.DocType == type);
        }
    }
}
