using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Data.Gamecast;
using LineOps.Web.Services;

namespace LineOps.Web.Tests.Support;

/// <summary>
/// The Gamecast window's data seam, in memory: a header, the plays and drives a test sets, and a
/// record of every read — the sequence each plays read asked after, null for every play — so a
/// test can say whether a notice appended, corrected or reloaded.
///
/// <para>
/// <see cref="Gate"/> holds every plays read until the test releases it, so two loads really
/// interleave: both are past their first await before either has its plays.
/// </para>
/// </summary>
public sealed class FakeGamecastData : IGamecastData
{
    public const int HomeId = 1;
    public const int AwayId = 2;

    public GamecastHeader? Header { get; set; } = Live();

    /// <summary>When set, the header read throws it.</summary>
    public Exception? HeaderError { get; set; }

    /// <summary>When set, every plays read throws it.</summary>
    public Exception? PlaysError { get; set; }

    public List<GamecastPlay> Plays { get; } = [];

    public List<GamecastDrive> Drives { get; } = [];

    public GameBoxScore? Box { get; set; }

    /// <summary>When set, every plays read waits on it before answering from <see cref="Plays"/>.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Plays reads that have reached the gate and not yet passed it.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    private int _waiting;

    public int HeaderReads { get; private set; }
    public int DriveReads { get; private set; }
    public int BoxReads { get; private set; }

    /// <summary>The <c>afterSequence</c> of every plays read, in order; null for a read of every play.</summary>
    public List<long?> PlaysReadsAfter { get; } = [];

    public Task<GamecastHeader?> HeaderAsync(int gameId, CancellationToken ct = default)
    {
        HeaderReads++;
        return HeaderError is { } error ? Task.FromException<GamecastHeader?>(error) : Task.FromResult(Header);
    }

    public Task<IReadOnlyList<GamecastPlay>> PlaysAsync(int gameId, CancellationToken ct = default)
        => Read(null);

    public Task<IReadOnlyList<GamecastPlay>> PlaysSinceAsync(int gameId, long afterSequence, CancellationToken ct = default)
        => Read(afterSequence);

    private async Task<IReadOnlyList<GamecastPlay>> Read(long? after)
    {
        PlaysReadsAfter.Add(after);

        if (PlaysError is { } error)
            throw error;

        if (Gate is { } gate)
        {
            Interlocked.Increment(ref _waiting);
            await gate.Task;
            Interlocked.Decrement(ref _waiting);
        }

        return Plays.Where(p => after is not { } a || p.Sequence > a)
            .OrderBy(p => p.Sequence).ThenBy(p => p.Id)
            .ToList();
    }

    public Task<IReadOnlyList<GamecastDrive>> DrivesAsync(int gameId, CancellationToken ct = default)
    {
        DriveReads++;
        return Task.FromResult<IReadOnlyList<GamecastDrive>>(Drives.ToList());
    }

    public Task<GameBoxScore?> BoxScoreAsync(int gameId, CancellationToken ct = default)
    {
        BoxReads++;
        return Task.FromResult(Box);
    }

    /// <summary>PHI at WSH, live in the second quarter: PHI's ball, 2nd and 6 at WSH 34 — 34 yards from the home goal.</summary>
    public static GamecastHeader Live(string sport = "nfl", GameStatus status = GameStatus.Live) => new(
        GameId: BoardRows.GameId,
        SportKey: sport,
        SportName: sport.ToUpperInvariant(),
        Home: new GamecastTeam(HomeId, "Washington Commanders", "WSH"),
        Away: new GamecastTeam(AwayId, "Philadelphia Eagles", "PHI"),
        StartsAt: DateTimeOffset.UtcNow.AddMinutes(-40),
        Status: status,
        HomeScore: 3,
        AwayScore: 7,
        Period: 2,
        DisplayClock: "4:12",
        ClockSeconds: 252,
        StatusDetail: "4:12 - 2nd",
        PossessionTeamId: AwayId,
        Down: 2,
        Distance: 6,
        YardLine: 34,
        DownDistanceText: "2nd & 6 at WSH 34",
        PossessionText: "WSH 34",
        IsRedZone: false,
        HomeTimeouts: 3,
        AwayTimeouts: 2,
        LastPlay: "J.Hurts pass short right to A.Brown for 9 yards",
        HomeLinescore: [3],
        AwayLinescore: [7],
        Diamond: null,
        LiveUpdatedAt: DateTimeOffset.UtcNow,
        Close: new GamecastClose(3m, 44.5m, true));

    public static GamecastPlay Play(long sequence, int? drive, string text, bool scoring = false, double? win = null, long? id = null)
        => new(
            Id: id ?? 1000 + sequence, Sequence: sequence, DriveSeq: drive, Period: 2, Clock: "5:00", ClockSeconds: 300,
            Type: "Pass", Text: text, HomeScore: 3, AwayScore: 7, IsScoring: scoring, IsTurnover: false,
            IsPenalty: false, Yardage: 5, TeamId: AwayId, Start: null, End: null,
            HomeWinPct: win, SpreadCoverPctHome: null, TotalOverPct: null,
            Wallclock: DateTimeOffset.UtcNow, Modified: null);

    public static GamecastDrive Drive(int sequence, long first, long last, bool current, string? result = null,
        int? plays = null, int? yards = null, string? elapsed = null, bool score = false)
        => new(sequence, AwayId, "PHI", null, result, yards, plays, elapsed, score, current,
            2, "15:00", 75, null, null, null, (int)(last - first + 1), first, last);
}
