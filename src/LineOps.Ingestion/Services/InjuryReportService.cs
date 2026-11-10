using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Writes a league's injury report onto its players: the club's word on each — Out, Doubtful,
/// Questionable, Injured Reserve, Active — and the report's note.
///
/// <para>
/// The Board names an NFL club's quarterback from its last box score (<c>Starters</c>), and a box
/// score cannot know that the club has since ruled him out. The report can. It is one request for
/// the whole league, read beside the slate twice an hour, and recorded as an ordinary run so the
/// reliability layer sees it.
/// </para>
///
/// <para>
/// It annotates and never creates. A player is found by the provider's own id, and failing that
/// by name within the league — where two players share the name, by the club the report puts him
/// on, and where the club cannot decide, not at all.
/// </para>
///
/// <para>
/// A player the report has stopped naming is left as he was. ESPN lists twenty-five entries a
/// club, newest first, and its <c>limit</c> parameter changes nothing, so a quarterback ruled out
/// weeks ago falls off the bottom as Active entries push him out — his absence says the list is
/// full, not that he is well. Instead a status ages: the Board reads one the report has not
/// repeated within <see cref="Player.InjuryStatusLife"/> as no status
/// (<c>BoardService.CurrentInjuries</c>).
/// </para>
/// </summary>
public class InjuryReportService(
    LineOpsDbContext db,
    IEnumerable<IInjurySource> sources,
    ILogger<InjuryReportService> logger)
{
    /// <summary>The run row's job key.</summary>
    public const string JobKey = "espn:injuries";

    public async Task<IngestionOutcome> RefreshAsync(string sportKey, string jobKey, CancellationToken ct)
    {
        if (sources.FirstOrDefault() is not { } source)
            return new IngestionOutcome(0, RunStatus.Partial, 0, "No injury source registered.");

        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == sportKey, ct);
        if (sport is null)
            return new IngestionOutcome(0, RunStatus.Success, 0, null);

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

        IReadOnlyList<CanonicalInjury> report;

        try
        {
            report = await source.FetchInjuriesAsync(sportKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Source}/{Sport}: injury report unavailable", source.Key, sportKey);

            run.RequestsMade = 1;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Status = RunStatus.Failed;
            run.Error = $"{ex.GetType().Name}: {ex.Message}";
            await db.SaveChangesAsync(ct);

            return new IngestionOutcome(run.Id, run.Status, 0, run.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var players = await db.Players.Where(p => p.SportId == sport.Id).ToListAsync(ct);
        var teams = await db.Teams.AsNoTracking().Where(t => t.SportId == sport.Id).ToDictionaryAsync(t => t.Id, ct);

        var byId = new Dictionary<string, Player>();
        foreach (var player in players)
        {
            if (player.ExternalIds.TryGetValue(source.Key, out var id))
                byId.TryAdd(id, player);
        }

        var byName = players.ToLookup(p => p.FullName.Trim(), StringComparer.OrdinalIgnoreCase);

        var named = new HashSet<int>();
        var unmatched = 0;

        foreach (var entry in report)
        {
            var player = byId.GetValueOrDefault(entry.SourcePlayerId) ?? ByName(byName, teams, source.Key, entry);

            if (player is null || !named.Add(player.Id))
            {
                if (player is null)
                    unmatched++;
                continue;
            }

            player.InjuryStatus = Truncate(entry.Status, 32);
            player.InjuryNote = Truncate(entry.Note, 512);
            player.InjuryUpdatedAt = now;

            // A player the report named and the roster matched by name alone now carries the
            // report's id, so the next report finds him exactly and a shared name cannot lose him.
            // A new dictionary rather than a key added to the tracked one, as the other writers of
            // ExternalIds do, so the change is a new value whatever the column's comparer sees.
            if (!player.ExternalIds.ContainsKey(source.Key))
            {
                player.ExternalIds = new Dictionary<string, string>(player.ExternalIds) { [source.Key] = entry.SourcePlayerId };
                byId.TryAdd(entry.SourcePlayerId, player);
            }
        }

        run.RequestsMade = 1;
        run.RowsIngested = named.Count;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = RunStatus.Success;
        run.Detail = $"{report.Count} on the report, {named.Count} matched, {unmatched} not on file.";
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Source}/{Sport}: injury report {Detail}", source.Key, sportKey, run.Detail);

        return new IngestionOutcome(run.Id, run.Status, run.RowsIngested, null);
    }

    /// <summary>
    /// The player a report entry names, by name, when the provider's id is not on file. Never
    /// one the provider already knows under another id — that is a different person — and where
    /// the name is still shared, only the one on the entry's club.
    /// </summary>
    private static Player? ByName(
        ILookup<string, Player> byName, IReadOnlyDictionary<int, Team> teams, string sourceKey, CanonicalInjury entry)
    {
        var candidates = byName[entry.FullName.Trim()]
            .Where(p => !p.ExternalIds.TryGetValue(sourceKey, out var id) || id == entry.SourcePlayerId)
            .ToList();

        if (candidates.Count <= 1)
            return candidates.SingleOrDefault();

        var onClub = candidates
            .Where(p => p.TeamId is { } teamId && teams.TryGetValue(teamId, out var team) && OnClub(team, sourceKey, entry))
            .ToList();

        return onClub.Count == 1 ? onClub[0] : null;
    }

    private static bool OnClub(Team team, string sourceKey, CanonicalInjury entry)
        => (entry.SourceTeamId is { } id && team.ExternalIds.TryGetValue(sourceKey, out var known) && known == id)
           || string.Equals(team.Name, entry.TeamName, StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
