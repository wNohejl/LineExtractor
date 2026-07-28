using LineOps.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data;

/// <summary>
/// The schedule as already ingested, for sources that price fixtures rather than discover them.
/// See <see cref="IScheduleReader"/> for why this exists.
/// </summary>
public class ScheduleReader(LineOpsDbContext db) : IScheduleReader
{
    public async Task<IReadOnlyList<ScheduledGame>> GetUpcomingAsync(
        string sportKey, TimeSpan window, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now + window;

        // Games already under way are excluded: this feeds a pre-match price scan, and a game
        // in progress has no pre-match line left to quote.
        return await db.Games
            .Where(g => g.Sport!.Key == sportKey && g.StartsAt > now && g.StartsAt <= horizon)
            .OrderBy(g => g.StartsAt)
            .Select(g => new ScheduledGame(
                sportKey,
                g.HomeTeam!.Name,
                g.AwayTeam!.Name,
                g.StartsAt))
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
