using System.Xml;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>
/// The descriptive projection, used for the three document types that describe rather than
/// instruct: the modification dossier, the internal order that asked for it and the
/// pre-analysis work order that assesses it.
/// </summary>
public static partial class CsdbProjection
{
    private static XmlElement BuildDescription(XmlDocument doc, TechDocument document, object? payload,
        IReadOnlyList<FigurePlan> figures)
    {
        var description = El(doc, "description");
        var placed = 0;

        // The abstract first: on a dossier it is the summary a reader wants before the reason.
        if (!string.IsNullOrWhiteSpace(document.Abstract))
        {
            description.With(El(doc, "levelledPara").With(Text(doc, "title", "Summary"))
                .WithAll(Paragraphs(doc, document.Abstract)));
        }

        foreach (var (key, text) in document.Sections)
        {
            var section = El(doc, "levelledPara").With(Text(doc, "title", Humanise(key)))
                .WithAll(Paragraphs(doc, text));

            // The first figure goes under the first section, where a reader meets the subject;
            // the rest follow the sections they can be attached to.
            if (placed < figures.Count && placed < 1)
            {
                section.With(Figure(doc, figures[placed++]));
            }

            description.With(section);
        }

        var data = TypeDataSection(doc, document, payload);
        if (data is not null) description.With(data);

        var applicability = El(doc, "levelledPara").With(
            Text(doc, "title", "Applicability and effectivity"),
            Text(doc, "para", ApplicabilityText(document)));

        if (document.Embodiment is { } embodiment)
        {
            var table = DataTable(doc, "Embodiment", [
                ("Status", Words(embodiment.Status)),
                ("Embodiment points", string.Join(", ", embodiment.Points.Select(Words))),
                ("Production effectivity", embodiment.ProductionEffectivity),
                ("Production cut-in", embodiment.ProductionCutInDate),
                ("Retrofit through", string.Join(", ", embodiment.RetrofitVia)),
            ]);
            if (table is not null) applicability.With(table);
        }

        // The remaining figure, if there is one, sits with the applicability — the last block
        // before the references, and the one a location view reads naturally against.
        while (placed < figures.Count) applicability.With(Figure(doc, figures[placed++]));

        description.With(applicability);

        if (References(doc, document) is { } references) description.With(references);
        return description;
    }

    /// <summary>The type-specific data block, as a levelled paragraph of tables.</summary>
    private static XmlElement? TypeDataSection(XmlDocument doc, TechDocument document, object? payload) =>
        payload switch
        {
            ModData mod => ModSection(doc, mod),
            InternalOrderData io => InternalOrderSection(doc, io),
            PreAnalysisWorkOrderData pawo => PreAnalysisSection(doc, document, pawo),
            _ => null,
        };

    private static XmlElement ModSection(XmlDocument doc, ModData mod)
    {
        var section = El(doc, "levelledPara").With(Text(doc, "title", "Modification data"));

        var certification = mod.Certification;
        var table = DataTable(doc, "Modification data", [
            ("Modification number", $"MOD-{mod.ModNumber}"),
            ("Class", Words(mod.ModClass)),
            ("Change category", Words(mod.ChangeCategory)),
            ("Trigger", Words(mod.Trigger)),
            ("Customer driven", mod.CustomerDriven ? "Yes" : "No"),
            ("Certification impact", certification is null ? null : Words(certification.Impact)),
            ("Authority", certification is null ? null : string.Join(", ", certification.Authority)),
            ("Certification basis", certification is null ? null : string.Join(", ", certification.Cs25Paragraphs)),
            ("Flight test required", certification is null ? null : certification.RequiresFlightTest ? "Yes" : "No"),
            ("Mass change", $"{Num(mod.WeightImpactKg)} kg"),
            ("Balance", mod.BalanceImpact),
            ("Electrical load change", $"{Num(mod.ElectricalLoadImpactVa)} VA"),
            ("Interchangeability", Words(mod.Interchangeability)),
            ("Estimated man-hours", Num(mod.EstimatedManhours)),
            ("Estimated cost", $"{Num(mod.EstimatedCostKeur)} kEUR"),
            ("Lead time", $"{mod.LeadTimeWeeks} weeks"),
            ("Risk level", Words(mod.RiskLevel)),
        ]);
        if (table is not null) section.With(table);

        var parts = PartsTable(doc, [
            ("Added", mod.PartsAdded),
            ("Removed", mod.PartsRemoved),
            ("Modified", mod.PartsModified),
        ]);
        if (parts is not null) section.With(parts);

        if (!string.IsNullOrWhiteSpace(mod.ProductionImpact))
        {
            section.With(Text(doc, "para", mod.ProductionImpact));
        }

        return section;
    }

    private static XmlElement? PartsTable(XmlDocument doc, IReadOnlyList<(string Action, List<string> Parts)> groups)
    {
        var rows = groups
            .SelectMany(g => g.Parts.Select(p => (IReadOnlyList<string>)[p, g.Action]))
            .ToList();
        return rows.Count == 0
            ? null
            : Table(doc, "Parts affected", ["Part number", "Action"], ["1.6*", "1*"], rows);
    }

