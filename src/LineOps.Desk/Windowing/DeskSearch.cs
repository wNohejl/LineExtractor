using MudBlazor;

namespace LineOps.Desk.Windowing;

/// <summary>
/// One thing the command palette can take you to.
/// </summary>
/// <param name="Group">The heading it is listed under — "Customers", "Windows".</param>
/// <param name="Title">What it is called.</param>
/// <param name="Detail">A line that tells two similar results apart: a date, a region, an account.</param>
/// <param name="Icon">A MudBlazor icon string.</param>
/// <param name="Open">What choosing it does — usually opens a window on the desk.</param>
public sealed record DeskSearchResult(string Group, string Title, string? Detail, string Icon, Action Open);

/// <summary>
/// A source of palette results. The desk supplies its own windows and workspaces; an
/// application registers one of these (scoped, so it can reach the circuit's window manager) for
/// the things it knows about — customers, orders, products — and the palette lists both.
/// </summary>
public interface IDeskSearch
{
    /// <summary>Results for what has been typed. Called after a short pause, never per keystroke.</summary>
    Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct);
}

/// <summary>
/// The desk's own commands — the things its chords do — as palette results, so each one can
/// also be found by name and its chord learned from the line beside it. Matched on the title,
/// like the windows and workspaces; nothing here is listed until something is typed.
/// </summary>
/// <remarks>
/// The palette lists every registered <see cref="IDeskSearch"/>, so this needs registering
/// beside the application's own provider (scoped, for the circuit's window manager).
/// </remarks>
public sealed class DeskCommands(WindowManager manager) : IDeskSearch
{
    private const string Group = "Commands";

    public Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct)
    {
        DeskSearchResult[] all =
        [
            new(Group, "Move window left", "Ctrl+Alt+←", Icons.Material.Filled.West,
                () => Desk.MoveFocused(manager, -1)),
            new(Group, "Move window right", "Ctrl+Alt+→", Icons.Material.Filled.East,
                () => Desk.MoveFocused(manager, +1)),
            new(Group, "Settings", "Ctrl+,", Icons.Material.Filled.Tune,
                () => Desk.OpenSettings(manager))
        ];

        var typed = text.Trim();

        IReadOnlyList<DeskSearchResult> found = all
            .Where(c => typed.Length > 0 && c.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(found);
    }
}
