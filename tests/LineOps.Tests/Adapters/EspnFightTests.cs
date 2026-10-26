using System.Text.Json;
using LineOps.Core.Contracts;
using LineOps.Core.Entities;
using LineOps.Ingestion.Adapters;

namespace LineOps.Tests.Adapters;

/// <summary>
/// ESPN's UFC, pinned against recorded payloads from UFC 300 (13 April 2024) and Alex Pereira's
/// athlete pages, trimmed of media. The fight center carries the card; the scoreboard carries the
/// scheduled rounds; the athlete pages carry a career (ADR 0021).
/// </summary>
public class EspnFightTests
{
    private static JsonElement Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static List<CanonicalGame> Card(List<CanonicalPlayer>? players = null, List<CanonicalPlayerStat>? stats = null)
    {
        var rounds = EspnStatsAdapter.ReadScoreboardCards(Load("espn.ufc.scoreboard.json")).Single().Rounds;
        return EspnStatsAdapter.ParseFightCenter(Load("espn.ufc.fightcenter.json"), "ufc", rounds, players, stats).ToList();
    }

    [Fact]
    public void TheScoreboardNamesTheCardAndEachBoutsDistance()
    {
        var card = EspnStatsAdapter.ReadScoreboardCards(Load("espn.ufc.scoreboard.json")).Single();

        Assert.Equal("600041053", card.EventId);
        Assert.Equal(13, card.Rounds.Count);
        Assert.Equal(5, card.Rounds["401619309"]);
        Assert.Equal(3, card.Rounds["401630738"]);
    }

    [Fact]
    public void EveryBoutOnTheCardIsAGame()
    {
        var card = Card();

        Assert.Equal(13, card.Count);
        Assert.All(card, b => Assert.Equal("final", b.Status));
        Assert.All(card, b => Assert.Equal("600041053", b.Bout!.EventId));
        Assert.All(card, b => Assert.Equal("UFC 300: Pereira vs. Hill", b.Bout!.EventName));
        Assert.All(card, b => Assert.Equal(2024, b.SeasonYear));
        Assert.All(card, b => Assert.Equal(SeasonType.Regular, b.SeasonType));
    }

    [Fact]
    public void TheMainEventIsReadWhole()
    {
        var main = Card().Single(b => b.SourceGameId == "401619309");
        var bout = main.Bout!;

        // The red corner, ESPN's order 1, is home — the side ESPN's odds call home too.
        Assert.Equal("Alex Pereira", main.HomeTeamName);
        Assert.Equal("Jamahal Hill", main.AwayTeamName);
        Assert.Equal("4705658", main.Home!.SourceTeamId);
        Assert.Equal("Pereira", main.Home.Abbrev);
        Assert.Equal("Light Heavyweight", main.Home.Qualifier);

        Assert.Equal((1, 0), (main.HomeScore, main.AwayScore));
        Assert.Equal(new DateTimeOffset(2024, 4, 14, 2, 0, 0, TimeSpan.Zero), main.StartsAt);

        Assert.Equal("Main Card", bout.CardSegment);
        Assert.Equal(1, bout.MatchNumber);
        Assert.Equal("Light Heavyweight", bout.WeightClass);
        Assert.Equal(5, bout.ScheduledRounds);
        Assert.True(bout.TitleFight);
        Assert.Equal("13-4-0", bout.HomeRecord);
        Assert.Equal("12-4-0", bout.AwayRecord);

        Assert.Equal(BoutOutcome.Win, bout.Outcome);
        Assert.Equal("KO/TKO", bout.Method);
        Assert.Equal("Punch", bout.MethodDetail);
        Assert.Equal(1, bout.EndRound);
        Assert.Equal(194, bout.EndSeconds);
    }

    [Fact]
    public void ABlueCornerWinScoresForTheAwaySide()
    {
        // Holloway, from the blue corner, stopped Gaethje at 4:59 of the fifth.
        var bmf = Card().Single(b => b.SourceGameId == "401632020");

        Assert.Equal("Justin Gaethje", bmf.HomeTeamName);
        Assert.Equal("Max Holloway", bmf.AwayTeamName);
        Assert.Equal((0, 1), (bmf.HomeScore, bmf.AwayScore));
        Assert.Equal((5, 299), (bmf.Bout!.EndRound, bmf.Bout.EndSeconds));
        Assert.False(bmf.Bout.TitleFight);
        Assert.Equal(5, bmf.Bout.ScheduledRounds);
    }

    [Fact]
    public void DecisionsAndSubmissionsKeepTheirMethod()
    {
        var card = Card();

        var split = card.Single(b => b.SourceGameId == "401626640").Bout!;
        Assert.Equal("Decision - Split", split.Method);
        Assert.Null(split.MethodDetail);
        Assert.Equal((3, 300), (split.EndRound, split.EndSeconds));

        var choke = card.Single(b => b.SourceGameId == "401619310").Bout!;
        Assert.Equal("Submission", choke.Method);
        Assert.Equal("Rear Naked Choke", choke.MethodDetail);
    }

    [Fact]
    public void SegmentsAreNamedAsTheCardBillsThem()
    {
        var segments = Card().GroupBy(b => b.Bout!.CardSegment).ToDictionary(g => g.Key!, g => g.Count());

        Assert.Equal(5, segments["Main Card"]);
        Assert.Equal(4, segments["Prelims"]);
        Assert.Equal(4, segments["Early Prelims"]);
    }

