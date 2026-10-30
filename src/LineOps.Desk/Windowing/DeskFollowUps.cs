namespace LineOps.Desk.Windowing;

/// <summary>
/// The desk's own floating layer, for follow-ups launched from inside something that already
/// floats.
///
/// <para>
/// A panel normally owns its launcher and draws its layer inside itself (<c>FollowUpLayer</c>),
/// which is right while the panel is a desk window. It is wrong once the panel is itself a
/// floating dialog: the dialog's material (<c>backdrop-filter</c>) makes it the containing block
/// for anything <c>position: fixed</c> inside it, so a dialog opened from within is positioned
/// against, and clipped by, the dialog that opened it. A list row opening an order form was the
/// case that found it — the Save key sat below the visible edge. Follow-ups launched here are
/// drawn by the desk itself, beside the windows, where nothing clips them.
/// </para>
///
/// <para>Circuit-scoped, like every other piece of desk state.</para>
/// </summary>
public sealed class DeskFollowUps
{
    public FollowUpLauncher Launcher { get; } = new();
}
