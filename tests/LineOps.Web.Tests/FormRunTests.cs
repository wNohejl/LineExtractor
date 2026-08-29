using Bunit;
using LineOps.Web.Components;

namespace LineOps.Web.Tests;

/// <summary>
/// The form run's whole job is the reading direction.
///
/// The lookups hand their rows over newest first, which is right for a table and wrong for a
/// run: every other time axis on the desk advances left to right. That inversion is invisible
/// in a screenshot unless you already know the dates, and a run rendered backwards still looks
/// like a plausible run — which is exactly the kind of bug that survives review. So it is
/// pinned here.
/// </summary>
public class FormRunTests : DeskTestContext
{
    /// <summary>Newest first, the way <c>GameLogService</c> and the cross-reference produce them.</summary>
    private static IReadOnlyList<FormResult> Log() =>
    [
        new(new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero), "Yankees", false, false, 0, 1),
        new(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), "Marlins", false, false, 0, 4),
        new(new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero), "Marlins", false, true, 7, 3),
        new(new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero), "Marlins", false, true, 4, 2),
        new(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero), "Giants", true, true, 5, 4)
    ];

    private static string[] Letters(IRenderedFragment cut)
        => cut.FindAll(".formrun .tag").Select(t => t.TextContent.Trim()).ToArray();

    [Fact]
    public void Reads_oldest_first_so_the_run_runs_the_same_way_as_time()
    {
        var cut = RenderComponent<FormRun>(p => p.Add(x => x.Log, Log()));

        // The log above is L L W W W newest-first, so in reading order it is W W W L L.
        Assert.Equal(["W", "W", "W", "L", "L"], Letters(cut));
    }

    [Fact]
    public void Marks_the_newest_result_at_the_right_hand_end()
    {
        var cut = RenderComponent<FormRun>(p => p.Add(x => x.Log, Log()));

        var tags = cut.FindAll(".formrun .tag");
        var marked = tags.Where(t => t.ClassList.Contains("formrun__latest")).ToList();

        Assert.Single(marked);
        Assert.Same(tags[^1], marked[0]);

        // Keyed on the score rather than the date: titles render in local time, so a UTC
        // midnight fixture would name the previous day on any machine behind Greenwich.
        Assert.Contains("Yankees", marked[0].GetAttribute("title"));
        Assert.Contains("(0-1)", marked[0].GetAttribute("title"));
        Assert.Contains("most recent", marked[0].GetAttribute("title"));
    }

    /// <summary>
    /// Take applies to the newest games and then the run is reversed. Reversing first would
    /// quietly show the five <i>oldest</i> results — a run that is wrong about the team's
    /// current form while looking entirely reasonable.
    /// </summary>
    [Fact]
    public void Takes_the_newest_games_not_the_oldest_ones()
    {
        var cut = RenderComponent<FormRun>(p => p
            .Add(x => x.Log, Log())
            .Add(x => x.Take, 3));

        // Newest three are Aug 28 (L), Aug 26 (L), Aug 25 (W) — shown oldest first.
        Assert.Equal(["W", "L", "L"], Letters(cut));

        var tags = cut.FindAll(".formrun .tag");
        Assert.Contains("(7-3)", tags[0].GetAttribute("title"));
        Assert.Contains("(0-1)", tags[^1].GetAttribute("title"));
    }

    /// <summary>
    /// An unfinished game is not a loss. It takes the neutral tone and its own glyph rather
    /// than being rounded into the nearer result — see <see cref="TagTones.Won"/>.
    /// </summary>
    [Fact]
    public void An_undecided_game_is_neither_a_win_nor_a_loss()
    {
        IReadOnlyList<FormResult> log =
        [
            new(new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero), "Mets", true, null, null, null)
        ];

        var cut = RenderComponent<FormRun>(p => p.Add(x => x.Log, log));
        var tag = cut.Find(".formrun .tag");

        Assert.Equal("–", tag.TextContent.Trim());
        Assert.DoesNotContain("tag--bad", tag.ClassList);
        Assert.DoesNotContain("tag--good", tag.ClassList);
    }

    [Fact]
    public void A_side_with_no_games_renders_an_empty_run_rather_than_failing()
    {
        var cut = RenderComponent<FormRun>(p => p.Add(x => x.Log, Array.Empty<FormResult>()));

        Assert.Empty(cut.FindAll(".formrun .tag"));
    }

    /// <summary>
    /// The title is the only place the game behind a one-character tag is named, so it carries
    /// venue, opponent and score.
    /// </summary>
    [Fact]
    public void Each_tag_names_the_game_it_stands_for()
    {
        var cut = RenderComponent<FormRun>(p => p.Add(x => x.Log, Log()));

        var oldest = cut.FindAll(".formrun .tag")[0].GetAttribute("title");

        Assert.Contains("vs Giants", oldest);
        Assert.Contains("(5-4)", oldest);
    }
}
