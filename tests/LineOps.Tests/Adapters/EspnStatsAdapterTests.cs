using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// Pinned against recorded ESPN payloads, so the suite stays deterministic, offline and free —
/// the same reason the history backfill is the only thing that talks to ESPN for real.
///
/// The case under test is the one that broke a backfill: ESPN reports a player once per stat
/// group, storage allows one line per player per game, and emitting one row per group put two
/// inserts with the same key into a single transaction.
/// </summary>
public class EspnStatsAdapterTests
{
    private static JsonElement Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static (List<CanonicalPlayer> Players, List<CanonicalPlayerStat> Stats) Parse(
        string fixture, string sportKey)
    {
        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();

        EspnStatsAdapter.ParseBoxScore(Load(fixture), sportKey, "game-1", players, stats);

        return (players, stats);
    }

    private static Dictionary<string, string> LineFor(
        List<CanonicalPlayerStat> stats, string playerId)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            stats.Single(s => s.SourcePlayerId == playerId).StatLineJson)!;

    [Fact]
    public void ParseBoxScore_EmitsOneStatLinePerPlayerPerGame()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");

        // One player appears in two groups, another in one. Two lines, not three.
        Assert.Equal(2, stats.Count);

        var keys = stats.Select(s => (s.SourcePlayerId, s.SourceGameId)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void ParseBoxScore_KeepsKeysPlainWhenGroupsDoNotCollide()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");
        var line = LineFor(stats, "p-200");

        // This player is in one group only, so nothing needs qualifying. Plain keys are what
        // the Players panel turns into column headers.
        Assert.Equal("18", line["CAR"]);
        Assert.Equal("94", line["YDS"]);
        Assert.False(line.ContainsKey("rushing.YDS"));
    }

    [Fact]
    public void ParseBoxScore_QualifiesBothSidesOfACollision()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");
        var line = LineFor(stats, "p-100");

        // YDS means passing yards in one group and rushing yards in the other. Neither may win,
        // and no bare "YDS" may remain — its meaning would depend on iteration order.
        Assert.Equal("289", line["passing.YDS"]);
        Assert.Equal("17", line["rushing.YDS"]);
        Assert.False(line.ContainsKey("YDS"));

        // Keys that never collided stay plain even on a line that has some that did.
        Assert.Equal("22/31", line["C/ATT"]);
        Assert.Equal("4", line["CAR"]);

        // TD disagrees across the two groups (3 passing, 0 rushing), so it qualifies too.
        Assert.Equal("3", line["passing.TD"]);
        Assert.Equal("0", line["rushing.TD"]);
    }

    [Fact]
    public void ParseBoxScore_CollidesCorrectlyWhenTheProviderNamesNoGroups()
    {
        // MLB: ESPN sends no group name at all, which is where the collisions actually are.
        var (_, stats) = Parse("espn.summary.unnamed.json", "mlb");
        var line = LineFor(stats, "p-100");

        // H is hits batting and hits allowed pitching. Both survive under positional labels,
        // because there is no name to use.
        Assert.Equal("2", line["g0.H"]);
        Assert.Equal("5", line["g1.H"]);
        Assert.False(line.ContainsKey("H"));

        // Everything uncontested stays readable.
        Assert.Equal("4", line["AB"]);
        Assert.Equal("6.0", line["IP"]);

        // No group was named, so no category is claimed rather than a row of placeholders.
        Assert.False(line.ContainsKey("category"));
    }

    [Fact]
    public void ParseBoxScore_RecordsNamedGroupsAsCategoryMetadata()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");

        // The Players panel treats "category" as metadata and keeps it out of its columns.
        Assert.Equal("passing,rushing", LineFor(stats, "p-100")["category"]);
        Assert.Equal("rushing", LineFor(stats, "p-200")["category"]);
    }

    [Fact]
    public void ParseBoxScore_StaysAFlatStringMap()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");

        // The Players panel deserialises to Dictionary<string, string>; nesting the groups
        // instead of qualifying keys would throw here rather than in the UI.
        Assert.All(stats, s => Assert.NotNull(
            JsonSerializer.Deserialize<Dictionary<string, string>>(s.StatLineJson)));
    }

    [Fact]
    public void ParseBoxScore_SkipsPlayersWithNoRecordedValues()
    {
        var (_, stats) = Parse("espn.summary.json", "nfl");

        // The kicker appears in the payload with an empty stats array. An empty line is worse
        // than no line: it would occupy the player's one row for the game.
        Assert.DoesNotContain(stats, s => s.SourcePlayerId == "p-300");
    }

    [Fact]
    public void ParseBoxScore_RecordsEachPlayerOnce()
    {
        var (players, _) = Parse("espn.summary.json", "nfl");

        Assert.Equal(3, players.Count);
        Assert.Equal(players.Count, players.Select(p => p.SourcePlayerId).Distinct().Count());
        Assert.Equal("Seattle Seahawks", players.Single(p => p.SourcePlayerId == "p-100").TeamName);
    }
}
