using System.Xml;
using Aeralis.Dataset.Documents;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>
/// The procedural projection: maintenance tasks and structural repairs, both of which issue
/// as a procedural data module.
/// </summary>
public static partial class CsdbProjection
{
    /// <summary>A step whose text begins with one of these is that kind of notice, not a step.</summary>
    private static readonly (string Prefix, string Element)[] NoticePrefixes =
    [
        ("WARNING:", "warning"),
        ("CAUTION:", "caution"),
        ("NOTE:", "note"),
    ];

    // --------------------------------------------------------------- maintenance task

    private static XmlElement BuildProcedure(XmlDocument doc, TechDocument document, AmmTaskData? task,
        IReadOnlyList<FigurePlan> figures)
    {
        var sections = document.Sections;
        var procedure = El(doc, "procedure");

        var common = El(doc, "commonInfo")
            .WithAll(Paragraphs(doc, sections.GetValueOrDefault("purpose") ?? document.Abstract));
        if (sections.GetValueOrDefault("job_setup_note") is { Length: > 0 } setup)
        {
            common.With(El(doc, "note").With(Text(doc, "notePara", setup)));
        }
        procedure.With(common);

        procedure.With(PreliminaryRequirements(doc, document, task));
        procedure.With(MainProcedure(doc, sections.GetValueOrDefault("procedure"), figures));
        procedure.With(CloseRequirements(doc, sections.GetValueOrDefault("close_out"), task?.Access));

        return procedure;
    }

    private static XmlElement PreliminaryRequirements(XmlDocument doc, TechDocument document, AmmTaskData? task)
    {
        var preliminary = El(doc, "preliminaryRqmts");

        var conditions = El(doc, "reqCondGroup");
        var access = task?.Access;
        if (access is not null)
        {
            if (access.Zones.Count > 0)
            {
                conditions.With(Condition(doc, $"Access is available to zone {string.Join(", ", access.Zones)}."));
            }
            if (access.AccessPanels.Count > 0)
            {
                conditions.With(Condition(doc,
                    $"Access panel {string.Join(", ", access.AccessPanels)} is open."));
            }
            if (access.Doors.Count > 0)
            {
                conditions.With(Condition(doc, $"Door {string.Join(", ", access.Doors)} is open."));
            }
        }
        if (!string.IsNullOrWhiteSpace(task?.EffectivityNote))
        {
            conditions.With(Condition(doc, task.EffectivityNote));
        }
        if (!conditions.HasChildNodes)
        {
            conditions.With(Condition(doc, "The aircraft is safe for maintenance."));
        }
        preliminary.With(conditions);

        var setup = task?.JobSetup;

        // Support equipment, supplies and spares each have a schema-mandated "none" element,
        // which is what a real module carries rather than an absent section.
        var tooling = setup?.Tooling ?? [];
        preliminary.With(tooling.Count == 0
            ? El(doc, "reqSupportEquips").With(El(doc, "noSupportEquips"))
            : El(doc, "reqSupportEquips").With(El(doc, "supportEquipDescrGroup").WithAll(
                tooling.Select(t => Descriptor(doc, "supportEquipDescr", t.Description ?? t.Reference,
                    t.Reference, null, "1")))));

        var consumables = setup?.Consumables ?? [];
        preliminary.With(consumables.Count == 0
            ? El(doc, "reqSupplies").With(El(doc, "noSupplies"))
            : El(doc, "reqSupplies").With(El(doc, "supplyDescrGroup").WithAll(
                consumables.Select(c => Descriptor(doc, "supplyDescr", c, null, null, "As required")))));

        var expendables = setup?.Expendables ?? [];
        preliminary.With(expendables.Count == 0
            ? El(doc, "reqSpares").With(El(doc, "noSpares"))
            : El(doc, "reqSpares").With(El(doc, "spareDescrGroup").WithAll(
                expendables.Select(e => Descriptor(doc, "spareDescr", e.PartNumber, e.PartNumber, null,
                    Num(e.Qty))))));

        var precautions = setup?.SafetyPrecautions ?? [];
        if (precautions.Count > 0)
        {
            var safety = El(doc, "safetyRqmts");
            foreach (var precaution in precautions)
            {
                safety.With(El(doc, "warning").With(Text(doc, "warningAndCautionPara", precaution)));
            }
            preliminary.With(El(doc, "reqSafety").With(safety));
        }

        return preliminary;
    }

