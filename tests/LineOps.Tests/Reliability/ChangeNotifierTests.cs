using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Data.Changes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LineOps.Tests.Reliability;

/// <summary>
/// A save is announced on the change channel, naming the topics it touched, so a window in
/// another process hears about it. Against real Postgres, because LISTEN/NOTIFY is the point.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ChangeNotifierTests(PostgresFixture fixture)
{
    private LineOpsDbContext NotifyingContext()
        => new(new DbContextOptionsBuilder<LineOpsDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new ChangeNotifier(NullLogger<ChangeNotifier>.Instance))
            .Options);

    private async Task<(NpgsqlConnection Connection, List<string> Heard)> ListenAsync()
    {
        var heard = new List<string>();
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        connection.Notification += (_, e) => { lock (heard) heard.Add(e.Payload); };

        await using var listen = new NpgsqlCommand($"LISTEN {DataTopics.Channel}", connection);
        await listen.ExecuteNonQueryAsync();

        return (connection, heard);
    }

    private static async Task<List<string>> DrainAsync(NpgsqlConnection connection, List<string> heard)
    {
        // Notices arrive asynchronously; give the server a moment, then read what came.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            while (true)
                await connection.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lock (heard)
            return [.. heard];
    }

    [Fact]
    public async Task A_journal_entry_saved_is_announced_as_journal()
    {
        var (connection, heard) = await ListenAsync();
        await using var _ = connection;

        await using (var db = NotifyingContext())
        {
            db.JournalEntries.Add(new JournalEntry
            {
                Market = "other", FreeTextMarket = "notifier test", Outcome = "—", Book = "draftkings",
                PriceTaken = -110, Stake = 1m, PlacedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var payloads = await DrainAsync(connection, heard);

        Assert.Contains(payloads, p => p.Split(',').Contains(DataTopics.Journal));
    }

    [Theory]
    [InlineData(typeof(Game), DataTopics.Games)]
    [InlineData(typeof(PlayerGameStat), DataTopics.Games)]
    [InlineData(typeof(OddsSnapshot), DataTopics.Odds)]
    [InlineData(typeof(ClosingLine), DataTopics.Odds)]
    [InlineData(typeof(JournalEntry), DataTopics.Journal)]
    [InlineData(typeof(Alert), DataTopics.Alerts)]
    [InlineData(typeof(IngestionRun), DataTopics.Runs)]
    [InlineData(typeof(Sport), null)]
    public void Each_entity_belongs_to_the_topic_a_window_would_watch(Type entity, string? topic)
        => Assert.Equal(topic, DataTopics.For(entity));
}
