using System.Globalization;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Ingestion.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Services;

/// <summary>
/// The Odds API account's month, as the desk states it before anything is spent: what is left, the
/// reserve held back for pulls on request, and when it resets — and, for a pull of a given price,
/// whether it can run, runs as asked, or asks first (ADR 0026). One answer for the menu, the
/// scheduler and Ops, so a key never offers what the guard would refuse.
/// </summary>
public sealed class CreditAllowance(XLinesDbContext db, IOptions<IngestionOptions> options)
{
    /// <summary>
    /// A pull at or under this asks nothing beyond its stated price, unless it would dip into the
    /// reserve: one league's lines (three markets) or scores (two). A larger one confirms first.
    /// </summary>
    public const int TrivialCredits = 3;

    /// <summary>The account's month now; null where no credit-metered row is held (a database not yet seeded).</summary>
    public async Task<AllowanceState?> ReadAsync(CancellationToken ct = default)
    {
        var source = await db.Sources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SourceAccounts.TheOddsApi, ct);

        if (source?.MonthlyCreditBudget is not { } budget)
            return null;

        var now = DateTimeOffset.UtcNow;
        var state = await MonthlyCredits.StateAsync(db, source, now, ct);
        var nextMonth = DateOnly.FromDateTime(MonthlyCredits.NextReset(now).UtcDateTime);

        return new AllowanceState(budget, state.Spent, state.ProviderRemaining is not null,
            options.Value.LinePolling.MonthlyCreditReserve, nextMonth);
    }
}

/// <param name="Reported">
/// Whether the provider has said this month what is left on the key. Without it the remainder is
/// only this database's count, which misses another machine's spend, so it is not stated as a fact.
/// </param>
/// <param name="ResetsOn">The first of next month (UTC), when the provider restores the allowance.</param>
public sealed record AllowanceState(int Budget, int Spent, bool Reported, int Reserve, DateOnly ResetsOn)
{
    public int Remaining => Math.Max(0, Budget - Spent);

    /// <summary>"Dec 1" — when the credits come back.</summary>
    public string ResetsOnText => ResetsOn.ToString("MMM d", CultureInfo.InvariantCulture);

    /// <summary>
    /// What a pull of <paramref name="cost"/> credits may do now. Refused when the month is spent or
    /// cannot cover it — the guard would refuse it, so it is not offered; Confirm when it is more than
    /// <see cref="CreditAllowance.TrivialCredits"/> or would leave less than the reserve.
    /// </summary>
    public CreditCheck Check(int cost)
    {
        if (cost <= 0)
            return CreditCheck.Go;

        if (Spent >= Budget)
            return CreditCheck.Refuse($"This month's {Budget:N0} credits are spent. They reset on {ResetsOnText}.");

        if (Remaining < cost)
            return CreditCheck.Refuse($"Needs {cost:N0} credits and {Remaining:N0} are left this month. They reset on {ResetsOnText}.");

        var after = Remaining - cost;

        if (after < Reserve)
            return CreditCheck.Ask($"This spends {cost:N0} credits and leaves {after:N0} of {Budget:N0}, under the {Reserve:N0} held in reserve until {ResetsOnText}. Spend them?");

        if (cost > CreditAllowance.TrivialCredits)
            return CreditCheck.Ask($"This spends {cost:N0} credits and leaves {after:N0} of {Budget:N0} until {ResetsOnText}. Spend them?");

        return CreditCheck.Go;
    }

    /// <summary>Whether the scheduler may spend <paramref name="cost"/> unattended: never into the reserve.</summary>
    public bool AffordsUnattended(int cost) => Spent < Budget && Remaining - cost >= Reserve;
}

/// <summary>A pull's standing against the allowance: run, ask first (with the question), or not offered (with why).</summary>
public sealed record CreditCheck(bool Allowed, string? Question, string? Refusal)
{
    public static CreditCheck Go { get; } = new(true, null, null);

    public static CreditCheck Ask(string question) => new(true, question, null);

    public static CreditCheck Refuse(string why) => new(false, null, why);
}
