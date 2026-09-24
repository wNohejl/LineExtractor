using LineOps.Data.CrossReference;
using LineOps.Desk.Windowing;
using LineOps.Web.Windowing;
using MudBlazor;

namespace LineOps.Web.Services;

/// <summary>
/// What LineOps adds to the command palette: teams, players and games, each opening its window.
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
        var games = await search.GamesAsync(text, 6, ct);

        return
        [
            .. teams.Select(t => new DeskSearchResult(
                "Teams", t.Name, $"{t.Sport?.Key.ToUpperInvariant()} · {t.Abbrev}",
                Icons.Material.Filled.Shield, () => manager.OpenTeam(t.Id, t.Name))),

            .. games.Select(g => new DeskSearchResult(
                "Games", $"{g.AwayTeam?.Name} at {g.HomeTeam?.Name}",
                $"{LineOps.Web.Components.DisplayTime.Format(g.StartsAt, "ddd MMM d, HH:mm")} {LineOps.Web.Components.DisplayTime.Label} · {g.Sport?.Key.ToUpperInvariant()} · {g.Status}",
                Icons.Material.Filled.Event, () => manager.OpenGame(g))),

            .. players.Select(p => new DeskSearchResult(
                "Players", p.FullName,
                string.Join(" · ", new[] { p.Position, p.Team?.Name, p.Sport?.Key.ToUpperInvariant() }.Where(x => !string.IsNullOrEmpty(x))),
                Icons.Material.Filled.Person, () => manager.OpenPlayer(p.Id, p.FullName)))
        ];
    }
}
