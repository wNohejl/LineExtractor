using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Services;

namespace XLines.Web.Tests.Support;

/// <summary>
/// The Line movement window's reads for two games, Bills at Chiefs (<see cref="BoardRows.GameId"/>)
/// and Jets at Dolphins (<see cref="DolphinsId"/>), each with a moneyline on file. A game's snapshot
/// read or its form read can be held open, every snapshot read says whether it was asked to stop,
/// and a game can be left out of the picker's list while the game read still finds it — what a
/// window turned faster than it reads needs to be shown.
/// </summary>
public sealed class FakeLineMovementData : ILineMovementData
{
    public const int DolphinsId = 8;

    public List<Game> Games { get; } = [BoardRows.Game(), Dolphins()];

    /// <summary>Snapshot reads held open by game until the test sets them.</summary>
    public Dictionary<int, TaskCompletionSource> SnapshotGates { get; } = [];

    /// <summary>Form reads held open by game until the test sets them.</summary>
    public Dictionary<int, TaskCompletionSource> FormGates { get; } = [];

    /// <summary>Every snapshot read, in order, with the token it was given.</summary>
    public List<(int Game, CancellationToken Ct)> SnapshotReads { get; } = [];

    /// <summary>Games the list leaves out, as one past the picker's cap is.</summary>
    public HashSet<int> Unlisted { get; } = [];

    public static Game Dolphins(DateTimeOffset? startsAt = null) => new()
    {
        Id = DolphinsId,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        HomeTeam = new Team { Name = "Miami Dolphins", Abbrev = "MIA" },
        AwayTeam = new Team { Name = "New York Jets", Abbrev = "NYJ" },
        StartsAt = startsAt ?? DateTimeOffset.UtcNow.AddHours(5),
        Status = GameStatus.Scheduled
    };

    public Task<IReadOnlyList<Game>> GamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(Games.Where(g => !Unlisted.Contains(g.Id)).ToList());

    public Task<Game?> GameAsync(int gameId, CancellationToken ct = default)
        => Task.FromResult(Games.FirstOrDefault(g => g.Id == gameId));

    public async Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, string market, CancellationToken ct = default)
    {
        SnapshotReads.Add((gameId, ct));

        // Held open without heeding the token: the worst case, a read that comes back anyway.
        if (SnapshotGates.TryGetValue(gameId, out var gate))
            await gate.Task;

        var game = Games.First(g => g.Id == gameId);
        return
        [
            new OddsSnapshot { GameId = gameId, Market = market, Outcome = game.HomeTeam!.Name, Book = "draftkings", PriceAmerican = -120, CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-30) },
            new OddsSnapshot { GameId = gameId, Market = market, Outcome = game.AwayTeam!.Name, Book = "fanduel", PriceAmerican = 100, CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-30) }
        ];
    }

    public Task<bool> ScheduledAsync(CancellationToken ct = default) => Task.FromResult(false);

    public async Task<MatchupForm?> FormAsync(int gameId, TimeSpan window, CancellationToken ct = default)
    {
        if (FormGates.TryGetValue(gameId, out var gate))
            await gate.Task;

        return null;
    }

    public Task<SlateOverview> SlateAsync(CancellationToken ct = default) => Task.FromResult(SlateOverview.Empty);
}
