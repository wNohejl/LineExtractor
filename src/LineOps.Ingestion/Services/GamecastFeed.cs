using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Follows games in play, play by play (spec 2026-10-24 §6.3).
///
/// <para>
/// Every <see cref="GamecastOptions.Poll"/> it asks which games are on — the scheduler's own
/// question, <see cref="IngestionJobs.InPlay"/>, so the feed never follows a game the live pass
/// would not refresh — and for each reads ESPN's summary and writes it through
/// <see cref="GamecastWriter"/>. A write that changed something raises <c>plays:{gameId}</c>, and
/// a Gamecast window watching that appends the plays; a poll that saw nothing new writes nothing
/// and raises nothing. The core API's probabilities are a second request, asked every
/// <see cref="GamecastOptions.ProbabilityPoll"/> once the summary calls the game live, and laid
/// onto every write in between, so a play keeps its spread-cover and total-over numbers between
/// asks rather than flickering to empty.
/// </para>
///
/// <para>
/// The box score is live too (§6.1), and costs no request: every
/// <see cref="GamecastOptions.BoxPoll"/> the summary being read anyway is also parsed for its box,
/// by the results pass's own parse, and the players' lines are written through the results
/// pass's path under <see cref="IngestionJobs.EspnGamecastLive"/>, a job apart from the final
/// box's, because a mid-game line is not a final's stats. Only the lines and the players they need — never the
/// game, whose status and score are the scoreboard pass's — and a box that moved no line writes
/// no run and raises no notice.
/// </para>
///
/// <para>
/// One port for the whole poll. ESPN's adapter spaces its requests by its own
/// <c>RequestDelay</c>, measured from its own last request, so a port per game would start each
/// game's clock afresh and send every summary — half a megabyte each — back to back.
/// </para>
///
/// <para>
/// When a game goes final the feed writes the final summary, clears the game's current drive —
/// a drive ESPN stopped calling current can otherwise stay current here for good — and writes the
/// box score once, through the path the results sweep takes, so the final box is on the desk
/// without waiting for the sweep. Then it lets the game go. "Goes final" is read from ESPN's
/// summary, not from our row: the scoreboard pass often lands the final first, and the summary
/// is cached apart and can still say live for a poll or two. So a game that drops out of play
/// while followed is read on until its summary says final too, up to <see cref="LeavingReads"/>
/// reads, and finished regardless after that.
/// </para>
///
/// <para>
/// Bounded three times over. At most <see cref="GamecastOptions.MaxFollowedGames"/> games at
/// once, earliest start first, and said once when the ceiling bites rather than every fifteen
/// seconds. A failure on one game is logged and the rest of the poll carries on, as the loop
/// carries on past a failed poll. And a game that fails, or whose summary stays "pre", for
/// <see cref="FailuresBeforeBackOff"/> reads in a row is backed off for a growing while and gives
/// its slot to the healthy games — a stale event id would otherwise hold slot one for the day
/// the game stays in play. A crashed feed would leave every window silent, which is worse than a
/// game missing a poll.
/// </para>
///
/// <para>
/// Each game is written in a fresh scope, so one game's change tracker never carries into the
/// next game's save. What the feed holds between polls — the games followed, when each last asked
/// for probabilities, the games it has finished with, the games in trouble — is in memory: a
/// restart re-reads what is on and loses nothing the store does not already hold.
/// </para>
/// </summary>
public class GamecastFeed(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<GamecastFeed> logger) : BackgroundService
{
    /// <summary>The run row the final box score is recorded under, beside the results sweep's.</summary>
    public const string JobKey = "espn:gamecast";

    /// <summary>
    /// Reads a game gets after it leaves play here while its summary has not yet said final:
    /// eight polls, about two minutes, comfortably past ESPN's summary cache.
    /// </summary>
    public const int LeavingReads = 8;

    /// <summary>Failed or "pre" reads in a row before a game is backed off.</summary>
    public const int FailuresBeforeBackOff = 4;

