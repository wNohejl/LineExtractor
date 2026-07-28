using System.Text.Json;
using LineOps.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace LineOps.Ingestion.Adapters;

/// <summary>
/// Offline counterpart to <see cref="DemoOddsSource"/>: deterministic rosters and box scores
/// so player pages, game logs and settlement all work on a cold clone with no API keys.
/// Generated names are obviously synthetic, so demo data can never be mistaken for real.
/// </summary>
public class DemoStatsSource(ILogger<DemoStatsSource> logger) : IStatsSource, IFailureInjectable
{
    public const string SourceKey = "demo-stats";

    public string Key => SourceKey;

    public string? FailureMode { get; set; }

    private static readonly string[] FirstNames =
        ["Avery", "Blake", "Casey", "Drew", "Emerson", "Finley", "Gray", "Harper",
         "Indigo", "Jordan", "Kai", "Logan", "Marlow", "Nico", "Oakley", "Parker"];

    private static readonly string[] LastNames =
        ["Ash", "Brooks", "Calder", "Dane", "Ellis", "Frost", "Glass", "Hale",
         "Ives", "Jansen", "Kerr", "Lowe", "Mercer", "North", "Orr", "Pike"];

    private static readonly Dictionary<string, string[]> Positions = new()
    {
        ["nfl"] = ["QB", "RB", "WR", "TE"],
        ["nba"] = ["PG", "SG", "SF", "PF", "C"],
        ["mlb"] = ["P", "C", "1B", "SS", "OF"],
        ["nhl"] = ["C", "LW", "RW", "D", "G"]
    };

    public Task<StatsFetchResult> FetchScheduleAsync(string sportKey, DateOnly date, CancellationToken ct)
    {
        ThrowIfInjected();
        return Task.FromResult(new StatsFetchResult([], [], [], new FetchCost(1)));
    }

    public Task<StatsFetchResult> FetchRosterAsync(string sportKey, CancellationToken ct)
    {
        ThrowIfInjected();

        var players = BuildRoster(sportKey).ToList();
        logger.LogInformation("{Source}: {Count} players for {Sport}", SourceKey, players.Count, sportKey);

        return Task.FromResult(new StatsFetchResult([], players, [], new FetchCost(1)));
    }

    public Task<StatsFetchResult> FetchBoxScoresAsync(string sportKey, DateOnly date, CancellationToken ct)
    {
        ThrowIfInjected();

        var players = BuildRoster(sportKey).ToList();
        var stats = new List<CanonicalPlayerStat>();

        // Deterministic per player and date, so repeated runs are idempotent.
        foreach (var player in players)
        {
            var rng = new Random(HashCode.Combine(player.SourcePlayerId, date.DayNumber));
            var line = sportKey switch
            {
                "nfl" => new Dictionary<string, string>
                {
                    ["category"] = "offense",
                    ["YDS"] = rng.Next(0, 320).ToString(),
                    ["TD"] = rng.Next(0, 4).ToString(),
                    ["REC"] = rng.Next(0, 12).ToString()
                },
                "nba" => new Dictionary<string, string>
                {
                    ["category"] = "starters",
                    ["PTS"] = rng.Next(0, 41).ToString(),
                    ["REB"] = rng.Next(0, 16).ToString(),
                    ["AST"] = rng.Next(0, 13).ToString()
                },
                "mlb" => new Dictionary<string, string>
                {
                    ["category"] = "batting",
                    ["H"] = rng.Next(0, 5).ToString(),
                    ["RBI"] = rng.Next(0, 5).ToString(),
                    ["HR"] = rng.Next(0, 3).ToString()
                },
                _ => new Dictionary<string, string>
                {
                    ["category"] = "skaters",
                    ["G"] = rng.Next(0, 3).ToString(),
                    ["A"] = rng.Next(0, 4).ToString(),
                    ["SOG"] = rng.Next(0, 9).ToString()
                }
            };

            stats.Add(new CanonicalPlayerStat(
                player.SourcePlayerId,
                $"demo-{sportKey}-0-{date:yyyyMMdd}",
                JsonSerializer.Serialize(line)));
        }

        return Task.FromResult(new StatsFetchResult([], players, stats, new FetchCost(1)));
    }

    private static IEnumerable<CanonicalPlayer> BuildRoster(string sportKey)
    {
        if (!Positions.TryGetValue(sportKey, out var positions))
            yield break;

        for (var i = 0; i < 24; i++)
        {
            var name = $"{FirstNames[i % FirstNames.Length]} {LastNames[(i * 7) % LastNames.Length]}";

            yield return new CanonicalPlayer(
                SourcePlayerId: $"demo-{sportKey}-p{i:D3}",
                SportKey: sportKey,
                FullName: name,
                Position: positions[i % positions.Length],
                SourceTeamId: null,
                TeamName: null);
        }
    }

    private void ThrowIfInjected()
    {
        if (FailureMode == "error")
            throw new HttpRequestException("Injected failure: simulated upstream 503.");

        if (FailureMode == "timeout")
            throw new TaskCanceledException("Injected failure: simulated request timeout.");
    }
}
