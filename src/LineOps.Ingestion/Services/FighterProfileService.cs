using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Keeps each fighter's profile — bio, career rates, and every recorded fight with its stat sheet —
/// as current as their last fight (ADR 0021).
///
/// <para>
/// A career only changes when its fighter fights, so a profile is owed in three cases and no
/// others: a fighter the desk has never described; one who has fought since the profile was
/// taken, once ESPN has had half a day to write the fight up; and one on a card ahead whose
/// profile is a week old, so the comparison an operator bets from is not a month stale. Four
/// requests a fighter against an unmetered courtesy endpoint, paced by the adapter like every
/// other ESPN call, and recorded as an ordinary run so the reliability layer sees it.
/// </para>
/// </summary>
public class FighterProfileService(
    LineOpsDbContext db,
    IEnumerable<IFighterSource> sources,
    ILogger<FighterProfileService> logger)
{
    /// <summary>How long after a fight ESPN is given to write it into the fighter's history.</summary>
    public static readonly TimeSpan WriteUpDelay = TimeSpan.FromHours(12);

    /// <summary>How far ahead a card's fighters are kept fresh.</summary>
    public static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(14);

    /// <summary>How old an upcoming fighter's profile may be before it is taken again.</summary>
    public static readonly TimeSpan UpcomingFreshness = TimeSpan.FromDays(7);

    /// <summary>One fighter owed a profile, and why it is owed — fighters on a card ahead first.</summary>
    public sealed record Owed(int TeamId, string SourceFighterId, string Name, bool Upcoming, DateTimeOffset? LastFought);

    /// <summary>The fighters owed a profile in one sport, most pressing first.</summary>
    public async Task<IReadOnlyList<Owed>> OwedAsync(string sportKey, string sourceKey, CancellationToken ct)
    {
        if (!FightSports.IsFight(sportKey))
            return [];

        var now = DateTimeOffset.UtcNow;

        var bouts = await db.Games.AsNoTracking()
            .Where(g => g.Sport!.Key == sportKey && g.Status != GameStatus.Postponed)
            .Select(g => new { g.HomeTeamId, g.AwayTeamId, g.StartsAt, g.Status })
            .ToListAsync(ct);

        var appearances = bouts
            .SelectMany(b => new[] { (TeamId: b.HomeTeamId, b.StartsAt, b.Status), (TeamId: b.AwayTeamId, b.StartsAt, b.Status) })
            .GroupBy(a => a.TeamId)
            .ToDictionary(g => g.Key, g => (
                LastFought: g.Where(a => a.Status == GameStatus.Final).Select(a => (DateTimeOffset?)a.StartsAt).Max(),
                Next: g.Where(a => a.Status == GameStatus.Scheduled && a.StartsAt >= now).Select(a => (DateTimeOffset?)a.StartsAt).Min()));

        var profiles = await db.FighterProfiles.AsNoTracking()
            .Where(p => p.Team!.Sport!.Key == sportKey)
            .ToDictionaryAsync(p => p.TeamId, p => p.RefreshedAt, ct);

        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.Sport!.Key == sportKey)
            .ToListAsync(ct);

        var owed = new List<Owed>();

        foreach (var team in teams)
        {
            if (!appearances.TryGetValue(team.Id, out var seen)
                || !team.ExternalIds.TryGetValue(sourceKey, out var fighterId)
                || fighterId == team.Name)
                continue;

            var upcoming = seen.Next is { } next && next - now <= UpcomingWindow;
            var held = profiles.TryGetValue(team.Id, out var refreshedAt) ? refreshedAt : (DateTimeOffset?)null;

            var due = held is not { } at
                      || (seen.LastFought is { } fought && at < fought + WriteUpDelay && now >= fought + WriteUpDelay)
                      || (upcoming && now - at >= UpcomingFreshness);

            if (due)
                owed.Add(new Owed(team.Id, fighterId, team.Name, upcoming, seen.LastFought));
        }

        return owed
            .OrderByDescending(o => o.Upcoming)
            .ThenByDescending(o => o.LastFought ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>How many fighters in one sport are owed a profile from the registered fighter source.</summary>
    public async Task<int> OwedCountAsync(string sportKey, CancellationToken ct)
        => sources.FirstOrDefault() is { } source ? (await OwedAsync(sportKey, source.Key, ct)).Count : 0;

    /// <summary>
    /// Takes up to <paramref name="limit"/> owed profiles from the first fighter source, as one run.
    /// </summary>
    /// <param name="progress">Told the running count after each fighter, for a long backfill pass.</param>
    public async Task<IngestionOutcome> RefreshAsync(
        string sportKey, string jobKey, int limit, CancellationToken ct, IProgress<int>? progress = null)
    {
        if (sources.FirstOrDefault() is not { } source)
            return new IngestionOutcome(0, RunStatus.Partial, 0, "No fighter source registered.");

        var sourceRow = await db.Sources.FirstOrDefaultAsync(s => s.Key == source.Key, ct)
            ?? throw new InvalidOperationException($"Source '{source.Key}' is not registered.");

        var owed = (await OwedAsync(sportKey, source.Key, ct)).Take(limit).ToList();

        if (owed.Count == 0)
            return new IngestionOutcome(0, RunStatus.Success, 0, null);

        var run = new IngestionRun
        {
            SourceId = sourceRow.Id,
            JobKey = jobKey,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var written = 0;
        var failures = 0;
        var requests = 0;
        string? lastError = null;

        foreach (var fighter in owed)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fetched = await source.FetchFighterAsync(sportKey, fighter.SourceFighterId, ct);
                requests += fetched.Cost.Requests;

                if (fetched.Fighter is { } found)
                {
                    await UpsertAsync(fighter.TeamId, found, ct);
                    written++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One fighter ESPN cannot describe must not cost the rest of the card.
                failures++;
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                logger.LogWarning(ex, "{Source}: profile unavailable for {Fighter} ({Id})",
                    source.Key, fighter.Name, fighter.SourceFighterId);
            }

            progress?.Report(written + failures);
        }

        run.RequestsMade = requests;
        run.RowsIngested = written;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = failures == 0 ? RunStatus.Success : written > 0 ? RunStatus.Partial : RunStatus.Failed;
        run.Error = failures == 0 ? null : $"{failures} of {owed.Count} fighters failed; last: {lastError}";
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Source}/{Sport}: {Written} of {Owed} fighter profiles taken in {Requests} requests",
            source.Key, sportKey, written, owed.Count, requests);

        return new IngestionOutcome(run.Id, run.Status, written, run.Error);
    }

    /// <summary>
    /// Writes one profile. The columns are the few facts a tale of the tape leads with; the two
    /// payloads are ESPN's figures as ESPN shaped them, serialised whole.
    /// </summary>
    private async Task UpsertAsync(int teamId, CanonicalFighter fighter, CancellationToken ct)
    {
        var profile = await db.FighterProfiles.FirstOrDefaultAsync(p => p.TeamId == teamId, ct);

        if (profile is null)
        {
            profile = new FighterProfile { TeamId = teamId };
            db.FighterProfiles.Add(profile);
        }

        profile.Nickname = Truncate(fighter.Nickname, 64);
        profile.Country = Truncate(fighter.Country, 64);
        profile.Association = Truncate(fighter.Association, 128);
        profile.Stance = Truncate(fighter.Stance, 32);
        profile.WeightClass = Truncate(fighter.WeightClass, 48);
        profile.HeightInches = fighter.HeightInches;
        profile.ReachInches = fighter.ReachInches;
        profile.BornOn = fighter.BornOn;
        profile.Record = Truncate(fighter.Record, 24);
        profile.Career = FighterPayload.Write(fighter.Career);
        profile.History = FighterPayload.Write(fighter.History);
        profile.RefreshedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
