using LineOps.Core.Entities;
using LineOps.Data;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Reliability;

/// <summary>
/// Closes runs whose host stopped under them.
///
/// <para>
/// A run is opened as Running and finished by the code that started it; kill the process in
/// between and nothing finishes it. Seven such rows sat at Running from July 2026 onward —
/// excluded from every success rate, explained by nothing. The reliability evaluator calls this
/// at host start and on every interval after, so it is also the sweep that tidies up after a
/// crash.
/// </para>
/// </summary>
public static class OrphanRuns
{
    public const string Reason = "The host stopped before the run finished.";

    /// <summary>Marks every run still Running after <paramref name="after"/> as Failed. Returns how many.</summary>
    public static Task<int> ReapAsync(LineOpsDbContext db, TimeSpan after, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - after;

        return db.IngestionRuns
            .Where(r => r.Status == RunStatus.Running && r.StartedAt < cutoff)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, RunStatus.Failed)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.Error, Reason), ct);
    }
}
