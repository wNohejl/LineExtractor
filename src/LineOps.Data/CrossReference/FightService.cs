using System.Globalization;
using LineOps.Core.Analytics;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.CrossReference;

/// <summary>
/// What the desk reads about a fight and its fighters (ADR 0021): the tale of the tape for a bout,
/// and one fighter on their own.
///
/// <para>
/// Everything here is computed from what ESPN states and the desk stores — the profile's few typed
/// facts, ESPN's career rates and its per-fight stat sheets kept whole. Averages are taken over the
/// fights ESPN kept a sheet for, and say how many that was; a figure with no fights behind it is
/// absent rather than zero. Which side a row favours is a reading of the number, not advice.
/// </para>
/// </summary>
public class FightService(LineOpsDbContext db)
{
    /// <summary>How many of each fighter's fights the tape lists.</summary>
    public const int RecentFights = 5;

    /// <summary>A bout and its card, with both fighters loaded, or null when the game is not a fight.</summary>
    public async Task<(Game Game, Bout Bout)?> BoutAsync(int gameId, CancellationToken ct = default)
    {
        var bout = await db.Bouts.AsNoTracking()
            .Include(b => b.Game!).ThenInclude(g => g.HomeTeam)
            .Include(b => b.Game!).ThenInclude(g => g.AwayTeam)
            .Include(b => b.Game!).ThenInclude(g => g.Sport)
            .FirstOrDefaultAsync(b => b.GameId == gameId, ct);

        return bout?.Game is { } game ? (game, bout) : null;
    }

    /// <summary>Each game's bout, for a list of games — the board reads a card's rows at once.</summary>
    public async Task<Dictionary<int, Bout>> BoutsAsync(IReadOnlyCollection<int> gameIds, CancellationToken ct = default)
        => gameIds.Count == 0
            ? []
            : await db.Bouts.AsNoTracking().Where(b => gameIds.Contains(b.GameId)).ToDictionaryAsync(b => b.GameId, ct);

