using LineOps.Desk;
using LineOps.Desk.Primitives;
using LineOps.Ingestion.Services;
using LineOps.Web.Components.Desk;
using MudBlazor;

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
///
/// <para>
/// One entry asks before it runs: a date range (<see cref="RangePull"/>), whose league and days
/// a sheet asks for. It is the Worker's <c>--job espn:range</c>, and it sits under the catalogue
/// because it is not one of the scheduler's jobs — nothing runs it on a timer.
/// </para>
/// </summary>
public sealed class IngestionDeskJobs(IngestionJobs jobs, RangePull range) : IDeskJobs
{
    private readonly Dictionary<string, string> _labels = [];

    public async Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
    {
        var described = await jobs.DescribeAsync(ct);

        foreach (var job in described)
            _labels[job.Key] = job.Label;

        var entries = described.Select(Describe).ToList();

        // A stats source is what walks it; without one the entry stays, dimmed, with the reason.
        var stats = described.FirstOrDefault(j => j.Key == IngestionJobs.EspnResults);
        entries.Add(Range(stats?.Available ?? false, stats?.Unavailable));

        return entries;
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

    /// <summary>The date range's entry: its sheet asks for the league and the days, then the menu runs it.</summary>
    private DeskJob Range(bool available, string? unavailable)
        => new(IngestionJobs.EspnRange,
            "Pull a date range…",
            "One league's games over the days you name, with each final's box score and closing line — "
            + "for history the backfill seasons do not reach, or a range that came in incomplete.",
            "1 req a game day + 1 a final",
            available,
            unavailable,
            Sheet: async sheets => await sheets.ShowAsync(new DialogParameters<RangePullSheet>
            {
                { x => x.Leagues, await range.LeaguesAsync() },
                { x => x.Pull, range.RunAsync }
            }));

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

    /// <summary>
    /// A range pull's summary as the sentence the desk toasts: a refusal in its own words, a failed
    /// day as a warning, and a range with no game held or found as nothing new.
    /// </summary>
    public static DeskJobOutcome RangeOutcome(RangePullSummary summary)
        => summary switch
        {
            { Refused: { } refused } => new(DeskJobResult.Failed, refused),
            { Failures: > 0 } => new(DeskJobResult.Failed, summary.Sentence),
            { Games: 0, DaysSkipped: 0 } => new(DeskJobResult.Nothing, summary.Sentence),
            _ => new(DeskJobResult.Done, summary.Sentence)
        };
}
