using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Analytics;
using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The Place wager window against the wizard recipe (apple-hig-components, wizards.md): the
/// header is the game as a one-line scoreboard, the price row echoes the side and trails the
/// price and its book, the stake's footer says what it wins as it is typed, a key that cannot be
/// used yet is not on screen, Duplicate sits at the header's trailing edge, and a closing line is
/// said once, as a status note under the steps card.
/// </summary>
public class WagerRecipeTests : WagerTestContext
{
    private IRenderedComponent<BoardWager> Render(string? market = Markets.Moneyline, string? outcome = BoardRows.Home)
        => RenderComponent<BoardWager>(p =>
        {
            p.Add(x => x.GameId, BoardRows.GameId);
            if (market is not null) p.Add(x => x.Market, market);
            if (outcome is not null) p.Add(x => x.Outcome, outcome);
        });

    private static IElement? Key(IRenderedFragment cut, string startsWith)
        => cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim().StartsWith(startsWith, StringComparison.Ordinal));

    private static string Text(IElement e) => Regex.Replace(e.TextContent, @"\s+", " ").Trim();

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    // ---- The header card ------------------------------------------------------------------

    [Fact]
    public void The_header_is_the_game_as_a_one_line_scoreboard()
    {
        var cut = Render();

        var board = cut.Find(".whead .ptitle__name .scoreboard.scoreboard--compact");
        Assert.Equal(["Buffalo Bills", "Kansas City Chiefs"],
            board.QuerySelectorAll(".desklink__label").Select(l => l.TextContent.Trim()));
        Assert.Contains("at", board.QuerySelectorAll(".scoreboard__at").Select(a => a.TextContent.Trim()));
    }

    [Fact]
    public void The_window_sets_no_inline_style()
    {
        Assert.DoesNotContain("style=\"", Source("src", "LineOps.Web", "Components", "Panels", "BoardWager.razor"));
    }

    [Fact]
    public void Duplicate_is_a_plain_key_at_the_headers_trailing_edge_not_under_the_log_key()
    {
        var cut = Render();

        var duplicate = cut.Find(".whead .ptitle__keys button");
        Assert.StartsWith("Duplicate", duplicate.TextContent.Trim());
        Assert.Contains("desk-btn--plain", duplicate.ClassName);
        Assert.DoesNotContain(cut.FindAll(".waction button"), b => b.TextContent.Trim().StartsWith("Duplicate", StringComparison.Ordinal));
    }

    // ---- The price row --------------------------------------------------------------------

    [Fact]
    public void The_price_row_echoes_the_side_over_its_market_and_trails_the_price_then_the_book()
    {
        var cut = Render(Markets.Spread, BoardRows.Away);
        Change(cut, "Bet");

        var row = cut.Find(".wbet__price.wager-offer");
        Assert.Equal("Bills +2.5", Text(row.QuerySelector(".wbet__side")!));
        Assert.Equal("Spread · at Chiefs", Text(row.QuerySelector(".wbet__detail")!));
        // The price is said once on the row: the line is in the echo, not beside the price.
        Assert.Empty(row.QuerySelectorAll(".wager-offer__top .wager-offer__line"));
        var price = row.QuerySelector(".wager-offer__top .wager-offer__price")!;
        Assert.Equal("-105", price.TextContent.Trim());
        Assert.Contains("book-picker", price.NextElementSibling!.ClassList);
        // No "Price" label: the echo is the label.
        Assert.DoesNotContain(row.QuerySelectorAll(".wgroup__label"), l => l.TextContent.Trim() == "Price");
    }

    // ---- The stake ------------------------------------------------------------------------

    [Fact]
    public void To_win_is_the_stake_groups_footer_and_follows_the_stake_as_it_is_typed()
    {
        var cut = Render();
        Assert.Empty(cut.FindAll(".wstake__win"));

        cut.Find("input[placeholder='Stake']").Input("100");
        Assert.Contains($"To win {OddsMath.ProfitOnWin(-128, 100m):C0}", Text(cut.Find(".wstake__win")));

        cut.Find("input[placeholder='Stake']").Input("40");
        Assert.Contains($"To win {OddsMath.ProfitOnWin(-128, 40m):C0}", Text(cut.Find(".wstake__win")));

        // The footer sits under the group, outside it; the row no longer carries Returns.
        Assert.Contains("wgroup__foot", cut.Find(".wstake__win").ClassList);
        Assert.Empty(cut.FindAll(".wstake .stake-row__returns"));
    }

    [Fact]
    public void Quick_values_are_plain_keys_and_the_field_has_no_spinner()
    {
        var cut = Render();
        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        var quick = cut.FindAll(".stake-row button").ToList();
        Assert.All(quick, k => Assert.Contains("desk-btn--plain", k.ClassName));
        Assert.Equal("true", quick.Single(k => k.TextContent.Trim() == 50m.ToString("C0")).GetAttribute("aria-pressed"));

        var css = Source("src", "LineOps.Desk", "wwwroot", "css", "lineops.css");
        Assert.Contains("::-webkit-inner-spin-button", css);
        Assert.Matches(@"(?s)\.stake-row input\[type=""number""\][^{]*\{[^}]*appearance: textfield", css);
    }

    // ---- The keys -------------------------------------------------------------------------

    [Fact]
    public void A_key_that_cannot_be_used_yet_is_not_on_screen_and_Log_says_why_beside_it()
    {
        var cut = Render();

        Assert.Null(Key(cut, "Add to slip"));
        var log = cut.Find("button.desk-btn--filled");
        Assert.True(log.HasAttribute("disabled"));
        Assert.Equal("Needs a stake above zero.", cut.Find(".waction__keys .wager-why").TextContent.Trim());

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.NotNull(Key(cut, "Add to slip"));
        Assert.Empty(cut.FindAll(".wager-why"));
        // One Filled key, and it is Log, the last of the keys: Continue is rightmost.
        var keys = cut.FindAll(".waction__keys > button");
        Assert.Single(keys, k => k.ClassList.Contains("desk-btn--filled"));
        Assert.StartsWith("Log", keys[^1].TextContent.Trim());
        Assert.All(keys.Take(keys.Count - 1), k => Assert.Contains("desk-btn--plain", k.ClassName));
    }

    // ---- The status note ------------------------------------------------------------------

    /// <summary>
    /// A started game opens on a typed price that starts from its close, the bet step open on
    /// it, and one sentence under the card saying to make it the price that was got.
    /// </summary>
    [Fact]
    public void A_started_game_opens_on_its_close_typed_and_says_to_change_it()
    {
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render();

        var note = cut.Find(".wcard + .wstatus");
        Assert.Contains("wstatus--warning", note.ClassList);
        Assert.NotNull(note.QuerySelector(".wstatus__mark svg"));
        Assert.Equal("The game has started, so the price starts from its closing line. Change it to the one you got before you log it.",
            Text(note.QuerySelector(".wstatus__text")!));
        Assert.Equal("-128", cut.Find("input[placeholder='-110']").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".check--negative"));
    }

    [Fact]
    public void A_closing_line_with_no_stake_still_says_the_stake_is_needed()
    {
        // Log is blocked by the close and Add to slip is out of sight until there is a stake, so
        // the line beside Log is what says the slip is a stake away; the close is the note's.
        Data.Row = BoardRows.Row(closing: true, startsAt: DateTimeOffset.UtcNow.AddMinutes(-42));
        var cut = Render();

        Assert.NotNull(cut.Find(".wcard + .wstatus"));
        Assert.Null(Key(cut, "Add to slip"));
        Assert.Equal("Needs a stake above zero.", cut.Find(".waction__keys .wager-why").TextContent.Trim());

        cut.FindAll("button").First(b => b.TextContent.Trim() == 50m.ToString("C0")).Click();

        Assert.NotNull(Key(cut, "Add to slip"));
        Assert.Empty(cut.FindAll(".wager-why"));
    }

    /// <summary>
    /// Maximised, the window split the review out as a card of its own beside the form and
    /// dropped its marker and its piece of the rail, so the sequence fell apart. The stepper is
    /// one column at every width now, and each step's name is a bordered capsule on the rail
    /// rather than a word beside it, the step being worked on the accent edge.
    /// </summary>
    [Fact]
    public void The_stepper_is_one_column_at_every_width_and_each_step_name_is_a_capsule()
    {
        var css = Source("src", "LineOps.Desk", "wwwroot", "css", "lineops.css");

        Assert.DoesNotContain("@container (min-width: 1000px)", css);
        Assert.DoesNotContain(".wstep--review > .wstep__dot { display: none", css);

        Assert.Matches(@"\.wstep__title\s*\{[^}]*border:\s*1px solid var\(--separator-strong\);[^}]*border-radius:\s*999px;", css);
        Assert.Matches(@"\.wstep--current \.wstep__title\s*\{[^}]*border-color:\s*var\(--accent\);", css);
    }

    [Fact]
    public void An_open_market_has_no_status_note()
    {
        var cut = Render();

        Assert.Empty(cut.FindAll(".wstatus"));
    }
}
