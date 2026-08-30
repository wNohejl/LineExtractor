using LineOps.Core.Analytics;
using LineOps.Core.Entities;

namespace LineOps.Tests.Analytics;

/// <summary>
/// How a game reads on the board once it has been played.
///
/// The bug these pin is the one a screenshot showed rather than a stack trace: a slate of
/// finished games printing "—" in every score column while the scores sat in the table, and a
/// board dropping every price the moment a game started. Both were presentation decisions, so
/// both are testable here without a database or a rendered page.
/// </summary>
public class GamePresentationTests
{
    private static Game Played(int? away, int? home, string awayAbbrev = "NYY", string homeAbbrev = "BOS")
        => new()
        {
            AwayScore = away,
            HomeScore = home,
            AwayTeam = new Team { Name = "New York Yankees", Abbrev = awayAbbrev },
            HomeTeam = new Team { Name = "Boston Red Sox", Abbrev = homeAbbrev },
            Status = GameStatus.Final
        };

    [Fact]
    public void AFinishedGameReadsAsAScoreNotADash()
    {
        // The whole complaint: ESPN's result is ingested, and the grid showed nothing.
        Assert.Equal("NYY 3–5 BOS", Scoreline.Format(Played(3, 5)));
    }

    [Fact]
    public void TheAwaySideLeads()
    {
        // Every other column on this desk names a game "away at home". A score column that
        // reversed it would disagree with the matchup cell beside it on the same row.
        var line = Scoreline.Format(Played(7, 1));

        Assert.StartsWith("NYY 7", line);
        Assert.EndsWith("1 BOS", line);
    }

    [Fact]
    public void AGameWithNoResultSaysSoRatherThanShowingZeros()
    {
        var scheduled = new Game
        {
            HomeTeam = new Team { Abbrev = "BOS" },
            AwayTeam = new Team { Abbrev = "NYY" }
        };

        Assert.False(Scoreline.Has(scheduled));
        Assert.Equal("—", Scoreline.Format(scheduled));
        Assert.Equal(ScoreLeader.None, Scoreline.Leader(scheduled));
    }

    [Fact]
    public void HalfAScoreStillCounts()
    {
        // A shutout arrives as one side scoring and the other holding null on some feeds. A
        // "both or nothing" check would blank the row that most obviously has a result.
        var shutout = Played(away: 4, home: null);

        Assert.True(Scoreline.Has(shutout));
        Assert.Equal("NYY 4–0 BOS", Scoreline.Format(shutout));
    }

    [Theory]
    [InlineData(3, 5, ScoreLeader.Home)]
    [InlineData(5, 3, ScoreLeader.Away)]
    [InlineData(4, 4, ScoreLeader.Level)]
    public void TheLeaderIsReported(int away, int home, ScoreLeader expected)
        => Assert.Equal(expected, Scoreline.Leader(Played(away, home)));

    [Fact]
    public void ATeamWithNoAbbreviationGetsOneRatherThanADash()
    {
        // ADR 0011 made the provider's own abbreviation come through the pipeline, but a source
        // that offers none still has to render — as something readable, not as blank.
        var derived = Scoreline.Abbrev(new Team { Name = "Colorado Rockies" }, "AWY");

        Assert.Equal("COL", derived);
        Assert.Equal("AWY", Scoreline.Abbrev(null, "AWY"));
    }

    [Fact]
    public void AClosingPriceSaysWhatItIsAndHowEarlyItWasTaken()
    {
        var start = new DateTimeOffset(2026, 8, 29, 23, 5, 0, TimeSpan.Zero);
        var text = PriceProvenance.Explain(start.AddHours(-2), start);

        // The reader has to be able to tell this from a live price at a glance, and then from
        // hover to know how much weight it carries.
        Assert.Contains("closing price", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2.0h before start", text);
    }

    [Fact]
    public void APriceTakenAtTheStartDoesNotClaimALead()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Contains("at the start", PriceProvenance.Explain(start, start));

        // Promotion waits ten minutes past the start (ADR 0010), so a scan a few seconds late
        // can be promoted. A negative lead must not print as "-0.0h before start".
        Assert.Contains("at the start", PriceProvenance.Explain(start.AddSeconds(30), start));
    }

    [Fact]
    public void WithoutAStartTimeTheProvenanceStillSaysWhatTheNumberIs()
    {
        var text = PriceProvenance.Explain(DateTimeOffset.UtcNow, startsAt: null);

        Assert.Contains("closing price", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("before start", text);
    }

    [Theory]
    [InlineData(0.5, "under a minute")]
    [InlineData(40, "40m")]
    [InlineData(150, "2.5h")]
    [InlineData(2880, "2.0d")]
    public void LeadTimeIsWrittenAtThePrecisionAReaderCanUse(double minutes, string expected)
        => Assert.Equal(expected, PriceProvenance.Lead(TimeSpan.FromMinutes(minutes)));
}
