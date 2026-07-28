using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Services;

public record SettlementSummary(int Graded, int ClvResolved, int LeftPending);

/// <summary>
/// Settles journal entries once their game finishes, and resolves closing-line value.
///
/// <para>
/// CLV needs the market as it stood at first pitch. That used to mean reading the append-only
/// scan table; it now means reading <see cref="ClosingLine"/>, which
/// <see cref="OddsRetentionService"/> promotes once a game starts and which is never pruned.
/// The result is still denormalised onto the entry, so the number outlives even that.
/// </para>
///
/// <para>
/// Because the close is written by a separate pass, settlement can arrive first — a game that
/// finishes inside one retention interval, or a manual <see cref="MarkFinalAsync"/>. So CLV is
/// retried for already-graded entries rather than only attempted while an entry is pending;
/// otherwise a race between two background jobs would silently cost a number that cannot be
/// recomputed later.
/// </para>
/// </summary>
public class SettlementService(LineOpsDbContext db, ILogger<SettlementService> logger)
{
    public async Task<SettlementSummary> SettleAsync(CancellationToken ct = default)
    {
        var graded = 0;
        var clvResolved = 0;
        var pending = 0;

        var candidates = await db.JournalEntries
            .Include(e => e.Game).ThenInclude(g => g!.HomeTeam)
            .Include(e => e.Game).ThenInclude(g => g!.AwayTeam)
            .Where(e => e.Result == EntryResult.Pending && e.GameId != null)
            .ToListAsync(ct);

        foreach (var entry in candidates)
        {
            var game = entry.Game;

            if (game is null
                || game.Status != GameStatus.Final
                || game.HomeScore is not { } homeScore
                || game.AwayScore is not { } awayScore)
            {
                pending++;
                continue;
            }

            // Resolve CLV first: it depends only on the game having started, not on the
            // result, and it must happen before old snapshots are ever pruned.
            if (entry.ClosingSnapshotId is null && await ResolveClvAsync(entry, game, ct))
                clvResolved++;

            var result = Grading.Grade(
                entry, game.HomeTeam?.Name ?? string.Empty, game.AwayTeam?.Name ?? string.Empty,
                homeScore, awayScore);

            if (result is null)
            {
                // Not automatically gradable — a free-text or unmapped market. Leave it for
                // the user rather than inventing a result.
                pending++;
                continue;
            }

            PerformanceAnalytics.ApplyResult(entry, result.Value);
            graded++;
        }

        clvResolved += await BackfillMissingClvAsync(ct);

        if (graded > 0 || clvResolved > 0)
            await db.SaveChangesAsync(ct);

        if (graded > 0 || clvResolved > 0 || pending > 0)
        {
            logger.LogInformation(
                "Settlement: {Graded} graded, {Clv} CLV resolved, {Pending} left pending",
                graded, clvResolved, pending);
        }

        return new SettlementSummary(graded, clvResolved, pending);
    }

    /// <summary>
    /// Picks up entries that were graded before their game's close had been promoted.
    ///
    /// The main loop only visits pending entries, so without this an entry settled in the gap
    /// between first pitch and the next retention pass would keep <c>ClosingSnapshotId</c> null
    /// for ever — nothing would ever look at it again. Promotion is idempotent and the close is
    /// permanent, so retrying costs one indexed query per settled-but-unresolved entry and
    /// eventually always succeeds.
    /// </summary>
    private async Task<int> BackfillMissingClvAsync(CancellationToken ct)
    {
        var stragglers = await db.JournalEntries
            .Include(e => e.Game)
            .Where(e => e.Result != EntryResult.Pending
                        && e.ClosingSnapshotId == null
                        && e.GameId != null)
            .ToListAsync(ct);

        var resolved = 0;

        foreach (var entry in stragglers)
        {
            if (entry.Game is { } game && await ResolveClvAsync(entry, game, ct))
                resolved++;
        }

        return resolved;
    }

    /// <summary>
    /// Finds the last price observed before kick-off for the same book, market and outcome.
    /// Falls back to any book when the entry's own book was not tracked — a cross-book close
    /// is a weaker comparison but far better than no CLV at all.
    /// </summary>
    private async Task<bool> ResolveClvAsync(JournalEntry entry, Game game, CancellationToken ct)
    {
        var closing = await FindClosingAsync(entry, game, sameBookOnly: true, ct)
                      ?? await FindClosingAsync(entry, game, sameBookOnly: false, ct);

        if (closing is null)
            return false;

        entry.ClosingSnapshotId = closing.Id;
        entry.ClosingCapturedAt = closing.CapturedAt;
        entry.ClosingPrice = closing.PriceAmerican;
        return true;
    }

    /// <summary>
    /// Reads the promoted close rather than the scan stream it came from.
    ///
    /// This used to search <c>OddsSnapshots</c> for the newest row at or before the start.
    /// That worked while every scan was kept for ever, and would break silently the moment
    /// retention began deleting them — an entry settled a week late would simply find nothing
    /// and lose its CLV. <c>ClosingLines</c> holds exactly one row per game, book, market and
    /// outcome, chosen by the same rule, and is never pruned.
    /// </summary>
    private async Task<ClosingLine?> FindClosingAsync(
        JournalEntry entry, Game game, bool sameBookOnly, CancellationToken ct)
    {
        var query = db.ClosingLines
            .Where(c => c.GameId == game.Id
                        && c.Market == entry.Market
                        && c.Outcome == entry.Outcome);

        if (sameBookOnly)
            query = query.Where(c => c.Book == entry.Book);

        // Unique per (game, book, market, outcome), so the same-book lookup returns at most
        // one. The any-book fallback can match several; the latest close wins.
        return await query.OrderByDescending(c => c.CapturedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Marks games final from ingested scores. Real score feeds arrive via the stats sources;
    /// this keeps the settlement path testable independently of them.
    /// </summary>
    public async Task<int> MarkFinalAsync(
        int gameId, int homeScore, int awayScore, CancellationToken ct = default)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
            return 0;

        game.HomeScore = homeScore;
        game.AwayScore = awayScore;
        game.Status = GameStatus.Final;

        await db.SaveChangesAsync(ct);

        var summary = await SettleAsync(ct);
        return summary.Graded;
    }
}
