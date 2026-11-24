using XLines.Data.CrossReference;
using XLines.Data.Gamecast;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using Microsoft.Extensions.Options;

namespace XLines.Web.Services;

/// <summary>
/// What the Gamecast window reads (spec 2026-10-24 §6.1, §6.4): the header, the plays after the
/// last one it holds, the drives, and the box score once the game has one.
///
/// <para>
/// An interface so the window renders in tests without a database, as the Game window's
/// <see cref="IGameWindowData"/> does. The probability line and the scoring summary are not
/// asked for separately: every play the window holds already carries its probabilities and its
/// scoring flag, so both are drawn from the plays, and an append moves them with no query of
/// their own.
/// </para>
/// </summary>
public interface IGamecastData
{
    /// <summary>The game, the state of play and the pre-game close; null when there is no such game.</summary>
    Task<GamecastHeader?> HeaderAsync(int gameId, CancellationToken ct = default);

    /// <summary>Every play, in order — those stored at sequence zero included.</summary>
    Task<IReadOnlyList<GamecastPlay>> PlaysAsync(int gameId, CancellationToken ct = default);

    /// <summary>The plays strictly after <paramref name="afterSequence"/>, in order.</summary>
    Task<IReadOnlyList<GamecastPlay>> PlaysSinceAsync(int gameId, long afterSequence, CancellationToken ct = default);

    /// <summary>The drives in order, each with the sequence range of the plays held under it.</summary>
    Task<IReadOnlyList<GamecastDrive>> DrivesAsync(int gameId, CancellationToken ct = default);

    /// <summary>The box score, the Game window's own read; null until the game has one.</summary>
    Task<GameBoxScore?> BoxScoreAsync(int gameId, CancellationToken ct = default);

    /// <summary>
    /// A window is open on this game: the feed in this process reads it first
    /// (<see cref="GamecastLanes.Watch"/>). Called once per window; renewed with <see cref="Renew"/>
    /// and given back with <see cref="Unwatch"/>, exactly once each.
    /// </summary>
    void Watch(int gameId);

    /// <summary>The window is still open on the game: its interest's lease runs on, counted once.</summary>
    void Renew(int gameId);

    /// <summary>The window closed, or the game ended: its interest is given back.</summary>
    void Unwatch(int gameId);

    /// <summary>How the feed in this process is reading the game, or that it does not run here.</summary>
    GamecastFeedReading Feed(int gameId);
}

/// <summary>
/// How the gamecast feed stood on its last poll, for one game, as the window words it.
///
/// <para>
/// <see cref="Here"/> is false on a host whose feed has not polled — the feed runs on the machine
/// that runs the schedule — and then nothing else but the configuration is known. Otherwise
/// <see cref="Lane"/> is the game's lane (null when the feed is not reading it),
/// <see cref="InPlay"/> the games in play, <see cref="FastLaneSize"/> those read every poll and
/// <see cref="SlowGames"/> those read in turn. <see cref="Poll"/> and <see cref="SlowLanePerPoll"/>
/// are the configured pace (<see cref="GamecastOptions"/>). <see cref="Promoted"/> says the game
/// is watched and the watched games fit the fast lane, so its next poll reads it every time.
/// </para>
/// </summary>
public sealed record GamecastFeedReading(
    bool Here,
    GamecastLane? Lane,
    int InPlay,
    int FastLaneSize,
    int SlowGames,
    TimeSpan Poll,
    int SlowLanePerPoll,
    bool Promoted)
{
    /// <summary>The feed does not run in this process: only the configured pace is known.</summary>
    public static GamecastFeedReading NotHere(TimeSpan poll, int slowLanePerPoll)
        => new(false, null, 0, 0, 0, poll, slowLanePerPoll, false);

    /// <summary>
    /// How often a game in turn is read: ceil(slow games / per poll) polls. Null when the turn is
    /// off (<see cref="SlowLanePerPoll"/> zero).
    /// </summary>
    public TimeSpan? SlowEvery => SlowLanePerPoll <= 0
        ? null
        : Poll * Math.Ceiling(Math.Max(1, SlowGames) / (double)SlowLanePerPoll);
}

/// <summary>
/// The seam over <see cref="GamecastService"/> and <see cref="GameLogService"/>, and over the
/// feed's lanes (<see cref="GamecastLanes"/>, a singleton of the ingestion library, resolvable
/// where the feed does not run). The gamecast service takes a context per call from the factory;
/// the box score's reader is scoped, so it gets a scope per call, as <see cref="GameWindowData"/> does.
/// </summary>
public sealed class GamecastData(
    GamecastService gamecast,
    IServiceScopeFactory scopes,
    GamecastLanes lanes,
    IOptions<IngestionOptions> options) : IGamecastData
{
    public void Watch(int gameId) => lanes.Watch(gameId, DateTimeOffset.UtcNow);

    public void Renew(int gameId) => lanes.Renew(gameId, DateTimeOffset.UtcNow);

    public void Unwatch(int gameId) => lanes.Unwatch(gameId);

    public GamecastFeedReading Feed(int gameId)
    {
        var pace = options.Value.Gamecast;

        // Read once: the feed replaces the snapshot after every poll.
        if (lanes.Snapshot is not { } snapshot)
            return GamecastFeedReading.NotHere(pace.Poll, pace.SlowLanePerPoll);

        var watched = lanes.Watched(DateTimeOffset.UtcNow);

        return new GamecastFeedReading(
            Here: true,
            Lane: snapshot.Games.TryGetValue(gameId, out var lane) ? lane : null,
            InPlay: snapshot.InPlay,
            FastLaneSize: snapshot.FastLaneSize,
            SlowGames: snapshot.Games.Values.Count(l => l == GamecastLane.Slow),
            Poll: pace.Poll,
            SlowLanePerPoll: pace.SlowLanePerPoll,
            Promoted: watched.Contains(gameId) && watched.Count <= pace.MaxFollowedGames);
    }

    public Task<GamecastHeader?> HeaderAsync(int gameId, CancellationToken ct = default)
        => gamecast.HeaderAsync(gameId, ct);

    public Task<IReadOnlyList<GamecastPlay>> PlaysAsync(int gameId, CancellationToken ct = default)
        => gamecast.PlaysAsync(gameId, ct);

    public Task<IReadOnlyList<GamecastPlay>> PlaysSinceAsync(int gameId, long afterSequence, CancellationToken ct = default)
        => gamecast.PlaysSinceAsync(gameId, afterSequence, ct);

    public Task<IReadOnlyList<GamecastDrive>> DrivesAsync(int gameId, CancellationToken ct = default)
        => gamecast.DrivesAsync(gameId, ct);

    public async Task<GameBoxScore?> BoxScoreAsync(int gameId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GameLogService>().BoxScoreAsync(gameId, ct);
    }
}
