using System.Text.RegularExpressions;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data.CrossReference;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Desk;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace LineOps.Web.Tests;

/// <summary>
/// HIG finding 12: information carried only by a hover title on text that cannot take focus,
/// which a keyboard and a touch screen never raise and a screen reader often skips. Each window
/// below now says it in visible text, in a DeskTooltip on a trigger that takes focus, or in the
/// accessible name of the thing it describes.
/// </summary>
public class HoverOnlyTitleTests : WagerTestContext
{
    // ---- Every book ------------------------------------------------------------------------

    /// <summary>A hold the book cannot have is said in the cell, not in a title on a dash.</summary>
    [Fact]
    public void Every_book_says_in_the_cell_why_a_hold_is_missing()
    {
        var cut = RenderComponent<BoardAllBets>(p => p.Add(x => x.GameId, BoardRows.GameId));

        Assert.Contains(cut.FindAll("span.dim"), s => s.TextContent.Trim() == "no pair");
        Assert.DoesNotContain("does not quote both sides", cut.Markup);
    }

    // ---- The board ---------------------------------------------------------------------------

    /// <summary>
    /// The starter's full name rides on the matchup link — which takes focus — rather than on a
    /// title on the starter's span. The link's tooltip still opens with the game it opens.
    /// </summary>
    [Fact]
    public void The_board_s_matchup_link_says_each_starter_in_full()
    {
        var row = BoardRows.Row();
        row.Game.AwayProbablePitcher = "Robert Gasser";
        row.Game.HomeProbablePitcher = "Freddy Peralta";

        var tooltip = BoardPanel.MatchupTooltip(row);

        Assert.StartsWith("Buffalo Bills at Kansas City Chiefs", tooltip);
        Assert.Contains("Probable starting pitcher: Robert Gasser", tooltip);
        Assert.Contains("Probable starting pitcher: Freddy Peralta", tooltip);
        Assert.True(tooltip.IndexOf("Gasser", StringComparison.Ordinal) < tooltip.IndexOf("Peralta", StringComparison.Ordinal),
            "away first, as the cell draws it");
    }

    [Fact]
    public void Without_starters_the_matchup_link_says_only_the_game()
        => Assert.Equal("Buffalo Bills at Kansas City Chiefs", BoardPanel.MatchupTooltip(BoardRows.Row()));

    /// <summary>
    /// The gap's reason is on the banner or the open row, the score's sentence is in its sr-only
    /// line, and the starter's is on the link: none of them keeps a title of its own.
    /// </summary>
    [Fact]
    public void The_board_s_gap_score_and_starter_carry_no_title()
    {
        var source = Source("Panels", "BoardPanel.razor");

        Assert.DoesNotMatch(new Regex("class=\"board__gap\"[^>]*title="), source);
        Assert.DoesNotMatch(new Regex("class=\"board__scores\"[^>]*title="), source);
        Assert.DoesNotMatch(new Regex("class=\"board__starter\"[^>]*title="), source);
        Assert.Contains("Tooltip=\"@MatchupTooltip(context.Item)\"", source);
    }

    // ---- Movement (the Odds window) ---------------------------------------------------------

    [Fact]
    public void The_odds_window_s_open_time_is_a_sentence()
    {
        var at = new DateTimeOffset(2026, 10, 24, 16, 0, 0, TimeSpan.Zero);
        var row = new OddsPanel.MovementRow("draftkings", new OddsSnapshot { PriceAmerican = -110, CapturedAt = at },
            new OddsSnapshot { PriceAmerican = -110, CapturedAt = at }, 0, false, [], null);

        Assert.Equal($"Opened {LineOps.Web.Components.DisplayTime.Stamp(at)}", OddsPanel.OpenedTooltip(row));
    }

    /// <summary>
    /// The open's time, the move in words and the exact time of the last move are each a
    /// DeskTooltip on a trigger that takes focus; a book seen once says so in the cell.
    /// </summary>
    [Fact]
    public void The_odds_window_s_details_are_on_focusable_triggers()
    {
        var source = Source("Panels", "OddsPanel.razor");

        Assert.Contains("<DeskTooltip Text=\"@OpenedTooltip(context.Item)\">\n                        <span class=\"dim movement__quote\" tabindex=\"0\">", Lf(source));
        Assert.Contains("<DeskTooltip Text=\"@MoveTitle(context.Item, _market)\">", source);
        Assert.Matches(new Regex("class=\"movement__move[^>]*tabindex=\"0\""), source);
        Assert.Contains("<DeskTooltip Text=\"@($\"Last moved {DisplayTime.Stamp(at)}\")\">", source);
        Assert.Contains("<span class=\"dim\">seen once</span>", source);
        Assert.DoesNotContain("title=\"Seen once", source);
        Assert.DoesNotMatch(new Regex("movement__(quote|move)\"?[^>]*title="), source);
    }

