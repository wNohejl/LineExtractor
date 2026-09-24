using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Ingestion.Adapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

/// <summary>What one day's pass matched and learned.</summary>
public sealed record SpineOutcome(DateOnly Date, int Scheduled, int Matched, int Probables, int Unmatched);

/// <summary>
/// Joins MLB's own schedule to the games ESPN created: MLB's game id, the doubleheader number,
/// and the announced starting pitchers.
///
/// <para>
/// ADR 0014 called MLB's schedule the entity spine, and the adapter that reads it was written
/// and registered and then called by nothing. This is the call. It never creates a game — ESPN
/// owns the fixture list (ADR 0011) — it only annotates the ones there are.
/// </para>
///
/// <para>
/// Matching goes by MLB's id once a game has one, and before that by the two teams on the
/// league day. The team names agree between the providers (checked against all thirty on
/// 23 September 2026), so a name is enough to find the team, and the MLB team id is written onto
/// it the first time so a later rename cannot break the join. A doubleheader is two candidates for
/// one pair of teams; MLB numbers them, and the numbers are taken in start order.
/// </para>
/// </summary>
public class MlbSpineService(LineOpsDbContext db, MlbStatsApiAdapter adapter, ILogger<MlbSpineService> logger)
{
    public const string Key = MlbStatsApiAdapter.SourceKey;

    public async Task<SpineOutcome> SyncAsync(DateOnly date, CancellationToken ct = default)
    {
        var slate = (await adapter.FetchScheduleAsync(date, ct)).Where(g => g.Counts).ToList();
        return await ApplyAsync(date, slate, ct);
    }

    /// <summary>The matching, apart from the fetch, so it can be tested on a recorded slate.</summary>
    public async Task<SpineOutcome> ApplyAsync(DateOnly date, IReadOnlyList<MlbScheduledGame> slate, CancellationToken ct = default)
    {
        var sport = await db.Sports.FirstOrDefaultAsync(s => s.Key == "mlb", ct);
        if (sport is null || slate.Count == 0)
            return new SpineOutcome(date, slate.Count, 0, 0, slate.Count);

        var teams = await db.Teams.Where(t => t.SportId == sport.Id).ToListAsync(ct);

        // The league day, with room either side: a 10pm Eastern first pitch is still that day's.
        var from = LeagueClock.StartOf(date).AddHours(-6);
        var to = LeagueClock.StartOf(date.AddDays(1)).AddHours(6);

        var games = await db.Games
            .Where(g => g.SportId == sport.Id && g.StartsAt >= from && g.StartsAt < to)
            .OrderBy(g => g.StartsAt)
            .ToListAsync(ct);

        var claimed = new HashSet<int>();
        int matched = 0, probables = 0, unmatched = 0;

        // Game 1 before game 2, so a doubleheader's numbers are taken in the order they are played.
        foreach (var scheduled in slate.OrderBy(g => g.GameNumber).ThenBy(g => g.StartsAt))
        {
            var home = TeamFor(teams, scheduled.Home);
            var away = TeamFor(teams, scheduled.Away);
            var pk = scheduled.GamePk.ToString();

            var game = games.FirstOrDefault(g => g.ExternalIds.TryGetValue(Key, out var id) && id == pk);

            if (game is null && home is not null && away is not null)
            {
                var candidates = games
                    .Where(g => !claimed.Contains(g.Id)
                                && g.HomeTeamId == home.Id && g.AwayTeamId == away.Id
                                && !g.ExternalIds.ContainsKey(Key)
                                && LeagueClock.DateOf(g.StartsAt) == date)
                    .ToList();

                game = scheduled.IsDoubleHeader
                    ? candidates.OrderBy(g => g.StartsAt).FirstOrDefault()
                    : candidates.OrderBy(g => (g.StartsAt - scheduled.StartsAt).Duration()).FirstOrDefault();
            }

            if (game is null)
            {
                unmatched++;
                continue;
            }

            claimed.Add(game.Id);
            matched++;

            game.ExternalIds = new Dictionary<string, string>(game.ExternalIds) { [Key] = pk };
            game.DoubleHeaderGame = scheduled.IsDoubleHeader ? scheduled.GameNumber : null;

            // As MLB states it, including when it takes a name back: a scratched starter is news,
            // and a stale name would be a claim the source no longer makes.
            game.HomeProbablePitcher = scheduled.Home.ProbablePitcher?.FullName;
            game.AwayProbablePitcher = scheduled.Away.ProbablePitcher?.FullName;

            if (game.HomeProbablePitcher is not null || game.AwayProbablePitcher is not null)
                probables++;
        }

        await db.SaveChangesAsync(ct);

        if (unmatched > 0)
            logger.LogInformation("{Source}: {Date} — {Unmatched} of {Count} MLB games have no ESPN fixture yet",
                Key, date, unmatched, slate.Count);

        return new SpineOutcome(date, slate.Count, matched, probables, unmatched);
    }

    /// <summary>
    /// The team for one side: by MLB's team id once it has been recorded, by name the first
    /// time — when the id is written onto the team for every time after.
    /// </summary>
    private static Team? TeamFor(IReadOnlyList<Team> teams, MlbSide side)
    {
        var id = side.TeamId.ToString();

        var team = teams.FirstOrDefault(t => t.ExternalIds.TryGetValue(Key, out var known) && known == id)
                   ?? teams.FirstOrDefault(t => string.Equals(t.Name, side.Name, StringComparison.OrdinalIgnoreCase));

        if (team is not null && !team.ExternalIds.ContainsKey(Key))
            team.ExternalIds = new Dictionary<string, string>(team.ExternalIds) { [Key] = id };

        return team;
    }
}
