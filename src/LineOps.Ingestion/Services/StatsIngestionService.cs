using System.Text.Json;
using System.Text.Json.Nodes;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Ingests schedules, scores, rosters and box scores.
///
/// Mirrors <see cref="OddsIngestionService"/>: same run recording, same budget guard, same
/// failure semantics — so the reliability layer treats stats and odds sources identically
/// and one KPI dashboard covers both.
/// </summary>
public class StatsIngestionService(
    LineOpsDbContext db,
    EntityResolver resolver,
    CreditBudgetGuard budget,
    ILogger<StatsIngestionService> logger)
{
    /// <summary>Schedules, scores and box scores for a day.</summary>
    public Task<IngestionOutcome> IngestAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly: false, ct);

    /// <summary>
    /// Schedules and scores only — no per-game box scores.
    ///
    /// A box-score pass costs one request per finished game on top of the scoreboard, and
    /// before the games are played there is nothing to summarise. Fetching a slate for today
    /// and a slate for yesterday are therefore very different prices, and only one of them is
    /// worth paying twice a day.
    /// </summary>
    public Task<IngestionOutcome> IngestScheduleAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly: true, ct);

    /// <summary>
    /// The scoreboard for a day, written only for the events being played or just finished.
    ///
    /// <para>
    /// Same request as <see cref="IngestScheduleAsync"/> — the scoreboard already carries every
    /// live score — but run every few seconds while a game is on, so it does none of the slate's
    /// other work. An event is kept when the provider says it is live, or says it is over while
    /// our row still has it in play: that last poll is what lands the final score and status.
    /// Everything else on the board — tonight's games, last night's finals — is the slate's and
    /// the results sweep's, and resolving it here would repeat their writes on every poll.
    /// </para>
    ///
    /// <para>
    /// A game we already hold is resolved without its team references, so the resolver does
    /// not re-read both sides' identity for it: names, ids and abbreviations do not change
    /// mid-game, and the slate keeps them current. A live event we have never seen keeps its
    /// references, because creating its teams from names alone would lose the provider's ids.
    /// </para>
    /// </summary>
    public Task<IngestionOutcome> IngestLiveAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly: true, ct,
            narrow: (result, sourceKey, c) => InPlayAsync(result, sportKey, sourceKey, c), quietPoll: true);

    /// <summary>
    /// One day of a range the operator named (<see cref="RangePull"/>): the box-score pass for a
    /// day gone by, the schedule pass for today and after, through the same run row, budget guard
    /// and persist as either — except that a game we hold as final is not walked back by it.
    ///
    /// <para>
    /// A range reaches into history, and ESPN's view of an old fixture is not always the one it
    /// had the night it was played: an event moved or relisted reads as scheduled, scoreless. The
    /// resolver takes whatever status and score it is handed (only a missing score is left
    /// alone), so a re-pull would put a graded game back before its first play. A held final
    /// therefore keeps its status, score and state of play unless the provider still calls it
    /// final, when its score — a correction — is taken as the results sweep takes it.
    /// </para>
    /// </summary>
    public Task<IngestionOutcome> IngestRangeDayAsync(
        IStatsSource source, string sportKey, string jobKey, DateOnly date, bool schedulesOnly, CancellationToken ct)
        => IngestAsync(source, sportKey, jobKey, date, schedulesOnly, ct,
            narrow: async (result, sourceKey, c) => result.Games.Count == 0
                ? result
                : result with { Games = KeepHeldFinals(result.Games, await HeldStatusesAsync(result, sportKey, sourceKey, c)) });

    /// <summary>
    /// A scoreboard with every game we hold as final kept so: one the provider no longer calls
    /// final carries no status, score or state of play, which the resolver reads as no news. See
    /// <see cref="IngestRangeDayAsync"/>.
    /// </summary>
    internal static IReadOnlyList<CanonicalGame> KeepHeldFinals(
        IReadOnlyList<CanonicalGame> scoreboard, IReadOnlyDictionary<string, GameStatus> ours)
        => scoreboard
            .Select(g => ours.TryGetValue(g.SourceGameId, out var held) && held == GameStatus.Final
                         && EntityResolver.MapStatus(g.Status) != GameStatus.Final
                ? g with { Status = null, HomeScore = null, AwayScore = null, Live = null }
                : g)
            .ToList();

    private async Task<StatsFetchResult> InPlayAsync(
        StatsFetchResult result, string sportKey, string sourceKey, CancellationToken ct)
    {
        if (result.Games.Count == 0)
            return result;

        return result with { Games = SelectInPlay(result.Games, await HeldStatusesAsync(result, sportKey, sourceKey, ct)) };
    }

    /// <summary>Our status for each event on a scoreboard that we hold, keyed by the provider's event id.</summary>
    private async Task<Dictionary<string, GameStatus>> HeldStatusesAsync(
        StatsFetchResult result, string sportKey, string sourceKey, CancellationToken ct)
    {
        var from = result.Games.Min(g => g.StartsAt).AddDays(-2);
        var to = result.Games.Max(g => g.StartsAt).AddDays(2);

        return (await db.Games
                .Where(g => g.Sport!.Key == sportKey && g.StartsAt > from && g.StartsAt < to)
                .Select(g => new { g.ExternalIds, g.Status })
                .ToListAsync(ct))
            .Where(g => g.ExternalIds.ContainsKey(sourceKey))
            .GroupBy(g => g.ExternalIds[sourceKey])
            .ToDictionary(g => g.Key, g => g.First().Status);
    }

    /// <summary>
    /// The events a live pass writes, from a scoreboard and our status for each event we hold
    /// (keyed by the provider's event id). See <see cref="IngestLiveAsync"/>.
    /// </summary>
    internal static IReadOnlyList<CanonicalGame> SelectInPlay(
        IReadOnlyList<CanonicalGame> scoreboard, IReadOnlyDictionary<string, GameStatus> ours)
    {
        var kept = new List<CanonicalGame>();

        foreach (var game in scoreboard)
        {
            var held = ours.TryGetValue(game.SourceGameId, out var status);
            var stillInPlayHere = held && status is not (GameStatus.Final or GameStatus.Postponed);

            var keep = EntityResolver.MapStatus(game.Status) switch
            {
                GameStatus.Live => true,
                // Just finished: over on the provider, still in play here. A final we already
                // hold is the results sweep's, corrections included.
                GameStatus.Final or GameStatus.Postponed => stillInPlayHere,
                _ => false
            };

            if (keep)
                kept.Add(held ? game with { Home = null, Away = null } : game);
        }

        return kept;
    }

    /// <summary>
    /// One game's box score, written exactly as a day's is (<see cref="IngestAsync(IStatsSource, string, string, DateOnly, CancellationToken)"/>):
    /// the same run row, budget guard and persist, so the final score, every player's line and
    /// the close land as the results sweep would land them. The gamecast feed's, for the game it
    /// was following, the moment that game is final (spec 2026-10-24 §6.3.4).
    /// </summary>
    public Task<IngestionOutcome> IngestBoxScoreAsync(
        IGamecastSource source, string sportKey, string jobKey, DateOnly date, string sourceGameId, CancellationToken ct)
        => IngestAsync(source.Key, source, sportKey, jobKey,
            c => source.FetchBoxScoreAsync(sportKey, date, sourceGameId, c), ct);

    /// <summary>
    /// A game in play's box score as it stands, from a summary the gamecast feed has already read
    /// (<see cref="IGamecastSource.FetchGamecastWithBoxAsync"/>): the players and their lines,
    /// through the same run row, budget guard and persist as the final box.
    ///
    /// <para>
    /// Only the players and their lines. Whatever games or lines the box states are dropped
    /// here, so a live write cannot move the game: its status and score are the scoreboard
    /// pass's, and a box that called it final, or scoreless, would undo them. The lines resolve
    /// to the game we already hold by its event id, as a stat line for a game the payload does
    /// not carry always does.
    /// </para>
    ///
    /// <para>
    /// Quiet the way a live scoreboard poll is: on an unmetered source, a box that changed
    /// nothing — every line as stored, no player new — writes no run row and raises no notice.
    /// </para>
    /// </summary>
    public Task<IngestionOutcome> IngestLiveBoxScoreAsync(
        string sourceKey, string sportKey, string jobKey, StatsFetchResult box, CancellationToken ct)
        => IngestAsync(sourceKey, adapter: null, sportKey, jobKey,
            _ => Task.FromResult(box with { Games = [], ClosingLines = null }), ct, live: true);

    private Task<IngestionOutcome> IngestAsync(
        IStatsSource source,
        string sportKey,
        string jobKey,
        DateOnly date,
        bool schedulesOnly,
        CancellationToken ct,
        Func<StatsFetchResult, string, CancellationToken, Task<StatsFetchResult>>? narrow = null,
        bool quietPoll = false)
        => IngestAsync(source.Key, source, sportKey, jobKey,
            c => schedulesOnly
                ? source.FetchScheduleAsync(sportKey, date, c)
                : source.FetchBoxScoresAsync(sportKey, date, c),
            ct, narrow, quietPoll: quietPoll);

    private async Task<IngestionOutcome> IngestAsync(
        string sourceKey,
        object? adapter,
        string sportKey,
        string jobKey,
        Func<CancellationToken, Task<StatsFetchResult>> fetch,
        CancellationToken ct,
        Func<StatsFetchResult, string, CancellationToken, Task<StatsFetchResult>>? narrow = null,
        bool live = false,
        bool quietPoll = false)
    {
        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == sourceKey, ct)
            ?? throw new InvalidOperationException($"Source '{sourceKey}' is not registered.");

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        // A live poll runs every fifteen seconds all game, and most see the board the last one
        // saw. Its run is written only once there is something to say — a change, a failure, a
        // refusal — so a poll that moved nothing leaves no row and raises no notice: a run row is
        // a `runs` notice, and every open Ops, Runs and History window reloaded on each one. A
        // metered source keeps its row every time, because the budget guard counts requests from
        // the runs and a request with no row would be spent without being counted. A live box
        // score is quiet on the same terms; a range day, narrowed too, is not a poll and keeps its row.
        var quiet = (quietPoll || live) && IsUnmetered(sourceRow);

        if (!quiet)
            await RecordAsync(run, ct);

        var saved = 0;
        void Count(object? sender, SavedChangesEventArgs e) => saved += e.EntitiesSavedCount;

        try
        {
            if (!await budget.TryReserveAsync(sourceRow, estimatedRequests: 2, ct))
            {
                run.Status = RunStatus.Partial;
                run.Error = "Skipped: source is at its configured rate/credit budget.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await RecordAsync(run, ct);
                return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
            }

            if (adapter is IFailureInjectable injectable)
                injectable.FailureMode = sourceRow.FailureMode;

            var result = await fetch(ct);

            if (narrow is not null)
                result = await narrow(result, sourceRow.Key, ct);

            run.RequestsMade = result.Cost.Requests;
            run.CreditsSpent = result.Cost.Credits;

            // Every save in the pass, the resolver's own included: what tells a poll that changed
            // something from one that only read.
            db.SavedChanges += Count;
            int rows;

            try
            {
                // Stat lines carry the run's id, so the first line written or changed records a
                // quiet pass's run; a pass whose lines all stand as stored needs none.
                rows = await PersistAsync(sourceRow, result, sportKey, async c =>
                {
                    if (run.Id == 0)
                        await RecordAsync(run, c);

                    return run.Id;
                }, live, ct);
            }
            finally
            {
                db.SavedChanges -= Count;
            }

            if (quiet && run.Id == 0 && saved == 0 && !db.ChangeTracker.HasChanges())
            {
                logger.LogDebug("{Source}/{Sport}: live poll, nothing moved", sourceKey, sportKey);
                return new IngestionOutcome(0, RunStatus.Success, 0, null);
            }

            run.RowsIngested = rows;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Status = RunStatus.Success;

            await RecordAsync(run, ct);

            logger.LogInformation("{Source}/{Sport}: {Rows} stat rows", sourceKey, sportKey, rows);
            return new IngestionOutcome(run.Id, run.Status, rows, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A persist that threw leaves the change tracker holding exactly the entities that
            // could not be saved. Recording the failure means calling SaveChanges again, which
            // would retry them and throw a second time — out of the catch block, past the run
            // row, and into the caller. That turned one bad day into a dead backfill. Discard
            // the pending work first, then re-read the run to carry the outcome.
            db.ChangeTracker.Clear();

            var error = $"{ex.GetType().Name}: {ex.Message}";

            // A live poll's run not written yet is written now: a failure is always recorded.
            var failed = run.Id == 0
                ? run
                : await db.IngestionRuns.FirstOrDefaultAsync(r => r.Id == run.Id, ct);

            if (failed is not null)
            {
                failed.Status = RunStatus.Failed;
                failed.Error = error;
                failed.FinishedAt = DateTimeOffset.UtcNow;
                await RecordAsync(failed, ct);
            }

            logger.LogError(ex, "{Source}/{Sport}: stats ingestion failed", sourceKey, sportKey);
            return new IngestionOutcome(failed?.Id ?? run.Id, RunStatus.Failed, 0, error);
        }
    }

    /// <summary>The live box's runs (<see cref="IngestionJobs.EspnGamecastLive"/>) behind this source's lines for these games.</summary>
    private async Task<HashSet<long>> LiveRunsAsync(Source sourceRow, List<int> gameIds, CancellationToken ct)
        => (await db.PlayerGameStats
                .Where(s => gameIds.Contains(s.GameId) && s.SourceId == sourceRow.Id
                            && db.IngestionRuns.Any(r => r.Id == s.IngestionRunId
                                                         && r.JobKey == IngestionJobs.EspnGamecastLive))
                .Select(s => s.IngestionRunId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

    /// <summary>Saves the run, adding it first if it has no row yet.</summary>
    private async Task RecordAsync(IngestionRun run, CancellationToken ct)
    {
        if (run.Id == 0)
            db.IngestionRuns.Add(run);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Whether a stored stat line says what a parsed one says. The column is jsonb, which Postgres
    /// hands back re-spaced and with its keys in its own order, so the stored string is never the
    /// parsed one; compared as strings every re-walk rewrote every line, and a live box that had
    /// not moved wrote a run regardless. Compared as JSON, falling back to the strings for a line
    /// that is not JSON.
    /// </summary>
    public static bool SameStatLine(string stored, string parsed)
    {
        if (stored == parsed)
            return true;

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(parsed));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A source with no request or credit ceiling, whose spend the budget guard never reads.</summary>
    private static bool IsUnmetered(Source source)
        => source.RateLimitPerHour is null && source.RateLimitPerDay is null && source.MonthlyCreditBudget is null;

    /// <summary>
    /// Finished games in one sport without the close they are owed from any source (a fight's
    /// moneyline, any other game's total — <see cref="OwedMarket"/>), asked of a provider
    /// that can state a close per game — the backport for a season the box-score walk stored
    /// without lines. What it finds is written exactly as the walk writes it, under the stats
    /// provider's own source and only where that source holds nothing for the side yet.
    ///
    /// <para>
    /// A game the provider answers without the close it is owed is set aside
    /// (<see cref="ClosingLineMiss"/>): not asked again for two weeks, then four, eight, and at
    /// most every ninety days, because ESPN does fill a close in after the fact — but not for a
    /// fight card from two seasons ago, which every press used to ask about again. Where it has
    /// none, there is none: nothing is fabricated.
    /// </para>
    ///
    /// <para>
    /// The run row says what the press did in <see cref="IngestionRun.Detail"/> — "NFL: 2 asked,
    /// 0 answered; 2 set aside until Nov 23." — and keeps <see cref="IngestionRun.Error"/> for what
    /// went wrong. A write that throws marks the run Failed with its error rather than leaving it
    /// Running.
    /// </para>
    /// </summary>
    /// <param name="now">The moment the press is taken at: what a set-aside game's retry is measured from.</param>
    public async Task<IngestionOutcome> RefillClosingLinesAsync(
        IClosingLineSource source, string sportKey, string jobKey, int limit, CancellationToken ct,
        DateTimeOffset? from = null, DateTimeOffset? to = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;

        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == source.Key, ct)
            ?? throw new InvalidOperationException($"Source '{source.Key}' is not registered.");

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        // A backfill range asks only for its own games; the scheduled job asks for every final.
        // Either way a game set aside by this source is left until its retry date.
        var owedQuery = ClosingLinesOwed(sportKey, sourceRow.Id, source.Key, at);
        if (from is { } since)
            owedQuery = owedQuery.Where(g => g.StartsAt >= since);
        if (to is { } until)
            owedQuery = owedQuery.Where(g => g.StartsAt < until);

        var owed = await owedQuery
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .OrderByDescending(g => g.StartsAt)
            .Take(limit)
            .ToListAsync(ct);

        var lines = new List<CanonicalOdds>();
        var gameMap = new Dictionary<string, Game>();
        var asked = new List<Game>();
        var requests = 0;
        var answered = 0;
        var failures = 0;

        // A fight's odds live under its card's event, not its own id (ADR 0021).
        var owedIds = owed.Select(g => g.Id).ToList();
        var cards = await db.Bouts
            .Where(b => owedIds.Contains(b.GameId))
            .ToDictionaryAsync(b => b.GameId, b => b.EventId, ct);

        foreach (var game in owed)
        {
            ct.ThrowIfCancellationRequested();

            if (!game.ExternalIds.TryGetValue(source.Key, out var eventId)
                || game.HomeTeam is null || game.AwayTeam is null)
                continue;

            try
            {
                requests++;
                var found = await source.FetchClosingLinesAsync(sportKey,
                    new ClosingLineRequest(eventId, game.HomeTeam.Name, game.AwayTeam.Name, game.StartsAt,
                        cards.GetValueOrDefault(game.Id)), ct);

                gameMap[eventId] = game;
                lines.AddRange(found);
                asked.Add(game);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One game ESPN cannot answer for must not cost the rest of the season. A request
                // that threw is not set aside: the next press asks it again.
                failures++;
                logger.LogWarning(ex, "{Source}: closing odds unavailable for {GameId}", source.Key, eventId);
            }
        }

        var written = 0;
        DateTimeOffset[] setAside = [];

        try
        {
            // Answered means the close the game is owed was written: a reply with a spread and no
            // total, or a moneyline the writer could not place on the bout, leaves the game owed,
            // and a game left owed is set aside so the next press does not ask it again.
            var persisted = await PersistClosingLinesWithClosedAsync(sourceRow, lines, gameMap, ct);
            written = persisted.Written;
            answered = asked.Count(g => persisted.Closed.Contains(g.Id));
            setAside = await SetAsideAsync(sourceRow.Id, asked.Where(g => !persisted.Closed.Contains(g.Id)).ToList(), at, ct);

            run.Status = failures == 0 || written > 0 ? RunStatus.Success : RunStatus.Failed;
            run.Error = failures > 0 ? $"{failures} of {requests} games had no answer from the provider." : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // What was not written is not kept half-written: the closes and misses in flight are
            // dropped, and the run says why rather than sitting at Running for ever.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity != run
                         && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
                entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Unchanged;

            logger.LogError(ex, "{Source}/{Sport}: closing lines could not be written", source.Key, sportKey);

            written = 0;
            setAside = [];
            run.Status = RunStatus.Failed;
            run.Error = $"{ex.GetType().Name}: {ex.Message}";
        }

        run.RequestsMade = requests;
        run.RowsIngested = written;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Detail = owed.Count == 0
            ? await NothingToAskAsync(sportKey, sourceRow.Id, at, from, to, ct)
            : RefillDetail(sportKey, requests, answered, setAside);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Source}/{Sport}: {Detail} {Rows} lines written",
            source.Key, sportKey, run.Detail, written);

        return new IngestionOutcome(run.Id, run.Status, written, run.Error);
    }

    /// <summary>
    /// A press that asked nothing: "NFL: nothing owed." when no final lacks its close, and when
    /// every one that does is set aside, how many and until when — so a quiet run is not read as
    /// a league with nothing missing.
    /// </summary>
    private async Task<string> NothingToAskAsync(
        string sportKey, int sourceId, DateTimeOffset now, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var owed = ClosingLinesOwed(sportKey);
        if (from is { } since)
            owed = owed.Where(g => g.StartsAt >= since);
        if (to is { } until)
            owed = owed.Where(g => g.StartsAt < until);

        var retries = await db.ClosingLineMisses
            .Where(m => m.SourceId == sourceId && m.RetryAt > now && owed.Any(g => g.Id == m.GameId))
            .Select(m => m.RetryAt)
            .ToListAsync(ct);

        return retries.Count == 0
            ? $"{sportKey.ToUpperInvariant()}: nothing owed."
            : WithSetAside($"{sportKey.ToUpperInvariant()}: nothing to ask", retries);
    }

    /// <summary>The first wait after a miss; each further miss doubles it, up to <see cref="MaxMissWait"/>.</summary>
    public static readonly TimeSpan FirstMissWait = TimeSpan.FromDays(14);

    public static readonly TimeSpan MaxMissWait = TimeSpan.FromDays(90);

    /// <summary>How long a game is left after its <paramref name="attempts"/>th miss: 14, 28, 56, then 90 days.</summary>
    public static TimeSpan MissWait(int attempts)
    {
        var days = FirstMissWait.TotalDays * Math.Pow(2, Math.Clamp(attempts, 1, 16) - 1);
        return TimeSpan.FromDays(Math.Min(days, MaxMissWait.TotalDays));
    }

    /// <summary>
    /// Remembers each game asked and left without its close, so the next press does not ask it
    /// again before its retry date. Returns the retry dates written.
    /// </summary>
    private async Task<DateTimeOffset[]> SetAsideAsync(
        int sourceId, IReadOnlyList<Game> games, DateTimeOffset now, CancellationToken ct)
    {
        if (games.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return [];
        }

        var ids = games.Select(g => g.Id).ToList();
        var held = await db.ClosingLineMisses
            .Where(m => m.SourceId == sourceId && ids.Contains(m.GameId))
            .ToDictionaryAsync(m => m.GameId, ct);

        var retries = new List<DateTimeOffset>(games.Count);

        foreach (var game in games)
        {
            if (!held.TryGetValue(game.Id, out var miss))
            {
                miss = new ClosingLineMiss { GameId = game.Id, SourceId = sourceId };
                db.ClosingLineMisses.Add(miss);
                held[game.Id] = miss;
            }

            miss.Attempts++;
            miss.LastTriedAt = now;
            miss.RetryAt = now + MissWait(miss.Attempts);
            retries.Add(miss.RetryAt);
        }

        await db.SaveChangesAsync(ct);
        return [.. retries];
    }

    /// <summary>"NFL: 2 asked, 0 answered; 2 set aside until Nov 23."</summary>
    internal static string RefillDetail(string sportKey, int asked, int answered, IReadOnlyCollection<DateTimeOffset> setAside)
        => WithSetAside($"{sportKey.ToUpperInvariant()}: {asked:N0} asked, {answered:N0} answered", setAside);

    /// <summary>The sentence, and how many games are set aside until when, if any are.</summary>
    private static string WithSetAside(string sentence, IReadOnlyCollection<DateTimeOffset> setAside)
    {
        if (setAside.Count == 0)
            return sentence + ".";

        var first = LeagueClock.DateOf(setAside.Min());
        var last = LeagueClock.DateOf(setAside.Max());
        var until = first == last
            ? Day(first)
            : $"between {Day(first)} and {Day(last)}";

        return $"{sentence}; {setAside.Count:N0} set aside until {until}.";

        static string Day(DateOnly day) => day.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The close a game is owed: a fight's moneyline, because a fight has no total to close at
    /// (ADR 0021); any other game's total, because it is the market a stats provider's walk is
    /// likeliest to have missed and the one every game has.
    /// </summary>
    public static string OwedMarket(string sportKey)
        => FightSports.IsFight(sportKey) ? Markets.Moneyline : Markets.Total;

    /// <summary>
    /// Finals in one sport that no source has given the close they are owed
    /// (<see cref="OwedMarket"/>) — a game with that close from somewhere has had its close read.
    /// </summary>
    public IQueryable<Game> ClosingLinesOwed(string sportKey)
    {
        var market = OwedMarket(sportKey);

        return db.Games.Where(g => g.Sport!.Key == sportKey
                                   && g.Status == GameStatus.Final
                                   && !db.ClosingLines.Any(c => c.GameId == g.Id && c.Market == market));
    }

    /// <summary>
    /// The finals a press will ask <paramref name="sourceId"/> about at <paramref name="now"/>: those
    /// owed a close that the source knows by its own event id, less the ones it answered without
    /// one whose retry date is still ahead. A game with no id for the source cannot be asked, so it
    /// is neither asked nor set aside; counting it would quote games no press reaches. What the
    /// menu quotes is this count, so the quote is what a press asks.
    /// </summary>
    public IQueryable<Game> ClosingLinesOwed(string sportKey, int sourceId, string sourceKey, DateTimeOffset now)
        => ClosingLinesOwed(sportKey)
            .Where(g => EF.Functions.JsonExists(g.ExternalIds, sourceKey))
            .Where(g => !db.ClosingLineMisses.Any(m => m.GameId == g.Id && m.SourceId == sourceId && m.RetryAt > now));

    /// <summary>
    /// How many finals in one sport a press of the refill will ask <paramref name="sourceKey"/>
    /// about, at most <paramref name="limit"/>: zero for a source not registered.
    /// </summary>
    public async Task<int> ClosingLinesToAskAsync(
        string sportKey, string sourceKey, DateTimeOffset now, int limit, CancellationToken ct)
    {
        var sourceId = await db.Sources.Where(s => s.Key == sourceKey).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);

        return sourceId is { } id
            ? Math.Min(await ClosingLinesOwed(sportKey, id, sourceKey, now).CountAsync(ct), limit)
            : 0;
    }

    private async Task<int> PersistAsync(
        Source sourceRow, StatsFetchResult result, string sportKey, Func<CancellationToken, Task<long>> runId,
        bool live, CancellationToken ct)
    {
        var sport = await resolver.ResolveSportAsync(sportKey, ct);
        var rows = 0;

        // Games first: scores arriving here are what make settlement possible.
        var gameMap = new Dictionary<string, Game>();
        foreach (var canonical in result.Games)
        {
            var game = await resolver.ResolveGameAsync(sport, sourceRow.Key, canonical, ct);
            gameMap[canonical.SourceGameId] = game;
            rows++;

            if (canonical.Bout is { } bout)
                rows += await UpsertBoutAsync(game, bout, ct);
        }

        // Resolve each stat line to a game *before* creating anyone, so that a player only
        // enters the database because they played in a game we can point at.
        //
        // A player who never appears in a box score is a row that will never be read: the
        // cross-reference skips them, the Players panel shows them with nothing, and they cost
        // an index entry each. Any source that reports a roster ahead of the games it played in
        // produces exactly that: every player created, and every stat line then dropped for
        // having nowhere to go.
        var resolved = new List<(CanonicalPlayerStat Stat, Game Game)>();
        Dictionary<string, Game>? knownByExternalId = null;

        foreach (var stat in result.PlayerStats)
        {
            if (!gameMap.TryGetValue(stat.SourceGameId, out var game))
            {
                // The box score references a game this payload did not carry. Load the sport's
                // games once and index them, rather than re-querying per stat line.
                knownByExternalId ??= (await db.Games
                        .Where(g => g.SportId == sport.Id)
                        .ToListAsync(ct))
                    .Where(g => g.ExternalIds.ContainsKey(sourceRow.Key))
                    .GroupBy(g => g.ExternalIds[sourceRow.Key])
                    .ToDictionary(g => g.Key, g => g.First());

                if (!knownByExternalId.TryGetValue(stat.SourceGameId, out game))
                    continue;

                gameMap[stat.SourceGameId] = game;
            }

            resolved.Add((stat, game));
        }

        var participated = resolved.Select(r => r.Stat.SourcePlayerId).ToHashSet();
        var participants = result.Players.Where(p => participated.Contains(p.SourcePlayerId)).ToList();

        if (participants.Count < result.Players.Count)
        {
            logger.LogInformation(
                "{Source}/{Sport}: {Kept} of {Total} reported players actually appeared; the rest are not stored",
                sourceRow.Key, sportKey, participants.Count, result.Players.Count);
        }

        // The newest game each player appears in within this payload, which decides whether it
        // is allowed to say where the player is now (see UpsertPlayersAsync).
        var playedAt = resolved
            .GroupBy(r => r.Stat.SourcePlayerId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Game.StartsAt));

        var playerMap = await UpsertPlayersAsync(sport, sourceRow.Key, participants, playedAt, ct);
        rows += playerMap.Count;

        // Storage allows one line per player per game per source, and the key is the
        // *resolved* PlayerId — not the provider's athlete id. Those are not the same thing:
        // UpsertPlayersAsync falls back to matching on full name, so two source ids can land
        // on one Player row, and a source that reports a player under several stat categories
        // arrives with several lines to begin with. Either way two inserts with one key end
        // up in the same transaction and the whole day dies on a duplicate.
        //
        // So the batch is keyed the way the constraint is, after resolution, and a repeat
        // updates the row already in flight rather than adding a second.
        var written = new Dictionary<(int PlayerId, int GameId), PlayerGameStat>();
        var collisions = 0;

        // The live box's runs behind lines this write is about to meet. A line from the live box
        // is not a final's stats (IngestionJobs.StatsOwed), so a final write takes every such
        // line onto its own run even where the numbers have not moved — a final box that says
        // what the last live one said would otherwise leave the game owed on every sweep.
        var liveRuns = live || resolved.Count == 0
            ? []
            : await LiveRunsAsync(sourceRow, resolved.Select(r => r.Game.Id).Distinct().ToList(), ct);

        foreach (var (stat, game) in resolved)
        {
            if (!playerMap.TryGetValue(stat.SourcePlayerId, out var player))
                continue;

            // Second sighting of a key already handled in this batch. The row is in the change
            // tracker and not yet in the database, so the query below would not find it and we
            // would add a duplicate.
            if (written.TryGetValue((player.Id, game.Id), out var pending))
            {
                collisions++;

                if (pending.StatLine != stat.StatLineJson)
                {
                    pending.StatLine = stat.StatLineJson;
                    pending.CapturedAt = DateTimeOffset.UtcNow;
                    pending.IngestionRunId = await runId(ct);
                }

                continue;
            }

            var side = await SideAsync(sport, sourceRow.Key, stat, game, ct);

            // Upsert: a re-run of the same day refreshes the line rather than duplicating it.
            var existing = await db.PlayerGameStats.FirstOrDefaultAsync(
                s => s.PlayerId == player.Id && s.GameId == game.Id && s.SourceId == sourceRow.Id, ct);

            if (existing is null)
            {
                existing = new PlayerGameStat
                {
                    PlayerId = player.Id,
                    GameId = game.Id,
                    SourceId = sourceRow.Id,
                    TeamId = side,
                    StatLine = stat.StatLineJson,
                    CapturedAt = DateTimeOffset.UtcNow,
                    IngestionRunId = await runId(ct)
                };

                db.PlayerGameStats.Add(existing);
                rows++;
            }
            else if (!SameStatLine(existing.StatLine, stat.StatLineJson)
                     || (side is not null && existing.TeamId != side)
                     || liveRuns.Contains(existing.IngestionRunId))
            {
                // A re-walk is also how a line stored before sides were recorded gets one, so
                // a line whose numbers have not moved is still refreshed when its side has.
                existing.StatLine = stat.StatLineJson;
                existing.TeamId = side ?? existing.TeamId;
                existing.CapturedAt = DateTimeOffset.UtcNow;
                existing.IngestionRunId = await runId(ct);
                rows++;
            }

            written[(player.Id, game.Id)] = existing;
        }

        if (collisions > 0)
        {
            // Worth saying out loud: it usually means two of the provider's athlete ids
            // resolved to one player, which is an entity-resolution question rather than a
            // storage one.
            logger.LogWarning(
                "{Source}/{Sport}: {Count} stat lines resolved onto a player/game already written; merged",
                sourceRow.Key, sportKey, collisions);
        }

        rows += await PersistClosingLinesAsync(sourceRow, result.Lines, gameMap, ct);

        await db.SaveChangesAsync(ct);
        return rows;
    }

    /// <summary>
    /// Stores what the line closed at, for games that have already finished.
    ///
    /// <para>
    /// These are a <i>reference</i>, never the market. They are written under the stats
    /// provider's own source id, which is what keeps them distinguishable downstream — a reader
    /// wanting the market can require rows from an odds provider and fall back to these only
    /// when a fixture has none. Nothing here can contaminate a book consensus, because nothing
    /// here claims to be one (ADR 0011).
    /// </para>
    ///
    /// <para>
    /// Written once and left alone. A closing line is a fact about a moment that has passed, so
    /// a later walk over the same day must not rewrite it — and re-walking days is the ordinary
    /// case, not the exception.
    /// </para>
    /// </summary>
    private async Task<int> PersistClosingLinesAsync(
        Source sourceRow,
        IReadOnlyList<CanonicalOdds> lines,
        Dictionary<string, Game> gameMap,
        CancellationToken ct)
        => (await PersistClosingLinesWithClosedAsync(sourceRow, lines, gameMap, ct)).Written;

    /// <summary>
    /// As <see cref="PersistClosingLinesAsync"/>, and which games it actually gave the close they
    /// were owed. The refill decides "answered" from this, not from the reply: a moneyline ESPN
    /// states for a bout whose fighters it names differently is a reply the writer drops, and a
    /// game counted answered on the reply alone was never set aside and was asked again every press.
    /// </summary>
    private async Task<(int Written, HashSet<int> Closed)> PersistClosingLinesWithClosedAsync(
        Source sourceRow,
        IReadOnlyList<CanonicalOdds> lines,
        Dictionary<string, Game> gameMap,
        CancellationToken ct)
    {
        if (lines.Count == 0)
            return (0, []);

        var gameIds = gameMap.Values.Select(g => g.Id).ToHashSet();

        // A fight's close names its fighters as the provider bills them; a fighter whose name is
        // shared is held under a qualified one ("Bruno Silva (Flyweight)"), and a side the board
        // cannot pair is a close nobody reads. So a fight's sides are written as its own (ADR 0021).
        var fighters = await db.Games
            .Where(g => gameIds.Contains(g.Id) && FightSports.Keys.Contains(g.Sport!.Key))
            .Select(g => new { g.Id, Home = g.HomeTeam!.Name, Away = g.AwayTeam!.Name })
            .ToDictionaryAsync(g => g.Id, g => (g.Home, g.Away), ct);

        var held = (await db.ClosingLines
                .Where(c => c.SourceId == sourceRow.Id && gameIds.Contains(c.GameId))
                .Select(c => new { c.GameId, c.Market, c.Outcome })
                .ToListAsync(ct))
            .Select(c => (c.GameId, c.Market, c.Outcome))
            .ToHashSet();

        var written = 0;
        var closed = new HashSet<int>();

        foreach (var line in lines)
        {
            if (!gameMap.TryGetValue(line.SourceGameId, out var game))
                continue;

            var outcome = line.Outcome;

            if (line.Market != Markets.Total && fighters.TryGetValue(game.Id, out var corners))
            {
                if (FighterNames.Same(outcome, corners.Home) && !FighterNames.Same(outcome, corners.Away))
                    outcome = corners.Home;
                else if (FighterNames.Same(outcome, corners.Away) && !FighterNames.Same(outcome, corners.Home))
                    outcome = corners.Away;
            }

            if (!held.Add((game.Id, line.Market, outcome)))
                continue;

            db.ClosingLines.Add(new ClosingLine
            {
                GameId = game.Id,
                SourceId = sourceRow.Id,
                Book = line.Book,
                Market = line.Market,
                Outcome = outcome,
                Line = line.Line,
                PriceAmerican = line.PriceAmerican,
                CapturedAt = line.CapturedAt
            });

            written++;

            if (line.Market == (fighters.ContainsKey(game.Id) ? Markets.Moneyline : Markets.Total))
                closed.Add(game.Id);
        }

        // A game that now has the close it was owed is no longer set aside by anyone: the memory
        // of having asked goes with the debt.
        if (closed.Count > 0)
        {
            db.ClosingLineMisses.RemoveRange(await db.ClosingLineMisses
                .Where(m => closed.Contains(m.GameId))
                .ToListAsync(ct));
        }

        if (written > 0)
            logger.LogInformation("{Source}: {Count} closing lines recorded", sourceRow.Key, written);

        return (written, closed);
    }

    /// <summary>
    /// A fight's card and result beside its game (ADR 0021): written on first sight, refreshed as
    /// the card changes and the result arrives. The provider is the authority on its own card, so
    /// every field follows it — except a result, which a later read that has lost it (a fight
    /// center gone quiet, the scoreboard standing in) does not erase.
    /// </summary>
    /// <returns>1 when the row was written or changed.</returns>
    private async Task<int> UpsertBoutAsync(Game game, CanonicalBout canonical, CancellationToken ct)
    {
        var bout = await db.Bouts.FirstOrDefaultAsync(b => b.GameId == game.Id, ct);

        if (bout is null)
        {
            bout = new Bout { GameId = game.Id };
            db.Bouts.Add(bout);
        }

        var before = (bout.EventId, bout.EventName, bout.EventShortName, bout.CardSegment, bout.MatchNumber, bout.WeightClass,
            bout.ScheduledRounds, bout.TitleFight, bout.HomeRecord, bout.AwayRecord, bout.Outcome, bout.Method,
            bout.MethodDetail, bout.EndRound, bout.EndSeconds);

        bout.EventId = canonical.EventId;
        bout.EventName = Truncate(canonical.EventName, 160)!;
        bout.EventShortName = Truncate(canonical.EventShortName, 64) ?? bout.EventShortName;
        bout.CardSegment = Truncate(canonical.CardSegment, 32) ?? bout.CardSegment;
        bout.MatchNumber = canonical.MatchNumber ?? bout.MatchNumber;
        bout.WeightClass = Truncate(canonical.WeightClass, 48) ?? bout.WeightClass;
        bout.ScheduledRounds = canonical.ScheduledRounds;
        bout.TitleFight = canonical.TitleFight;
        bout.HomeRecord = Truncate(canonical.HomeRecord, 16) ?? bout.HomeRecord;
        bout.AwayRecord = Truncate(canonical.AwayRecord, 16) ?? bout.AwayRecord;

        if (canonical.Outcome is not null || bout.Outcome is null)
        {
            bout.Outcome = canonical.Outcome;
            bout.Method = Truncate(canonical.Method, 64) ?? (canonical.Outcome is null ? null : bout.Method);
            bout.MethodDetail = Truncate(canonical.MethodDetail, 96);
            bout.EndRound = canonical.EndRound;
            bout.EndSeconds = canonical.EndSeconds;
        }

        // A no contest has no winner and so no score, whatever a scoreboard said before the ruling.
        if (bout.Outcome == BoutOutcome.NoContest && (game.HomeScore is not null || game.AwayScore is not null))
        {
            game.HomeScore = null;
            game.AwayScore = null;
        }

        var after = (bout.EventId, bout.EventName, bout.EventShortName, bout.CardSegment, bout.MatchNumber, bout.WeightClass,
            bout.ScheduledRounds, bout.TitleFight, bout.HomeRecord, bout.AwayRecord, bout.Outcome, bout.Method,
            bout.MethodDetail, bout.EndRound, bout.EndSeconds);

        if (db.Entry(bout).State != EntityState.Added && before == after)
            return 0;

        await db.SaveChangesAsync(ct);
        return 1;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// The team a stat line was made for, when the source said and it is one of the game's two
    /// sides. A name that resolves to neither is not believed: storing it would credit a game
    /// to a club that was not in it, which is worse than falling back to the player's team.
    /// </summary>
    private async Task<int?> SideAsync(
        Sport sport, string sourceKey, CanonicalPlayerStat stat, Game game, CancellationToken ct)
    {
        if (stat.TeamName is null)
            return null;

        var team = await resolver.ResolveTeamAsync(
            sport, sourceKey, new CanonicalTeamRef(stat.TeamName, stat.SourceTeamId), ct);

        return team.Id == game.HomeTeamId || team.Id == game.AwayTeamId ? team.Id : null;
    }

    private async Task<Dictionary<string, Player>> UpsertPlayersAsync(
        Sport sport,
        string sourceKey,
        IReadOnlyList<CanonicalPlayer> canonicals,
        IReadOnlyDictionary<string, DateTimeOffset> playedAt,
        CancellationToken ct)
    {
        var map = new Dictionary<string, Player>();

        if (canonicals.Count == 0)
            return map;

        var existing = await db.Players.Where(p => p.SportId == sport.Id).ToListAsync(ct);

        // Each known player's most recent stored appearance. A payload older than that is
        // history, and history does not get to say where a player is now: a backfill walks
        // newest day first, so letting every day write the team left each player on the
        // oldest club the walk reached.
        var latest = await db.PlayerGameStats
            .Where(s => s.Game!.SportId == sport.Id)
            .GroupBy(s => s.PlayerId)
            .Select(g => new { g.Key, Latest = g.Max(s => s.Game!.StartsAt) })
            .ToDictionaryAsync(x => x.Key, x => x.Latest, ct);

        foreach (var canonical in canonicals)
        {
            // The team first, so a name shared by two people can be told apart by it — by the
            // provider's id where it gave one, since a fighter's team is the fighter, and two
            // fighters can share a name (ADR 0021).
            var team = canonical.TeamName is null
                ? null
                : await resolver.ResolveTeamAsync(sport, sourceKey,
                    new CanonicalTeamRef(canonical.TeamName, canonical.SourceTeamId), ct);

            var player = MatchPlayer(existing, canonical, sourceKey, team?.Id);

            if (player is null)
            {
                player = new Player
                {
                    SportId = sport.Id,
                    FullName = canonical.FullName,
                    Position = canonical.Position,
                    Status = canonical.Status
                };

                db.Players.Add(player);
                existing.Add(player);
            }

            if (!player.ExternalIds.ContainsKey(sourceKey))
            {
                player.ExternalIds = new Dictionary<string, string>(player.ExternalIds)
                {
                    [sourceKey] = canonical.SourcePlayerId
                };
            }

            // Players move between teams, so the team follows the newest game on record — and
            // only the newest. An older day re-walked keeps its side on the stat line instead.
            var current = !latest.TryGetValue(player.Id, out var last)
                          || !playedAt.TryGetValue(canonical.SourcePlayerId, out var played)
                          || played >= last;

            if (team is not null && current)
                player.TeamId = team.Id;

            map[canonical.SourcePlayerId] = player;
        }

        await db.SaveChangesAsync(ct);
        return map;
    }

    /// <summary>
    /// The stored player a source's athlete is, if any.
    ///
    /// <para>
    /// By the source's own id first — that is identity. By name only as a fallback, for a player
    /// another source created, and never onto a player this source already knows under a
    /// different id: that is a different person with the same name. It used to take any name
    /// match, so two ESPN athletes called Will Smith became one player and one of every pair of
    /// their stat lines was thrown away as a duplicate (ADR 0009's open item). Where more than one
    /// name match remains, the one on the athlete's team is the one; where the team cannot decide
    /// between them, none is taken and a new player is made — a duplicate row can be merged
    /// later, a merged person cannot be split.
    /// </para>
    /// </summary>
    public static Player? MatchPlayer(
        IReadOnlyList<Player> existing, CanonicalPlayer canonical, string sourceKey, int? teamId)
    {
        var byId = existing.FirstOrDefault(p =>
            p.ExternalIds.TryGetValue(sourceKey, out var id) && id == canonical.SourcePlayerId);

        if (byId is not null)
            return byId;

        var byName = existing
            .Where(p => string.Equals(p.FullName, canonical.FullName, StringComparison.Ordinal)
                        && !p.ExternalIds.ContainsKey(sourceKey))
            .ToList();

        if (byName.Count <= 1)
            return byName.FirstOrDefault(p => teamId is null || p.TeamId is null || p.TeamId == teamId);

        var onTeam = byName.Where(p => teamId is not null && p.TeamId == teamId).ToList();
        return onTeam.Count == 1 ? onTeam[0] : null;
    }
}
