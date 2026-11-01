using Bunit;
using LineOps.Core.Entities;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// The game picker: a search field that heads a popover list (HIG 6). Focus stays in the field;
/// the arrows move the active option, Enter picks it, the first Escape closes the list and the
/// second clears the field. The list is a floating material, not a bordered group in the form.
/// </summary>
public class GamePickerTests : WagerTestContext
{
    private static Game Game(int id, string away, string home) => new()
    {
        Id = id,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        AwayTeam = new Team { Name = away, Abbrev = away[..3].ToUpperInvariant() },
        HomeTeam = new Team { Name = home, Abbrev = home[..3].ToUpperInvariant() },
        StartsAt = DateTimeOffset.UtcNow.AddDays(id),
        Status = GameStatus.Scheduled
    };

    private IRenderedComponent<GamePicker> Picker(List<int?>? picked = null, bool offerNoGame = false)
    {
        Data.Games = [Game(1, "Pittsburgh Steelers", "New Orleans Saints"), Game(2, "Pittsburgh Steelers", "Baltimore Ravens")];

        return RenderComponent<GamePicker>(p => p
            .Add(x => x.OfferNoGame, offerNoGame)
            .Add(x => x.ValueChanged, (int? id) => picked?.Add(id)));
    }

    /// <summary>Types into the field and waits for the debounced search to open the list.</summary>
    private static void Type(IRenderedComponent<GamePicker> cut, string text)
    {
        cut.Find("input").Input(text);
        cut.WaitForAssertion(() => cut.Find("[role=listbox]"), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Before_anything_is_typed_the_field_is_a_closed_combobox_with_no_list()
    {
        var cut = Picker();

        var input = cut.Find("input");
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[role=listbox]"));
        Assert.Empty(cut.FindAll(".popover__panel"));
    }

    [Fact]
    public void Typing_opens_the_games_as_a_popover_list_under_the_field()
    {
        var cut = Picker();

        Type(cut, "ste");

        Assert.Equal(["ste"], Data.Searches);
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-expanded"));

        // Floating, as every popover on the desk, and not a bordered group inside the form.
        var panel = cut.Find(".popover__panel");
        var list = panel.QuerySelector("[role=listbox]")!;
        Assert.Empty(panel.QuerySelectorAll(".wgroup"));

        var options = list.QuerySelectorAll("[role=option]");
        Assert.Equal(2, options.Length);
        Assert.Contains("Pittsburgh Steelers at New Orleans Saints", options[0].TextContent);

        // The list names the field's aria-controls, and the first option is the active one.
        Assert.Equal(list.Id, cut.Find("input").GetAttribute("aria-controls"));
        Assert.Equal(options[0].Id, cut.Find("input").GetAttribute("aria-activedescendant"));
        Assert.Equal("true", options[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task The_arrows_move_the_active_option_and_Enter_picks_it()
    {
        var picked = new List<int?>();
        var cut = Picker(picked);
        Type(cut, "ste");

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        var options = cut.FindAll("[role=option]");
        Assert.Equal(options[1].Id, cut.Find("input").GetAttribute("aria-activedescendant"));
        Assert.Equal("true", options[1].GetAttribute("aria-selected"));
        Assert.Equal("false", options[0].GetAttribute("aria-selected"));

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([2], picked);
        // The choice replaces the field with the game itself.
        Assert.Empty(cut.FindAll("[role=listbox]"));
        Assert.Contains("Baltimore Ravens", cut.Find(".game-search__chosen").TextContent);
    }

    [Fact]
    public async Task The_first_Escape_closes_the_list_and_keeps_the_text_and_the_second_clears_it()
    {
        var cut = Picker();
        Type(cut, "ste");

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=listbox]"));
        Assert.Equal("false", cut.Find("input").GetAttribute("aria-expanded"));
        Assert.Equal("ste", cut.Find("input").GetAttribute("value"));

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
    }

    [Fact]
    public void No_game_is_offered_as_the_last_option_when_asked_for()
    {
        var cut = Picker(offerNoGame: true);
        Type(cut, "ste");

        var options = cut.FindAll("[role=option]");
        Assert.Equal(3, options.Count);
        Assert.Contains("No game", options[2].TextContent);
    }

    [Fact]
    public void A_click_on_an_option_picks_it()
    {
        var picked = new List<int?>();
        var cut = Picker(picked);
        Type(cut, "ste");

        cut.FindAll("[role=option]")[1].Click();

        Assert.Equal([2], picked);
    }
}
