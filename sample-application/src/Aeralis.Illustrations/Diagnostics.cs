namespace Aeralis.Illustrations;

/// <summary>
/// Prints what the sampler sees, so a section outline aimed at the wrong part of a drawing shows
/// up as numbers rather than as a callout that looks slightly odd in a rendered page.
/// </summary>
/// <remarks>
/// Two things are worth checking. <em>Coverage</em> is the share of the silhouette mask counted
/// as aircraft: near zero means the measurement failed and the outlines are being used unfiltered,
/// and a plan view normally sits well above a side view because an aircraft seen from above fills
/// more of its own bounding box. <em>Targets</em> are where each section would actually put its
/// arrow, in view fractions with (0,0) at the top-left — the quickest way to see that, say, the
/// nose is being aimed at the tail because a drawing is the other way up.
/// </remarks>
internal static class Diagnostics
{
    public static void Run(string[] args)
    {
        var aircraft = args.Length > 0 ? Parse<AircraftType>(args[0]) : null;
        var view = args.Length > 1 ? Parse<ViewKind>(args[1]) : null;

        var types = aircraft is null ? Enum.GetValues<AircraftType>() : [aircraft.Value];

        Console.WriteLine($"{"aircraft",-16} {"view",-6} {"size",-13} coverage");

        foreach (var type in types)
        foreach (var kind in AircraftIllustrator.AvailableViews(type))
        {
            if (view is not null && kind != view) continue;

            var (coverage, width, height) = AircraftIllustrator.Inspect(type, kind);
            Console.WriteLine($"{AircraftIllustrator.Designation(type),-16} {kind,-6} {width,5:F0} x {height,-5:F0} {coverage,8:P1}");
        }

        // Sampled targets are only readable a drawing at a time, so they wait for a named one.
        if (aircraft is null) return;

        foreach (var kind in AircraftIllustrator.AvailableViews(aircraft.Value))
        {
            if (view is not null && kind != view) continue;

            Console.WriteLine($"\ntargets on {AircraftIllustrator.Designation(aircraft.Value)} {kind} (u right, v down, 0..1)");

            foreach (var section in Enum.GetValues<AircraftSection>())
            {
                var near = AircraftIllustrator.Target(aircraft.Value, kind, section, 0.0, 7);
                var far = AircraftIllustrator.Target(aircraft.Value, kind, section, 1.0, 7);
                var any = AircraftIllustrator.Target(aircraft.Value, kind, section, null, 7);
                Console.WriteLine($"   {section,-22} start ({near.U:F2},{near.V:F2})   end ({far.U:F2},{far.V:F2})   free ({any.U:F2},{any.V:F2})");
            }
        }
    }

    /// <summary>Accepts the enum name with a dash where it has none, e.g. Cirrane13 or Cirrane-13.</summary>
    private static T? Parse<T>(string text) where T : struct, Enum =>
        Enum.TryParse<T>(text.Replace("-", "_"), ignoreCase: true, out var value)
            ? value
            : throw new ArgumentException($"'{text}' is not one of {string.Join(", ", Enum.GetNames<T>())}");
}
