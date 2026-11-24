using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Testing;
using XLines.Desk.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DeskShell = XLines.Desk.Windowing.Desk;

namespace XLines.Desk.Tests;

/// <summary>
/// The columns' own chrome: the divider between two of them, and the bar on each.
///
/// <para>
/// The divider is a slider in all but name: it says where it stands and how far it can go, it
/// moves by keys as well as by pointer, and a key moves it within the same bounds a drag has —
/// the pair's combined width never changes, and neither column crosses its minimum.
/// </para>
///
/// <para>
/// A bar gives width back in order at a column's narrowest, so whatever it stops drawing it
/// still says under the pointer.
/// </para>
/// </summary>
public class DeskColumnTests : DeskTestContext
{
    private (IRenderedComponent<DeskShell> Desk, WindowManager Manager, WindowInstance Left, WindowInstance Right) TwoColumns()
    {
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());

        var manager = Services.GetRequiredService<WindowManager>();
        manager.SetViewport(1400, 800);
        var first = manager.Open(manager.Catalog.Find(FakeWindowCatalog.One)!);
        var second = manager.Open(manager.Catalog.Find(FakeWindowCatalog.Two)!);

        var row = manager.Windows.OrderBy(w => w.X).ToList();
        Assert.Equal(2, row.Count);
        Assert.Contains(first, row);
        Assert.Contains(second, row);

        return (RenderComponent<DeskShell>(), manager, row[0], row[1]);
    }

    [Fact]
    public void The_divider_says_where_it_stands_and_how_far_it_can_go()
    {
        var (desk, _, left, right) = TwoColumns();

        var divider = desk.Find(".divider");
        var now = (int)Math.Round(left.Width);
        var total = (int)Math.Round(left.Width + right.Width);

        Assert.Equal("separator", divider.GetAttribute("role"));
        Assert.Equal(now.ToString(), divider.GetAttribute("aria-valuenow"));
        Assert.Equal(left.Definition.MinWidth.ToString(), divider.GetAttribute("aria-valuemin"));
        Assert.Equal((total - right.Definition.MinWidth).ToString(), divider.GetAttribute("aria-valuemax"));
        Assert.Equal($"win-{left.Id} win-{right.Id}", divider.GetAttribute("aria-controls"));
        Assert.Contains($"{left.Title} {now} pixels wide", divider.GetAttribute("aria-valuetext"));
    }

    [Fact]
    public void The_readout_carries_both_widths_and_is_hidden_from_a_reader()
    {
        var (desk, _, left, right) = TwoColumns();

        var readout = desk.Find(".divider__readout");
        var total = (int)Math.Round(left.Width + right.Width);
        var now = (int)Math.Round(left.Width);

        Assert.Equal("true", readout.GetAttribute("aria-hidden"));
        Assert.Equal(now.ToString(), readout.QuerySelector("[data-split-left]")!.GetAttribute("data-width"));
        Assert.Equal((total - now).ToString(), readout.QuerySelector("[data-split-right]")!.GetAttribute("data-width"));
    }

    [Fact]
    public void End_gives_the_leading_column_everything_the_other_can_spare()
    {
        var (desk, manager, left, right) = TwoColumns();
        var total = left.Width + right.Width;

        desk.Find(".divider").KeyDown("End");

        var after = manager.Windows.OrderBy(w => w.X).ToList();
        Assert.Equal(right.Definition.MinWidth, after[1].Width, 0);
        Assert.Equal(total, after[0].Width + after[1].Width, 0);
    }

    [Fact]
    public void Home_takes_the_leading_column_to_its_minimum_and_keeps_the_pair_s_width()
    {
        var (_, _, left, right) = TwoColumns();
        left.Width = 700;
        right.Width = 500;

        var split = DeskShell.SplitByKey("Home", shift: false, left, right);

        Assert.Equal((left.Definition.MinWidth, 1200d - left.Definition.MinWidth), split);
    }

    [Theory]
    [InlineData("ArrowRight", false, 716, 484)]
    [InlineData("ArrowLeft", false, 684, 516)]
    [InlineData("ArrowRight", true, 748, 452)]
    [InlineData("ArrowLeft", true, 652, 548)]
    public void An_arrow_moves_the_divider_a_step_and_the_pair_keeps_its_width(string key, bool shift, double leftAfter, double rightAfter)
    {
        var (_, _, left, right) = TwoColumns();
        left.Width = 700;
        right.Width = 500;

        Assert.Equal((leftAfter, rightAfter), DeskShell.SplitByKey(key, shift, left, right));
    }

    [Fact]
    public void An_arrow_at_a_stop_stops_rather_than_growing_the_pair()
    {
        var (_, _, left, right) = TwoColumns();
        left.Width = 700;
        right.Width = right.Definition.MinWidth + 5;

        // A step of 16 would cross the trailing column's minimum: it goes the 5 there is.
        Assert.Equal((705d, (double)right.Definition.MinWidth), DeskShell.SplitByKey("ArrowRight", false, left, right));

        right.Width = right.Definition.MinWidth;
        Assert.Null(DeskShell.SplitByKey("ArrowRight", false, left, right));
    }

    [Fact]
    public void A_key_that_is_not_the_divider_s_does_nothing()
    {
        var (_, _, left, right) = TwoColumns();

        Assert.Null(DeskShell.SplitByKey("Enter", false, left, right));
        Assert.Null(DeskShell.SplitByKey("ArrowUp", false, left, right));
    }

    [Fact]
    public void A_desk_too_narrow_for_both_minimums_still_reads_a_range_that_holds_the_divider()
    {
        var (_, _, left, right) = TwoColumns();
        left.Width = 200;
        right.Width = 220;

        var range = DeskShell.SplitRange.Of(left, right);

        Assert.InRange(range.Now, range.Min, range.Max);
    }

    [Fact]
    public void A_title_carries_its_status_whole_as_its_tooltip()
    {
        var (desk, manager, left, _) = TwoColumns();

        manager.SetPulse(left.Id, PulseState.Healthy, "3 open");

        desk.WaitForAssertion(() =>
            Assert.Equal($"{left.Title} — 3 open", desk.Find($"#win-{left.Id} .win__title").GetAttribute("title")));
    }

    [Fact]
    public void A_collapsed_column_names_itself_under_the_pointer()
    {
        var (desk, manager, left, _) = TwoColumns();

        manager.ToggleMinimise(left.Id);

        desk.WaitForAssertion(() =>
            Assert.Equal($"{left.Title} — press to restore", desk.Find($"#win-{left.Id} .win__bar").GetAttribute("title")));
    }

    [Fact]
    public void An_open_column_s_bar_carries_no_tooltip_of_its_own()
    {
        var (desk, _, left, _) = TwoColumns();

        Assert.Null(desk.Find($"#win-{left.Id} .win__bar").GetAttribute("title"));
    }

    /// <summary>
    /// The bar answers to its column's width, not the viewport's, and gives the status up before
    /// the title. Read from the sheet, because a container query is the browser's to apply.
    /// </summary>
    [Fact]
    public void The_bar_gives_way_by_its_own_width_status_first()
    {
        var css = File.ReadAllText(Path.Combine(Support.Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));

        Assert.Matches(@"\.win__bar\s*\{[^}]*container:\s*winbar\s*/\s*inline-size", css);
        Assert.Matches(@"@container winbar \(max-width: 380px\)\s*\{\s*\.win__status\s*\{\s*display:\s*none", css);
    }
}
