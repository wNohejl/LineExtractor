namespace XLines.Desk.Theming;

/// <summary>
/// How much air the desk keeps between things, chosen.
///
/// <para>
/// One block per position rescales the spacing ramp (<c>--space-*</c>), the chrome's fixed
/// heights (<c>--titlebar-h</c>, <c>--footer-h</c>), the control heights (<c>--control-h</c>,
/// <c>-sm</c>, <c>-lg</c>), the settings row (<c>--row-h</c>) and a table cell's padding
/// (<c>--cell-pad-y</c>, <c>--cell-inset</c>), which MudDataGrid reads through the bridge.
/// </para>
///
/// <para>
/// Compact takes the air out and keeps the targets pressable: the toolbar control, the small key and
/// a cell's padding stay at Standard's, and only the large key, a settings row and the title bar
/// come down, to 30px — nothing Standard draws at 28px or more falls under 28px, so a denser desk is
/// never a desk that is harder to hit. Roomy grows both. Like the glass and the shape, it belongs to every family.
/// </para>
/// </summary>
public enum DeskDensity
{
    /// <summary>The desk's rhythm as it has always been. The default, and stamps nothing.</summary>
    Standard,

    /// <summary>Tighter gaps and chrome, no 28px target made smaller — stamped as <c>data-density="compact"</c>.</summary>
    Compact,

    /// <summary>More air and taller controls — stamped as <c>data-density="roomy"</c>.</summary>
    Roomy
}
