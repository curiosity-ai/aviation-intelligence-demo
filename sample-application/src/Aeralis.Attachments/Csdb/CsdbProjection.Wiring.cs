using System.Globalization;
using System.Xml;
using Aeralis.Dataset.Documents;
using static Aeralis.Attachments.Csdb.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>The wiring and flight-crew projections.</summary>
public static partial class CsdbProjection
{
    // ------------------------------------------------------------------ wiring data

    /// <summary>
    /// A wiring sheet as a wiring data module.
    /// </summary>
    /// <remarks>
    /// Harnesses, connectors and equipment come straight from <c>type_data</c>. The wire list
    /// is the one thing the dataset does not hold row by row — it records <c>wire_count</c> and
    /// <c>wire_gauge_range</c>, not the individual wires — so the rows are laid out here from
    /// the connectors the sheet does list, in the count it declares. That is a rendering
    /// decision, not dataset content: nothing is written back, and a sheet that declared
    /// twelve wires shows twelve, between the connectors it names, with the gauges it gives.
    /// Every choice is drawn from the document's own identifier, so the sheet is the same on
    /// every run.
    /// </remarks>
    private static XmlElement BuildWiringData(XmlDocument doc, TechDocument document, WdmSheetData? wdm,
        IReadOnlyList<FigurePlan> figures)
    {
        var wiring = El(doc, "wiringData");
        var connectors = wdm?.Connectors ?? [];

        if (connectors.Count >= 2 && wdm is { WireCount: > 0 })
        {
            wiring.With(WireList(doc, document.DocId, wdm, connectors));
        }

        var harnesses = wdm?.Harnesses ?? [];
        if (harnesses.Count > 0)
        {
            var harnessData = El(doc, "harnessData");
            var zones = document.Entities?.Zones ?? [];

            for (var i = 0; i < harnesses.Count; i++)
            {
                var harness = El(doc, "harness", "harnessIdent", harnesses[i]).With(
                    Text(doc, "name", HarnessName(wdm, harnesses[i], i)));
                if (zones.Count > 0)
                {
                    harness.With(El(doc, "zoneRef", "zoneNumber", zones[i % zones.Count]));
                }
                harnessData.With(harness);
            }
            wiring.With(harnessData);
        }

        if (connectors.Count > 0)
        {
            var connectorData = El(doc, "connectorData");
            foreach (var connector in connectors)
            {
                var element = El(doc, "connector",
                    "connectorIdent", connector.Reference,
                    "contactQuantity", connector.Contacts > 0
                        ? connector.Contacts.ToString(CultureInfo.InvariantCulture)
                        : null);

                element.With(Text(doc, "name", ConnectorName(wdm, connector)));
                if (!string.IsNullOrWhiteSpace(connector.PartNumber))
                {
                    element.With(El(doc, "partRef",
                        "manufacturerCodeValue", EnterpriseCode,
                        "partNumberValue", connector.PartNumber));
                }
                connectorData.With(element);
            }
            wiring.With(connectorData);
        }

        // Equipment, the routing note and the figures are not part of the wiring content
        // model's three lists, so they follow as ordinary content — the stylesheet's generic
        // handling picks them up in document order.
        var equipment = wdm?.Equipment ?? [];
        if (equipment.Count > 0)
        {
            wiring.With(Table(doc, "Equipment on this sheet",
                ["FIN", "Description", "Zone"],
                ["0.9*", "3*", "0.7*"],
                equipment.Select(e => (IReadOnlyList<string>)[e.Fin, e.Description ?? "", e.Zone ?? "—"])));
        }

        foreach (var key in new[] { "purpose", "sheet_note", "routing_note" })
        {
            if (document.Sections.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                wiring.WithAll(Paragraphs(doc, text));
            }
        }

        if (wdm?.Routes.Count > 0)
        {
            wiring.With(Text(doc, "para", $"Routing: {string.Join("; ", wdm.Routes)}."));
        }

        foreach (var figure in figures) wiring.With(Figure(doc, figure));

        return wiring;
    }

    /// <summary>
    /// The wire rows, laid out between the connectors the sheet names. See
    /// <see cref="BuildWiringData"/> for why they are derived rather than read.
    /// </summary>
    private static XmlElement WireList(XmlDocument doc, string docId, WdmSheetData wdm,
        IReadOnlyList<WdmConnector> connectors)
    {
        var random = Seeded.Random(docId, "wires");
        var gauges = Gauges(wdm.WireGaugeRange);
        var wireData = El(doc, "wireData");

        // Long sheets are cut: a wire list is a reference, and forty rows of it in a demo PDF
        // is forty rows of the same thing. The count is stated in the note instead.
        var shown = Math.Min(wdm.WireCount, 18);

        for (var i = 0; i < shown; i++)
        {
            var from = connectors[i % connectors.Count];
            var to = connectors[(i / connectors.Count + 1 + i) % connectors.Count];
            if (ReferenceEquals(from, to)) to = connectors[(i + 1) % connectors.Count];

            var gauge = gauges[random.Next(gauges.Count)];
            var contactFrom = $"{(char)('A' + i % 4)}{i % 24 + 1:00}";
            var contactTo = $"{(i * 7) % 48 + 1:00}";
            var length = 1.5 + random.Next(0, 90) / 10.0;

            wireData.With(El(doc, "wire",
                    "wireIdent", $"{from.Reference}-{contactFrom}",
                    "wireGauge", gauge,
                    "wireType", i % 5 == 0 ? "FEP-S" : "FEP",
                    "wireLength", length.ToString("0.00", CultureInfo.InvariantCulture) + " m")
                .With(
                    El(doc, "wireEndFrom", "connectorIdent", from.Reference, "contactIdent", contactFrom),
                    El(doc, "wireEndTo", "connectorIdent", to.Reference, "contactIdent", contactTo)));
        }

        if (wdm.WireCount > shown)
        {
            wireData.With(El(doc, "note").With(Text(doc, "notePara",
                $"The sheet carries {wdm.WireCount} wires. The first {shown} are listed; "
                + "the remainder follow the same routing.")));
        }

        return wireData;
    }

