using System.Globalization;
using System.Xml;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>The service bulletin projection.</summary>
/// <remarks>
/// The SB schema is the one place where the dataset and S1000D agree section for section:
/// planning information, material information and accomplishment instructions are the three
/// parts of a real bulletin and the three parts the dataset carries.
/// </remarks>
public static partial class CsdbProjection
{
    /// <summary>
    /// S1000D compliance categories. <c>sbcc01</c> is alert, <c>02</c> recommended,
    /// <c>03</c> optional; the dataset's mandatory maps onto alert, which is what a bulletin
    /// carrying an airworthiness directive is issued as.
    /// </summary>
    private static string ComplianceCategory(Compliance compliance) => compliance switch
    {
        Compliance.MANDATORY => "sbcc01",
        Compliance.RECOMMENDED => "sbcc02",
        _ => "sbcc03",
    };

    private static string TaskCategory(SbType type) => type switch
    {
        SbType.MODIFICATION => "sbtc03",
        SbType.INSPECTION => "sbtc01",
        SbType.REPAIR => "sbtc02",
        SbType.SOFTWARE_LOAD => "sbtc04",
        SbType.CONVERSION => "sbtc05",
        _ => "sbtc06",
    };

    private static XmlElement BuildServiceBulletin(XmlDocument doc, TechDocument document,
        ServiceBulletinData? sb, IReadOnlyList<FigurePlan> figures)
    {
        var sections = document.Sections;
        var bulletin = El(doc, "sb");

        // ---------------------------------------------------------------- management
        var management = El(doc, "sbManagementInfo").With(
            Text(doc, "sbNumber", sb?.SbNumber ?? document.DocId),
            Text(doc, "sbAta", document.Ata?.Ata6d ?? ""),
            El(doc, "sbCompliance", "sbComplianceCategory", ComplianceCategory(sb?.Compliance ?? Compliance.OPTIONAL)),
            El(doc, "sbTaskType", "sbTaskCategory", TaskCategory(sb?.SbType ?? SbType.INFORMATION)));

        if (DateOnly.TryParse(document.Dates?.Released, CultureInfo.InvariantCulture, out var released))
        {
            management.With(El(doc, "sbOriginalIssueDate").With(
                El(doc, "issueDate",
                    "year", released.Year.ToString(CultureInfo.InvariantCulture),
                    "month", released.Month.ToString("00", CultureInfo.InvariantCulture),
                    "day", released.Day.ToString("00", CultureInfo.InvariantCulture))));
        }
        bulletin.With(management);

        // ------------------------------------------------------------------ revision
        var history = sb?.RevisionHistory ?? [];
        if (history.Count > 0)
        {
            var summary = El(doc, "sbRevisionSummary");
            foreach (var entry in history)
            {
                var reason = string.IsNullOrWhiteSpace(entry.Reason) ? "Issued." : entry.Reason;
                summary.With(Text(doc, "para", $"Revision {entry.Revision} ({entry.Date ?? "—"}): {reason}"));
            }
            bulletin.With(El(doc, "sbRevisionInfo").With(summary));
        }

        // ------------------------------------------------------------------- summary
        bulletin.With(El(doc, "sbSummary").WithAll(Paragraphs(doc, document.Abstract)));

        // ------------------------------------------------------------------ planning
        var planning = El(doc, "sbPlanningInfo");

        var effectivity = El(doc, "sbEffectivity")
            .WithAll(Paragraphs(doc, sections.GetValueOrDefault("effectivity_note")));
        foreach (var element in AircraftEffectivity(doc, document)) effectivity.With(element);
        planning.With(effectivity);

        planning.With(El(doc, "sbReason").WithAll(Paragraphs(doc, sections.GetValueOrDefault("planning_reason"))));

        var descriptionSection = El(doc, "sbDescription")
            .WithAll(Paragraphs(doc, sections.GetValueOrDefault("description")));
        if (figures.Count > 0) descriptionSection.With(Figure(doc, figures[0]));
        planning.With(descriptionSection);

        planning.With(El(doc, "sbCompliance").With(
            Text(doc, "para", ComplianceText(sb))));

        if (sb?.AdLinked == true)
        {
            planning.With(El(doc, "sbApproval").With(Text(doc, "para",
                $"The technical content of this service bulletin is approved and is the subject of "
                + $"airworthiness directive {sb.AdReference ?? "—"}.")));
        }
        else
        {
            planning.With(El(doc, "sbApproval").With(Text(doc, "para",
                "The technical content of this service bulletin is approved under the authority of the "
                + "Aeralis design organisation approval.")));
        }

        if (sb?.Manpower is { } manpower)
        {
            planning.With(El(doc, "sbManpower").With(Text(doc, "para",
                $"The estimated man-hours are {Num(manpower.Manhours)} man-hours for one aircraft, based on "
                + $"{manpower.Personnel} technicians working at the same time, and an elapsed time of "
                + $"{Num(manpower.ElapsedTimeHours)} hours. The downtime category is "
                + $"{Words(manpower.DowntimeCategory)}.")));
        }

        planning.With(El(doc, "sbWeightBalance").With(Text(doc, "para",
            sb is null || Math.Abs(sb.WeightChangeKg) < 0.01
                ? "The change of mass is negligible and no revision of the aircraft weighing record is required."
                : $"The change of mass is {Num(sb.WeightChangeKg)} kg per aircraft. The aircraft weighing "
                  + "record is to be amended at accomplishment.")));

        planning.With(El(doc, "sbInterchangeability").With(Text(doc, "para",
            InterchangeabilityText(sb?.Interchangeability ?? Interchangeability.Unknown))));

        var affected = document.References
            .Where(r => r.Relation == RelationType.IMPACTS || r.Relation == RelationType.DOCUMENTED_IN)
            .Select(r => r.DocId)
            .ToList();
        if (affected.Count > 0)
        {
            planning.With(El(doc, "sbPublicationsAffected").With(Text(doc, "para",
                $"The following are revised at the next scheduled revision: {string.Join(", ", affected)}.")));
        }

        bulletin.With(planning);

        // ------------------------------------------------------------------ material
        if (sb?.Material is { } material)
        {
            var info = El(doc, "sbMaterialInfo");

            info.With(El(doc, "sbMaterialPrice").With(Text(doc, "para",
                string.IsNullOrWhiteSpace(material.KitNumber)
                    ? "No kit is issued for this service bulletin. Parts are ordered separately."
                    : $"The material is supplied as kit {material.KitNumber}"
                      + (sb.WarrantyApplicable ? " at no charge under warranty" : "")
                      + ", one kit per aircraft.")));

            if (material.PartsRequired.Count > 0)
            {
                info.With(El(doc, "sbMaterialKit").With(Table(doc,
                    string.IsNullOrWhiteSpace(material.KitNumber)
                        ? "Parts required, one aircraft"
                        : $"Kit {material.KitNumber} contents, one kit per aircraft",
                    ["Part number", "Quantity", "Unit"],
                    ["2*", "0.8*", "0.8*"],
                    material.PartsRequired.Select(p => (IReadOnlyList<string>)
                        [p.PartNumber, Num(p.Qty), p.Uom ?? "EA"]))));
            }

            var availability = new List<string>();
            if (!string.IsNullOrWhiteSpace(material.KitAvailableFrom))
            {
                availability.Add($"The kit is available from {material.KitAvailableFrom}.");
            }
            if (material.PartsReturned.Count > 0)
            {
                availability.Add("The following are to be returned: "
                                 + string.Join(", ", material.PartsReturned.Select(p => p.PartNumber)) + ".");
            }
            if (material.Consumables.Count > 0)
            {
                availability.Add("Consumables: " + string.Join(", ", material.Consumables) + ".");
            }
            if (material.Tooling.Count > 0)
            {
                availability.Add("Tooling: " + string.Join(", ", material.Tooling) + ".");
            }
            if (!string.IsNullOrWhiteSpace(sections.GetValueOrDefault("material_note")))
            {
                availability.Add(Clean(sections["material_note"]));
            }
            if (availability.Count > 0)
            {
                info.With(El(doc, "sbMaterialAvailability").With(Text(doc, "para", string.Join(" ", availability))));
            }

            bulletin.With(info);
        }

        // ----------------------------------------------------------- accomplishment
        var instructions = El(doc, "sbAccomplishmentInstructions");
        var general = El(doc, "sbGeneral");

        var condition = document.Applicability?.EmbodimentCondition;
        if (condition?.PreMod.Count > 0 || condition?.PreSb.Count > 0)
        {
            general.With(El(doc, "warning").With(Text(doc, "warningAndCautionPara",
                "Confirm the pre-embodiment condition of the aircraft before you start. This service "
                + "bulletin applies only to aircraft that do not have "
                + string.Join(", ", condition.PreMod.Concat(condition.PreSb)) + " embodied.")));
        }

        var accomplishment = sections.GetValueOrDefault("accomplishment_summary");
        var steps = NumberedSteps(accomplishment);
        general.WithAll(Paragraphs(doc, steps.Count > 1 ? null : accomplishment));
        if (!general.HasChildNodes) general.With(Text(doc, "para", document.Abstract));
        if (figures.Count > 1) general.With(Figure(doc, figures[1]));
        instructions.With(general);

        if (steps.Count > 1)
        {
            var procedure = El(doc, "sbProcedure");
            foreach (var step in steps) procedure.With(Step(doc, step));
            instructions.With(procedure);
        }

        bulletin.With(instructions);

        bulletin.With(El(doc, "sbAdditionalInfo").With(Text(doc, "para",
            "Report the accomplishment of this service bulletin through the customer services portal, "
            + "quoting the aircraft serial number and the date of accomplishment.")));

        return bulletin;
    }

