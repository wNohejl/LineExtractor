using XLines.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace XLines.Data;

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
    /// <summary>
    /// The first instant of <paramref name="now"/>'s calendar month in UTC, when the provider's
    /// allowance last reset. The one definition of the month every credit reader counts from, so the
    /// planner, the allowance and the spent count cannot disagree about which runs are this month's.
    /// Taken in UTC whatever offset <paramref name="now"/> carries: the month is the provider's, not
    /// the caller's clock.
    /// </summary>
    public static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The first instant of the next calendar month in UTC, when the allowance is restored.</summary>
    public static DateTimeOffset NextReset(DateTimeOffset now) => MonthStart(now).AddMonths(1);

    /// <summary>The larger of the two accounts of the month. Pure, so the rule is testable alone.</summary>
    public static int Reconcile(int recordedHere, int? monthlyBudget, int? providerRemaining)
    {
        if (monthlyBudget is not { } budget || providerRemaining is not { } remaining)
            return recordedHere;

        return Math.Max(recordedHere, budget - remaining);
    }

    /// <summary>
    /// Credits gone this calendar month (UTC) for one source, by <see cref="Reconcile"/> — counted
    /// across every row of the source's account (<see cref="SourceAccounts"/>), since the key's
    /// allowance is spent by both. A source alone on its account counts only its own runs, as before.
    /// </summary>
    public static async Task<int> SpentAsync(
        XLinesDbContext db, Source source, DateTimeOffset now, CancellationToken ct = default)
        => (await StateAsync(db, source, now, ct)).Spent;

    /// <summary>
    /// The month as the account stands: spent (by <see cref="Reconcile"/>), and the provider's own
    /// newest report of what is left, when one has been made this month.
    /// </summary>
    public static async Task<MonthlyCreditState> StateAsync(
        XLinesDbContext db, Source source, DateTimeOffset now, CancellationToken ct = default)
    {
        var monthStart = MonthStart(now);

        var keys = SourceAccounts.Sharing(source.Key);
        List<int> ids = keys.Count == 1
            ? [source.Id]
            : await db.Sources.Where(s => keys.Contains(s.Key)).Select(s => s.Id).ToListAsync(ct);

        if (!ids.Contains(source.Id))
            ids.Add(source.Id);

        var runs = db.IngestionRuns.Where(r => ids.Contains(r.SourceId) && r.StartedAt >= monthStart);

        var recorded = await runs.SumAsync(r => (int?)r.CreditsSpent, ct) ?? 0;

        var reported = await runs
            .Where(r => r.ProviderCreditsRemaining != null)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => r.ProviderCreditsRemaining)
            .FirstOrDefaultAsync(ct);

        return new MonthlyCreditState(Reconcile(recorded, source.MonthlyCreditBudget, reported), source.MonthlyCreditBudget, reported);
    }
}

/// <summary>A credit-metered account's month: what is gone, the budget, and the provider's last word on what is left.</summary>
public readonly record struct MonthlyCreditState(int Spent, int? Budget, int? ProviderRemaining)
{
    /// <summary>What is left by the larger account of what is gone; null for a source with no monthly budget.</summary>
    public int? Remaining => Budget is { } budget ? Math.Max(0, budget - Spent) : null;
}
