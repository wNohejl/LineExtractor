namespace LineOps.Web.Theming;

/// <summary>
/// Which desk the operator is looking at.
///
/// <para>
/// Three positions rather than two, and <see cref="System"/> is not a convenience — it
/// is a different <i>kind</i> of answer. <see cref="Dark"/> and <see cref="Light"/> are
/// statements about the desk; <see cref="System"/> is a statement about who decides, and
/// it keeps deciding. An operator on <see cref="System"/> whose machine turns dark at
/// sunset gets a desk that turns with it, which a two-position toggle cannot express at
/// all: the moment you resolve "system" down to whichever theme it meant at load, you
/// have thrown away the only thing the setting was for.
/// </para>
/// </summary>
public enum DeskThemeMode
{
    /// <summary>Follow <c>prefers-color-scheme</c>, and keep following it.</summary>
    System,

    /// <summary>The desk is dark, whatever the machine thinks.</summary>
    Dark,

    /// <summary>The desk is light, whatever the machine thinks.</summary>
    Light
}
