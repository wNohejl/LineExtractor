using LineOps.Desk;
using LineOps.Ingestion.Services;

namespace LineOps.Web.Services;

/// <summary>
/// LineOps's named pulls, as the desk's job menu offers them.
///
/// <para>
/// The desk draws the menu, the busy key and the toast; this says what each pull is, what it
/// costs in the unit that runs out, and the sentence its outcome is reported in. Every entry is
/// the job the scheduler runs (<see cref="IngestionJobs"/>) — nothing here is a second
/// implementation. Scoped, so the labels it remembers are one circuit's.
/// </para>
/// </summary>
public sealed class IngestionDeskJobs(IngestionJobs jobs) : IDeskJobs
{
    private readonly Dictionary<string, string> _labels = [];

    public async Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
    {
        var described = await jobs.DescribeAsync(ct);

        foreach (var job in described)
            _labels[job.Key] = job.Label;

        return described.Select(Describe).ToList();
    }

    public async Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
    {
        // The sentence names the pull, and a run can be asked for by key before the menu was
        // ever opened, so an unknown key is looked up rather than reported by its key.
        if (!_labels.ContainsKey(key))
            await DescribeAsync(ct);

        var label = _labels.GetValueOrDefault(key, key);

        return Outcome(label, await jobs.RunAsync(key, ct));
    }

    /// <summary>One pull as a menu entry.</summary>
    public static DeskJob Describe(IngestionJob job)
        => new(job.Key, job.Label, job.Description, Cost(job), job.Available, job.Unavailable);

    /// <summary>
    /// Credits where the provider bills in them, because that is the number that runs out — a
    /// call costing one request can cost several credits.
    /// </summary>
    public static string Cost(IngestionJob job)
        => job.EstimatedCredits > 0
            ? $"{job.EstimatedCredits} credits"
            : job.EstimatedRequests == 0 ? "no requests" : $"~{job.EstimatedRequests} req";

    /// <summary>
    /// A pull's outcome as the sentence the desk toasts. Zero rows is not a failure — the scan may
    /// simply not have covered a fixture — but it is not "done" either.
    /// </summary>
    public static DeskJobOutcome Outcome(string label, JobOutcome outcome)
    {
        if (!outcome.Succeeded)
            return new(DeskJobResult.Failed, outcome.Detail ?? $"{label}: {outcome.Failures} source failed.");

        if (outcome.Rows == 0)
            return new(DeskJobResult.Nothing, $"{label}: nothing new to store.");

        return new(DeskJobResult.Done, outcome.Detail ?? $"{label}: {outcome.Rows:N0} rows.");
    }
}
