namespace LineOps.Desk.Primitives;

/// <summary>
/// Which side of its trigger a <c>DeskTooltip</c> opens on. Leading and trailing rather than
/// left and right, so a right-to-left page puts it where the reader expects.
/// </summary>
public enum DeskTooltipPlacement
{
    /// <summary>Above the trigger — the usual side, clear of the pointer.</summary>
    Top,

    /// <summary>Below, for a trigger near the top of a window where above would be clipped.</summary>
    Bottom,

    /// <summary>On the leading side.</summary>
    Start,

    /// <summary>On the trailing side.</summary>
    End
}
