using AngleSharp.Dom;
using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Panels;
using XLines.Web.Services;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The shared price picker and wager key as each surface draws them: the Place wager window's
/// step Bet and key row, and the Slip's keys. The same parts in the same order everywhere, the
/// one Filled key last in its row.
/// </summary>
public class WagerSurfacesTests : WagerTestContext
{
    private static SlipItem Item()
        => new(Guid.Empty, BoardRows.GameId, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, BoardRows.Home, null, "draftkings", -110, DateTimeOffset.UtcNow, false, 50m, null, null, null);

    private static IReadOnlyList<IElement> Keys(IElement row) => row.QuerySelectorAll("button").ToList();

    [Fact]
    public void Before_a_side_the_wager_windows_picker_waits_and_says_why_in_view()
    {
        var cut = RenderComponent<BoardWager>(p => p.Add(x => x.GameId, BoardRows.GameId));

        var key = cut.Find(".wbet__price--empty " + PricePickers.Key);
        Assert.True(key.HasAttribute("disabled"));
        Assert.Equal("Book", key.QuerySelector(".popup__label")!.TextContent);
        Assert.Equal("Choose a side above.", cut.Find("#" + key.GetAttribute("aria-describedby")).TextContent.Trim());
    }

    [Fact]
    public void The_wager_windows_price_row_is_the_number_then_the_shared_picker()
    {
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home));
        Change(cut, "Bet");

        var key = cut.Find(".wager-offer__top " + PricePickers.Key);
        Assert.Equal("DraftKings", key.QuerySelector(".popup__label")!.TextContent);
        Assert.Equal("Best", key.QuerySelector(".popup__mark")!.TextContent);
        Assert.Empty(cut.FindAll(".wager-offer__top .wager-manual-key"));
    }

    [Fact]
    public void The_wager_windows_keys_are_wager_keys_with_log_filled_and_last()
    {
        using var dollars = new UsDollars();
        var cut = RenderComponent<BoardWager>(p => p
            .Add(x => x.GameId, BoardRows.GameId)
            .Add(x => x.Market, Markets.Moneyline)
            .Add(x => x.Outcome, BoardRows.Home)
            .Add(x => x.Stake, "25"));

        var keys = Keys(cut.Find(".waction .keyrow"));
        Assert.All(keys, k => Assert.NotNull(k.GetAttribute("data-wager-key")));
        Assert.Equal("log", keys[^1].GetAttribute("data-wager-key"));
        Assert.Contains("desk-btn--filled", keys[^1].ClassName);
        Assert.Equal($"Log {25m:C0}", keys[^1].TextContent.Trim());
        Assert.Single(cut.FindAll(".waction .desk-btn--filled"));
        Assert.Contains(keys, k => k.GetAttribute("data-wager-key") == "slip");
    }

    [Fact]
    public void The_slips_log_is_the_wager_key_filled_and_last()
    {
        Slip.Add(Item());
        var cut = RenderComponent<SlipPanel>();

        var keys = Keys(cut.Find(".waction .keyrow"));
        Assert.Equal("log", keys[^1].GetAttribute("data-wager-key"));
        Assert.Contains("desk-btn--filled", keys[^1].ClassName);
        Assert.StartsWith("Log 1 ready", keys[^1].TextContent.Trim());
    }

    [Fact]
    public void An_empty_slips_door_is_the_place_wager_key()
    {
        var cut = RenderComponent<SlipPanel>();

        var key = cut.Find(".empty__action button");
        Assert.Equal("place", key.GetAttribute("data-wager-key"));
        Assert.Equal("Place wager", key.TextContent.Trim());
    }
}
