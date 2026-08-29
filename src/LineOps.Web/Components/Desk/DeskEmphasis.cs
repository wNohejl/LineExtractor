namespace LineOps.Web.Components.Desk;

/// <summary>
/// How much visual weight a button claims, following Apple's button hierarchy.
///
/// <para>
/// This replaces ADR 0008's <c>DeskTone</c>, which named a consequence and painted it as
/// a hue. Under ADR 0016 hue is no longer a channel: an interface with five coloured
/// button types has no primary action, only five competing ones. Emphasis says how loud;
/// <see cref="DeskRole"/> says whether the action is dangerous. Those are the two
/// questions a caller can actually answer.
/// </para>
///
/// <para>
/// The rule at a call site: <b>exactly one Filled per context.</b> If a panel seems to
/// need two, one of them is not the primary action.
/// </para>
/// </summary>
public enum DeskEmphasis
{
    /// <summary>
    /// Transparent until pointed at. Toolbar and inline actions, and the escape from a
    /// thing (Cancel, Close, Back) — anything that must be available without competing
    /// with the action beside it that commits.
    /// </summary>
    Plain,

    /// <summary>
    /// An accent wash behind accent text. Secondary actions that still need to be found
    /// at a glance in a dense panel.
    /// </summary>
    Tinted,

    /// <summary>
    /// Solid accent. The one action a context exists to commit — save, apply, run.
    /// </summary>
    Filled
}

/// <summary>
/// Whether an action destroys something. Kept separate from <see cref="DeskEmphasis"/>
/// because a destructive action can be any weight: a Filled "Delete" in a confirmation
/// alert, a Plain "Remove" in a row's overflow menu.
/// </summary>
public enum DeskRole
{
    Normal,

    /// <summary>
    /// Destroys, or is hard to undo. Renders in red, and never becomes the default
    /// button in an alert — see DeskAlert.
    /// </summary>
    Destructive
}

/// <summary>How much of the panel a button claims. Chrome is small, a page's intent is large.</summary>
public enum DeskKeySize
{
    Small,
    Medium,
    Large
}
