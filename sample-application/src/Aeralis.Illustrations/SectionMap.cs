namespace Aeralis.Illustrations;

/// <summary>
/// A point in a view's own space, as a fraction of that view's width and height. (0,0) is the
/// top-left of the drawing's bounding box and (1,1) the bottom-right.
/// </summary>
public readonly record struct Frac(double U, double V)
{
    public static Frac Lerp(Frac a, Frac b, double t) => new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);
}

/// <summary>
/// Where a section sits in a view, and which way along it the position parameter runs.
/// </summary>
/// <param name="Polygon">
/// A rough outline of the section in view fractions. It is deliberately generous: the sampler
/// intersects it with the drawing's actual silhouette, so the polygon only has to say
/// <em>which part</em> of the aircraft is meant, not trace its outline.
/// </param>
/// <param name="StationStart">Where <c>position = 0</c> falls — the nose, or a wing root.</param>
/// <param name="StationEnd">Where <c>position = 1</c> falls — the tail, or a wing tip.</param>
public sealed record SectionShape(Frac[] Polygon, Frac StationStart, Frac StationEnd);

/// <summary>
/// Rough section outlines for each view, shared by every aircraft type.
/// </summary>
/// <remarks>
/// <para>
/// One set of outlines covers the whole fleet because the drawings are consistent — every view
/// is drawn in the orientation this file documents, and airliners of this class share their
/// proportions closely enough that a wing root sits at much the same fraction of the span on a
/// ZEPHYR-18 as on a TITAN-88. What differs between types — where the geometry actually is —
/// comes from the silhouette the sampler measures per drawing, not from these numbers.
/// </para>
/// <para>
/// All three views are drawn looking along the aircraft's own forward axis or down onto it, so
/// the port wing lands on the right of the plan and front views. That is a property of the
/// drawings, not a mistake here.
/// </para>
/// </remarks>
internal static class SectionMap
{
    public static SectionShape Get(ViewKind view, AircraftSection section) => view switch
    {
        ViewKind.Plan => Plan(section),
        ViewKind.Side => Side(section),
        ViewKind.Front => Front(section),
        _ => throw new ArgumentOutOfRangeException(nameof(view)),
    };

    private static Frac[] Box(double u1, double v1, double u2, double v2) =>
        [new(u1, v1), new(u2, v1), new(u2, v2), new(u1, v2)];

