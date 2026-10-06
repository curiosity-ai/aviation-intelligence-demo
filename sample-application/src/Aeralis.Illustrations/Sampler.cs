namespace Aeralis.Illustrations;

/// <summary>
/// Picks the point in a section an annotation should point at.
/// </summary>
internal static class Sampler
{
    /// <summary>How far along the station axis a point may sit from the requested position.</summary>
    private const double StationTolerance = 0.10;

    private const int Attempts = 3000;

    /// <summary>
    /// Chooses a point inside <paramref name="shape"/> that lies on the aircraft. When
    /// <paramref name="position"/> is given the point is also constrained to that station along
    /// the section — 0 for the forward or inboard end, 1 for the aft or outboard end.
    /// </summary>
    /// <remarks>
    /// Rejection sampling rather than anything cleverer: the regions are small, the tests are
    /// cheap, and it stays uniform over whatever shape the outline and the silhouette leave
    /// between them. Each fallback below widens the constraint by one step rather than giving
    /// up, so a section that is thin or partly off the drawing still yields a sensible point.
    /// </remarks>
    public static Frac Pick(SectionShape shape, Silhouette silhouette, double? position, Random random)
    {
        if (position is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(position), "position must be between 0 and 1");

        double u1 = shape.Polygon.Min(p => p.U), u2 = shape.Polygon.Max(p => p.U);
        double v1 = shape.Polygon.Min(p => p.V), v2 = shape.Polygon.Max(p => p.V);

        // Pass 1: inside the outline, on the aircraft, at the requested station.
        // Pass 2: drop the station constraint's tightness. Pass 3: drop the silhouette test.
        for (var pass = 0; pass < 3; pass++)
        {
            var tolerance = StationTolerance * (pass + 1);

            for (var i = 0; i < Attempts; i++)
            {
                var candidate = new Frac(u1 + random.NextDouble() * (u2 - u1), v1 + random.NextDouble() * (v2 - v1));

                if (!InPolygon(candidate, shape.Polygon)) continue;
                if (pass < 2 && !silhouette.Contains(candidate)) continue;
                if (position is not null && Math.Abs(Station(candidate, shape) - position.Value) > tolerance) continue;

                return candidate;
            }
        }

        // Nothing fit, so aim at the station itself.
        return position is null
            ? Centroid(shape.Polygon)
            : Frac.Lerp(shape.StationStart, shape.StationEnd, position.Value);
    }

    /// <summary>
    /// How far along the section's own axis a point sits, as 0 to 1. This is the scalar
    /// projection onto the station axis, so it ignores how far off the axis the point is —
    /// a point near a wing's leading edge and one near its trailing edge at the same distance
    /// from the root count as the same station.
    /// </summary>
    private static double Station(Frac point, SectionShape shape)
    {
        double ax = shape.StationEnd.U - shape.StationStart.U, ay = shape.StationEnd.V - shape.StationStart.V;
        var lengthSquared = ax * ax + ay * ay;
        if (lengthSquared < 1e-12) return 0;

        double px = point.U - shape.StationStart.U, py = point.V - shape.StationStart.V;
        return Math.Clamp((px * ax + py * ay) / lengthSquared, 0, 1);
    }

    private static bool InPolygon(Frac point, Frac[] polygon)
    {
        var inside = false;

        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if (polygon[i].V > point.V == polygon[j].V > point.V) continue;

            var x = (polygon[j].U - polygon[i].U) * (point.V - polygon[i].V) / (polygon[j].V - polygon[i].V) + polygon[i].U;
            if (point.U < x) inside = !inside;
        }

        return inside;
    }

    private static Frac Centroid(Frac[] polygon) =>
        new(polygon.Average(p => p.U), polygon.Average(p => p.V));
}
