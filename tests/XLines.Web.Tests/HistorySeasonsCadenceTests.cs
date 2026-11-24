using XLines.Web.Components.Panels;

namespace XLines.Web.Tests;

/// <summary>
/// The History window re-reads the whole-database season coverage only when it is due, not on
/// every runs notice: a backfill raises one per one-second settle, and the table is a grouped read
/// of every game.
/// </summary>
public class HistorySeasonsCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_runs_notice_inside_the_interval_reads_only_what_a_run_changes()
        => Assert.False(HistoryPanel.SeasonsDue(asked: false, Now.AddSeconds(-5), Now, walkEnded: false));

    [Fact]
    public void A_runs_notice_once_the_interval_has_passed_reads_the_seasons_again()
        => Assert.True(HistoryPanel.SeasonsDue(asked: false, Now - HistoryPanel.SeasonsInterval, Now, walkEnded: false));

    [Fact]
    public void Opening_a_press_or_a_retry_always_reads_the_seasons()
        => Assert.True(HistoryPanel.SeasonsDue(asked: true, Now.AddSeconds(-1), Now, walkEnded: false));

    [Fact]
    public void Seasons_never_read_are_read()
        => Assert.True(HistoryPanel.SeasonsDue(asked: false, null, Now, walkEnded: false));

    [Fact]
    public void The_end_of_a_walk_reads_the_seasons_at_once_so_the_figure_left_is_exact()
        => Assert.True(HistoryPanel.SeasonsDue(asked: false, Now.AddSeconds(-1), Now, walkEnded: true));
}
