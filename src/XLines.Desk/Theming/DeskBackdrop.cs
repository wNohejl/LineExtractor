namespace XLines.Desk.Theming;

/// <summary>
/// What is drawn on the desk's ground behind the windows, chosen.
///
/// <para>
/// Only the ground: the <c>.desk</c> element the windows are placed on. Windows are opaque and
/// chrome is material, so nothing printed on a window ever sits on the pattern; the one thing that
/// does is the empty desk's start screen, and every pattern is drawn from tokens the contrast tests
/// already hold secondary text to — <c>--surface-2</c> and <c>--surface-3</c> for the lines and dots,
/// and <c>--accent</c> mixed into <c>--surface-0</c> at <c>--backdrop-tint</c> for the glow, which
/// ThemeContrastTests measures on every desk in every accent. A Windows contrast theme gets the
/// plain ground whatever is chosen.
/// </para>
///
/// <para>
/// Dots is the faint grid the desk has always drawn, so it is the default and stamps nothing.
/// </para>
/// </summary>
public enum DeskBackdrop
{
    /// <summary>The ground alone, in <c>--surface-0</c> — <c>data-backdrop="plain"</c>.</summary>
    Plain,

    /// <summary>A soft glow of the accent from the top of the desk — <c>data-backdrop="gradient"</c>.</summary>
    Gradient,

    /// <summary>A faint square grid of hairlines — <c>data-backdrop="grid"</c>.</summary>
    Grid,

    /// <summary>The faint dot grid: windows read as placed, and the eye has a reference when dragging. The default.</summary>
    Dots
}