    /// <summary>The gauges a sheet uses, read from "20 to 24" or "20-24".</summary>
    private static List<string> Gauges(string? range)
    {
        var digits = (range ?? "")
            .Split([' ', '-', 't', 'o', 'A', 'W', 'G'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.All(char.IsAsciiDigit) && part.Length is 1 or 2)
            .Select(int.Parse)
            .ToList();

        if (digits.Count == 0) return ["20"];
        if (digits.Count == 1) return [digits[0].ToString(CultureInfo.InvariantCulture)];

        var low = Math.Min(digits[0], digits[1]);
        var high = Math.Max(digits[0], digits[1]);
        // AWG runs in even sizes on a harness of this kind.
        return Enumerable.Range(low, high - low + 1)
            .Where(g => g % 2 == 0 || high - low < 2)
            .Select(g => g.ToString(CultureInfo.InvariantCulture))
            .DefaultIfEmpty(low.ToString(CultureInfo.InvariantCulture))
            .ToList();
    }

    private static string HarnessName(WdmSheetData? wdm, string ident, int index)
    {
        var equipment = wdm?.Equipment ?? [];
        if (index < equipment.Count && !string.IsNullOrWhiteSpace(equipment[index].Description))
        {
            return equipment[index].Description!;
        }
        return wdm?.SheetTitle is { Length: > 0 } title ? title : ident;
    }

    private static string ConnectorName(WdmSheetData? wdm, WdmConnector connector)
    {
        var equipment = (wdm?.Equipment ?? []).FirstOrDefault(e => e.Zone == connector.LocationZone);
        if (equipment?.Description is { Length: > 0 } description) return description;
        return connector.LocationZone is { Length: > 0 } zone
            ? $"Connector in zone {zone}"
            : connector.Reference;
    }

    // -------------------------------------------------------------------- crew data

    /// <summary>
    /// A flight-crew entry as a crew data module: the applicability and the description as
    /// reference cards, and the crew procedure as a drill of action/response pairs.
    /// </summary>
    private static XmlElement BuildCrew(XmlDocument doc, TechDocument document, FcomEntryData? fcom,
        IReadOnlyList<FigurePlan> figures)
    {
        var sections = document.Sections;
        var crew = El(doc, "crew");

        var opening = El(doc, "crewRefCard").With(Text(doc, "title", "Applicability and conditions"));
        opening.WithAll(Paragraphs(doc, sections.GetValueOrDefault("purpose") ?? document.Abstract));

        if (fcom is not null)
        {
            var table = DataTable(doc, "Entry", [
                ("Section", $"{fcom.Section} — {fcom.SectionTitle}"),
                ("Entry number", fcom.EntryNumber),
                ("Flight phase", Words(fcom.FlightPhase)),
                ("Operational impact", Words(fcom.OperationalImpact)),
                ("Systems", string.Join(", ", fcom.Systems)),
                ("Affects QRH", fcom.AffectsQrh ? "Yes" : "No"),
                ("Affects MEL", fcom.AffectsMel ? "Yes" : "No"),
                ("MEL items", string.Join(", ", fcom.MelItems)),
                ("Supersedes", fcom.SupersedesEntry),
            ]);
            if (table is not null) opening.With(table);
        }

        if (!string.IsNullOrWhiteSpace(sections.GetValueOrDefault("limitation_note")))
        {
            opening.With(El(doc, "caution").With(
                Text(doc, "warningAndCautionPara", sections["limitation_note"])));
        }
        if (figures.Count > 0) opening.With(Figure(doc, figures[0]));
        crew.With(opening);

        if (!string.IsNullOrWhiteSpace(sections.GetValueOrDefault("description")))
        {
            crew.With(El(doc, "crewRefCard")
                .With(Text(doc, "title", "Description"))
                .WithAll(Paragraphs(doc, sections["description"])));
        }

        var procedure = sections.GetValueOrDefault("crew_procedure");
        var steps = NumberedSteps(procedure);
        if (steps.Count > 0)
        {
            var drill = El(doc, "crewDrill").With(Text(doc, "title", document.Title));
            foreach (var step in steps) drill.With(DrillStep(doc, step));
            crew.With(drill);
        }

        if (!string.IsNullOrWhiteSpace(fcom?.CrewAction))
        {
            crew.With(El(doc, "crewRefCard")
                .With(Text(doc, "title", "Crew action"))
                .With(Text(doc, "para", fcom.CrewAction)));
        }

        if (figures.Count > 1) crew.With(El(doc, "crewRefCard").With(Figure(doc, figures[1])));

        return crew;
    }

    /// <summary>
    /// One line of a drill. A drill line is an item and what to do with it; where the step
    /// reads "ITEM — action" or "ITEM: action" the two are already there, and where it does
    /// not the whole sentence is the action.
    /// </summary>
    private static XmlElement DrillStep(XmlDocument doc, string step)
    {
        foreach (var separator in new[] { " — ", " – ", ": " })
        {
            var index = step.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index > 42) continue;

            return El(doc, "crewDrillStep").With(
                Text(doc, "crewDrillAction", step[..index].Trim().ToUpperInvariant()),
                Text(doc, "crewDrillResponse", step[(index + separator.Length)..].Trim()));
        }

        return El(doc, "crewDrillStep").With(
            Text(doc, "crewDrillAction", "ACTION"),
            Text(doc, "crewDrillResponse", step));
    }
}
