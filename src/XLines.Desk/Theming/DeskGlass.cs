namespace XLines.Desk.Theming;

/// <summary>
/// How much the glass lets through, chosen.
///
/// <para>
/// The desk's floating chrome — the header, the footer, menus, popovers, dialogs and sheets — is
/// a material: a translucent fill over a blur of whatever it floats above. Clear is that material
/// as the desk has always drawn it. Tinted is the same material made markedly more opaque, so the
/// words on it hold against a busy desk beneath, while it keeps its blur and so still reads as
/// glass. It is macOS's own Liquid Glass setting (System Settings › Appearance), and the HIG's
/// Materials guidance names it beside Reduce Transparency and Increase Contrast as a preference
/// the glass must answer.
/// </para>
///
/// <para>
/// It sits between the two looks the desk already had. Reduce Transparency, the machine's
/// setting, is fully opaque and drops the blur; Tinted is the operator's step toward it that
/// stops short of a flat fill. When both apply the machine's wins, because a preference the
/// desk can soften is not the preference it was asked for — and with no glass left to tint,
/// the Window manager does not offer the choice.
/// </para>
///
/// <para>
/// Unlike the <see cref="DeskAccent"/>, it belongs to every family. It changes what the
/// materials are made of, not which palette they are in: the stylesheet derives each tinted
/// material from the showing family's own, so no family declares a tinted tier of its own and
/// a family added later is tinted without being told.
/// </para>
/// </summary>
public enum DeskGlass
{
    /// <summary>The materials as each family declares them. The default, and the desk the product has always been.</summary>
    Clear,

    /// <summary>
    /// The materials raised toward the opaque tier beneath them, blur kept — stamped as
    /// <c>data-glass="tinted"</c> on <c>&lt;html&gt;</c>.
    /// </summary>
    Tinted
}