    // Plan view: nose at the bottom (v = 1), tail at the top (v = 0), span across u.
    // The aircraft's left wing is therefore on the right of the image.
    private static SectionShape Plan(AircraftSection s) => s switch
    {
        AircraftSection.Nose => new(Box(0.42, 0.86, 0.58, 1.00), new(0.50, 1.00), new(0.50, 0.86)),
        AircraftSection.Cockpit => new(Box(0.44, 0.88, 0.56, 0.98), new(0.50, 0.98), new(0.50, 0.88)),
        AircraftSection.ForwardFuselage => new(Box(0.43, 0.60, 0.57, 0.98), new(0.50, 0.98), new(0.50, 0.60)),
        AircraftSection.Fuselage => new(Box(0.43, 0.08, 0.57, 1.00), new(0.50, 1.00), new(0.50, 0.08)),
        AircraftSection.AftFuselage => new(Box(0.43, 0.10, 0.57, 0.45), new(0.50, 0.45), new(0.50, 0.10)),

        // Wings are quads following the sweep. The nose is at the bottom, so sweeping aft
        // moves the tip towards smaller v: the tip sits nearer the top of the view than the
        // root does. The outline is generous because sweep angle varies across the fleet — the
        // silhouette narrows it down per drawing.
        AircraftSection.LeftWing => new([new(0.54, 0.72), new(1.00, 0.46), new(1.00, 0.22), new(0.54, 0.35)],
            new(0.56, 0.55), new(0.98, 0.34)),
        AircraftSection.RightWing => new([new(0.46, 0.72), new(0.00, 0.46), new(0.00, 0.22), new(0.46, 0.35)],
            new(0.44, 0.55), new(0.02, 0.34)),

        AircraftSection.VerticalStabilizer => new(Box(0.46, 0.02, 0.54, 0.20), new(0.50, 0.20), new(0.50, 0.02)),
        AircraftSection.HorizontalStabilizer => new([new(0.18, 0.10), new(0.82, 0.10), new(0.78, 0.00), new(0.22, 0.00)],
            new(0.50, 0.06), new(0.20, 0.06)),
        AircraftSection.Tail => new(Box(0.16, 0.00, 0.84, 0.22), new(0.50, 0.22), new(0.50, 0.00)),

        AircraftSection.LeftEngine => new(Box(0.56, 0.46, 0.80, 0.70), new(0.58, 0.58), new(0.78, 0.58)),
        AircraftSection.RightEngine => new(Box(0.20, 0.46, 0.44, 0.70), new(0.42, 0.58), new(0.22, 0.58)),

        AircraftSection.NoseLandingGear => new(Box(0.45, 0.78, 0.55, 0.90), new(0.50, 0.90), new(0.50, 0.78)),
        AircraftSection.MainLandingGear => new(Box(0.41, 0.48, 0.59, 0.68), new(0.50, 0.48), new(0.50, 0.68)),

        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    // Side view: nose at the left (u = 0), tail at the right (u = 1), fin at the top (v = 0).
    // Both wings project onto the same place, so left and right share an outline.
    private static SectionShape Side(AircraftSection s) => s switch
    {
        AircraftSection.Nose => new(Box(0.00, 0.38, 0.12, 0.90), new(0.01, 0.62), new(0.12, 0.62)),
        AircraftSection.Cockpit => new(Box(0.02, 0.40, 0.11, 0.66), new(0.03, 0.52), new(0.11, 0.52)),
        AircraftSection.ForwardFuselage => new(Box(0.04, 0.36, 0.42, 0.92), new(0.05, 0.62), new(0.42, 0.62)),
        AircraftSection.Fuselage => new(Box(0.02, 0.34, 0.88, 0.94), new(0.02, 0.62), new(0.88, 0.62)),
        AircraftSection.AftFuselage => new(Box(0.58, 0.34, 0.92, 0.88), new(0.58, 0.60), new(0.92, 0.60)),

        AircraftSection.LeftWing or AircraftSection.RightWing =>
            new(Box(0.30, 0.58, 0.72, 0.80), new(0.36, 0.70), new(0.70, 0.70)),

        AircraftSection.VerticalStabilizer => new([new(0.74, 0.52), new(1.00, 0.52), new(1.00, 0.00), new(0.86, 0.00)],
            new(0.80, 0.50), new(0.95, 0.06)),
        AircraftSection.HorizontalStabilizer => new(Box(0.82, 0.44, 1.00, 0.66), new(0.84, 0.56), new(1.00, 0.56)),
        AircraftSection.Tail => new(Box(0.74, 0.00, 1.00, 0.80), new(0.76, 0.60), new(0.98, 0.10)),

        AircraftSection.LeftEngine or AircraftSection.RightEngine =>
            new(Box(0.26, 0.68, 0.50, 0.94), new(0.28, 0.80), new(0.48, 0.80)),

        AircraftSection.NoseLandingGear => new(Box(0.06, 0.84, 0.20, 1.00), new(0.13, 0.85), new(0.13, 1.00)),
        AircraftSection.MainLandingGear => new(Box(0.36, 0.84, 0.60, 1.00), new(0.48, 0.85), new(0.48, 1.00)),

        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    // Front view: fin at the top (v = 0), gear at the bottom (v = 1), span across u.
    // The aircraft's left wing is on the right of the image.
    private static SectionShape Front(AircraftSection s) => s switch
    {
        AircraftSection.Nose or AircraftSection.Cockpit => new(Box(0.43, 0.30, 0.57, 0.58), new(0.50, 0.32), new(0.50, 0.56)),
        AircraftSection.ForwardFuselage or AircraftSection.AftFuselage or AircraftSection.Fuselage =>
            new(Box(0.42, 0.26, 0.58, 0.86), new(0.50, 0.28), new(0.50, 0.84)),

        // Dihedral lifts the tip, so the outboard end sits a little higher than the root.
        AircraftSection.LeftWing => new([new(0.58, 0.50), new(1.00, 0.38), new(1.00, 0.56), new(0.58, 0.72)],
            new(0.60, 0.60), new(0.97, 0.47)),
        AircraftSection.RightWing => new([new(0.42, 0.50), new(0.00, 0.38), new(0.00, 0.56), new(0.42, 0.72)],
            new(0.40, 0.60), new(0.03, 0.47)),

        AircraftSection.VerticalStabilizer => new(Box(0.46, 0.00, 0.54, 0.32), new(0.50, 0.32), new(0.50, 0.00)),
        AircraftSection.HorizontalStabilizer => new(Box(0.24, 0.18, 0.76, 0.36), new(0.50, 0.27), new(0.26, 0.27)),
        AircraftSection.Tail => new(Box(0.24, 0.00, 0.76, 0.36), new(0.50, 0.34), new(0.50, 0.02)),

        AircraftSection.LeftEngine => new(Box(0.58, 0.54, 0.82, 0.84), new(0.60, 0.68), new(0.80, 0.68)),
        AircraftSection.RightEngine => new(Box(0.18, 0.54, 0.42, 0.84), new(0.40, 0.68), new(0.20, 0.68)),

        AircraftSection.NoseLandingGear => new(Box(0.45, 0.82, 0.55, 1.00), new(0.50, 0.83), new(0.50, 1.00)),
        AircraftSection.MainLandingGear => new(Box(0.32, 0.82, 0.68, 1.00), new(0.50, 0.83), new(0.50, 1.00)),

        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}