    // ---- The player window --------------------------------------------------------------------

    /// <summary>An option shows no title in most browsers; the season's label is the whole of it.</summary>
    [Fact]
    public void The_player_window_s_season_options_carry_no_title()
        => Assert.DoesNotMatch(new Regex("<option[^>]*title="), Source("Panels", "PlayerPanel.razor"));

    // ---- The price cell ---------------------------------------------------------------------

    [Fact]
    public void A_price_that_cannot_be_taken_is_a_focusable_tooltip_trigger()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer, BoardRows.Row().Moneyline.First));

        var cell = cut.Find(".price");
        Assert.Equal("0", cell.GetAttribute("tabindex"));
        Assert.Null(cell.GetAttribute("title"));
        Assert.Contains("DK -128", cut.FindComponent<DeskTooltip>().Instance.Text);
    }

    [Fact]
    public void A_closing_price_is_a_focusable_tooltip_trigger()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer, BoardRows.Row(closing: true).Moneyline.First));

        Assert.Equal("0", cut.Find(".price").GetAttribute("tabindex"));
        Assert.Contains("closing price", cut.FindComponent<DeskTooltip>().Instance.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rail is one named picture of every book; its ticks are marks within it.</summary>
    [Fact]
    public void The_rail_names_every_book_and_its_ticks_are_hidden()
    {
        var cut = RenderComponent<PriceCell>(p => p.Add(x => x.Offer, BoardRows.Row().Moneyline.First));

        var rail = cut.Find(".rail");
        Assert.Equal("img", rail.GetAttribute("role"));
        Assert.Equal("Books: DK -128, FD -135", rail.GetAttribute("aria-label"));

        var ticks = cut.FindAll(".rail__tick");
        Assert.Equal(2, ticks.Count);
        Assert.All(ticks, t =>
        {
            Assert.Equal("true", t.GetAttribute("aria-hidden"));
            Assert.Null(t.GetAttribute("title"));
        });
    }

    // ---- The Gamecast -------------------------------------------------------------------------

    /// <summary>The whole last play is a tooltip on the cut line, which takes focus.</summary>
    [Fact]
    public void The_gamecast_s_whole_last_play_is_on_a_focusable_trigger()
    {
        var cut = Gamecast();

        var last = cut.Find(".gc__last");
        Assert.Equal("0", last.GetAttribute("tabindex"));
        Assert.Null(last.GetAttribute("title"));
        Assert.Contains(cut.FindComponents<DeskTooltip>(),
            t => t.Instance.Text == "J.Hurts pass short right to A.Brown for 9 yards");
    }

    private IRenderedComponent<GamecastPanelTests.Bench> Gamecast()
    {
        GamecastPanelTests.Bench.Clock = null;
        Services.AddSingleton<IGamecastData>(new FakeGamecastData());
        Services.AddSingleton<IGameWindowData>(new FakeGameWindowData());
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
        JSInterop.SetupModule("./js/gameclock.js").Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<GamecastPanelTests.Bench>(p => p.Add(g => g.GameId, BoardRows.GameId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gc__last")));
        return cut;
    }

    // ---- Kept on purpose ----------------------------------------------------------------------

    /// <summary>
    /// What remains in these windows is a title on something that takes focus (a takeable
    /// price's button) or one that repeats the element's own visible, truncated text.
    /// </summary>
    [Theory]
    [InlineData("Panels", "BoardAllBets.razor")]
    [InlineData("Panels", "BoardPanel.razor")]
    [InlineData("Panels", "BoardForm.razor")]
    [InlineData("Panels", "OddsPanel.razor")]
    [InlineData("Panels", "GamecastPanel.razor")]
    [InlineData("Panels", "RunsPanel.razor")]
    [InlineData("Panels", "PlayerPanel.razor")]
    [InlineData("Desk", "PriceCell.razor")]
    public void Every_title_left_is_on_a_button_or_repeats_visible_text(string folder, string file)
    {
        var titled = Regex.Matches(Source(folder, file), "<(\\w+)[^>]*\\stitle=\"([^\"]*)\"[^>]*>([^<]*)");

        Assert.All(titled, m =>
        {
            var (tag, title, text) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            Assert.True(tag == "button" || title == text.Trim(), $"{file}: {m.Value}");
        });
    }

    private static string Source(string folder, string file)
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", folder, file));

    private static string Lf(string text) => text.Replace("\r\n", "\n");
}
