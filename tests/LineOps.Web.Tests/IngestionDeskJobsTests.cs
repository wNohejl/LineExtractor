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
    public void Zero_rows_is_nothing_new()
        => Assert.Equal(new DeskJobOutcome(DeskJobResult.Nothing, "Prices: nothing new to store."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 0, 0, "ignored")));

    [Fact]
    public void Rows_are_done_and_the_detail_wins_when_there_is_one()
    {
        Assert.Equal(new DeskJobOutcome(DeskJobResult.Done, "Prices: 1,204 rows."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 1204, 0, null)));
        Assert.Equal(new DeskJobOutcome(DeskJobResult.Done, "40 prices for 3 fixtures."),
            IngestionDeskJobs.Outcome("Prices", new JobOutcome("odds:lines", true, 40, 0, "40 prices for 3 fixtures.")));
    }
}
