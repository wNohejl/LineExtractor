using System.Data;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LineOps.Data.Changes;

/// <summary>
/// What changed, named coarsely enough that a window can decide whether it cares.
/// </summary>
public static class DataTopics
{
    /// <summary>The Postgres channel every change is announced on.</summary>
    public const string Channel = "lineops_changes";

    public const string Games = "games";
    public const string Odds = "odds";
    public const string Journal = "journal";
    public const string Alerts = "alerts";
    public const string Runs = "runs";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Games, Odds, Journal, Alerts, Runs };

    /// <summary>The topic an entity's rows belong to, or null for reference data nobody watches.</summary>
    public static string? For(Type entity) => entity.Name switch
    {
        nameof(Game) or nameof(PlayerGameStat) or nameof(StatSnapshot) or nameof(Player) => Games,
        nameof(OddsSnapshot) or nameof(ClosingLine) => Odds,
        nameof(JournalEntry) => Journal,
        nameof(Alert) or nameof(Incident) => Alerts,
        nameof(IngestionRun) or nameof(BackfillCheckpoint) => Runs,
        _ => null
    };
}

/// <summary>
/// Announces every save on <see cref="DataTopics.Channel"/>, naming the topics it touched.
///
/// <para>
/// The desk used to learn about new data only when someone pressed a button: no window
/// refreshed itself, so a result landing in the worker reached an open Board whenever the
/// operator next clicked. One interceptor on the context covers every writer at once — the
/// worker's ingestion, the web host's own schedule, the wager form, settlement — without any of
/// them having to remember to say so, and <c>NOTIFY</c> crosses the process boundary that an
/// in-memory event cannot.
/// </para>
///
/// <para>
/// Sent after the save, on the context's own connection, so it can only describe committed rows
/// (there are no explicit transactions; retry-on-failure rules them out). Best-effort by design:
/// a notice that fails to send costs a refresh, which the listener's fallback poll makes up, and
/// must never fail the save it describes. Bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> calls do
/// not pass through here; the few there are touch rows no window is waiting on.
/// </para>
/// </summary>
public sealed class ChangeNotifier(ILogger<ChangeNotifier> logger) : SaveChangesInterceptor
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        await AnnounceAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AnnounceAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
            return;

        var topics = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => DataTopics.For(e.Metadata.ClrType))
            .OfType<string>()
            .ToHashSet();

        if (topics.Count > 0)
            _pending.AddOrUpdate(context, topics);
    }

    private async Task AnnounceAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null || !_pending.TryGetValue(context, out var topics))
            return;

        _pending.Remove(context);

        // The raw connection rather than Database.ExecuteSql: this runs inside SaveChanges, and
        // a second EF operation on the same context here would trip its concurrency guard.
        var connection = context.Database.GetDbConnection();
        var opened = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
                opened = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pg_notify('{DataTopics.Channel}', @topics)";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "topics";
            parameter.Value = string.Join(',', topics.Order());
            command.Parameters.Add(parameter);

            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Change notice for {Topics} not sent", string.Join(',', topics));
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