    /// <summary>One fighter with their profile read out, or null when the team is not a fighter.</summary>
    public async Task<FighterCard?> FighterAsync(int teamId, CancellationToken ct = default)
    {
        var team = await db.Teams.AsNoTracking().Include(t => t.Sport).FirstOrDefaultAsync(t => t.Id == teamId, ct);

        if (team is null || !FightSports.IsFight(team.Sport?.Key))
            return null;

        var profile = await db.FighterProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.TeamId == teamId, ct);
        return Card(team, profile, null);
    }

    /// <summary>The two fighters of a bout side by side, or null when the game is not a fight.</summary>
    public async Task<FightTape?> TapeAsync(int gameId, CancellationToken ct = default)
    {
        if (await BoutAsync(gameId, ct) is not var (game, bout))
            return null;

        var profiles = await db.FighterProfiles.AsNoTracking()
            .Where(p => p.TeamId == game.HomeTeamId || p.TeamId == game.AwayTeamId)
            .ToDictionaryAsync(p => p.TeamId, ct);

        var red = Card(game.HomeTeam!, profiles.GetValueOrDefault(game.HomeTeamId), bout.HomeRecord, game.StartsAt);
        var blue = Card(game.AwayTeam!, profiles.GetValueOrDefault(game.AwayTeamId), bout.AwayRecord, game.StartsAt);

        return new FightTape(game, bout, red, blue,
            Physical(red, blue), Career(red, blue), PerFight(red, blue), Common(red, blue));
    }

    /// <summary>
    /// A fighter as the tape reads them. History is cut at <paramref name="before"/> when given —
    /// a past bout's tape compares the fighters as they came into it, not as they are now.
    /// </summary>
    private static FighterCard Card(Team team, FighterProfile? profile, string? recordGoingIn, DateTimeOffset? before = null)
    {
        var career = FighterPayload.ReadCareer(profile?.Career);
        var history = FighterPayload.ReadHistory(profile?.History)
            .Where(f => before is null || f.Date is null || f.Date < before.Value.AddHours(-12))
            .ToList();

        return new FighterCard(team, profile, career, history, recordGoingIn ?? profile?.Record, before);
    }

    // ---- Rows ------------------------------------------------------------------------------

    private static IReadOnlyList<TapeRow> Physical(FighterCard red, FighterCard blue)
    {
        var rows = new List<TapeRow>
        {
            Text("Record", red.Record, blue.Record),
            Number("Age", red.Age, blue.Age, "0", Edge.None),
            Number("Height", (double?)red.Profile?.HeightInches, (double?)blue.Profile?.HeightInches, "0.#", Edge.Higher, Inches),
            Number("Reach", (double?)red.Profile?.ReachInches, (double?)blue.Profile?.ReachInches, "0.#", Edge.Higher, v => $"{v:0.#}\""),
            Text("Stance", red.Profile?.Stance, blue.Profile?.Stance),
            Text("Weight class", red.Profile?.WeightClass, blue.Profile?.WeightClass),
            Text("Gym", red.Profile?.Association, blue.Profile?.Association),
            Text("Country", red.Profile?.Country, blue.Profile?.Country)
        };

        return rows.Where(r => r.Home is not null || r.Away is not null).ToList();
    }

    /// <summary>ESPN's career rates and record splits, every one either fighter has, in ESPN's order.</summary>
    private static IReadOnlyList<TapeRow> Career(FighterCard red, FighterCard blue)
    {
        var rows = new List<TapeRow>();

        foreach (var key in red.Career.Rates.Select(r => r.Key).Union(blue.Career.Rates.Select(r => r.Key)))
        {
            var a = red.Career.Rates.FirstOrDefault(r => r.Key == key);
            var b = blue.Career.Rates.FirstOrDefault(r => r.Key == key);
            var label = (a ?? b)!.Label;
            var percent = key.Contains("Accuracy", StringComparison.OrdinalIgnoreCase) || key.Contains("Percentage", StringComparison.OrdinalIgnoreCase);

            rows.Add(Number(label, Parse(a?.Value), Parse(b?.Value), percent ? "0.#'%'" : "0.00",
                CareerEdge(key)));
        }

        foreach (var key in red.Career.Record.Select(r => r.Key).Union(blue.Career.Record.Select(r => r.Key)))
        {
            if (key == "wins-losses-draws")
                continue;

            var a = red.Career.Record.FirstOrDefault(r => r.Key == key);
            var b = blue.Career.Record.FirstOrDefault(r => r.Key == key);
            rows.Add(Text((a ?? b)!.Label, a?.Value, b?.Value));
        }

        return rows;
    }

    /// <summary>Which way a career rate favours: more is better except where a rate is only a style.</summary>
    private static Edge CareerEdge(string key) => key switch
    {
        "decisionPercentage" => Edge.None,
        _ => Edge.Higher
    };

    /// <summary>
    /// Averages over each fighter's fights with a stat sheet, from ESPN's per-fight sheets, and the
    /// shape of the record from every recorded fight — how they win, how long they go, how they
    /// have done lately.
    /// </summary>
    private static IReadOnlyList<TapeRow> PerFight(FighterCard red, FighterCard blue)
    {
        var a = red.Sheets;
        var b = blue.Sheets;

        var rows = new List<TapeRow>
        {
            Text("Fights with stats", a.Count.ToString(CultureInfo.InvariantCulture), b.Count.ToString(CultureInfo.InvariantCulture)),
            Number("Sig. strikes landed", Mean(a, "SSL"), Mean(b, "SSL"), "0.0", Edge.Higher),
            Number("Sig. strike accuracy", Ratio(a, "SSL", "SSA"), Ratio(b, "SSL", "SSA"), "0.#'%'", Edge.Higher),
            Number("Total strikes landed", Mean(a, "TSL"), Mean(b, "TSL"), "0.0", Edge.Higher),
            Number("Knockdowns", Mean(a, "KD"), Mean(b, "KD"), "0.00", Edge.Higher),
            Number("Takedowns landed", Mean(a, "TDL"), Mean(b, "TDL"), "0.00", Edge.Higher),
            Number("Takedown accuracy", Ratio(a, "TDL", "TDA"), Ratio(b, "TDL", "TDA"), "0.#'%'", Edge.Higher),
            Number("Submission attempts", Mean(a, "SM"), Mean(b, "SM"), "0.00", Edge.Higher),
            Number("Ground strikes landed", Mean(a, "SGHL", "SGBL", "SGLL"), Mean(b, "SGHL", "SGBL", "SGLL"), "0.0", Edge.Higher),
            Number("Finish rate", red.FinishRate, blue.FinishRate, "0'%'", Edge.Higher),
            Number("Minutes a fight", red.MinutesPerFight, blue.MinutesPerFight, "0.0", Edge.None),
            Text("Last five", red.Form, blue.Form)
        };

        return rows.Where(r => r.Home is not null || r.Away is not null).ToList();
    }

    /// <summary>Opponents both have fought, and how each fared — the one direct comparison two careers offer.</summary>
    private static IReadOnlyList<CommonOpponent> Common(FighterCard red, FighterCard blue)
    {
        static string Key(FighterFight f) => f.OpponentId ?? FighterNames.Fold(f.Opponent);

        var redIds = red.History.Where(f => f.Opponent is not null).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var blueIds = blue.History.Where(f => f.Opponent is not null).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());

        // Each other is not a common opponent; that is head to head.
        var redId = red.Team.ExternalIds.GetValueOrDefault("espn");
        var blueId = blue.Team.ExternalIds.GetValueOrDefault("espn");

        return redIds.Keys.Intersect(blueIds.Keys)
            .Where(k => k != redId && k != blueId)
            .Select(k => new CommonOpponent(redIds[k][0].Opponent!, redIds[k], blueIds[k]))
            .OrderByDescending(c => c.Home.Concat(c.Away).Max(f => f.Date ?? DateTimeOffset.MinValue))
            .ToList();
    }

    // ---- Arithmetic -----------------------------------------------------------------------

    private static double? Mean(IReadOnlyList<IReadOnlyList<FighterStat>> sheets, params string[] keys)
    {
        var values = sheets
            .Select(s => keys.Select(k => Parse(s.FirstOrDefault(x => x.Key == k)?.Value)).ToList())
            .Where(v => v.Any(x => x is not null))
            .Select(v => v.Sum(x => x ?? 0))
            .ToList();

        return values.Count == 0 ? null : values.Average();
    }

    private static double? Ratio(IReadOnlyList<IReadOnlyList<FighterStat>> sheets, string landed, string attempted)
    {
        double hit = 0, tried = 0;

        foreach (var sheet in sheets)
        {
            if (Parse(sheet.FirstOrDefault(x => x.Key == landed)?.Value) is { } l
                && Parse(sheet.FirstOrDefault(x => x.Key == attempted)?.Value) is { } a)
            {
                hit += l;
                tried += a;
            }
        }

        return tried > 0 ? hit / tried * 100 : null;
    }

    internal static double? Parse(string? value)
        => value is not null
           && double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    private static string Inches(double inches) => $"{(int)inches / 12}' {inches % 12:0.#}\"";

    private enum Edge { None, Higher }

    private static TapeRow Text(string label, string? home, string? away) => new(label, home, away, 0);

    private static TapeRow Number(string label, double? home, double? away, string format, Edge edge,
        Func<double, string>? show = null)
    {
        string? Show(double? v) => v is { } x ? show?.Invoke(x) ?? x.ToString(format, CultureInfo.InvariantCulture) : null;

        var lean = edge == Edge.Higher && home is { } h && away is { } a && Math.Abs(h - a) > 1e-9
            ? h > a ? 1 : -1
            : 0;

        return new TapeRow(label, Show(home), Show(away), lean);
    }
}

