using Aeralis.Illustrations;

namespace Aeralis.Attachments;

/// <summary>
/// Joins the aircraft type as the dataset spells it to the three-view drawing that illustrates
/// it, and picks one type for a document that applies to several.
/// </summary>
public static class AircraftMapping
{
    /// <summary>
    /// Dataset <c>aircraft_type</c> → the drawing to use for it.
    ///
    /// Two entries are not the identity. <c>SOLANO-30</c> is published as two sheets, one per
    /// vertical tail plane height; the taller one is the common build, so that is the sheet a
    /// page for an unqualified SOLANO-30 gets. <c>MERIDIAN-42F</c> has no sheet of its own — Aeralis
    /// publishes none — and it is the -1000 airframe, so it borrows that drawing.
    ///
    /// The two CIRRANE sheets are published as a plan view alone, so those documents are
    /// illustrated on the plan — see <c>IllustrationPlanner.OnAvailableViews</c>.
    /// </summary>
    private static readonly Dictionary<string, AircraftType> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CIRRANE-11"] = AircraftType.Cirrane11,
        ["CIRRANE-13"] = AircraftType.Cirrane13,
        ["AUSTER-20"] = AircraftType.Auster20,
        ["AUSTER-24"] = AircraftType.Auster24,
        ["ZEPHYR-18"] = AircraftType.Zephyr18,
        ["ZEPHYR-20"] = AircraftType.Zephyr20,
        ["ZEPHYR-22"] = AircraftType.Zephyr22,
        ["ZEPHYR-24"] = AircraftType.Zephyr24,
        ["SOLANO-30"] = AircraftType.Solano30,
        ["SOLANO-30F"] = AircraftType.Solano30F,
        ["SOLANO-34"] = AircraftType.Solano34,
        ["SOLANO-38"] = AircraftType.Solano38,
        ["SOLANO-39"] = AircraftType.Solano39,
        ["VANTOR-62"] = AircraftType.Vantor62,
        ["VANTOR-64"] = AircraftType.Vantor64,
        ["VANTOR-66"] = AircraftType.Vantor66,
        ["VANTOR-68"] = AircraftType.Vantor68,
        ["MERIDIAN-42"] = AircraftType.Meridian42,
        ["MERIDIAN-46"] = AircraftType.Meridian46,
        ["MERIDIAN-42F"] = AircraftType.Meridian42,
        ["TITAN-88"] = AircraftType.Titan88,
    };

    /// <summary>The drawing for one dataset aircraft type, or null if none is published.</summary>
    public static AircraftType? Resolve(string? aircraftType) =>
        aircraftType is not null && ByName.TryGetValue(aircraftType, out var type) ? type : null;

    /// <summary>
    /// The type a document is illustrated as.
    /// </summary>
    /// <param name="aircraftTypes">The types the document applies to.</param>
    /// <param name="fleet">
    /// How many aircraft of each type are in the registry. A document usually applies to a
    /// whole family, and the family's own aircraft — the one most of the fleet is — is the one
    /// to draw. Taking the first listed type instead would illustrate a ZEPHYR-22 family document
    /// with a ZEPHYR-18, of which the demo fleet holds fourteen against the ZEPHYR-22's two hundred, and
    /// would date the publication to 2003 rather than to 1988 — which decides its mark, its
    /// typeface, and whether it is held as a scan.
    /// </param>
    public static AircraftType? Primary(IEnumerable<string> aircraftTypes,
        IReadOnlyDictionary<string, int>? fleet = null)
    {
        AircraftType? best = null;
        var bestSize = -1;

        foreach (var name in aircraftTypes)
        {
            if (Resolve(name) is not { } type) continue;

            var size = fleet?.GetValueOrDefault(name) ?? 0;
            if (size > bestSize)
            {
                best = type;
                bestSize = size;
            }
        }

        return best;
    }

    /// <summary>
    /// The heading a manual page carries, which lists the types the page covers — for example
    /// "ZEPHYR-20/ZEPHYR-22/ZEPHYR-24". Long lists are cut at four types, as a real page does.
    /// </summary>
    public static string Heading(IReadOnlyList<string> aircraftTypes, AircraftType fallback)
    {
        if (aircraftTypes.Count == 0) return Designation(fallback);
        return aircraftTypes.Count <= 4
            ? string.Join("/", aircraftTypes)
            : string.Join("/", aircraftTypes.Take(3)) + "/…";
    }

    /// <summary>The designation of a drawing's type, as the illustrator prints it.</summary>
    public static string Designation(AircraftType aircraft) => AircraftIllustrator.Designation(aircraft);
}
