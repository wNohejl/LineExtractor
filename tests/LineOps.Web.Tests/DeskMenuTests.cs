using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// The menu primitive and the pull-down built on it. The DOM half of the keyboard — focus into
/// the panel, outside click, edge flip, focus return — is popover.js and cannot run here; what
/// is pinned is everything C# decides: roles, the roving tab stop, arrows, typeahead, Escape,
/// and the rule that only one popover is open at a time.
/// </summary>
public class DeskMenuTests : DeskTestContext
{
    private IRenderedComponent<DeskMenu> Menu(params string[] labels)
        => RenderComponent<DeskMenu>(p =>
        {
            p.Add(x => x.Label, "Jobs");

            foreach (var label in labels)
                p.AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, label));
        });

    private static string? Focused(IRenderedComponent<DeskMenu> menu)
        => menu.FindAll("[role=menuitem]")
            .Single(i => i.GetAttribute("tabindex") == "0")
            .QuerySelector(".menu__label")?.TextContent;

    [Fact]
    public void Menu_items_carry_roles_and_roving_tabindex()
    {
        var menu = Menu("Pull odds", "Fetch games", "Settle");

        Assert.Equal("Jobs", menu.Find("[role=menu]").GetAttribute("aria-label"));

        var items = menu.FindAll("[role=menuitem]");
        Assert.Equal(3, items.Count);
        Assert.Single(items, i => i.GetAttribute("tabindex") == "0");
        Assert.All(items, i => Assert.Equal("button", i.GetAttribute("type")));
    }

    [Fact]
    public void Arrow_down_moves_focus_and_wraps()
    {
        var menu = Menu("Pull odds", "Fetch games", "Settle");

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("Fetch games", Focused(menu));

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("Pull odds", Focused(menu));
    }

    [Fact]
    public void Arrow_up_wraps_and_home_and_end_jump()
    {
        var menu = Menu("Pull odds", "Fetch games", "Settle");

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("Settle", Focused(menu));

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("Pull odds", Focused(menu));

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("Settle", Focused(menu));
    }

    [Fact]
    public void Typing_a_letter_jumps_to_the_matching_item()
    {
        var menu = Menu("Pull odds", "Fetch games", "Settle");

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "f" });

        Assert.Equal("Fetch games", Focused(menu));
    }

    [Fact]
    public void Disabled_item_states_its_reason()
    {
        var menu = RenderComponent<DeskMenu>(p => p
            .Add(x => x.Label, "Jobs")
            .AddChildContent<DeskMenuItem>(i => i
                .Add(x => x.Label, "Pull odds")
                .Add(x => x.Description, "Every book, every market.")
                .Add(x => x.Disabled, true)
                .Add(x => x.DisabledReason, "Needs an API key")));

        var item = menu.Find("[role=menuitem]");

        Assert.Equal("true", item.GetAttribute("aria-disabled"));
        Assert.Equal("Needs an API key", item.GetAttribute("title"));
        Assert.Contains("Needs an API key", item.TextContent);
        Assert.DoesNotContain("Every book", item.TextContent);
    }

    [Fact]
    public void A_disabled_item_does_not_run_its_command()
    {
        var ran = false;

        var menu = RenderComponent<DeskMenu>(p => p
            .Add(x => x.Label, "Jobs")
            .AddChildContent<DeskMenuItem>(i => i
                .Add(x => x.Label, "Pull odds")
                .Add(x => x.Disabled, true)
                .Add(x => x.OnClick, () => ran = true)));

        menu.Find("[role=menuitem]").Click();

        Assert.False(ran);
    }

    [Fact]
    public void A_radio_menu_marks_the_checked_item()
    {
        var menu = RenderComponent<DeskMenu>(p => p
            .Add(x => x.Label, "Settle")
            .Add(x => x.Radio, true)
            .AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, "Win").Add(x => x.Checked, true))
            .AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, "Loss")));

        var radios = menu.FindAll("[role=menuitemradio]");

        Assert.Equal(2, radios.Count);
        Assert.Equal("true", radios[0].GetAttribute("aria-checked"));
        Assert.Equal("false", radios[1].GetAttribute("aria-checked"));
    }

    [Fact]
    public void Pull_down_key_announces_the_menu()
    {
        var cut = RenderComponent<DeskPullDown>(p => p
            .Add(x => x.Label, "Pull data")
            .AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, "Pull odds")));

        var key = cut.Find("button.desk-btn");
        Assert.Equal("menu", key.GetAttribute("aria-haspopup"));
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[role=menu]"));

        key.Click();

        Assert.Equal("true", cut.Find("button.desk-btn").GetAttribute("aria-expanded"));
        Assert.Single(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void Choosing_an_item_runs_it_and_closes_the_menu()
    {
        var ran = false;

        var cut = RenderComponent<DeskPullDown>(p => p
            .Add(x => x.Label, "Pull data")
            .AddChildContent<DeskMenuItem>(i => i
                .Add(x => x.Label, "Pull odds")
                .Add(x => x.OnClick, () => ran = true)));

        cut.Find("button.desk-btn").Click();
        cut.Find("[role=menuitem]").Click();

        Assert.True(ran);
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void Opening_a_second_menu_closes_the_first()
    {
        var cut = Render(b =>
        {
            PullDown(b, 0, "Pull data");
            PullDown(b, 10, "Settle");
        });

        var keys = cut.FindAll("button.desk-btn");
        keys[0].Click();
        Assert.Single(cut.FindAll("[role=menu]"));

        cut.FindAll("button.desk-btn")[1].Click();

        Assert.Single(cut.FindAll("[role=menu]"));
        var after = cut.FindAll("button.desk-btn");
        Assert.Equal("false", after[0].GetAttribute("aria-expanded"));
        Assert.Equal("true", after[1].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Escape_closes_and_the_key_regains_focus()
    {
        var cut = RenderComponent<DeskPullDown>(p => p
            .Add(x => x.Label, "Pull data")
            .AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, "Pull odds")));

        cut.Find("button.desk-btn").Click();
        cut.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=menu]"));

        var key = cut.Find("button.desk-btn");
        Assert.Equal("true", key.GetAttribute("data-popover-return"));
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_bound_pull_down_opens_when_its_owner_says_so()
    {
        var cut = RenderComponent<DeskPullDown>(p => p
            .Add(x => x.Label, "Pull data")
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, (bool _) => { })
            .AddChildContent<DeskMenuItem>(i => i.Add(x => x.Label, "Pull odds")));

        Assert.Single(cut.FindAll("[role=menu]"));
    }

    private static void PullDown(RenderTreeBuilder b, int seq, string label)
    {
        b.OpenComponent<DeskPullDown>(seq);
        b.AddAttribute(seq + 1, nameof(DeskPullDown.Label), label);
        b.AddAttribute(seq + 2, nameof(DeskPullDown.ChildContent), (RenderFragment)(c =>
        {
            c.OpenComponent<DeskMenuItem>(0);
            c.AddAttribute(1, nameof(DeskMenuItem.Label), label + " now");
            c.CloseComponent();
        }));
        b.CloseComponent();
    }
}
