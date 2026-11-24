using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Data;
using Microsoft.EntityFrameworkCore;

namespace XLines.Ingestion.Services;

/// <summary>
/// The small pieces every writer of an <see cref="IngestionRun"/> row repeats: finding the source's
/// row, copying a fetch's cost onto the run, wording an exception and a budget refusal.
///
/// <para>
/// Deliberately helpers and not a base class. The odds pass, the stats pass, the refill, the
/// injury report, the fighter profiles and the gamecast backport each open, record and close their
/// run differently on purpose — a live poll writes no row until something moves, a refill counts
/// one request a game, a batch survives one bad fighter — and a template method would either
/// flatten those differences or grow a hook for each. What they share is the wording and the
/// arithmetic, so the reliability layer reads one vocabulary whichever path wrote the row.
/// </para>
/// </summary>
internal static class RunRecord
{
    /// <summary>
    /// The error a run carries when the budget guard refused it before any call. The Runs and Ops
    /// windows show it as written, so every refusing path says it the same way.
    /// </summary>
    public const string BudgetRefused = "Skipped: source is at its configured rate/credit budget.";

    /// <summary>An exception as a run row records it: its type, then its message.</summary>
    public static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>The source row a run is written against, which must already exist.</summary>
    /// <exception cref="InvalidOperationException">No source is registered under <paramref name="key"/>.</exception>
    public static async Task<Source> SourceRowAsync(XLinesDbContext db, string key, CancellationToken ct)
        => await db.Sources.FirstOrDefaultAsync(s => s.Key == key, ct)
           ?? throw new InvalidOperationException($"Source '{key}' is not registered.");

    /// <summary>
    /// Records what one fetch cost on the run: its requests, its credits and the provider's own
    /// count of what is left on the key. Each is set, not added, because every caller charges a run
    /// for the single fetch it made. A null remainder is written as null too — the provider did not
    /// say — which is what the budget guard and MonthlyCredits expect of a source that never does.
    /// </summary>
    public static void Charge(this IngestionRun run, FetchCost cost)
    {
        run.RequestsMade = cost.Requests;
        run.CreditsSpent = cost.Credits;
        run.ProviderCreditsRemaining = cost.CreditsRemaining;
    }
}
