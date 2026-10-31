using LineOps.Desk.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LineOps.Desk;

/// <summary>
/// One named run an application offers from its job menu, described before it is chosen.
/// </summary>
/// <param name="Key">What the application runs it by; never shown.</param>
/// <param name="Label">A verb first, no article: "Refresh the index".</param>
/// <param name="Description">A second line saying what the run does.</param>
/// <param name="Cost">
/// What pressing it will spend, in the unit that runs out — "4 credits", "~12 requests", "free".
/// Null when the run costs nothing worth stating.
/// </param>
/// <param name="Available">False when the run cannot start here: it stays in the menu, dimmed.</param>
/// <param name="Unavailable">Why it cannot start, written where its description would be.</param>
public sealed record DeskJob(string Key, string Label, string Description, string? Cost, bool Available, string? Unavailable);

/// <summary>How a run went, which decides how the desk reports it.</summary>
public enum DeskJobResult
{
    /// <summary>It ran and something arrived or changed.</summary>
    Done,

    /// <summary>It ran and there was nothing to do — not a failure, but not news either.</summary>
    Nothing,

    /// <summary>It ran and part of it did not succeed.</summary>
    Failed
}

/// <summary>A run's result and the one sentence that reports it.</summary>
public sealed record DeskJobOutcome(DeskJobResult Result, string Summary);

/// <summary>
/// The named runs an application lets the operator start by hand.
///
/// <para>
/// The desk draws the menu, the busy key and the toast; the application says what the runs are,
/// what each costs, and how one went. Register it scoped, beside the desk, and a
/// <see cref="Primitives.DeskJobMenu"/> with no <c>Jobs</c> of its own picks it up.
/// </para>
/// </summary>
public interface IDeskJobs
{
    /// <summary>Every run, available or not. Read again whenever the menu opens: costs move.</summary>
    Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default);

    /// <summary>Runs one by its key, and says how it went.</summary>
    Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Runs an application's jobs the one way the desk reports them: refused for a visitor, a toast
/// in the result's own state, and an exception logged and told as "did not finish".
///
/// <para>
/// The job menu runs through this, and so can any control that offers a run where its absence
/// is stated — an empty state's "Read the profiles" key — so a run pressed there and one chosen
/// from the menu toast alike. Scoped, like the toasts it raises.
/// </para>
/// </summary>
public sealed class DeskJobRunner(IServiceProvider services, DeskToasts toasts, IDeskAccess access, ILogger<DeskJobRunner> log)
{
    /// <summary>The application's registered jobs, or null when it registers none.</summary>
    public IDeskJobs? Jobs => services.GetService<IDeskJobs>();

    /// <summary>
    /// Runs one job and toasts its outcome. False when nothing ran to completion: a visitor's
    /// press, or a run that threw.
    /// </summary>
    public async Task<bool> RunAsync(IDeskJobs jobs, DeskJob job)
    {
        // A visitor spends nothing, whichever control asked.
        if (!access.CanOperate)
            return false;

        try
        {
            var outcome = await jobs.RunAsync(job.Key);

            // Succeeding with nothing is worth saying differently: reported as a success it
            // reads as "done" when nothing arrived.
            switch (outcome.Result)
            {
                case DeskJobResult.Done:
                    toasts.Success(outcome.Summary);
                    break;
                case DeskJobResult.Nothing:
                    toasts.Info(outcome.Summary);
                    break;
                default:
                    toasts.Warn(outcome.Summary);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            // The exception is for the log; the toast says what did not happen and what to do.
            log.LogError(ex, "{Job} failed", job.Label);
            toasts.Fail($"{job.Label} did not finish. Try again.");
            return false;
        }
    }

    /// <summary>
    /// Runs the registered job with this key, read afresh. False when there are no jobs, the key
    /// is unknown or the job is unavailable — nothing ran. An unavailable job says why in a
    /// toast, since the key that asked for it has no menu to write the reason in.
    /// </summary>
    public async Task<bool> RunAsync(string key)
    {
        if (!access.CanOperate || Jobs is not { } jobs)
            return false;

        var job = (await jobs.DescribeAsync()).FirstOrDefault(j => j.Key == key);

        if (job is null)
            return false;

        if (!job.Available)
        {
            toasts.Warn(job.Unavailable ?? $"{job.Label} cannot run here.");
            return false;
        }

        return await RunAsync(jobs, job);
    }
}
