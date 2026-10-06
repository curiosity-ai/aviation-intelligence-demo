namespace Aeralis.Illustrations;

/// <summary>
/// The aircraft types Aeralis publishes an AutoCAD general-arrangement sheet for.
/// </summary>
/// <remarks>
/// All but two are three-view sheets. The Cirrane came to Aeralis with the programme rather
/// than off its own drawing boards, and its sheet was never redrawn: the CIRRANE-11 and
/// CIRRANE-13 publish a plan view on its own, so those two have
/// <see cref="ViewKind.Plan"/> and nothing else — ask
/// <see cref="AircraftIllustrator.AvailableViews"/> before picking a view.
/// </remarks>
public enum AircraftType
{
    Cirrane11,
    Cirrane13,
    Auster20,
    Auster24,
    Zephyr18,
    Zephyr20,
    Zephyr20Winglet,
    Zephyr22,
    Zephyr22Winglet,
    Zephyr24,
    Zephyr24Winglet,
    Solano30E,
    Solano30,
    Solano30F,
    Solano34,
    Solano38,
    Solano39,
    Vantor62,
    Vantor64,
    Vantor66,
    Vantor68,
    Meridian42,
    Meridian46,
    Titan88,
}

/// <summary>One of the three orthographic views on a published sheet.</summary>
public enum ViewKind
{
    /// <summary>Seen from above. Drawn nose-down, so the aircraft's left wing is on the right.</summary>
    Plan,

    /// <summary>Seen from the side. Drawn nose-left.</summary>
    Side,

    /// <summary>Seen head-on. Drawn fin-up, so the aircraft's left wing is on the right.</summary>
    Front,
}

/// <summary>
/// A part of the airframe an annotation can point at. Sections are named from the aircraft's
/// own point of view: <see cref="LeftWing"/> is the port wing, which appears on the right of
/// the plan and front views because both look along the aircraft's own forward axis.
/// </summary>
public enum AircraftSection
{
    Nose,
    Cockpit,
    ForwardFuselage,
    Fuselage,
    AftFuselage,
    LeftWing,
    RightWing,
    VerticalStabilizer,
    HorizontalStabilizer,
    Tail,
    LeftEngine,
    RightEngine,
    NoseLandingGear,
    MainLandingGear,
}
