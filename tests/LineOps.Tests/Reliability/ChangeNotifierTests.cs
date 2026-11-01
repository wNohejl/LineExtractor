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
                Market = "other",
                FreeTextMarket = "notifier test",
                Outcome = "—",
                Book = "draftkings",
                PriceTaken = -110,
                Stake = 1m,
                PlacedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var payloads = await DrainAsync(connection, heard);

        Assert.Contains(payloads, p => p.Split(',').Contains(DataTopics.Journal));
    }

    /// <summary>
    /// A sport, two teams and <paramref name="count"/> games, saved without a notifier so the
    /// seeding says nothing on the channel.
    /// </summary>
    private async Task<(Sport Sport, List<Game> Games)> SeedGamesAsync(int count)
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var sport = new Sport { Key = $"cn-{suffix}", Name = "TEST" };
        db.Sports.Add(sport);
        await db.SaveChangesAsync();

        var home = new Team { SportId = sport.Id, Name = $"Home {suffix}", Abbrev = "HOM" };
        var away = new Team { SportId = sport.Id, Name = $"Away {suffix}", Abbrev = "AWY" };
        db.Teams.AddRange(home, away);
        await db.SaveChangesAsync();

        var games = Enumerable.Range(0, count).Select(i => new Game
        {
            SportId = sport.Id,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            StartsAt = DateTimeOffset.UtcNow.AddDays(i),
            Status = GameStatus.Scheduled
        }).ToList();
        db.Games.AddRange(games);
        await db.SaveChangesAsync();

        return (sport, games);
    }

    private async Task<string> ScoreAsync(IEnumerable<Game> games)
    {
        var (connection, heard) = await ListenAsync();
        await using var _ = connection;

        await using (var db = NotifyingContext())
        {
            foreach (var game in games)
            {
                db.Games.Attach(game);
                game.HomeScore = (game.HomeScore ?? 0) + 1;
            }

            await db.SaveChangesAsync();
        }

        return Assert.Single(await DrainAsync(connection, heard));
    }

    [Fact]
    public async Task A_save_touching_two_games_names_each_of_them()
    {
        var (_, games) = await SeedGamesAsync(2);
        var ids = games.Select(g => g.Id).Order().ToList();

        var payload = await ScoreAsync(games);

        Assert.Equal($"games:{ids[0]},games:{ids[1]}", payload);
    }

    [Fact]
    public async Task A_save_touching_more_than_fifty_games_falls_back_to_the_bare_topic()
    {
        var (_, games) = await SeedGamesAsync(51);

        var payload = await ScoreAsync(games);

        Assert.Equal(DataTopics.Games, payload);
    }

    [Fact]
    public async Task A_new_game_is_addressed_by_the_id_the_save_gave_it()
    {
        var (_, games) = await SeedGamesAsync(1);
        var (connection, heard) = await ListenAsync();
        await using var _ = connection;

        Game added;
        await using (var db = NotifyingContext())
        {
            var seed = games[0];
            added = new Game
            {
                SportId = seed.SportId,
                HomeTeamId = seed.AwayTeamId,
                AwayTeamId = seed.HomeTeamId,
                StartsAt = seed.StartsAt.AddHours(3)
            };
            db.Games.Add(added);
            await db.SaveChangesAsync();
        }

        Assert.Equal($"games:{added.Id}", Assert.Single(await DrainAsync(connection, heard)));
    }

    [Fact]
    public async Task A_price_is_addressed_to_its_game_under_odds()
    {
        var (_, games) = await SeedGamesAsync(1);

        int sourceId;
        await using (var seed = fixture.CreateContext())
        {
            var source = new Source
            {
                Key = $"cn-src-{Guid.NewGuid():N}"[..20],
                Name = "Test odds",
                Kind = SourceKind.Odds,
                BaseUrl = "local://test"
            };
            seed.Sources.Add(source);
            await seed.SaveChangesAsync();
            sourceId = source.Id;
        }

        var (connection, heard) = await ListenAsync();
        await using var _ = connection;

        await using (var db = NotifyingContext())
        {
            db.OddsSnapshots.Add(new OddsSnapshot
            {
                GameId = games[0].Id,
                SourceId = sourceId,
                Book = "draftkings",
                Market = Markets.Moneyline,
                Outcome = "home",
                PriceAmerican = -110,
                CapturedAt = DateTimeOffset.UtcNow,
                IngestionRunId = 0
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal($"odds:{games[0].Id}", Assert.Single(await DrainAsync(connection, heard)));
    }

    [Fact]
    public async Task A_row_with_no_game_gives_its_bare_topic()
    {
        var (sport, _) = await SeedGamesAsync(1);
        var (connection, heard) = await ListenAsync();
        await using var _ = connection;

        await using (var db = NotifyingContext())
        {
            db.Players.Add(new Player { SportId = sport.Id, FullName = "Notifier Test" });
            await db.SaveChangesAsync();
        }

        Assert.Equal(DataTopics.Games, Assert.Single(await DrainAsync(connection, heard)));
    }

    [Fact]
    public void Every_topic_is_bare_so_the_fallback_reaches_every_window()
        => Assert.All(DataTopics.All, t => Assert.DoesNotContain(':', t));

    [Theory]
    [InlineData(DataTopics.Games, "4123", "games:4123")]
    [InlineData(DataTopics.Odds, "7", "odds:7")]
    public void An_address_is_the_topic_and_the_id(string topic, string id, string token)
        => Assert.Equal(token, DataTopics.Address(topic, id));

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
