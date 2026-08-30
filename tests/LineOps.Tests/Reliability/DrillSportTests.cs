using LineOps.Ingestion.Services;

namespace LineOps.Tests.Reliability;

/// <summary>
/// Which league the on-call drill spends its credits on.
///
/// <para>
/// The drill exercises ingest → rollup → alert, and with no fault injected it performs a
/// real pull against a metered provider. It used to be hardcoded to one league, which meant
/// pressing it on an August evening bought sixty-odd NFL fixtures weeks away while the MLB
/// slate on screen went unpriced. A drill that spends real money should buy rows somebody
/// is about to read.
/// </para>
/// </summary>
public class DrillSportTests
{
    private static readonly string[] Configured = ["nfl", "nba", "mlb", "nhl"];

    [Fact]
    public void The_league_in_play_is_the_one_drilled()
    {
        var upcoming = new Dictionary<string, int> { ["mlb"] = 31, ["nfl"] = 2 };

        Assert.Equal("mlb", IngestionJobs.DrillSport(upcoming, Configured));
    }

    [Fact]
    public void A_league_with_nothing_in_play_is_not_drilled_merely_for_being_first()
    {
        // The exact shape of the bug: nfl leads the configured list, mlb is what is on screen.
        var upcoming = new Dictionary<string, int> { ["mlb"] = 31 };

        Assert.Equal("mlb", IngestionJobs.DrillSport(upcoming, Configured));
    }

    [Fact]
    public void An_unconfigured_league_is_never_drilled_however_busy()
    {
        // Scanning a league the operator did not configure is exactly the accident a credit
        // budget cannot absorb — the same rule RunOddsAsync applies to a named sport.
        var upcoming = new Dictionary<string, int> { ["mls"] = 90, ["nhl"] = 4 };

        Assert.Equal("nhl", IngestionJobs.DrillSport(upcoming, Configured));
    }

    [Fact]
    public void With_nothing_in_play_it_falls_back_to_the_first_configured_league()
    {
        Assert.Equal("nfl", IngestionJobs.DrillSport(new Dictionary<string, int>(), Configured));
    }

    [Fact]
    public void With_no_configured_league_there_is_nothing_to_drill()
    {
        Assert.Null(IngestionJobs.DrillSport(new Dictionary<string, int> { ["mlb"] = 5 }, []));
    }

    [Fact]
    public void League_keys_are_matched_without_regard_to_case()
    {
        var upcoming = new Dictionary<string, int> { ["MLB"] = 31 };

        Assert.Equal("mlb", IngestionJobs.DrillSport(upcoming, Configured));
    }
}
