using LineOps.Data;
using LineOps.Data.Changes;
using LineOps.Desk.Windowing;
using Npgsql;

namespace LineOps.Web.Services;

/// <summary>
/// Holds one connection that <c>LISTEN</c>s on <see cref="DataTopics.Channel"/> and passes what
/// it hears to <see cref="DeskSignals"/>.
///
/// <para>
/// One connection for the whole host, outside the EF pool, rather than one per circuit: a
/// notice is about the data, so every open desk hears the same one. Reconnects on its own. While
/// it is down — the database restarting, a network blip — it publishes every topic once a
/// minute instead, so windows degrade to a slow refresh rather than going quiet; and on
/// reconnecting it publishes every topic once, because whatever changed in the gap was missed.
/// </para>
/// </summary>
public sealed class DataChangeListener(
    IConfiguration configuration,
    DeskSignals signals,
    ILogger<DataChangeListener> logger) : BackgroundService
{
    /// <summary>How often the fallback refreshes while the listener is down.</summary>
    public static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        // Keepalive so a half-open connection is noticed in seconds rather than never; the
        // listener otherwise sits in WaitAsync for as long as nothing is said.
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { KeepAlive = 30, Pooling = false };

        using var fallback = new PeriodicTimer(FallbackInterval);
        _ = PollWhileDownAsync(fallback, stoppingToken);

        var everConnected = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync(stoppingToken);

                connection.Notification += (_, e) =>
                    signals.Publish(e.Payload.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet());

                await using (var listen = new NpgsqlCommand($"LISTEN {DataTopics.Channel}", connection))
                    await listen.ExecuteNonQueryAsync(stoppingToken);

                signals.SetLive(true);
                logger.LogInformation("Listening for data changes on {Channel}", DataTopics.Channel);

                if (everConnected)
                    signals.Publish(DataTopics.All);

                everConnected = true;

                while (!stoppingToken.IsCancellationRequested)
                    await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (signals.Live)
                    logger.LogWarning(ex, "Data change listener dropped; windows refresh every {Interval}s until it is back",
                        FallbackInterval.TotalSeconds);

                signals.SetLive(false);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        signals.SetLive(false);
    }

    private async Task PollWhileDownAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!signals.Live)
                    signals.Publish(DataTopics.All);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