/// <summary>One fighter as the desk reads them: the profile, ESPN's figures, and what they add up to.</summary>
public sealed record FighterCard(
    Team Team,
    FighterProfile? Profile,
    FighterCareer Career,
    IReadOnlyList<FighterFight> History,
    string? Record,
    DateTimeOffset? AsOf)
{
    /// <summary>Age at the bout, or today for a fighter on their own.</summary>
    public double? Age => Profile?.BornOn is { } born
        ? Math.Floor(((AsOf ?? DateTimeOffset.UtcNow).Date - born.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.2425)
        : null;

    /// <summary>The stat sheets ESPN kept, one per fight that has one — UFC fights, in practice.</summary>
    public IReadOnlyList<IReadOnlyList<FighterStat>> Sheets => History.Where(f => f.Stats.Count > 0).Select(f => f.Stats).ToList();

    /// <summary>Of the fights won, the share won inside the distance. Null with no wins on record.</summary>
    public double? FinishRate
    {
        get
        {
            var wins = History.Where(f => f.Result == "W").ToList();
            if (wins.Count == 0)
                return null;

            var finishes = wins.Count(f => f.Method is { } m && !m.Contains("Decision", StringComparison.OrdinalIgnoreCase));
            return finishes * 100.0 / wins.Count;
        }
    }

    /// <summary>How long a fight lasts, from every recorded fight with a round and a clock.</summary>
    public double? MinutesPerFight
    {
        get
        {
            var lengths = History
                .Where(f => f.Round is > 0 && f.Clock is not null)
                .Select(f => (f.Round!.Value - 1) * 5 + (ParseClock(f.Clock) ?? 0) / 60.0)
                .ToList();

            return lengths.Count == 0 ? null : lengths.Average();
        }
    }

    /// <summary>The last five results, oldest first, as a run: "W W L W W".</summary>
    public string? Form
    {
        get
        {
            var recent = History.Where(f => f.Result is not null).Take(FightService.RecentFights).Reverse().Select(f => f.Result!).ToList();
            return recent.Count == 0 ? null : string.Join(' ', recent);
        }
    }

    private static int? ParseClock(string? clock)
        => clock?.Split(':') is [var m, var s] && int.TryParse(m, out var mm) && int.TryParse(s, out var ss) ? mm * 60 + ss : null;
}

/// <summary>
/// One row of the tape: what it measures, each fighter's reading, and which side it leans to —
/// 1 the red corner (home), -1 the blue, 0 neither or not a measure that leans.
/// </summary>
public sealed record TapeRow(string Label, string? Home, string? Away, int Lean);

public sealed record CommonOpponent(string Opponent, IReadOnlyList<FighterFight> Home, IReadOnlyList<FighterFight> Away);

/// <summary>A bout's two fighters side by side.</summary>
public sealed record FightTape(
    Game Game,
    Bout Bout,
    FighterCard Home,
    FighterCard Away,
    IReadOnlyList<TapeRow> Physical,
    IReadOnlyList<TapeRow> Career,
    IReadOnlyList<TapeRow> PerFight,
    IReadOnlyList<CommonOpponent> Common);