    private static IEnumerable<XmlElement> AircraftEffectivity(XmlDocument doc, TechDocument document)
    {
        var applicability = document.Applicability;
        if (applicability is null) yield break;

        var serials = applicability.Msns.Count > 0
            ? string.Join(", ", applicability.Msns)
            : applicability.MsnRanges.Count > 0
                ? string.Join(", ", applicability.MsnRanges.Select(r => $"{r.From} to {r.To}"))
                : "All aircraft";

        if (applicability.ExcludedMsns.Count > 0)
        {
            serials += $", except {string.Join(", ", applicability.ExcludedMsns)}";
        }

        var models = applicability.Models.Count > 0
            ? applicability.Models
            : applicability.AircraftTypes.Count > 0
                ? applicability.AircraftTypes
                : [document.ProgramName ?? "All"];

        foreach (var model in models)
        {
            yield return El(doc, "sbAircraftEffectivity").With(
                Text(doc, "sbAircraftModel", model),
                Text(doc, "sbSerialNumbers", serials));
        }
    }

    private static string ComplianceText(ServiceBulletinData? sb)
    {
        if (sb is null) return "Optional.";

        var text = Words(sb.Compliance) + ".";
        if (!string.IsNullOrWhiteSpace(sb.ComplianceTime))
        {
            text += $" Accomplish {sb.ComplianceTime}.";
        }
        if (sb.AdLinked)
        {
            text += $" This service bulletin is the subject of airworthiness directive {sb.AdReference ?? "—"}, "
                    + "which makes its accomplishment mandatory within the time it states.";
        }
        return text;
    }

    private static string InterchangeabilityText(Interchangeability interchangeability) => interchangeability switch
    {
        Interchangeability.INTERCHANGEABLE =>
            "The parts are two-way interchangeable at aircraft level.",
        Interchangeability.ONE_WAY_INTERCHANGEABLE =>
            "The parts are one-way interchangeable: the new standard replaces the old, but the old standard "
            + "must not be installed on an aircraft that has been brought to the new one.",
        Interchangeability.NOT_INTERCHANGEABLE =>
            "The parts are not interchangeable. A mixed installation is not permitted.",
        _ => "Interchangeability is not affected.",
    };
}
