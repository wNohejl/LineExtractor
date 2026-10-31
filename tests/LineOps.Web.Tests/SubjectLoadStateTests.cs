using System.Text.RegularExpressions;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// How the four subject windows — Game, Team, Head to head, Player — stand while they load and
/// after a load fails.
///
/// <para>
/// These read the panels' source rather than render them. Each one's first query goes through
/// a concrete EF service (<c>BoardService</c>, <c>GameLogService</c>, <c>MatchupCrossReference</c>)
/// on a Postgres context, so a render test can reach the failed first load
/// (<see cref="PanelLoadStateTests"/> does, for Head to head) but not a loaded window whose
/// refresh then fails, which is the case that matters. The rules are therefore pinned on the
/// markup's shape, the way <see cref="ScoreboardTests"/> pins its stylesheet.
/// </para>
///
/// <para>
/// The bug: each panel checked <c>@if (Failed)</c> first and drew its body only in the
/// <c>else</c>. These windows refresh on every signal they watch, so one transient error
/// replaced a loaded scoreboard, its lines and its players with "The game did not load." The
/// Board's order is the rule: the failure line first, above whatever is already held; the body
/// whenever there is data; the loading shape only before the first load and not after a failure.
/// </para>
/// </summary>
public class SubjectLoadStateTests
{
    private static string Panel(string name) => File.ReadAllText(Path.Combine(
        Repo.Root(), "src", "LineOps.Web", "Components", "Panels", name + ".razor"));

    /// <summary>The failure block, and whether an <c>else</c> hangs off it.</summary>
    private static readonly Regex FailureThenElse = new(
        @"@if \(Failed\)\s*\{\s*<PanelLoadFailure[^}]*\}\s*else", RegexOptions.Singleline);

    private static void AssertTheFailureSitsAboveTheBody(string panel)
    {
        var source = Panel(panel);

        Assert.Contains("<PanelLoadFailure", source);
        Assert.DoesNotMatch(FailureThenElse, source);

        // The placeholder is for a window with nothing yet, not one whose load just failed.
        Assert.Matches(new Regex(@"@if \(!Failed\)\s*\{\s*(@\*.*?\*@\s*)?<DeskSkeleton", RegexOptions.Singleline), source);
    }

    [Fact]
    public void A_failed_refresh_keeps_the_loaded_game() => AssertTheFailureSitsAboveTheBody("GamePanel");

    [Fact]
    public void A_failed_refresh_keeps_the_loaded_team() => AssertTheFailureSitsAboveTheBody("TeamPanel");

    [Fact]
    public void A_failed_refresh_keeps_the_loaded_meetings() => AssertTheFailureSitsAboveTheBody("HeadToHeadPanel");

    [Fact]
    public void A_failed_refresh_keeps_the_loaded_player() => AssertTheFailureSitsAboveTheBody("PlayerPanel");

    /// <summary>
    /// Head to head also blanked itself on every refresh that succeeded: a <c>_loading</c> flag
    /// went up at the start of each load and the body gave way to "Reading previous meetings…"
    /// until it came back down.
    /// </summary>
    [Fact]
    public void A_refresh_does_not_blank_the_meetings()
    {
        var source = Panel("HeadToHeadPanel");

        Assert.DoesNotContain("_loading", source);
        Assert.DoesNotContain("Reading previous meetings", source);
    }

    // ---- Loading is a shape ----------------------------------------------------------

    [Theory]
    [InlineData("GamePanel", "Loading the game")]
    [InlineData("TeamPanel", "Loading @(")]
    [InlineData("HeadToHeadPanel", "Reading previous meetings")]
    public void A_subject_window_loads_as_a_shape_not_a_sentence(string panel, string sentence)
    {
        var source = Panel(panel);

        Assert.DoesNotContain(sentence, source);
        Assert.Contains("<DeskSkeleton", source);
    }

    // ---- The Game window's tags ----------------------------------------------------------

    /// <summary>
    /// A scheduled game's phase is its start time, which the detail line under the title already
    /// says. The Board gives a scheduled game no status tag; neither does its window.
    /// </summary>
    [Fact]
    public void Scheduled_game_has_no_status_tag()
    {
        var source = Panel("GamePanel");

        Assert.Matches(
            @"@if \(game\.Status != GameStatus\.Scheduled\)\s*\{\s*<Tag State=""@StatusState\(game\.Status\)""",
            source);
        Assert.Single(Regex.Matches(source, @"@StatusState\(game\.Status\)"));
        Assert.Contains("@Starts(game.StartsAt)", source);
    }

    // ---- Rows and results ------------------------------------------------------------

    /// <summary>
    /// The Team window's game and roster rows disclose with the leading triangle, not a trailing
    /// chevron that flipped down and up — the HIG's disclosure button, on rows that want a
    /// triangle.
    /// </summary>
    [Fact]
    public void Team_rows_disclose_with_a_leading_triangle()
    {
        var source = Panel("TeamPanel");

        Assert.Equal(2, Regex.Matches(source, @"<DeskDisclosure\b").Count);
        Assert.DoesNotContain("logrow__chev", source);
        Assert.DoesNotContain("ExpandMore", source);
    }

    [Fact]
    public void Player_results_are_result_capsules()
    {
        var source = Panel("PlayerPanel");

        Assert.Contains("<ResultTag", source);
        Assert.DoesNotContain("tag--good", source);
        Assert.DoesNotContain("tag--bad", source);
    }

    /// <summary>
    /// The Team window's record reads 5-1-1 above a log with a T capsule in it. It printed wins
    /// and losses alone, so a tie vanished from the header while the list below still drew it.
    /// </summary>
    [Fact]
    public void The_team_record_counts_ties()
    {
        var source = Panel("TeamPanel");

        Assert.DoesNotContain("@_wins-@_losses", source);
        Assert.Contains("GameLogSplits.FormatRecord(_visible)", source);
    }
}
