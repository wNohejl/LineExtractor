namespace XLines.Desk.Theming;

/// <summary>
/// How round the desk's corners are, chosen.
///
/// <para>
/// The desk's radii are four tokens that grow with the surface — a tag, a control, a window, a
/// sheet (<c>--radius-sm</c>, <c>--radius</c>, <c>--radius-panel</c>, <c>--radius-sheet</c>, and
/// <c>--radius-xs</c> for the plate inside a control). A shape is one block that rescales all five
/// together, so the ladder keeps its order and a corner nested inside another (a gate's cap in its
/// track, a plate in its tab) stays concentric: every inner radius is written from the outer one,
/// never as a number of its own.
/// </para>
///
/// <para>
/// What is round by nature stays round in every shape: a switch's track and thumb, a radio's mark,
/// a status dot, an accent swatch. Those are circles and capsules, not corners — a square switch
/// reads as a checkbox and a square dot as a glyph — so they are drawn at 50% or 999px and no shape
/// touches them. Like the glass, a shape belongs to every family: it is geometry, not palette.
/// </para>
/// </summary>
public enum DeskShape
{
    /// <summary>Apple's radii, as the desk has always drawn them. The default, and stamps nothing.</summary>
    Standard,

    /// <summary>Tight corners, a step from square — stamped as <c>data-shape="sharp"</c>.</summary>
    Sharp,

    /// <summary>Generous corners; a control reads nearly as a capsule — <c>data-shape="round"</c>.</summary>
    Round
}