    /// <summary>The longest a game in trouble waits between asks.</summary>
    private static readonly TimeSpan MaxBackOff = TimeSpan.FromMinutes(15);

    private readonly GamecastOptions _options = options.Value.Gamecast;

    /// <summary>A game being followed, and the probabilities last fetched for it.</summary>
    private sealed class Followed(string sport, string eventId, DateTimeOffset startsAt)
    {
        public string Sport { get; } = sport;
        public string EventId { get; } = eventId;
        public DateTimeOffset StartsAt { get; } = startsAt;
        public DateTimeOffset? ProbabilitiesAt { get; set; }
        public IReadOnlyDictionary<string, CanonicalPlayProbability> Probabilities { get; set; }
            = new Dictionary<string, CanonicalPlayProbability>();

        /// <summary>When the live box score was last written from the summary, if it has been.</summary>
        public DateTimeOffset? BoxAt { get; set; }

        /// <summary>Reads since the game left play here, its summary not yet final.</summary>
        public int ReadsSinceLeaving { get; set; }
    }

    /// <summary>A game whose reads keep failing or keep saying "pre", and when to ask it next.</summary>
    private sealed class Trouble
    {
        public int Failures { get; set; }
        public DateTimeOffset RetryAt { get; set; }
    }

    private readonly Dictionary<int, Followed> _followed = [];

    /// <summary>
    /// Games seen final and let go, kept until they leave play here too: ESPN's summary can call a
    /// game final a poll or two before the scoreboard pass marks our row, and without this the feed
    /// would take it up again and write its box score a second time.
    /// </summary>
    private readonly HashSet<int> _finished = [];

    /// <summary>Games in trouble, forgotten when they recover or leave play.</summary>
    private readonly Dictionary<int, Trouble> _trouble = [];

    private bool _ceilingReported;
    private bool _noSourceReported;

    /// <summary>The games followed after the last poll, by id.</summary>
    public IReadOnlyCollection<int> Following => _followed.Keys;

    /// <summary>
    /// The configured cadences, or their defaults when configuration gives zero or less — as
    /// <see cref="IngestionScheduler"/> reads its own: <c>Task.Delay</c> throws on a negative
    /// delay, which would take the host down from the loop's catch, and zero would spin.
    /// </summary>
    private TimeSpan Poll => Positive(_options.Poll, new GamecastOptions().Poll);

    private TimeSpan ProbabilityPoll => Positive(_options.ProbabilityPoll, new GamecastOptions().ProbabilityPoll);

    private TimeSpan BoxPoll => Positive(_options.BoxPoll, new GamecastOptions().BoxPoll);

