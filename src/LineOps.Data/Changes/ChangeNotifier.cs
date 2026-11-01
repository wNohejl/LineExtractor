using System.Data;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LineOps.Data.Changes;

/// <summary>
/// What changed, named coarsely enough that a window can decide whether it cares.
///
/// <para>
/// A notice is a comma list of tokens, each a bare topic (<c>games</c>) or a topic addressed to
/// the game the row belongs to (<c>games:4123</c>, <c>odds:4123</c>). The address is what lets
/// the Game window reload for its own game and sit still while thirteen others score on a
/// Sunday; a window that watches the topic without naming a game hears both forms, so nothing
/// that watched <c>games</c> before has to change. The bare topic means "something here moved,
/// I cannot say which" — a row with no game, a save too large to list — and reaches everyone.
/// </para>
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

    /// <summary>
    /// The most ids one topic lists in one notice; past it the notice names the bare topic.
    /// <c>pg_notify</c> refuses a payload over 8000 bytes, and a backfill touches thousands of
    /// games in a save — nobody has a window open on each of them.
    /// </summary>
    public const int MaxAddressed = 50;

    /// <summary>
    /// Every topic, bare. What the listener publishes when it cannot know what changed — the
    /// fallback poll, a reconnect — and, being bare, it reaches every window including those
    /// that watch only their own game.
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Games, Odds, Journal, Alerts, Runs };

    /// <summary>
    /// The token for a change to one game's rows under a topic: <c>games:4123</c>. The same shape
    /// as the Desk's <c>DeskSignals.Address</c>, which is what reads it.
    /// </summary>
    public static string Address(string topic, string id) => $"{topic}:{id}";

    /// <summary>The topic an entity's rows belong to, or null for reference data nobody watches.</summary>
    public static string? For(Type entity) => entity.Name switch
    {
        nameof(Game) or nameof(PlayerGameStat) or nameof(StatSnapshot) or nameof(Player) => Games,
        nameof(OddsSnapshot) or nameof(ClosingLine) => Odds,
        nameof(JournalEntry) or nameof(Parlay) or nameof(AppSetting) => Journal,
        nameof(Alert) or nameof(Incident) => Alerts,
        nameof(IngestionRun) or nameof(BackfillCheckpoint) => Runs,
        _ => null
    };

    /// <summary>
    /// The game a row belongs to, for the rows a notice can address; null for any other row, and
    /// for one whose game is not known yet. Read after the save, so a game added in it carries the
    /// id the database gave it.
    /// </summary>
    public static int? GameOf(object row) => row switch
    {
        Game g => g.Id,
        PlayerGameStat s => s.GameId,
        StatSnapshot s => s.GameId,
        OddsSnapshot o => o.GameId,
        ClosingLine c => c.GameId,
        _ => 0
    } is var id and > 0 ? id : null;
}

/// <summary>
/// Announces every save on <see cref="DataTopics.Channel"/>, naming the topics it touched and,
/// where it can, the games under them (<see cref="Payload"/>).
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
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<(string Topic, object Row)>> _pending = new();

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

        // The rows themselves, not their ids: a game added in this save has no id until the
        // insert, and its stats' GameId is fixed up from it then. The address is read after.
        var rows = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (Topic: DataTopics.For(e.Metadata.ClrType), Row: e.Entity))
            .Where(r => r.Topic is not null)
            .Select(r => (r.Topic!, r.Row))
            .ToList();

        if (rows.Count > 0)
            _pending.AddOrUpdate(context, rows);
    }

    /// <summary>
    /// The notice for a save's rows: <c>games:1,games:2,odds:2</c>. A topic falls back to its bare
    /// name when any of its rows has no game to address — the bare topic already reaches every
    /// window the addresses would — or when it would list more than
    /// <see cref="DataTopics.MaxAddressed"/> games.
    /// </summary>
    internal static string Payload(IEnumerable<(string Topic, object Row)> rows)
    {
        var tokens = new List<string>();

        foreach (var topic in rows.GroupBy(r => r.Topic).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var games = topic.Select(r => DataTopics.GameOf(r.Row)).ToList();
            var ids = games.OfType<int>().Distinct().Order().ToList();

            if (games.Any(g => g is null) || ids.Count > DataTopics.MaxAddressed)
                tokens.Add(topic.Key);
            else
                tokens.AddRange(ids.Select(id => DataTopics.Address(topic.Key, id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        return string.Join(',', tokens);
    }

    private async Task AnnounceAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null || !_pending.TryGetValue(context, out var rows))
            return;

        _pending.Remove(context);
        var topics = Payload(rows);

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
            parameter.Value = topics;
            command.Parameters.Add(parameter);

            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Change notice for {Topics} not sent", topics);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
