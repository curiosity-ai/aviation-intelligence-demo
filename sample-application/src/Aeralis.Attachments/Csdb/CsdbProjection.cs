using System.Globalization;
using System.Xml;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using Aeralis.Presentation;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>
/// Projects a dataset document into the S1000D CSDB object that represents it, so it can be
/// laid out by <see cref="S1000DPresentation"/> and rendered to PDF.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is not S1000D — it is a document store shaped around MOD pre-analysis, with
/// prose in a <c>sections</c> map and structure in <c>type_data</c>. S1000D is the publication
/// format the same content would be issued in, and the two line up type by type: a maintenance
/// task is a procedural data module, a fault isolation procedure is a fault data module, a
/// service bulletin is an SB data module, a flight-crew entry is a crew data module. The
/// projection is that mapping, and nothing more: every value in the output comes from the
/// document, and nothing about the document is invented here.
/// </para>
/// <para>
/// The one exception is the wire list of a wiring sheet, which is derived rather than copied —
/// see <see cref="BuildWiringData"/> for why and how.
/// </para>
/// </remarks>
public static partial class CsdbProjection
{
    /// <summary>Aeralis' NATO commercial and government entity code, used throughout the samples.</summary>
    public const string EnterpriseCode = "U8025";

    /// <summary>The name printed as the publisher.</summary>
    public const string EnterpriseName = "AERALIS S.A.S.";

    /// <summary>The issue letter of a document, defaulting to A for one that declares none.</summary>
    public static string IssueOf(TechDocument document) => document.Issue?.Letter ?? "A";

    /// <summary>Which CSDB object type a dataset document is issued as.</summary>
    public static CsdbObjectType ObjectTypeFor(DocType docType) => docType switch
    {
        DocType.AMM or DocType.SRM => CsdbObjectType.Procedure,
        DocType.SB => CsdbObjectType.ServiceBulletin,
        DocType.TSM => CsdbObjectType.FaultIsolation,
        DocType.WDM => CsdbObjectType.WiringData,
        DocType.FCOM => CsdbObjectType.Crew,
        DocType.IPC => CsdbObjectType.IllustratedPartsCatalog,
        _ => CsdbObjectType.Description,
    };

    /// <summary>The publication a document belongs to, printed at the head of every page.</summary>
    public static string PublicationTitleFor(DocType docType) => docType switch
    {
        DocType.MOD => "MODIFICATION DOSSIER",
        DocType.SB => "SERVICE BULLETIN",
        DocType.IO => "INTERNAL ORDER",
        DocType.PAWO => "MOD PRE-ANALYSIS WORK ORDER",
        DocType.AMM => "AIRCRAFT MAINTENANCE MANUAL",
        DocType.IPC => "ILLUSTRATED PARTS CATALOGUE",
        DocType.SRM => "STRUCTURAL REPAIR MANUAL",
        DocType.TSM => "TROUBLE SHOOTING MANUAL",
        DocType.WDM => "WIRING DIAGRAM MANUAL",
        DocType.FCOM => "FLIGHT CREW OPERATING MANUAL",
        _ => "TECHNICAL PUBLICATION",
    };

    /// <summary>Builds the CSDB object for a document.</summary>
    /// <param name="document">The document envelope.</param>
    /// <param name="payload">Its <c>type_data</c>, already deserialised, or null.</param>
    /// <param name="figures">The illustrations to place in the content.</param>
    public static XmlDocument Build(TechDocument document, object? payload, IReadOnlyList<FigurePlan> figures)
    {
        ArgumentNullException.ThrowIfNull(document);
        figures ??= [];

        var type = ObjectTypeFor(document.DocType);
        var info = CsdbObjectTypes.Info(type);
        var model = DataModuleCode.ModelIdentFor(document.Program);
        var code = DataModuleCode.For(document, model, payload);

        var doc = new XmlDocument();
        var root = El(doc, "dmodule");
        root.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
        root.SetAttribute("noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance",
            $"http://www.s1000d.org/S1000D_6/xml_schema_flat/{info.Schema}.xsd");
        doc.AppendChild(root);

        root.With(BuildIdentAndStatus(doc, document, code));
        root.With(BuildContent(doc, document, payload, figures));
        return doc;
    }

    // ------------------------------------------------------ identification and status

