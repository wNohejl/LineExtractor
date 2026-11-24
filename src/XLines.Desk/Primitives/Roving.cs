using Microsoft.AspNetCore.Components.Web;

namespace XLines.Desk.Primitives;

/// <summary>
/// The radiogroup keyboard contract, in one place: arrows move the choice, Home and End reach the
/// ends. A group is one Tab stop on its chosen option (a roving tabindex) and this says where the
/// arrows take it; the group selects and focuses what it is handed.
/// </summary>
/// <remarks>
/// A group that uses it renders <see cref="Marker"/> on its radiogroup element, and theme.js then holds
/// exactly <see cref="Keys"/> from scrolling the page beneath it — Blazor's preventDefault is one flag
/// for every key and would swallow Tab with them.
/// </remarks>
public static class Roving
{
    /// <summary>The attribute a roving radiogroup carries, so theme.js holds its keys from the page.</summary>
    public const string Marker = "data-roving";

    /// <summary>Every key <see cref="Next"/> answers; anything else — Tab first — passes.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp", "Home", "End"];

    /// <summary>
    /// Where a key moves the choice from <paramref name="current"/> in a group of
    /// <paramref name="count"/>: the next or previous option — round the ends when
    /// <paramref name="wrap"/>, a ring of hues rather than a list — or the first or the last. Null for a
    /// key the contract does not answer, and for a group with nothing to move to. An index that does
    /// not move (an arrow at an end that does not wrap) comes back as itself.
    /// </summary>
    public static int? Next(int current, int count, string key, bool wrap)
    {
        if (count < 1)
            return null;

        return key switch
        {
            "ArrowRight" or "ArrowDown" => wrap ? (current + 1) % count : Math.Min(current + 1, count - 1),
            "ArrowLeft" or "ArrowUp" => wrap ? (current - 1 + count) % count : Math.Max(current - 1, 0),
            "Home" => 0,
            "End" => count - 1,
            _ => null
        };
    }

    /// <summary>
    /// <see cref="Next(int, int, string, bool)"/> for a key as the browser reports it. A key with Ctrl,
    /// Alt or Meta held is the browser's or the machine's — Alt+ArrowLeft is Back, Ctrl+Home scrolls
    /// to the top — and theme.js lets it through, so the group lets it through too, rather than move
    /// the choice under a shortcut that was meant for something else. Shift alone is still the
    /// group's: theme.js holds a shifted arrow, so the group answers it.
    /// </summary>
    public static int? Next(int current, int count, KeyboardEventArgs e, bool wrap)
        => IsShortcut(e) ? null : Next(current, count, e.Key, wrap);

    /// <summary>A key pressed with Ctrl, Alt or Meta held: never the group's.</summary>
    public static bool IsShortcut(KeyboardEventArgs e) => e.CtrlKey || e.AltKey || e.MetaKey;
}
