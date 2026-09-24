namespace LineOps.Desk.Windowing;

/// <summary>
/// One thing the command palette can take you to.
/// </summary>
/// <param name="Group">The heading it is listed under — "Teams", "Windows".</param>
/// <param name="Title">What it is called.</param>
/// <param name="Detail">A line that tells two similar results apart: a date, a team, a sport.</param>
/// <param name="Icon">A MudBlazor icon string.</param>
/// <param name="Open">What choosing it does — usually opens a window on the desk.</param>
public sealed record DeskSearchResult(string Group, string Title, string? Detail, string Icon, Action Open);

/// <summary>
/// A source of palette results. The desk supplies its own windows and workspaces; an
/// application registers one of these (scoped, so it can reach the circuit's window manager) for
/// the things it knows about — teams, players, games — and the palette lists both.
/// </summary>
public interface IDeskSearch
{
    /// <summary>Results for what has been typed. Called after a short pause, never per keystroke.</summary>
    Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct);
}