    /// <summary>
    /// The steps of the task. The dataset holds them as one numbered string, which is how a
    /// task is written; the numbers are dropped because the stylesheet renumbers in ATA style.
    /// </summary>
    /// <remarks>
    /// The steps are emitted flat, at the top level of the procedure, rather than nested under
    /// one titled step. A step lays out as a list item, and the layout engine does not break a
    /// list item across a page: nesting thirteen steps inside one makes a block taller than a
    /// page that cannot be split, which comes out as an empty page followed by content running
    /// off the bottom of the next.
    /// </remarks>
    private static XmlElement MainProcedure(XmlDocument doc, string? procedureText,
        IReadOnlyList<FigurePlan> figures)
    {
        var main = El(doc, "mainProcedure");
        var steps = NumberedSteps(procedureText);

        if (steps.Count == 0)
        {
            main.With(El(doc, "proceduralStep").With(Text(doc, "para", Clean(procedureText))));
            foreach (var figure in figures) main.With(FigureStep(doc, figure));
            return main;
        }

        // The first figure goes after the opening step, where the reader is still finding the
        // installation; the second after the last, where the work is checked.
        var figureAfter = new Dictionary<int, FigurePlan>();
        if (figures.Count > 0) figureAfter[0] = figures[0];
        if (figures.Count > 1) figureAfter[Math.Max(1, steps.Count - 1)] = figures[1];

        for (var i = 0; i < steps.Count; i++)
        {
            main.With(Step(doc, steps[i]));
            if (figureAfter.TryGetValue(i, out var figure)) main.With(FigureStep(doc, figure));
        }

        return main;
    }

    /// <summary>A step whose content is an illustration and the line that points to it.</summary>
    private static XmlElement FigureStep(XmlDocument doc, FigurePlan figure) =>
        El(doc, "proceduralStep").With(
            Text(doc, "para", $"See {figure.Caption}."),
            Figure(doc, figure));

    /// <summary>One step, or the notice it turns out to be.</summary>
    private static XmlElement Step(XmlDocument doc, string text)
    {
        foreach (var (prefix, element) in NoticePrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var body = text[prefix.Length..].Trim();
            return element == "note"
                ? El(doc, "proceduralStep").With(El(doc, "note").With(Text(doc, "notePara", body)))
                : El(doc, "proceduralStep").With(
                    El(doc, element).With(Text(doc, "warningAndCautionPara", body)));
        }

        return El(doc, "proceduralStep").With(Text(doc, "para", text));
    }

    private static XmlElement CloseRequirements(XmlDocument doc, string? closeOut, AmmAccess? access)
    {
        var conditions = El(doc, "reqCondGroup");
        foreach (var sentence in Sentences(closeOut))
        {
            conditions.With(Condition(doc, sentence));
        }
        if (access?.AccessPanels.Count > 0)
        {
            conditions.With(Condition(doc, $"Close access panel {string.Join(", ", access.AccessPanels)}."));
        }
        if (!conditions.HasChildNodes)
        {
            conditions.With(Condition(doc, "Make an entry in the aircraft technical log."));
        }

        return El(doc, "closeRqmts").With(conditions);
    }

    private static XmlElement Condition(XmlDocument doc, string text) =>
        El(doc, "reqCondNoRef").With(Text(doc, "reqCond", text));

    private static XmlElement Descriptor(XmlDocument doc, string element, string name, string? partNumber,
        string? shortName, string quantity)
    {
        var descriptor = El(doc, element).With(Text(doc, "name", name));
        if (!string.IsNullOrWhiteSpace(shortName)) descriptor.With(Text(doc, "shortName", shortName));

        if (!string.IsNullOrWhiteSpace(partNumber))
        {
            descriptor.With(El(doc, "identNumber").With(
                Text(doc, "manufacturerCode", EnterpriseCode),
                El(doc, "partAndSerialNumber").With(Text(doc, "partNumber", partNumber))));
        }

        return descriptor.With(Text(doc, "reqQuantity", quantity));
    }

    // ------------------------------------------------------------- structural repair

