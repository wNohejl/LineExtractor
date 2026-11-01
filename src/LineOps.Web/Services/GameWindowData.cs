using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.CrossReference;
using LineOps.Ingestion.Services;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Web.Services;

/// <summary>
/// What the Game window reads, split by how often it changes (spec 2026-10-24 §5 item 5).
///
/// <para>
/// The window used to reload all of it — ten queries — on every <c>games</c>, <c>odds</c> or
/// <c>journal</c> notice, from any game, to move two digits. Now a change to its own game reads
/// <see cref="GameAsync"/> (one query), a price change reads <see cref="RowAsync"/>, and the slow
/// part — the bets, the seasons, the box score, the rosters and the form — is
/// <see cref="DetailAsync"/>, read when the journal moves, a bare notice says anything may have,
/// or the game goes final. An interface so the window renders in tests without a database.
/// </para>
/// </summary>
public interface IGameWindowData
{
    /// <summary>The game with its prices, as the Board composes a row.</summary>
    Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default);

    /// <summary>The game alone, with its sport and both sides: the score and the state of play.</summary>
    Task<Game?> GameAsync(int gameId, CancellationToken ct = default);

    /// <summary>
    /// Everything below the title. <paramref name="seasonYear"/> is the season the operator
    /// switched to, kept when either side has played in it; <paramref name="withBets"/> is false
    /// for a visitor's desk, which never reads the journal (ADR 0018).
    /// </summary>
    Task<GameWindowDetail> DetailAsync(Game game, int? seasonYear, bool withBets, CancellationToken ct = default);
}

/// <summary>What the Game window shows below its title. See <see cref="IGameWindowData.DetailAsync"/>.</summary>
/// <param name="Bout">The card and result when the game is a fight; null for a team sport.</param>
/// <param name="Seasons">The seasons either side has played in, newest first, at most three.</param>
/// <param name="SeasonYear">The season the team data and rosters were drawn from; null when neither side has one.</param>
public sealed record GameWindowDetail(
    Bout? Bout,
    IReadOnlyList<JournalEntry> Bets,
    IReadOnlyList<int> Seasons,
    int? SeasonYear,
    GameBoxScore? Box,
    MatchupForm? Matchup,
    TeamForm? HomeTeam,
    TeamForm? AwayTeam)
{
    public static readonly GameWindowDetail Empty = new(null, [], [], null, null, null, null, null);

    /// <summary>
    /// The season to draw on: the one asked for if either side played in it, else the game's own
    /// if it has games behind it, else the latest there is — a preview of an unplayed season
    /// draws on the one before.
    /// </summary>
    public static int? PickSeason(IReadOnlyList<int> seasons, int? asked, int gameSeason)
        => asked is { } year && seasons.Contains(year) ? year
            : seasons.Contains(gameSeason) ? gameSeason
            : seasons.Count > 0 ? seasons[0] : null;
}

/// <summary>
/// The seam over the real services. A scope per call, as the window did before it, so it never
/// holds a DbContext across renders.
/// </summary>
public sealed class GameWindowData(IServiceScopeFactory scopes) : IGameWindowData
{
    /// <summary>Wide on purpose: only reached when there is no season to bound by.</summary>
    private static readonly TimeSpan FallbackWindow = TimeSpan.FromDays(365);

    public async Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().GetRowAsync(gameId, ct);
    }

    public async Task<Game?> GameAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LineOpsDbContext>().Games
            .Include(g => g.Sport)
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);
    }

    public async Task<GameWindowDetail> DetailAsync(Game game, int? seasonYear, bool withBets, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var bout = FightSports.IsFight(game.Sport?.Key)
            ? (await services.GetRequiredService<FightService>().BoutsAsync([game.Id], ct)).GetValueOrDefault(game.Id)
            : null;

        IReadOnlyList<JournalEntry> bets = withBets
            ? await services.GetRequiredService<JournalService>().OnGameAsync(game.Id, ct)
            : [];

        var lookup = services.GetRequiredService<MatchupCrossReference>();
        var log = services.GetRequiredService<GameLogService>();

        var home = await log.SeasonsForTeamAsync(game.HomeTeamId);
        var away = await log.SeasonsForTeamAsync(game.AwayTeamId);
        var seasons = home.Concat(away).Distinct().OrderByDescending(y => y).Take(3).ToList();
        var season = GameWindowDetail.PickSeason(seasons, seasonYear, game.SeasonYear);

        // The box score is the answer once there is one; the season aggregate is the fallback
        // for a game that has not been played. Both are loaded because the fallback has to be
        // ready the moment the box score turns out to be empty.
        var box = await log.BoxScoreAsync(game.Id);
        var matchup = await lookup.GetAsync(game.Id, FallbackWindow, maxPlayersPerTeam: 8, seasonYear: season);

        // Only the recent record is wanted here — the roster comes from the matchup lookup
        // above, which resolves both sides in one query rather than two.
        var homeForm = await lookup.GetTeamAsync(game.HomeTeamId, FallbackWindow, maxPlayers: 0, seasonYear: season);
        var awayForm = await lookup.GetTeamAsync(game.AwayTeamId, FallbackWindow, maxPlayers: 0, seasonYear: season);

        return new GameWindowDetail(bout, bets, seasons, season, box, matchup, homeForm, awayForm);
    }
}
