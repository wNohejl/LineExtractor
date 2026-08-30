using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
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

    private async Task<IngestionOutcome> IngestAsync(
        IStatsSource source,
        string sportKey,
        string jobKey,
        DateOnly date,
        bool schedulesOnly,
        CancellationToken ct)
    {
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

        try
        {
            if (!await budget.TryReserveAsync(sourceRow, estimatedRequests: 2, ct))
            {
                run.Status = RunStatus.Partial;
                run.Error = "Skipped: source is at its configured rate/credit budget.";
                run.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
            }

            if (source is IFailureInjectable injectable)
                injectable.FailureMode = sourceRow.FailureMode;

            var result = schedulesOnly
                ? await source.FetchScheduleAsync(sportKey, date, ct)
                : await source.FetchBoxScoresAsync(sportKey, date, ct);

            run.RequestsMade = result.Cost.Requests;
            run.CreditsSpent = result.Cost.Credits;

            var rows = await PersistAsync(sourceRow, result, sportKey, run.Id, ct);

            run.RowsIngested = rows;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Status = RunStatus.Success;

            await db.SaveChangesAsync(ct);

            logger.LogInformation("{Source}/{Sport}: {Rows} stat rows", source.Key, sportKey, rows);
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

            var failed = await db.IngestionRuns.FirstOrDefaultAsync(r => r.Id == run.Id, ct);
            var error = $"{ex.GetType().Name}: {ex.Message}";

            if (failed is not null)
            {
                failed.Status = RunStatus.Failed;
                failed.Error = error;
                failed.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            logger.LogError(ex, "{Source}/{Sport}: stats ingestion failed", source.Key, sportKey);
            return new IngestionOutcome(run.Id, RunStatus.Failed, 0, error);
        }
    }

    private async Task<int> PersistAsync(
        Source sourceRow, StatsFetchResult result, string sportKey, long runId, CancellationToken ct)
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
        }

        // Resolve each stat line to a game *before* creating anyone, so that a player only
        // enters the database because they played in a game we can point at.
        //
        // A player who never appears in a box score is a row that will never be read: the
        // cross-reference skips them, the Players panel shows them with nothing, and they cost
        // an index entry each. The demo fixture made this visible — it reports a full roster
        // with no games, so every one of its players was created and every one of its stat
        // lines was then dropped for having nowhere to go.
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

        var playerMap = await UpsertPlayersAsync(sport, sourceRow.Key, participants, ct);
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
                    pending.IngestionRunId = runId;
                }

                continue;
            }

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
                    StatLine = stat.StatLineJson,
                    CapturedAt = DateTimeOffset.UtcNow,
                    IngestionRunId = runId
                };

                db.PlayerGameStats.Add(existing);
                rows++;
            }
            else if (existing.StatLine != stat.StatLineJson)
            {
                existing.StatLine = stat.StatLineJson;
                existing.CapturedAt = DateTimeOffset.UtcNow;
                existing.IngestionRunId = runId;
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

        rows += await PersistClosingLinesAsync(sourceRow, result, gameMap, ct);

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
        StatsFetchResult result,
        Dictionary<string, Game> gameMap,
        CancellationToken ct)
    {
        if (result.Lines.Count == 0)
            return 0;

        var gameIds = gameMap.Values.Select(g => g.Id).ToHashSet();

        var held = (await db.ClosingLines
                .Where(c => c.SourceId == sourceRow.Id && gameIds.Contains(c.GameId))
                .Select(c => new { c.GameId, c.Market, c.Outcome })
                .ToListAsync(ct))
            .Select(c => (c.GameId, c.Market, c.Outcome))
            .ToHashSet();

        var written = 0;

        foreach (var line in result.Lines)
        {
            if (!gameMap.TryGetValue(line.SourceGameId, out var game))
                continue;

            if (!held.Add((game.Id, line.Market, line.Outcome)))
                continue;

            db.ClosingLines.Add(new ClosingLine
            {
                GameId = game.Id,
                SourceId = sourceRow.Id,
                Book = line.Book,
                Market = line.Market,
                Outcome = line.Outcome,
                Line = line.Line,
                PriceAmerican = line.PriceAmerican,
                CapturedAt = line.CapturedAt
            });

            written++;
        }

        if (written > 0)
            logger.LogInformation("{Source}: {Count} closing lines recorded", sourceRow.Key, written);

        return written;
    }

    private async Task<Dictionary<string, Player>> UpsertPlayersAsync(
        Sport sport, string sourceKey, IReadOnlyList<CanonicalPlayer> canonicals, CancellationToken ct)
    {
        var map = new Dictionary<string, Player>();

        if (canonicals.Count == 0)
            return map;

        var existing = await db.Players.Where(p => p.SportId == sport.Id).ToListAsync(ct);

        foreach (var canonical in canonicals)
        {
            var player = existing.FirstOrDefault(p =>
                             p.ExternalIds.TryGetValue(sourceKey, out var id)
                             && id == canonical.SourcePlayerId)
                         ?? existing.FirstOrDefault(p => p.FullName == canonical.FullName);

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

            // Players move between teams mid-season, so team is refreshed on every sync.
            if (canonical.TeamName is not null)
            {
                var team = await resolver.ResolveTeamAsync(sport, sourceKey, canonical.TeamName, ct);
                player.TeamId = team.Id;
            }

            map[canonical.SourcePlayerId] = player;
        }

        await db.SaveChangesAsync(ct);
        return map;
    }
}
