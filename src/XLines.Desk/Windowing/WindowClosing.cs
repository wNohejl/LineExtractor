using XLines.Desk.Primitives;

namespace XLines.Desk.Windowing;

/// <summary>
/// The one way a window's close key closes it, wherever the key is drawn — the title bar of a plain
/// window, a tab in a group's strip, a strip's menu, the Delete key on a tab.
///
/// <para>
/// A window holding unsaved input asks before it goes: the HIG's confirm-before-closing rule, and
/// the only route by which a half-typed entry used to vanish with one click. Discard is
/// destructive, so Return lands on Keep editing. A tab behind another holds its input exactly as a
/// window in view does, so it asks the same question.
/// </para>
/// </summary>
public static class WindowClosing
{
    /// <summary>True when the window is gone: it was clean, or the answer was Discard.</summary>
    public static async Task<bool> CloseAsync(WindowManager manager, IDeskAlerts alerts, WindowInstance window)
    {
        var id = window.Id;

        if (manager.TryClose(id))
            return true;

        var discard = await alerts.ConfirmAsync(
            $"Discard {window.UnsavedInputLabel ?? "unsaved changes"}?",
            message: "The window has input that has not been saved.",
            confirmLabel: "Discard",
            cancelLabel: "Keep editing",
            destructive: true);

        if (discard)
            manager.Close(id);

        return discard;
    }
}
