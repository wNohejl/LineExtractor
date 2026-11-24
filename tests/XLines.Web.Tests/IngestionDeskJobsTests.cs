using XLines.Desk;
using XLines.Ingestion.Services;
using XLines.Web.Services;

namespace XLines.Web.Tests;

/// <summary>
/// The Board's pull menu is XLines's ingestion jobs on the desk's job menu: the cost each entry
/// quotes and the sentence each outcome toasts are this app's, and they read as they did when the
/// menu was the app's own component.
/// </summary>
public class IngestionDeskJobsTests
{
    private static IngestionJob Job(int requests, int credits = 0, bool available = true)
        => new("odds:lines", "Prices", "Every book's line", requests, available, available ? null : "No key.", credits);

    [Theory]
    [InlineData(3, 12, "12 credits")]
    [InlineData(1, 1, "1 credit")]
    [InlineData(0, 0, "free")]
    [InlineData(7, 0, "free · ~7 req")]
    public void The_cost_is_quoted_in_the_unit_that_runs_out(int requests, int credits, string cost)
        => Assert.Equal(cost, IngestionDeskJobs.Describe(Job(requests, credits)).Cost);

    /// <summary>A metered pull states what is left beside its price when the provider has said; a free one carries no credit mark.</summary>
    [Fact]
    public void A_metered_pull_says_what_is_left_and_a_free_one_never_mentions_credits()
    {
        var metered = IngestionDeskJobs.Describe(Job(1, 2) with { CreditsLeft = 412 });
        var free = IngestionDeskJobs.Describe(Job(3));

        Assert.Equal("2 credits · 412 left", metered.Cost);
        Assert.StartsWith("free", free.Cost);
        Assert.DoesNotContain("credit", free.Cost);
    }

    /// <summary>
    /// A live desk on ESPN with no key listed "Pull lines — MLB · free · No odds source registered."
    /// A metered pull with no source has no price to quote, and is never "free": it says it needs a
    /// key, and its reason and tooltip say where the key goes.
    /// </summary>
    [Theory]
    [InlineData(0)]   // lines with no odds source: no adapter to quote a price
    [InlineData(2)]   // scores with no key: the price is known, the pull still cannot run
    public void A_metered_pull_without_a_key_never_says_free_and_names_the_key_as_the_remedy(int credits)
    {
        var job = new IngestionJob("odds:lines:mlb", "Pull lines — MLB", "Moneyline, spread across every book.", 0,
            Available: false, Unavailable: IngestionJobs.NoOddsKey, EstimatedCredits: credits,
            Group: JobGroups.Lines, Hint: "Pulls MLB prices from The Odds API, a credit per market. " + IngestionJobs.NoOddsKey,
            Metered: true, NeedsKey: true);

        var entry = IngestionDeskJobs.Describe(job);

        Assert.Equal("needs a key", entry.Cost);
        Assert.DoesNotContain("free", entry.Cost, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ops → Odds feeds", entry.Unavailable);
        Assert.Contains("The Odds API key", entry.Unavailable);
        Assert.Contains("Ops → Odds feeds", entry.Hint);
        Assert.DoesNotContain("registered", entry.Unavailable);
    }

    [Fact]
    public void A_metered_pull_whose_price_is_zero_is_never_free_either()
        => Assert.Equal("needs a key", IngestionDeskJobs.Describe(Job(0, 0) with { Metered = true }).Cost);

    [Fact]
    public void The_question_and_the_tooltip_reach_the_menu()
    {
        var job = IngestionDeskJobs.Describe(Job(1, 9) with { Confirm = "This spends 9 credits. Spend them?", Hint = "Calls the odds." });

        Assert.Equal("This spends 9 credits. Spend them?", job.Confirm);
        Assert.Equal("Calls the odds.", job.Hint);
    }

    [Fact]
    public void An_unavailable_pull_keeps_its_reason()
    {
        var job = IngestionDeskJobs.Describe(Job(1, available: false));

        Assert.False(job.Available);
        Assert.Equal("No key.", job.Unavailable);
        Assert.Equal("Every book's line", job.Description);
    }

    [Fact]
    public void An_entry_keeps_its_menu_section()
        => Assert.Equal(JobGroups.Lines,
            IngestionDeskJobs.Describe(Job(1, 4) with { Group = JobGroups.Lines }).Group);

    [Fact]
    public void A_range_pull_is_not_a_menu_entry_but_names_itself_as_one()
    {
        Assert.Equal(IngestionJobs.EspnRange, IngestionDeskJobs.RangeJob.Key);
        Assert.Equal("Pull a date range", IngestionDeskJobs.RangeJob.Label);
    }

    [Fact]
    public async Task A_range_run_tells_its_days_as_the_key_s_phrase_and_toasts_the_summary()
    {
        RangePullRequest? asked = null;
        var request = new RangePullRequest("nfl", new DateOnly(2024, 9, 5), new DateOnly(2024, 9, 9));

        var run = IngestionDeskJobs.RangeRun((r, p, _) =>
        {
            asked = r;
            p.Report(new RangePullProgress("nfl", new DateOnly(2024, 9, 8), 2, 4, 2, 0));
            return Task.FromResult(new RangePullSummary("nfl", r.From, r.To, 5, 4, true, 4, 0, 16, 16, 700, 64, 24, 0, null));
        }, request);

        var phrases = new List<string>();
        var outcome = await run(new SyncProgress(phrases.Add), CancellationToken.None);

        Assert.Equal(request, asked);
        Assert.Equal(["NFL Sep 8 · 3 of 4"], phrases);
        Assert.Equal(DeskJobResult.Done, outcome.Result);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
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
