using LineOps.Desk;
using LineOps.Ingestion.Services;
using LineOps.Web.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The Board's pull menu is LineOps's ingestion jobs on the desk's job menu: the cost each entry
/// quotes and the sentence each outcome toasts are this app's, and they read as they did when the
/// menu was the app's own component.
/// </summary>
public class IngestionDeskJobsTests
{
    private static IngestionJob Job(int requests, int credits = 0, bool available = true)
        => new("odds:lines", "Prices", "Every book's line", requests, available, available ? null : "No key.", credits);

    [Theory]
    [InlineData(3, 12, "12 credits")]
    [InlineData(0, 0, "no requests")]
    [InlineData(7, 0, "~7 req")]
    public void The_cost_is_quoted_in_the_unit_that_runs_out(int requests, int credits, string cost)
        => Assert.Equal(cost, IngestionDeskJobs.Describe(Job(requests, credits)).Cost);

    [Fact]
    public void An_unavailable_pull_keeps_its_reason()
    {
        var job = IngestionDeskJobs.Describe(Job(1, available: false));

        Assert.False(job.Available);
        Assert.Equal("No key.", job.Unavailable);
        Assert.Equal("Every book's line", job.Description);
    }

    [Fact]
    public void A_failure_names_the_failed_sources()
        => Assert.Equal(new DeskJobOutcome(DeskJobResult.Failed, "Prices: 2 source failed."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", false, 10, 2, null)));

    [Fact]
    public void Zero_rows_without_a_detail_is_nothing_new()
        => Assert.Equal(new DeskJobOutcome(DeskJobResult.Nothing, "Prices: nothing new to store."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 0, 0, null)));

    [Fact]
    public void Zero_rows_keeps_the_sentence_that_says_what_was_asked()
    {
        const string detail = "0 closing lines written; 0 of 3 games asked got a close; 520 set aside until their retry date.";

        Assert.Equal(new DeskJobOutcome(DeskJobResult.Nothing, detail),
            IngestionDeskJobs.Outcome("Closing lines owed", new JobOutcome("espn:closing-lines", true, 0, 0, detail)));
    }

    [Fact]
    public void Rows_are_done_and_the_detail_wins_when_there_is_one()
    {
        Assert.Equal(new DeskJobOutcome(DeskJobResult.Done, "Prices: 1,204 rows."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 1204, 0, null)));
        Assert.Equal(new DeskJobOutcome(DeskJobResult.Done, "40 prices for 3 fixtures."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 40, 0, "40 prices for 3 fixtures.")));
    }
}

/// <summary>
/// A range pull's summary as the toast the menu raises: a refusal or a failed day is a warning
/// that says so, a range that found no game is nothing new, and anything else is done.
/// </summary>
public class RangeOutcomeTests
{
    private static RangePullSummary Summary(int games = 16, int failures = 0, string? refused = null)
        => new("nfl", new DateOnly(2024, 9, 5), new DateOnly(2024, 9, 9), 5, 4, true, 4, 0, games, games, 700, 64, 24,
            failures, refused);

    [Fact]
    public void A_refusal_is_a_warning_in_its_own_words()
        => Assert.Equal(new DeskJobOutcome(DeskJobResult.Failed, "A range pull is already running on this host."),
            IngestionDeskJobs.RangeOutcome(Summary(refused: "A range pull is already running on this host.")));

    [Fact]
    public void A_failed_day_is_a_warning()
        => Assert.Equal(DeskJobResult.Failed, IngestionDeskJobs.RangeOutcome(Summary(failures: 1)).Result);

    [Fact]
    public void No_game_found_is_nothing_new()
        => Assert.Equal(DeskJobResult.Nothing, IngestionDeskJobs.RangeOutcome(Summary(games: 0)).Result);

    [Fact]
    public void A_pull_that_ran_says_what_it_did()
    {
        var outcome = IngestionDeskJobs.RangeOutcome(Summary());

        Assert.Equal(DeskJobResult.Done, outcome.Result);
        Assert.StartsWith("NFL 2024-09-05 to 2024-09-09: 4 of 5 days with games", outcome.Summary);
    }
}