    /// <summary>
    /// A repair issues as a procedural module too, but its content is a set of prose sections
    /// rather than a numbered task, so each section becomes a titled step with its sentences
    /// as sub-steps — which is how a repair reads on the page.
    /// </summary>
    private static XmlElement BuildRepairProcedure(XmlDocument doc, TechDocument document,
        SrmRepairData? repair, IReadOnlyList<FigurePlan> figures)
    {
        var sections = document.Sections;
        var procedure = El(doc, "procedure");

        procedure.With(El(doc, "commonInfo")
            .WithAll(Paragraphs(doc, sections.GetValueOrDefault("purpose") ?? document.Abstract)));

        var preliminary = El(doc, "preliminaryRqmts");
        var conditions = El(doc, "reqCondGroup");
        if (!string.IsNullOrWhiteSpace(repair?.AllowableDamageLimits))
        {
            conditions.With(Condition(doc, repair.AllowableDamageLimits));
        }
        if (document.Entities?.Zones.Count > 0)
        {
            conditions.With(Condition(doc,
                $"The damage is in zone {string.Join(", ", document.Entities.Zones)}."));
        }
        if (repair?.RequiresOemApproval == true)
        {
            conditions.With(Condition(doc, "A repair approval request has been raised with Aeralis."));
        }
        if (!conditions.HasChildNodes) conditions.With(Condition(doc, "The damage has been measured and recorded."));
        preliminary.With(conditions);

        var materials = repair?.Materials ?? [];
        preliminary.With(materials.Count == 0
            ? El(doc, "reqSupplies").With(El(doc, "noSupplies"))
            : El(doc, "reqSupplies").With(El(doc, "supplyDescrGroup").WithAll(
                materials.Select(m => Descriptor(doc, "supplyDescr", m.Description ?? m.PartNumber,
                    m.PartNumber, null, $"{Num(m.Qty)} {m.Uom ?? "EA"}".Trim())))));

        procedure.With(preliminary);

        var main = El(doc, "mainProcedure");
        var figureIndex = 0;

        // One step per section, its prose in paragraphs — not one step per sentence. A repair
        // section is written as an argument, not as a sequence of commands, and a step holding
        // three or four paragraphs stays short enough to lay out on one page (see
        // <see cref="MainProcedure"/> on why that matters).
        foreach (var key in new[] { "damage_assessment", "repair_procedure", "inspection_requirements" })
        {
            if (!sections.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text)) continue;

            foreach (var (paragraph, index) in Paragraphs(doc, text, sentencesPerParagraph: 3).Select((p, i) => (p, i)))
            {
                var step = El(doc, "proceduralStep");
                if (index == 0) step.With(Text(doc, "title", Humanise(key)));
                main.With(step.With(paragraph));
            }

            if (figureIndex < figures.Count)
            {
                main.With(FigureStep(doc, figures[figureIndex]));
                figureIndex++;
            }
        }

        if (!main.HasChildNodes) main.With(El(doc, "proceduralStep").With(Text(doc, "para", document.Abstract)));
        procedure.With(main);

        var repairData = RepairDataTable(doc, repair);
        var close = El(doc, "closeRqmts").With(
            El(doc, "reqCondGroup").With(
                Condition(doc, repair?.FatigueLifeNote ?? "Record the repair in the structural repair record."),
                Condition(doc, "Restore the external protection scheme and apply the repair stencil.")));
        procedure.With(close);

        if (repairData is not null)
        {
            // The data block is content, so it goes at the end of the main procedure rather
            // than into the close-out requirements, which take conditions only.
            main.With(El(doc, "proceduralStep")
                .With(Text(doc, "title", "Repair data"), repairData));
        }

        return procedure;
    }

    private static XmlElement? RepairDataTable(XmlDocument doc, SrmRepairData? repair) =>
        repair is null
            ? null
            : DataTable(doc, "Repair data", [
                ("Repair number", repair.RepairNumber),
                ("Category", Words(repair.RepairCategory)),
                ("Damage type", Words(repair.DamageType)),
                ("Fastener pattern", repair.FastenerPattern),
                ("NDT method", Words(repair.NdtMethod)),
                ("Static strength restored", $"{Num(repair.StaticStrengthRestoredPct)} %"),
                ("Mass penalty", $"{Num(repair.WeightPenaltyKg)} kg"),
                ("Aerodynamic smoothness", repair.AerodynamicSmoothness),
                ("Aeralis approval required", repair.RequiresOemApproval ? "Yes" : "No"),
                ("Repair drawings", string.Join(", ",
                    repair.RepairDrawings.Select(d => $"{d.Reference} ({d.Sheets} sheets)"))),
            ]);
}
