using System.Xml;
using Aeralis.Dataset.Documents;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>The fault isolation projection, for trouble shooting procedures.</summary>
public static partial class CsdbProjection
{
    private static XmlElement BuildFaultIsolation(XmlDocument doc, TechDocument document,
        TsmProcedureData? tsm, IReadOnlyList<FigurePlan> figures)
    {
        var sections = document.Sections;
        var isolation = El(doc, "faultIsolation");

        // ------------------------------------------------------------ fault description
        var descr = El(doc, "faultDescr");
        if (!string.IsNullOrWhiteSpace(tsm?.FaultSymptom))
        {
            descr.With(Text(doc, "para", tsm.FaultSymptom));
        }
        descr.WithAll(Paragraphs(doc, sections.GetValueOrDefault("fault_description") ?? document.Abstract));

        var messages = tsm?.WarningMessages ?? [];
        if (messages.Count > 0)
        {
            var para = El(doc, "para");
            para.AppendChild(doc.CreateTextNode("Flight deck effect: "));
            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0) para.AppendChild(doc.CreateTextNode(", "));
                para.With(Text(doc, "verbatimText", messages[i]));
            }
            para.AppendChild(doc.CreateTextNode("."));
            descr.With(para);
        }

        var codes = tsm?.FaultCodes ?? [];
        if (codes.Count > 0)
        {
            descr.With(Text(doc, "para", $"Maintenance fault codes: {string.Join(", ", codes)}."));
        }

        if (tsm?.PossibleCauses.Count > 0)
        {
            descr.With(Table(doc, "Possible causes, most likely first",
                ["Cause", "FIN", "Probability"],
                ["3.2*", "0.8*", "0.9*"],
                tsm.PossibleCauses
                    .OrderByDescending(c => c.ProbabilityPct)
                    .Select(c => (IReadOnlyList<string>)
                        [c.Cause, c.Fin ?? "—", $"{Num(c.ProbabilityPct)} %"])));
        }

        if (figures.Count > 0) descr.With(Figure(doc, figures[0]));
        isolation.With(descr);

        // ------------------------------------------------------------------ procedure
        var procedure = El(doc, "isolationProcedure");

        var preliminary = El(doc, "preliminaryRqmts");
        var conditions = El(doc, "reqCondGroup").With(
            Condition(doc, "The aircraft electrical power is available."));
        if (document.Entities?.Zones.Count > 0)
        {
            conditions.With(Condition(doc,
                $"Access is available to zone {string.Join(", ", document.Entities.Zones)}."));
        }
        preliminary.With(conditions);
        preliminary.With(El(doc, "reqSupportEquips").With(El(doc, "supportEquipDescrGroup").With(
            Descriptor(doc, "supportEquipDescr", "Portable maintenance access terminal", null, null, "1"))));
        preliminary.With(El(doc, "reqSupplies").With(El(doc, "noSupplies")));
        preliminary.With(El(doc, "reqSpares").With(El(doc, "noSpares")));
        procedure.With(preliminary);

        procedure.With(IsolationSteps(doc, sections.GetValueOrDefault("isolation_procedure"), tsm, figures));

        var close = El(doc, "reqCondGroup");
        if (!string.IsNullOrWhiteSpace(tsm?.ConfirmationTask))
        {
            close.With(Condition(doc, $"Do the confirmation task {tsm.ConfirmationTask}."));
        }
        close.With(Condition(doc,
            "Make sure that no fault message is shown and make an entry in the aircraft technical log."));
        procedure.With(El(doc, "closeRqmts").With(close));

        isolation.With(El(doc, "faultIsolationProcedure").With(procedure));

        // The dispatch note is not part of the isolation procedure — it is what the line
        // engineer needs if the fault cannot be cleared — so it goes after it, as its own
        // correlated information block.
        if (!string.IsNullOrWhiteSpace(sections.GetValueOrDefault("dispatch_note")) || tsm?.Dispatch is not null)
        {
            var correlated = El(doc, "correlatedFaultInfo");
            correlated.WithAll(Paragraphs(doc, sections.GetValueOrDefault("dispatch_note")));

            if (tsm?.Dispatch is { } dispatch)
            {
                var table = DataTable(doc, "Dispatch", [
                    ("MEL item", dispatch.MelItem),
                    ("Dispatchable", dispatch.Dispatchable ? "Yes" : "No"),
                    ("Condition", dispatch.DispatchCondition),
                ]);
                if (table is not null) correlated.With(table);
            }

            isolation.With(correlated);
        }

        return isolation;
    }

    /// <summary>
    /// The isolation tree. The dataset holds the procedure as a numbered sequence, which is
    /// how a trouble shooting procedure is written down; each step becomes an isolation step
    /// whose question is the step itself, answered yes to continue and no to go to the end.
    /// </summary>
    private static XmlElement IsolationSteps(XmlDocument doc, string? procedureText, TsmProcedureData? tsm,
        IReadOnlyList<FigurePlan> figures)
    {
        var main = El(doc, "isolationMainProcedure");
        var steps = NumberedSteps(procedureText);
        var causes = (tsm?.PossibleCauses ?? []).OrderByDescending(c => c.ProbabilityPct).ToList();

        if (steps.Count == 0)
        {
            main.With(El(doc, "isolationProcedureEnd", "id", "stp-end")
                .With(Text(doc, "para", Clean(procedureText))));
            return main;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var id = $"stp-{i + 1:000}";
            var next = i + 1 < steps.Count ? $"stp-{i + 2:000}" : "stp-end";

            var step = El(doc, "isolationStep", "id", id).With(
                El(doc, "isolationStepQuestion").With(Text(doc, "para", steps[i])));

            // The most likely cause is named against the step that would find it, so the tree
            // says what a yes means rather than only where it goes.
            var cause = i < causes.Count ? causes[i] : null;

            step.With(El(doc, "isolationStepAnswer", "nextActionRefId", next).With(
                Text(doc, "isolationStepAnswerValue", "DONE"),
                Text(doc, "para", cause is null
                    ? "Continue with the next step."
                    : $"If this identifies the fault, the cause is: {cause.Cause}"
                      + (cause.Fin is { Length: > 0 } ? $" ({cause.Fin})." : "."))));

            step.With(El(doc, "isolationStepAnswer", "nextActionRefId", "stp-end").With(
                Text(doc, "isolationStepAnswerValue", "NOT APPLICABLE"),
                El(doc, "correctiveAction").With(doc.CreateTextNode(
                    "Go to the end of the procedure and do the confirmation task."))));

            if (i == 0 && figures.Count > 1) step.With(Figure(doc, figures[1]));

            main.With(step);
        }

        main.With(El(doc, "isolationProcedureEnd", "id", "stp-end").With(Text(doc, "para",
            tsm?.ConfirmationTask is { Length: > 0 } task
                ? $"Do the confirmation task {task} and make sure that the fault does not return."
                : "Make sure that the fault does not return and close the maintenance message.")));

        return main;
    }
}