    private static XmlElement BuildIdentAndStatus(XmlDocument doc, TechDocument document, DataModuleCode code)
    {
        var issued = IssueDate(document);

        var dmCode = El(doc, "dmCode",
            "modelIdentCode", code.ModelIdentCode,
            "systemDiffCode", code.SystemDiffCode,
            "systemCode", code.SystemCode,
            "subSystemCode", code.SubSystemCode,
            "subSubSystemCode", code.SubSubSystemCode,
            "assyCode", code.AssyCode,
            "disassyCode", code.DisassyCode,
            "disassyCodeVariant", code.DisassyCodeVariant,
            "infoCode", code.InfoCode,
            "infoCodeVariant", code.InfoCodeVariant,
            "itemLocationCode", code.ItemLocationCode);

        var ident = El(doc, "dmIdent").With(
            dmCode,
            El(doc, "language", "languageIsoCode", document.Language ?? "en", "countryIsoCode", "GB"),
            El(doc, "issueInfo", "issueNumber", IssueNumber(document.Revision), "inWork", "00"));

        var addressItems = El(doc, "dmAddressItems").With(
            El(doc, "issueDate",
                "year", issued.Year.ToString(CultureInfo.InvariantCulture),
                "month", issued.Month.ToString("00", CultureInfo.InvariantCulture),
                "day", issued.Day.ToString("00", CultureInfo.InvariantCulture)),
            El(doc, "dmTitle").With(
                // techName is what the module is about — the ATA subject — and infoName what
                // it says about it, which is the document's own title.
                Text(doc, "techName", document.Ata?.Title ?? document.ProgramName ?? "Aircraft"),
                Text(doc, "infoName", document.Title)));

        var status = El(doc, "dmStatus", "issueType", IssueType(document.Revision)).With(
            El(doc, "security", "securityClassification", "01"),
            El(doc, "responsiblePartnerCompany", "enterpriseCode", EnterpriseCode)
                .With(Text(doc, "enterpriseName", EnterpriseName)),
            El(doc, "originator", "enterpriseCode", EnterpriseCode)
                .With(Text(doc, "enterpriseName", OriginatorName(document))),
            El(doc, "applic").With(
                El(doc, "displayText").With(Text(doc, "simplePara", ApplicabilityText(document)))),
            El(doc, "qualityAssurance").With(
                El(doc, "firstVerification", "verificationType", "tabtop")));

        return El(doc, "identAndStatusSection").With(
            El(doc, "dmAddress").With(ident, addressItems),
            status);
    }

