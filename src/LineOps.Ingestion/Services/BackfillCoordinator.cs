using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion.Services;

/// <summary>
/// Owns the one backfill that may be running, and outlives whoever started it.
///
/// <para>
/// A backfill takes minutes to hours. A Blazor panel takes as long as the operator leaves the
/// tab open, and its circuit dies on a refresh, a network blip or a redeploy. Those two
/// lifetimes cannot be the same object, so the walk lives here as a singleton and the panel
/// only starts it, watches it, and asks it to stop. Close the window mid-backfill and the
/// work continues; reopen it and the panel reattaches to the run in progress.
/// </para>
///
/// <para>
/// Single-flight by construction: <see cref="TryStart"/> refuses while a walk is active. Two
/// concurrent backfills would double the request rate against a provider being used as a
/// courtesy, which is the one thing this whole feature is arranged to avoid.
/// </para>
/// </summary>
public class BackfillCoordinator(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<BackfillCoordinator> logger) : BackgroundService
{
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _run;

    /// <summary>Raised on every completed day, and once more when the walk ends.</summary>
    public event Action? Changed;

    public bool IsRunning { get; private set; }
    public BackfillProgress? Progress { get; private set; }
    public BackfillReport? LastReport { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>
    /// Starts a walk unless one is already running.
    /// </summary>
    /// <returns>false when a backfill is already in flight.</returns>
    public bool TryStart(CancellationToken hostToken = default)
    {
        lock (_gate)
        {
            if (IsRunning)
                return false;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
            IsRunning = true;
            StartedAt = DateTimeOffset.UtcNow;
            FinishedAt = null;
            Progress = null;
            LastReport = null;
            _run = Task.Run(() => WalkAsync(_cts.Token));
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Asks the walk to stop. It finishes the day it is on, then returns.</summary>
    public void Stop()
    {
        lock (_gate)
            _cts?.Cancel();
    }

    private async Task WalkAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<HistoryBackfillService>();

            var progress = new Progress<BackfillProgress>(p =>
            {
                Progress = p;
                Changed?.Invoke();
            });

            LastReport = await service.RunAsync(progress, ct);

            logger.LogInformation(
                "Backfill finished: {Walked} days walked, {Skipped} already held, {Failed} failed, " +
                "{Games} games, {Rows} rows{Reason}",
                LastReport.Walked, LastReport.Skipped, LastReport.Failed,
                LastReport.GamesFound, LastReport.RowsIngested,
                LastReport.StoppedBecause is null ? "" : $" — {LastReport.StoppedBecause}");
        }
        catch (OperationCanceledException)
        {
            LastReport = new BackfillReport(
                Progress?.Walked ?? 0, Progress?.Skipped ?? 0, Progress?.Failed ?? 0,
                Progress?.GamesFound ?? 0, Progress?.RowsIngested ?? 0, "Stopped by the operator.");
            logger.LogInformation("Backfill stopped by the operator");
        }
        catch (Exception ex)
        {
            LastReport = new BackfillReport(
                Progress?.Walked ?? 0, Progress?.Skipped ?? 0, Progress?.Failed ?? 0,
                Progress?.GamesFound ?? 0, Progress?.RowsIngested ?? 0,
                $"{ex.GetType().Name}: {ex.Message}");
            logger.LogError(ex, "Backfill failed");
        }
        finally
        {
            lock (_gate)
            {
                IsRunning = false;
                FinishedAt = DateTimeOffset.UtcNow;
                _cts?.Dispose();
                _cts = null;
            }

            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Auto-start, when configured. Deliberately delayed: the host has just migrated, seeded
    /// and possibly run a startup ingest, and a backfill is the least urgent thing competing
    /// for that first minute.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Backfill.Enabled)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logger.LogInformation("Backfill auto-start: reaching back {Days} days",
            options.Value.Backfill.Days);

        TryStart(stoppingToken);

        // Keep the host's shutdown linked to the walk rather than returning immediately,
        // so a stop request cancels the day in flight instead of abandoning it mid-write.
        while (IsRunning && !stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
    }
}
