namespace XLines.Ingestion.Services;

/// <summary>Which of the gamecast feed's lanes a game in play is read in.</summary>
public enum GamecastLane
{
    /// <summary>Read every poll: one of the first <see cref="Configuration.GamecastOptions.MaxFollowedGames"/>.</summary>
    Fast,

    /// <summary>Past the ceiling, read in rotation, <see cref="Configuration.GamecastOptions.SlowLanePerPoll"/> games a poll.</summary>
    Slow,

    /// <summary>Its reads kept failing or kept saying "scheduled", and it waits out its back-off.</summary>
    BackedOff
}

/// <summary>
/// What the gamecast feed did on its last poll.
///
/// <para>
/// <see cref="InPlay"/> is every game in play the feed could address; <see cref="FastLaneSize"/>
/// the games it read in the fast lane. <see cref="Games"/> holds each game in play the feed is
/// reading or holding back, with its lane: a game it has finished with is not listed, and neither
/// is a game past the ceiling while the slow lane is off, because the feed is not reading it.
/// <see cref="LastReadAt"/> is when the feed last completed a read of a game, so a window can say
/// how fresh its plays are between a slow game's turns.
/// </para>
/// </summary>
public sealed record GamecastFeedSnapshot(
    DateTimeOffset At,
    int InPlay,
    int FastLaneSize,
    IReadOnlyDictionary<int, GamecastLane> Games,
    IReadOnlyDictionary<int, DateTimeOffset> LastReadAt);

/// <summary>
/// Where the windows and the gamecast feed meet, in memory and in this process only.
///
/// <para>
/// A window watching a game says so with <see cref="Watch"/>, and the feed puts watched games
/// first in its fast lane, so the game someone is looking at is the one read every poll when the
/// ceiling bites. Interest is a lease of <see cref="Lease"/>, renewed with <see cref="Renew"/>: a
/// window whose circuit dies without saying goodbye would otherwise hold a fast-lane slot for the
/// day. Each <see cref="Watch"/> counts once and each <see cref="Unwatch"/> takes one back, so one
/// window closing does not cut off another still open on the same game; <see cref="Renew"/>
/// moves the lease and leaves the count alone, so a window that watches once, renews and lets go
/// once leaves nothing behind. The lease running out drops the game whatever the count, and is
/// the backstop for a window that never said goodbye.
/// </para>
///
/// <para>
/// The feed writes <see cref="Snapshot"/> after every poll. It is null until the feed in this
/// process has polled once, and stays null on a host that does not run the feed — which is what a
/// caller reads it for: null says nobody here is reading plays, not that no game is on.
/// </para>
/// </summary>
public sealed class GamecastLanes
{
    /// <summary>How long one <see cref="Watch"/> holds a game's interest without being renewed.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(90);

    private sealed class Interest
    {
        public int Count { get; set; }
        public DateTimeOffset Until { get; set; }
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<int, Interest> _interest = [];
    private volatile GamecastFeedSnapshot? _snapshot;

    /// <summary>A window is watching this game, for <see cref="Lease"/> from now.</summary>
    public void Watch(int gameId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_interest.TryGetValue(gameId, out var interest))
                _interest[gameId] = interest = new Interest();

            interest.Count++;
            interest.Until = now + Lease;
        }
    }

    /// <summary>
    /// A window still watching this game: its lease runs for <see cref="Lease"/> from now. The
    /// count is not touched — calling <see cref="Watch"/> again to renew would count the window
    /// twice. A game not watched, or whose lease has already run out, is left unwatched: a renewal
    /// starts nothing.
    /// </summary>
    public void Renew(int gameId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_interest.TryGetValue(gameId, out var interest) && interest.Until > now)
                interest.Until = now + Lease;
        }
    }

    /// <summary>A window stopped watching this game. The game stays watched while another window holds it.</summary>
    public void Unwatch(int gameId)
    {
        lock (_gate)
        {
            if (_interest.TryGetValue(gameId, out var interest) && --interest.Count <= 0)
                _interest.Remove(gameId);
        }
    }

    /// <summary>The games watched now; leases that have run out are dropped on the way.</summary>
    public IReadOnlySet<int> Watched(DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var expired in _interest.Where(i => i.Value.Until <= now).Select(i => i.Key).ToList())
                _interest.Remove(expired);

            return _interest.Keys.ToHashSet();
        }
    }

    /// <summary>The feed's last poll, or null when the feed in this process has not polled.</summary>
    public GamecastFeedSnapshot? Snapshot => _snapshot;

    /// <summary>Written by the feed after every poll.</summary>
    public void Report(GamecastFeedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    /// <summary>The lane the game had on the feed's last poll, or null when the feed is not reading it.</summary>
    public GamecastLane? LaneOf(int gameId)
        => _snapshot is { } snapshot && snapshot.Games.TryGetValue(gameId, out var lane) ? lane : null;
}