    [Fact]
    public void AFinishedBoutGivesEachFighterAFightStatLine()
    {
        var players = new List<CanonicalPlayer>();
        var stats = new List<CanonicalPlayerStat>();
        Card(players, stats);

        Assert.Equal(26, players.Count);
        Assert.Equal(26, stats.Count);

        var pereira = stats.Single(s => s.SourcePlayerId == "4705658");
        Assert.Equal("401619309", pereira.SourceGameId);
        Assert.Equal("4705658", pereira.SourceTeamId);

        var line = JsonSerializer.Deserialize<Dictionary<string, string>>(pereira.StatLineJson)!;
        Assert.Equal("1", line["KD"]);
        Assert.Equal("24/30", line["SIG"]);
        Assert.Equal("12/15", line["HEAD"]);
        Assert.Equal("0:07", line["CTRL"]);
    }

    [Fact]
    public void TheScoreboardStandsInForAnUnreadableFightCenter()
    {
        var bouts = EspnStatsAdapter.ParseScoreboardBouts(Load("espn.ufc.scoreboard.json"), "600041053", "ufc").ToList();

        Assert.Equal(13, bouts.Count);

        var main = bouts.Single(b => b.SourceGameId == "401619309");
        Assert.Equal("Alex Pereira", main.HomeTeamName);
        Assert.Equal((1, 0), (main.HomeScore, main.AwayScore));
        Assert.Equal(5, main.Bout!.ScheduledRounds);
        Assert.Equal(BoutOutcome.Win, main.Bout.Outcome);
        Assert.Equal((1, 194), (main.Bout.EndRound, main.Bout.EndSeconds));

        // What only the fight center says is left unsaid rather than guessed.
        Assert.Null(main.Bout.Method);
        Assert.Null(main.Bout.MatchNumber);
    }

    [Fact]
    public void AFightCloseIsReadFromTheAthleteOdds()
    {
        var lines = EspnStatsAdapter.ParseCoreOdds(Load("espn.ufc.odds.json"), "401619309",
            "Alex Pereira", "Jamahal Hill", DateTimeOffset.UnixEpoch).ToList();

        // The live feed is skipped, and so is a book with no close; Bet365's is the first close.
        Assert.All(lines, l => Assert.Equal("Bet365", l.Book));

        Assert.Equal(-138, lines.Single(l => l.Market == Markets.Moneyline && l.Outcome == "Alex Pereira").PriceAmerican);
        Assert.Equal(110, lines.Single(l => l.Market == Markets.Moneyline && l.Outcome == "Jamahal Hill").PriceAmerican);

        var over = lines.Single(l => l.Market == Markets.Total && l.Outcome == "over");
        Assert.Equal((1.5m, -175), (over.Line, over.PriceAmerican));
        Assert.Equal(130, lines.Single(l => l.Market == Markets.Total && l.Outcome == "under").PriceAmerican);
    }

    [Fact]
    public void AFighterIsReadWithTheirWholeCareer()
    {
        var sheets = EspnStatsAdapter.ParseFightSheets(Load("espn.ufc.athlete-stats.json"));
        var rates = EspnStatsAdapter.ParseCareerRates(Load("espn.ufc.athlete-statistics.json"));
        var fighter = EspnStatsAdapter.ParseFighter(Load("espn.ufc.athlete.json"), sheets, rates, Load("espn.ufc.athlete-records.json"))!;

        Assert.Equal("4705658", fighter.SourceFighterId);
        Assert.Equal("Alex Pereira", fighter.FullName);
        Assert.Equal("Poatan", fighter.Nickname);
        Assert.Equal("Brazil", fighter.Country);
        Assert.Equal("Teixeira MMA & Fitness", fighter.Association);
        Assert.Equal("Orthodox", fighter.Stance);
        Assert.Equal(76m, fighter.HeightInches);
        Assert.Equal(79m, fighter.ReachInches);
        Assert.Equal(new DateOnly(1987, 7, 7), fighter.BornOn);
        Assert.Equal("13-4-0", fighter.Record);

        Assert.Contains(fighter.Career.Rates, r => r.Key == "strikeLPM" && r.Value == "5.01");
        Assert.Contains(fighter.Career.Record, r => r.Key == "tkos-tkoLosses" && r.Value == "11-2");
        Assert.Contains(fighter.Career.Record, r => r.Key == "titleWins" && r.Value == "6");

        // Every recorded fight, newest first, UFC and before.
        Assert.Equal(17, fighter.History.Count);
        Assert.Equal("Ciryl Gane", fighter.History[0].Opponent);
        Assert.Equal("L", fighter.History[0].Result);

        var ufc300 = fighter.History.Single(f => f.Id == "401619309");
        Assert.True(ufc300.Ufc);
        Assert.True(ufc300.Title);
        Assert.Equal("Jamahal Hill", ufc300.Opponent);
        Assert.Equal("KO/TKO", ufc300.Method);
        Assert.Equal((1, "3:14"), (ufc300.Round, ufc300.Clock));
        Assert.Contains(ufc300.Stats, s => s.Key == "SSL" && s.Value == "24");
        Assert.Contains(ufc300.Stats, s => s.Key == "SDHL/A" && s.Value == "1/1");
        Assert.Contains(ufc300.Stats, s => s.Key == "SGHL" && s.Value == "11");

        // A regional fight ESPN kept no sheet for is on record without one, not with dashes.
        var jungle = fighter.History.Last();
        Assert.False(jungle.Ufc);
        Assert.Empty(jungle.Stats);
    }

    [Theory]
    [InlineData("6' 4\"", 76)]
    [InlineData("79\"", 79)]
    [InlineData("72.5\"", 72.5)]
    [InlineData("5' 11\"", 71)]
    public void Inches(string shown, double expected)
        => Assert.Equal((decimal)expected, EspnStatsAdapter.ParseInches(shown));

    [Theory]
    [InlineData("3:14", 194)]
    [InlineData("5:00", 300)]
    [InlineData("-", null)]
    public void Clock(string shown, int? expected) => Assert.Equal(expected, EspnStatsAdapter.ParseClock(shown));
}
