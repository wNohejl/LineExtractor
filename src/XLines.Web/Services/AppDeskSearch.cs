using XLines.Data.CrossReference;
using XLines.Desk.Windowing;
using XLines.Web.Windowing;
using MudBlazor;

namespace XLines.Web.Services;

/// <summary>
/// What XLines adds to the command palette: teams, players and games, each opening its window.
/// The desk supplies windows and workspaces itself.
/// </summary>
public sealed class AppDeskSearch(IServiceScopeFactory scopes, WindowManager manager) : IDeskSearch
{
    public async Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct)
    {
        // A scope per search: the palette outlives any one query, and a context per query keeps
        // two quick searches from ever sharing one.
        await using var scope = scopes.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<SearchService>();

        var teams = await search.TeamsAsync(text, 5, ct);
        var players = await search.PlayersAsync(text, 6, ct);
        var games = await search.GamesAsync(text, 6, ct: ct);

        return
        [
            .. teams.Select(t => new DeskSearchResult(
                "Teams", t.Name, $"{t.Sport?.Key.ToUpperInvariant()} · {t.Abbrev}",
                Icons.Material.Filled.Shield, () => manager.OpenTeam(t.Id, t.Name))),

            .. games.Select(g => new DeskSearchResult(
                "Games", XLines.Web.Windowing.WindowShortcuts.Describe(g),
                $"{XLines.Web.Components.DisplayTime.Format(g.StartsAt, "ddd MMM d, HH:mm")} {XLines.Web.Components.DisplayTime.Label} · {g.Sport?.Key.ToUpperInvariant()} · {g.Status}",
                Icons.Material.Filled.Event, () => manager.OpenGame(g))),

            .. players.Select(p => new DeskSearchResult(
                "Players", p.FullName,
                string.Join(" · ", new[] { p.Position, p.Team?.Name, p.Sport?.Key.ToUpperInvariant() }.Where(x => !string.IsNullOrEmpty(x))),
                Icons.Material.Filled.Person, () => manager.OpenPlayer(p.Id, p.FullName)))
        ];
    }
}