    private static TimeSpan Positive(TimeSpan configured, TimeSpan fallback)
        => configured > TimeSpan.Zero ? configured : fallback;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Gamecast feed is switched off (Ingestion:Gamecast:Enabled)");
            return;
        }

        logger.LogInformation(
            "Gamecast feed started: {Sports}, every {Poll}, up to {Max} games",
            string.Join(", ", _options.EffectiveSports), Poll, _options.MaxFollowedGames);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(DateTimeOffset.UtcNow, stoppingToken);
                await Task.Delay(Poll, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database restarting, say. Wait the poll out rather than spinning on it.
                logger.LogError(ex, "Gamecast poll failed; continuing");

                try
                {
                    await Task.Delay(Poll, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Gamecast feed stopped");
    }

    /// <summary>One poll: every game in play read and written once. Public so a test can drive the clock.</summary>
    public async Task PollAsync(DateTimeOffset now, CancellationToken ct)
    {
        // The poll's scope holds the one port every request goes through, so its pacing spans the games.
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<IGamecastSource>() is not { } source)
        {
            if (!_noSourceReported)
                logger.LogInformation("Gamecast feed idle: no gamecast source is registered (Ingestion:Espn:Enabled)");

            _noSourceReported = true;
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();

        var inPlay = (await IngestionJobs.InPlay(db, now, _options.EffectiveSports)
                .OrderBy(g => g.StartsAt)
                .ThenBy(g => g.Id)
                .Select(g => new { g.Id, Sport = g.Sport!.Key, g.StartsAt, g.ExternalIds })
                .ToListAsync(ct))
            .Where(g => g.ExternalIds.ContainsKey(source.Key))
            .ToList();

        var onNow = inPlay.Select(g => g.Id).ToHashSet();

        // A finished game is forgotten once it has left play here as well, and so is a game in trouble.
        _finished.IntersectWith(onNow);

        foreach (var gone in _trouble.Keys.Where(id => !onNow.Contains(id)).ToList())
            _trouble.Remove(gone);

        // A backed-off game is out of the running until its wait is over, and a game still in
        // trouble queues behind the healthy ones (a stable sort, so earliest start first within each).
        var candidates = inPlay
            .Where(g => !_finished.Contains(g.Id) && !BackedOff(g.Id, now))
            .OrderBy(g => InTrouble(g.Id))
            .ToList();

        var max = Math.Max(0, _options.MaxFollowedGames);

        if (candidates.Count > max)
        {
            if (!_ceilingReported)
            {
                logger.LogInformation(
                    "Gamecast feed: {Count} games in play, following the first {Max} (Ingestion:Gamecast:MaxFollowedGames)",
                    candidates.Count, max);
            }

            _ceilingReported = true;
        }
        else
        {
            _ceilingReported = false;
        }

        var current = candidates.Take(max)
            .ToDictionary(g => g.Id, g => _followed.TryGetValue(g.Id, out var known)
                ? known
                : new Followed(g.Sport, g.ExternalIds[source.Key], g.StartsAt));

        // Followed and out of play now: most often the scoreboard pass has just marked it final,
        // and the feed owes it the final summary and the box score. It is read on until the
        // summary agrees, or for LeavingReads polls at most.
        var leaving = _followed.Where(f => !current.ContainsKey(f.Key) && !onNow.Contains(f.Key)).ToList();

        // Followed and still in play, but backed off or past the ceiling: let go until it is picked again.
        foreach (var displaced in _followed.Keys.Where(id => !current.ContainsKey(id) && onNow.Contains(id)).ToList())
            _followed.Remove(displaced);

        foreach (var (gameId, game) in current)
        {
            ct.ThrowIfCancellationRequested();
            await FollowAsync(source, gameId, game, leaving: false, now, ct);
        }

        foreach (var (gameId, game) in leaving)
        {
            ct.ThrowIfCancellationRequested();
            await FollowAsync(source, gameId, game, leaving: true, now, ct);
        }
    }

    private bool InTrouble(int gameId)
        => _trouble.TryGetValue(gameId, out var trouble) && trouble.Failures >= FailuresBeforeBackOff;

    private bool BackedOff(int gameId, DateTimeOffset now)
        => InTrouble(gameId) && now < _trouble[gameId].RetryAt;

    /// <summary>
    /// Reads and writes one game, finishing it if ESPN calls it final. A failure is this game's
    /// alone: it is logged, and a game still in play is asked again next poll, or after its
    /// back-off once it has failed too often.
    /// </summary>
    private async Task FollowAsync(
        IGamecastSource source, int gameId, Followed game, bool leaving, DateTimeOffset now, CancellationToken ct)
    {
        CanonicalGamecast? gamecast = null;
        StatsFetchResult? box = null;

        try
        {
            // The box score rides on the summary read anyway, parsed when its cadence is due.
            if (game.BoxAt is not { } boxed || now - boxed >= BoxPoll)
            {
                var read = await source.FetchGamecastWithBoxAsync(game.Sport, game.EventId, ct);
                gamecast = read?.Gamecast;
                box = read?.Box;
            }
            else
            {
                gamecast = await source.FetchGamecastAsync(game.Sport, game.EventId, ct);
            }

            if (gamecast is null)
            {
                // The port cannot address this league; asking every fifteen seconds will not change that.
                _followed.Remove(gameId);
                _trouble.Remove(gameId);
                return;
            }

            // Probabilities only once the game is on: a game not started, or stuck before its start, has none.
            if (gamecast.State == "live" && (game.ProbabilitiesAt is not { } asked || now - asked >= ProbabilityPoll))
                await RefreshProbabilitiesAsync(source, game, now, ct);

            var merged = EspnStatsAdapter.MergeProbabilities(gamecast, game.Probabilities);

            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var writer = scope.ServiceProvider.GetRequiredService<GamecastWriter>();
                var written = await writer.WriteAsync(gameId, source.Key, merged, ct);

                if (written.Changed)
                {
                    logger.LogInformation(
                        "Gamecast {GameId}: {Plays} plays in, {Updated} changed, {Drives} drives written",
                        gameId, written.PlaysInserted, written.PlaysUpdated, written.DrivesInserted + written.DrivesUpdated);
                }
            }

            // Live only: a final summary's box is the final path's, written whole with the game.
            if (gamecast.State == "live" && box is not null)
                await WriteLiveBoxAsync(source, gameId, game, box, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Failed(gameId, game, now, ex);
            gamecast = null;

            if (!leaving)
                return;
        }

        if (gamecast?.State == "final")
        {
            Recovered(gameId, game);
            await FinishAsync(gameId, game, source, summaryFinal: true, ct);
        }
        else if (gamecast?.State == "postponed")
        {
            // Let go for as long as it stays in play here, or it would be taken up every poll.
            _followed.Remove(gameId);
            _finished.Add(gameId);
            _trouble.Remove(gameId);
        }
        else if (leaving)
        {
            if (++game.ReadsSinceLeaving >= LeavingReads)
            {
                await FinishAsync(gameId, game, source, summaryFinal: false, ct);
            }
            else
            {
                _followed[gameId] = game;
            }
        }
        else
        {
            if (gamecast?.State == "pre")
                Stalled(gameId, game, now);
            else
                Recovered(gameId, game);

            _followed[gameId] = game;
        }
    }

    /// <summary>
    /// A read that threw. Said once at Warning, with the stack, and at Debug after that until the
    /// game recovers, so a stale event id does not write a stack trace every fifteen seconds.
    /// </summary>
    private void Failed(int gameId, Followed game, DateTimeOffset now, Exception ex)
    {
        var trouble = Note(gameId, now);

        if (trouble.Failures == 1)
            logger.LogWarning(ex, "Gamecast {GameId} ({EventId}): poll failed; continuing", gameId, game.EventId);
        else
            logger.LogDebug("Gamecast {GameId} ({EventId}): poll failed again ({Failures} in a row): {Error}",
                gameId, game.EventId, trouble.Failures, ex.Message);
    }

    /// <summary>A game past its start whose summary still says "pre": delayed or suspended.</summary>
    private void Stalled(int gameId, Followed game, DateTimeOffset now)
    {
        var trouble = Note(gameId, now);

        logger.LogDebug("Gamecast {GameId} ({EventId}): not started on ESPN ({Reads} reads in a row)",
            gameId, game.EventId, trouble.Failures);
    }

    /// <summary>Counts a bad read and, past the threshold, sets the next ask a growing while out.</summary>
    private Trouble Note(int gameId, DateTimeOffset now)
    {
        if (!_trouble.TryGetValue(gameId, out var trouble))
            _trouble[gameId] = trouble = new Trouble();

        trouble.Failures++;

        if (trouble.Failures >= FailuresBeforeBackOff)
        {
            // Four polls' worth, doubling: a minute, two, four … at the default cadence, up to the cap.
            var doublings = Math.Min(trouble.Failures - FailuresBeforeBackOff, 10);
            var wait = TimeSpan.FromTicks(Math.Min(Poll.Ticks * 4 * (1L << doublings), MaxBackOff.Ticks));
            trouble.RetryAt = now + wait;

            logger.LogDebug("Gamecast {GameId}: backed off until {RetryAt}", gameId, trouble.RetryAt);
        }

        return trouble;
    }

    private void Recovered(int gameId, Followed game)
    {
        if (_trouble.Remove(gameId, out var trouble) && trouble.Failures >= FailuresBeforeBackOff)
        {
            logger.LogInformation("Gamecast {GameId} ({EventId}): reading again after {Failures} bad reads",
                gameId, game.EventId, trouble.Failures);
        }
    }

    /// <summary>
    /// The core API's rows for the game, kept for the writes until the next ask. A failure keeps
    /// the last rows — the plays they describe have not changed — and waits the cadence out.
    /// </summary>
    private async Task RefreshProbabilitiesAsync(IGamecastSource source, Followed game, DateTimeOffset now, CancellationToken ct)
    {
        game.ProbabilitiesAt = now;

        try
        {
            game.Probabilities = await source.FetchProbabilitiesAsync(game.Sport, game.EventId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Gamecast {EventId}: probabilities unavailable; keeping the last", game.EventId);
        }
    }

    /// <summary>
    /// The box score as it stands, from the summary just read, written through the results pass's
    /// path under <see cref="IngestionJobs.EspnGamecastLive"/> — its own job, so a final holding
    /// only these lines is still owed its stats. The players and their lines only — the game's status and
    /// score stay the scoreboard pass's — and quietly: a box that moved no line writes no run. A
    /// failure is logged and waits the cadence out; it does not cost the game its plays.
    /// </summary>
    private async Task WriteLiveBoxAsync(
        IGamecastSource source, int gameId, Followed game, StatsFetchResult box, DateTimeOffset now, CancellationToken ct)
    {
        game.BoxAt = now;

        if (box.PlayerStats.Count == 0)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();
            var outcome = await stats.IngestLiveBoxScoreAsync(source.Key, game.Sport, IngestionJobs.EspnGamecastLive, box, ct);

            if (outcome.RunId != 0)
                logger.LogDebug("Gamecast {GameId}: live box score {Status}, {Rows} rows", gameId, outcome.Status, outcome.RowsIngested);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Gamecast {GameId} ({EventId}): live box score failed; asked again next cadence",
                gameId, game.EventId);
        }
    }

    /// <summary>
    /// A game over: let go, no drive current any more, and the box score asked for once. Run when
    /// the summary says final, and also when a game that left play never got a final summary in
    /// its <see cref="LeavingReads"/> — then the box score may well come back empty. A box score
    /// that will not come is not retried here — the results sweep owes every final its stats
    /// (<see cref="IngestionJobs.DatesAwaitingResultsAsync"/>) and heals it on its own back-off.
    /// </summary>
    private async Task FinishAsync(
        int gameId, Followed game, IGamecastSource source, bool summaryFinal, CancellationToken ct)
    {
        _followed.Remove(gameId);
        _finished.Add(gameId);
        _trouble.Remove(gameId);

        if (!summaryFinal)
        {
            logger.LogInformation(
                "Gamecast {GameId} ({EventId}): out of play, and the summary never said final in {Reads} reads; finishing it",
                gameId, game.EventId, LeavingReads);
        }

        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LineOpsDbContext>();
                var current = await db.GameDrives.Where(d => d.GameId == gameId && d.IsCurrent).ToListAsync(ct);

                if (current.Count > 0)
                {
                    foreach (var drive in current)
                    {
                        drive.IsCurrent = false;
                        drive.UpdatedAt = DateTimeOffset.UtcNow;
                    }

                    // Through SaveChanges, not a bulk update, so the window hears plays:{id} and drops the marker.
                    await db.SaveChangesAsync(ct);
                }
            }

            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var stats = scope.ServiceProvider.GetRequiredService<StatsIngestionService>();
                var outcome = await stats.IngestBoxScoreAsync(
                    source, game.Sport, JobKey, LeagueClock.DateOf(game.StartsAt), game.EventId, ct);

                logger.LogInformation(
                    "Gamecast {GameId}: final; box score {Status}, {Rows} rows. No longer followed",
                    gameId, outcome.Status, outcome.RowsIngested);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Gamecast {GameId} ({EventId}): finishing failed; the results sweep will heal the box score",
                gameId, game.EventId);
        }
    }
}
