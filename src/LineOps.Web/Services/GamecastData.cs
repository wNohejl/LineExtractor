using LineOps.Data.CrossReference;
using LineOps.Data.Gamecast;

namespace LineOps.Web.Services;

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
}

/// <summary>
/// The seam over <see cref="GamecastService"/> and <see cref="GameLogService"/>. The gamecast
/// service takes a context per call from the factory; the box score's reader is scoped, so it
/// gets a scope per call, as <see cref="GameWindowData"/> does.
/// </summary>
public sealed class GamecastData(GamecastService gamecast, IServiceScopeFactory scopes) : IGamecastData
{
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