    private static XmlElement InternalOrderSection(XmlDocument doc, InternalOrderData io)
    {
        var section = El(doc, "levelledPara").With(Text(doc, "title", "Order data"));

        var table = DataTable(doc, "Order data", [
            ("Order number", io.IoNumber),
            ("Category", Words(io.RequestCategory)),
            ("Priority", Words(io.Priority)),
            ("Status", Words(io.IoStatus)),
            ("Raised by", io.RaisedBy is null ? null : Words(io.RaisedBy.RequesterType)),
            ("Operator", io.RaisedBy?.OperatorCode),
            ("Contact role", io.RaisedBy?.ContactRole),
            ("Aeralis focal", io.RaisedBy is null
                ? null
                : string.Join(", ", new[] { io.RaisedBy.OemFocalDepartment, io.RaisedBy.OemFocalSite }
                    .Where(s => !string.IsNullOrWhiteSpace(s)))),
            ("Aircraft in scope", io.FleetScope?.AircraftCount.ToString()),
            ("Funding", io.Commercial is null ? null : Words(io.Commercial.Funding)),
            ("Budget", io.Commercial is null ? null : $"{Num(io.Commercial.BudgetKeur)} kEUR"),
            ("Contract", io.Commercial?.ContractReference),
            ("Response requested by", io.TargetDates?.RequestedResponse),
            ("Embodiment requested from", io.TargetDates?.RequestedEmbodimentStart),
            ("Modification raised", io.CreatedMod),
        ]);
        if (table is not null) section.With(table);

        if (!string.IsNullOrWhiteSpace(io.BusinessJustification))
        {
            section.With(Text(doc, "para", io.BusinessJustification));
        }
        if (!string.IsNullOrWhiteSpace(io.ClosureNote))
        {
            section.With(El(doc, "note").With(Text(doc, "notePara", io.ClosureNote)));
        }

        return section;
    }

    private static XmlElement PreAnalysisSection(XmlDocument doc, TechDocument document,
        PreAnalysisWorkOrderData pawo)
    {
        var section = El(doc, "levelledPara").With(Text(doc, "title", "Work order data"));

        var assigned = pawo.Assigned;
        var table = DataTable(doc, "Work order data", [
            ("Work order", pawo.PawoNumber),
            ("Subject modification", pawo.SubjectMod),
            ("Source order", pawo.SourceIo),
            ("Status", Words(pawo.PawoStatus)),
            ("Outcome", pawo.Outcome == PawoOutcome.Unknown ? null : Words(pawo.Outcome)),
            ("Raised", pawo.RaisedDate),
            ("Due", pawo.DueDate),
            ("Completed", pawo.CompletedDate),
            ("Analysis round", pawo.AnalysisRound.ToString()),
            ("Lead department", assigned?.LeadDepartment),
            ("Lead site", assigned?.LeadSite),
            ("Contributing departments", assigned is null ? null : string.Join(", ", assigned.ContributingDepartments)),
            ("Man-hours estimated", assigned is null ? null : Num(assigned.EstimatedManhours)),
            ("Man-hours spent", assigned is null ? null : Num(assigned.SpentManhours)),
            ("Recommendation", pawo.Recommendation == Recommendation.Unknown ? null : Words(pawo.Recommendation)),
        ]);
        if (table is not null) section.With(table);

        if (pawo.Tasks.Count > 0)
        {
            section.With(Table(doc, "Analysis tasks",
                ["Task", "Title", "Method", "Owner", "Status"],
                ["0.7*", "2.4*", "0.9*", "0.8*", "0.8*"],
                pawo.Tasks.Select(t => (IReadOnlyList<string>)
                    [t.TaskId, t.Title ?? "", Words(t.Method), t.Owner ?? "—", Words(t.Status)])));
        }

        if (pawo.ExpectedFindings.Count > 0)
        {
            section.With(Table(doc, "Findings",
                ["Ref", "Document", "Type", "Severity", "Rationale"],
                ["0.6*", "1.5*", "1*", "0.8*", "3*"],
                pawo.ExpectedFindings.Select(f => (IReadOnlyList<string>)
                [
                    f.FindingId, f.DocId, Words(f.FindingType), Words(f.Severity), f.Rationale ?? "",
                ])));
        }

        if (!string.IsNullOrWhiteSpace(pawo.Conclusion))
        {
            section.With(Text(doc, "para", pawo.Conclusion));
        }

        return section;
    }

    /// <summary>
    /// The documents this one points at. Written as a table of identifiers rather than as
    /// <c>dmRef</c> elements: a reference in the dataset is a document identifier, and the
    /// data module code of the target is not knowable from here without loading it.
    /// </summary>
    private static XmlElement? References(XmlDocument doc, TechDocument document)
    {
        if (document.References.Count == 0) return null;

        return El(doc, "levelledPara")
            .With(Text(doc, "title", "Referenced documents"))
            .With(Table(doc, "Referenced documents",
                ["Document", "Type", "Relationship"],
                ["1.8*", "0.8*", "1.4*"],
                document.References.Select(r => (IReadOnlyList<string>)
                    [r.DocId, r.DocType.ToString(), Words(r.Relation)])));
    }
}