    /// <summary>
    /// The issue number, which S1000D writes as three digits. The dataset spells a revision
    /// three ways depending on the document type — a letter (<c>A</c>, <c>B</c>), a number
    /// (<c>01</c>, <c>02</c>) or <c>-</c> for an original issue — and all three mean the same
    /// thing: how many times this has been issued.
    /// </summary>
    private static int Issue(string? revision)
    {
        var text = (revision ?? "").Trim().ToUpperInvariant();
        if (text.Length == 0 || text == "-") return 1;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Max(1, number);
        }
        return text.Length == 1 && char.IsAsciiLetterUpper(text[0]) ? text[0] - 'A' + 1 : 1;
    }

    private static string IssueNumber(string? revision) =>
        Issue(revision).ToString("000", CultureInfo.InvariantCulture);

    private static string IssueType(string? revision) => Issue(revision) == 1 ? "new" : "revised";

    private static DateOnly IssueDate(TechDocument document)
    {
        var dates = document.Dates;
        foreach (var candidate in new[] { dates?.Revised, dates?.Released, dates?.Created })
        {
            if (DateOnly.TryParse(candidate, CultureInfo.InvariantCulture, out var date)) return date;
        }
        return new DateOnly(2020, 1, 1);
    }

    private static string OriginatorName(TechDocument document)
    {
        var originator = document.Originator;
        var parts = new[] { originator?.Role, originator?.Site }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
        return parts.Length == 0 ? EnterpriseName : $"{EnterpriseName} — {string.Join(", ", parts)}";
    }

    /// <summary>
    /// The applicability statement, written the way the display text of an
    /// <c>applic</c> is written: what it applies to, then what it excludes.
    /// </summary>
    internal static string ApplicabilityText(TechDocument document)
    {
        var applicability = document.Applicability;
        if (applicability is null) return "All aircraft";

        var scope = applicability.Scope switch
        {
            ApplicabilityScope.ALL_PROGRAM => $"{document.ProgramName ?? "All"}, all aircraft",
            ApplicabilityScope.TYPE when applicability.AircraftTypes.Count > 0 =>
                $"{string.Join(" and ", applicability.AircraftTypes)}, all aircraft",
            ApplicabilityScope.MODEL when applicability.Models.Count > 0 =>
                string.Join(" and ", applicability.Models),
            ApplicabilityScope.MSN_LIST when applicability.Msns.Count > 0 =>
                $"MSN {string.Join(", ", applicability.Msns)}",
            ApplicabilityScope.MSN_RANGE when applicability.MsnRanges.Count > 0 =>
                "MSN " + string.Join(", ", applicability.MsnRanges.Select(r => $"{r.From} to {r.To}")),
            ApplicabilityScope.OPERATOR when applicability.Operators.Count > 0 =>
                $"Aircraft operated by {string.Join(", ", applicability.Operators)}",
            _ when applicability.AircraftTypes.Count > 0 =>
                $"{string.Join(" and ", applicability.AircraftTypes)}, all aircraft",
            _ => "All aircraft",
        };

        var qualifiers = new List<string>();
        if (applicability.ExcludedMsns.Count > 0)
        {
            qualifiers.Add($"except MSN {string.Join(", ", applicability.ExcludedMsns)}");
        }
        if (applicability.EngineTypes.Count > 0)
        {
            qualifiers.Add($"engine {string.Join(" or ", applicability.EngineTypes)}");
        }

        var condition = applicability.EmbodimentCondition;
        if (condition?.PostMod.Count > 0) qualifiers.Add($"post {string.Join(", ", condition.PostMod)}");
        if (condition?.PreMod.Count > 0) qualifiers.Add($"pre {string.Join(", ", condition.PreMod)}");
        if (condition?.PostSb.Count > 0) qualifiers.Add($"post {string.Join(", ", condition.PostSb)}");
        if (condition?.PreSb.Count > 0) qualifiers.Add($"pre {string.Join(", ", condition.PreSb)}");

        return qualifiers.Count == 0 ? scope : $"{scope}, {string.Join(", ", qualifiers)}";
    }

    // ------------------------------------------------------------------------ content

    private static XmlElement BuildContent(XmlDocument doc, TechDocument document, object? payload,
        IReadOnlyList<FigurePlan> figures)
    {
        var content = El(doc, "content");

        XmlElement body = document.DocType switch
        {
            DocType.AMM => BuildProcedure(doc, document, payload as AmmTaskData, figures),
            DocType.SRM => BuildRepairProcedure(doc, document, payload as SrmRepairData, figures),
            DocType.SB => BuildServiceBulletin(doc, document, payload as ServiceBulletinData, figures),
            DocType.TSM => BuildFaultIsolation(doc, document, payload as TsmProcedureData, figures),
            DocType.WDM => BuildWiringData(doc, document, payload as WdmSheetData, figures),
            DocType.FCOM => BuildCrew(doc, document, payload as FcomEntryData, figures),
            _ => BuildDescription(doc, document, payload, figures),
        };

        return content.With(body);
    }

    // -------------------------------------------------------------------- shared parts

    /// <summary>A figure, ready to drop into whatever container the schema allows one in.</summary>
    internal static XmlElement Figure(XmlDocument doc, FigurePlan figure) =>
        El(doc, "figure").With(
            Text(doc, "title", figure.Caption),
            El(doc, "graphic", "infoEntityIdent", figure.Icn));

    /// <summary>A paragraph per sentence group, so a long section is not one wall of text.</summary>
    internal static IEnumerable<XmlElement> Paragraphs(XmlDocument doc, string? text, int sentencesPerParagraph = 4)
    {
        var sentences = Sentences(text);
        if (sentences.Count == 0) yield break;

        for (var i = 0; i < sentences.Count; i += sentencesPerParagraph)
        {
            yield return Text(doc, "para", string.Join(" ", sentences.Skip(i).Take(sentencesPerParagraph)));
        }
    }

    /// <summary>A two-column table of name/value rows, for the data blocks each type carries.</summary>
    internal static XmlElement? DataTable(XmlDocument doc, string title, IEnumerable<(string Name, string? Value)> rows)
    {
        var kept = rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToList();
        if (kept.Count == 0) return null;

        var body = El(doc, "tbody");
        foreach (var (name, value) in kept)
        {
            body.With(El(doc, "row").With(
                El(doc, "entry").With(Text(doc, "para", name)),
                El(doc, "entry").With(Text(doc, "para", value))));
        }

        return El(doc, "table").With(
            Text(doc, "title", title),
            El(doc, "tgroup", "cols", "2").With(
                El(doc, "colspec", "colname", "c1", "colwidth", "1*"),
                El(doc, "colspec", "colname", "c2", "colwidth", "2.2*"),
                body));
    }

    /// <summary>A CALS table from a header row and a body.</summary>
    internal static XmlElement Table(XmlDocument doc, string title, IReadOnlyList<string> headers,
        IReadOnlyList<string> widths, IEnumerable<IReadOnlyList<string>> rows)
    {
        var group = El(doc, "tgroup", "cols", headers.Count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < headers.Count; i++)
        {
            group.With(El(doc, "colspec", "colname", $"c{i + 1}", "colwidth", widths[i]));
        }

        var head = El(doc, "row");
        foreach (var header in headers) head.With(Text(doc, "entry", header));
        group.With(El(doc, "thead").With(head));

        var body = El(doc, "tbody");
        foreach (var row in rows)
        {
            var element = El(doc, "row");
            foreach (var cell in row) element.With(Text(doc, "entry", cell));
            body.With(element);
        }
        group.With(body);

        return El(doc, "table").With(Text(doc, "title", title), group);
    }

    /// <summary>A number as it should be printed — no trailing zeros on a whole value.</summary>
    internal static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>An enum value as prose: <c>NOT_INTERCHANGEABLE</c> → "Not interchangeable".</summary>
    internal static string Words<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var text = value.ToString()!.Replace('_', ' ').ToLowerInvariant();
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
