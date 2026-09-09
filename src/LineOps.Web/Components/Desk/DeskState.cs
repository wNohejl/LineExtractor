namespace LineOps.Web.Components.Desk;

/// <summary>
/// TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.
///
/// What something *is*, for the things that report rather than act.
///
/// <para>
/// The desk used to have a single "tone" vocabulary that both painted buttons and named
/// states. This is the half of it that was never about buttons. Hue stops being an
/// affordance channel — an interface with five coloured button types has no primary action,
/// only five competing ones — and moves to where states actually live: a figure, a tag, a
/// toast, the pulse strip. Those still need to say "this is healthy" or "this breached", and
/// they say it here.
/// </para>
///
/// <para>
/// Hue is always the second channel, never the only one. A tag carries its word, a metric
/// carries its label and note, a toast carries its sentence — the state is legible with the
/// colour removed, and the colour only sharpens a reading that already works.
/// </para>
///
/// <para>
/// Buttons do not take a <c>DeskState</c>. They take <see cref="DeskEmphasis"/> and
/// <see cref="DeskRole"/>, because the question a button answers is how much weight it
/// claims and whether it destroys something — not what colour it is.
/// </para>
/// </summary>
public enum DeskState
{
    /// <summary>No state worth colouring. The default.</summary>
    Neutral,

    /// <summary>Healthy, settled, moved your way, won.</summary>
    Positive,

    /// <summary>Breached, failed, moved against you, lost.</summary>
    Negative,

    /// <summary>Proceeding, but the operator should know the cost. Budget pressure, pending.</summary>
    Warning,

    /// <summary>Called out for attention without implying good or bad.</summary>
    Info
}
