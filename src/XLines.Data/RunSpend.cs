using Microsoft.EntityFrameworkCore;

namespace XLines.Data;

/// <summary>
/// What a source's runs have recorded spending since an instant, read from <c>ingestion_run</c>.
/// One query for the reliability layer's usage and the planner's daily pacing, which used to each
/// write it out and could drift in what they counted.
/// </summary>
public static class RunSpend
{
    /// <summary>HTTP requests made by one source's runs started at or after <paramref name="since"/>.</summary>
    public static async Task<int> RequestsSinceAsync(
        XLinesDbContext db, int sourceId, DateTimeOffset since, CancellationToken ct = default)
        => await db.IngestionRuns
            .Where(r => r.SourceId == sourceId && r.StartedAt >= since)
            .SumAsync(r => (int?)r.RequestsMade, ct) ?? 0;
}
