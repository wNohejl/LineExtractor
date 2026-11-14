using System.Globalization;
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
///
/// <para>
/// A date range (<see cref="RangePull"/>, the Worker's <c>--job espn:range</c>) is not in the
/// menu: it asks for a league and days before it runs, so the Board gives it a key of its own and
/// a popover (RangePullKey). What it is called, the run it makes and the phrase its busy key
/// shows are here, beside the menu's, so both report a pull alike.
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

    /// <summary>
    /// The date range as the runner reports it: its label is the toast's subject ("Pull a date
    /// range did not finish"). Never in the menu; the Board's Date range key runs it.
    /// </summary>
    public static DeskJob RangeJob { get; } = new(
        IngestionJobs.EspnRange,
        "Pull a date range",
        "One league's games over the days you name, with each final's box score and closing line.",
        "1 req a game day + 1 a final",
        Available: true,
        Unavailable: null);

    /// <summary>
    /// The run a range pull is: the pull itself on the host, its progress told as the busy key's
    /// phrase, and its summary as the sentence the desk toasts.
    /// </summary>
    public static DeskJobRun RangeRun(
        Func<RangePullRequest, IProgress<RangePullProgress>, CancellationToken, Task<RangePullSummary>> pull,
        RangePullRequest request)
        => async (progress, ct) => RangeOutcome(await pull(request, new Phrases(progress), ct));

    /// <summary>A day's progress as the phrase the busy key shows: "NFL Sep 8 · 3 of 4".</summary>
    public static string Phrase(RangePullProgress p)
        => $"{p.Sport.ToUpperInvariant()} {p.Day.ToString("MMM d", CultureInfo.InvariantCulture)} · {p.Done + 1} of {p.Of}";

    private sealed class Phrases(IProgress<string> key) : IProgress<RangePullProgress>
    {
        public void Report(RangePullProgress value) => key.Report(Phrase(value));
    }

    /// <summary>One pull as a menu entry, in its menu section.</summary>
    public static DeskJob Describe(IngestionJob job)
        => new(job.Key, job.Label, job.Description, Cost(job), job.Available, job.Unavailable, Group: job.Group);

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
    /// simply not have covered a fixture — but it is not "done" either. It still says what the
    /// job did when the job says it: "0 of 3 games asked got a close" is the news a press of
    /// "Closing lines owed" exists to give, and "nothing new to store" hid it.
    /// </summary>
    public static DeskJobOutcome Outcome(string label, JobOutcome outcome)
    {
        if (!outcome.Succeeded)
            return new(DeskJobResult.Failed, outcome.Detail ?? $"{label}: {outcome.Failures} source failed.");

        if (outcome.Rows == 0)
            return new(DeskJobResult.Nothing, outcome.Detail ?? $"{label}: nothing new to store.");

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
