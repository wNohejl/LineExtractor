using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data;

/// <summary>
/// How much of a credit-metered provider's month is gone: what this database recorded spending,
/// or what the provider says has been spent on the key, whichever is more.
///
/// <para>
/// A database knows only its own runs. The desk is developed on more than one machine, each with
/// its own Postgres and the same API key, so each machine's count misses the other's. Both
/// would pace themselves against the whole allowance, and between them could spend it twice
/// over. The provider meters the key, not the database, and reports what is left on every
/// response (<see cref="IngestionRun.ProviderCreditsRemaining"/>). Its newest report this month
/// is the floor: spent is at least the budget less that remainder.
/// </para>
///
/// <para>
/// The report is as fresh as this machine's last call, so another machine's spend since then
/// is still unseen — at most one scan's worth, because the next call refreshes it. Reports from
/// before the month began are ignored: the allowance resets, and an old remainder would
/// overstate what is gone.
/// </para>
/// </summary>
public static class MonthlyCredits
{
    /// <summary>The larger of the two accounts of the month. Pure, so the rule is testable alone.</summary>
    public static int Reconcile(int recordedHere, int? monthlyBudget, int? providerRemaining)
    {
        if (monthlyBudget is not { } budget || providerRemaining is not { } remaining)
            return recordedHere;

        return Math.Max(recordedHere, budget - remaining);
    }

    /// <summary>Credits gone this calendar month (UTC) for one source, by <see cref="Reconcile"/>.</summary>
    public static async Task<int> SpentAsync(
        LineOpsDbContext db, Source source, DateTimeOffset now, CancellationToken ct = default)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var runs = db.IngestionRuns.Where(r => r.SourceId == source.Id && r.StartedAt >= monthStart);

        var recorded = await runs.SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0;

        var reported = await runs
            .Where(r => r.ProviderCreditsRemaining != null)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => r.ProviderCreditsRemaining)
            .FirstOrDefaultAsync(ct);

        return Reconcile(recorded, source.MonthlyCreditBudget, reported);
    }
}
