using Bunit;
using XLines.Desk.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace XLines.Desk.Tests;

/// <summary>
/// The pop-up: a key whose face is the current choice, over a radio menu of rich rows. What
/// popover.js does in a browser (the outside press, the flip, focus back on the key) cannot run
/// here; what C# decides is pinned: the key's face and name, the rows' parts, the roles, where
/// the Tab stop opens, the arrows and typeahead, Escape and a choice closing it, commands kept
/// apart from options, and the two degraded forms.
/// </summary>
public class DeskPopUpTests : DeskTestContext
{
    private static readonly IReadOnlyList<DeskPopUpOption<string>> Plans =
    [
        new("studio", "Studio") { Figure = "$24", Mark = "Best", Detail = "Per seat", DetailFigure = "+20%", DetailFigureState = DeskState.Positive },
        new("solo", "Personal") { Figure = "$12", DetailFigure = "−5%", DetailFigureState = DeskState.Warning },
        new("month", "Monthly") { Figure = "$30", Spoken = "Monthly, thirty dollars" },
        new("legacy", "Legacy") { Figure = "$18", Disabled = true, DisabledReason = "Closed to new accounts." }
    ];

    private string? _chosen;

    private IRenderedComponent<DeskPopUp<string>> PopUp(string? value = "solo", IReadOnlyList<DeskPopUpOption<string>>? options = null,
        RenderFragment? commands = null, bool disabled = false, string? footer = null)
        => RenderComponent<DeskPopUp<string>>(p =>
        {
            p.Add(x => x.Label, "Plan");
            p.Add(x => x.Options, options ?? Plans);
            p.Add(x => x.Value, value);
            p.Add(x => x.ValueChanged, v => _chosen = v);
            p.Add(x => x.Disabled, disabled);
            if (footer is not null) p.Add(x => x.Footer, footer);
            if (commands is not null) p.Add(x => x.Commands, commands);
            if (disabled) p.Add(x => x.DescribedBy, "why");
        });

    private static string? TabStop(IRenderedFragment cut)
        => cut.FindAll("[role=menuitemradio]").Single(i => i.GetAttribute("tabindex") == "0")
            .QuerySelector(".menu__label")?.TextContent;

    [Fact]
    public void The_key_shows_the_choice_and_says_it_opens_a_menu()
    {
        var cut = PopUp();

        var key = cut.Find("button.popup__key");
        Assert.Equal("menu", key.GetAttribute("aria-haspopup"));
        Assert.Equal("false", key.GetAttribute("aria-expanded"));
        Assert.Equal("true", key.GetAttribute("data-popover-return"));
        Assert.Equal("Personal", key.QuerySelector(".popup__label")!.TextContent);
        Assert.Equal("$12", key.QuerySelector(".popup__figure")!.TextContent);
        Assert.Equal("Plan, Personal $12", key.GetAttribute("aria-label"));
        Assert.NotNull(key.QuerySelector(".popup__chevron"));
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void The_key_carries_the_chosen_mark_and_can_leave_its_figure_to_the_host()
    {
        var cut = RenderComponent<DeskPopUp<string>>(p => p
            .Add(x => x.Label, "Plan")
            .Add(x => x.Options, Plans)
            .Add(x => x.Value, "studio")
            .Add(x => x.ShowFigure, false));

        var key = cut.Find("button.popup__key");
        Assert.Equal("Best", key.QuerySelector(".popup__mark")!.TextContent);
        Assert.Null(key.QuerySelector(".popup__figure"));
        Assert.Equal("Plan, Studio $24 Best", key.GetAttribute("aria-label"));
    }

    [Fact]
    public void Open_it_lists_every_option_with_its_figure_mark_and_detail()
    {
        var cut = PopUp(footer: "Prices per month.");
        cut.Find("button.popup__key").Click();

        Assert.Equal("true", cut.Find("button.popup__key").GetAttribute("aria-expanded"));
        Assert.Equal("Plan", cut.Find("[role=menu]").GetAttribute("aria-label"));

        var rows = cut.FindAll("[role=menuitemradio]");
        Assert.Equal(4, rows.Count);
        Assert.Equal("$24", rows[0].QuerySelector(".menu__figure")!.TextContent);
        Assert.Equal("Best", rows[0].QuerySelector(".menu__mark")!.TextContent.Trim());
        Assert.Equal("Per seat", rows[0].QuerySelector(".menu__desc")!.TextContent);
        Assert.Contains("menu__subfigure--positive", rows[0].QuerySelector(".menu__subfigure")!.ClassList);
        Assert.Contains("menu__subfigure--warning", rows[1].QuerySelector(".menu__subfigure")!.ClassList);
        Assert.Null(rows[1].QuerySelector(".menu__mark"));
        Assert.Equal("Monthly, thirty dollars", rows[2].GetAttribute("aria-label"));
        Assert.Equal("true", rows[3].GetAttribute("aria-disabled"));
        Assert.Contains("Closed to new accounts.", rows[3].TextContent);
        Assert.Equal("Prices per month.", cut.Find(".popup__foot").TextContent);
    }

    [Fact]
    public void The_chosen_option_is_checked_and_holds_the_tab_stop()
    {
        var cut = PopUp();
        cut.Find("button.popup__key").Click();

        var rows = cut.FindAll("[role=menuitemradio]");
        Assert.Equal(["false", "true", "false", "false"], rows.Select(r => r.GetAttribute("aria-checked")));
        Assert.Equal("Personal", TabStop(cut));
    }

    [Fact]
    public void Arrows_home_end_and_typeahead_move_from_the_choice()
    {
        var cut = PopUp();
        cut.Find("button.popup__key").Click();
        var menu = () => cut.Find("[role=menu]");

        menu().KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("Monthly", TabStop(cut));

        menu().KeyDown(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal("Studio", TabStop(cut));

        menu().KeyDown(new KeyboardEventArgs { Key = "End" });
        Assert.Equal("Legacy", TabStop(cut));

        menu().KeyDown(new KeyboardEventArgs { Key = "p" });
        Assert.Equal("Personal", TabStop(cut));
    }

    [Fact]
    public void A_choice_closes_the_list_and_reports_the_value()
    {
        var cut = PopUp();
        cut.Find("button.popup__key").Click();

        cut.FindAll("[role=menuitemradio]")[2].Click();

        Assert.Equal("month", _chosen);
        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Equal("false", cut.Find("button.popup__key").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_dimmed_option_cannot_be_chosen()
    {
        var cut = PopUp();
        cut.Find("button.popup__key").Click();

        cut.FindAll("[role=menuitemradio]")[3].Click();

        Assert.Null(_chosen);
    }

    [Fact]
    public void Escape_closes_the_list()
    {
        var cut = PopUp();
        cut.Find("button.popup__key").Click();

        cut.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void Commands_follow_the_options_behind_a_hairline_and_are_not_choices()
    {
        var ran = false;
        RenderFragment commands = b =>
        {
            b.OpenComponent<DeskMenuItem>(0);
            b.AddAttribute(1, nameof(DeskMenuItem.Label), "Other amount…");
            b.AddAttribute(2, nameof(DeskMenuItem.Command), true);
            b.AddAttribute(3, nameof(DeskMenuItem.OnClick), EventCallback.Factory.Create(this, () => ran = true));
            b.CloseComponent();
        };

        var cut = PopUp(commands: commands);
        cut.Find("button.popup__key").Click();

        Assert.Single(cut.FindAll("[role=menu] .menu__sep"));
        var command = Assert.Single(cut.FindAll("[role=menuitem]"));
        Assert.Null(command.GetAttribute("aria-checked"));
        Assert.Equal(4, cut.FindAll("[role=menuitemradio]").Count);

        command.Click();

        Assert.True(ran);
        Assert.Null(_chosen);
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void One_option_and_nothing_else_is_a_statement_not_a_key()
    {
        var cut = PopUp(value: "eu", options: [new("eu", "Frankfurt") { Figure = "18 ms" }]);

        Assert.Empty(cut.FindAll("button"));
        var statement = cut.Find(".popup--statement");
        Assert.Contains("Frankfurt", statement.TextContent);
        Assert.Contains("18 ms", statement.TextContent);
        Assert.Equal("Plan: ", statement.QuerySelector(".sr-only")!.TextContent);
    }

    [Fact]
    public void One_option_with_a_command_is_still_a_choice()
    {
        RenderFragment commands = b =>
        {
            b.OpenComponent<DeskMenuItem>(0);
            b.AddAttribute(1, nameof(DeskMenuItem.Label), "Other…");
            b.AddAttribute(2, nameof(DeskMenuItem.Command), true);
            b.CloseComponent();
        };

        var cut = PopUp(value: "eu", options: [new("eu", "Frankfurt")], commands: commands);

        Assert.NotNull(cut.Find("button.popup__key"));
    }

    [Fact]
    public void Disabled_it_keeps_its_place_says_why_and_never_opens()
    {
        var cut = PopUp(value: null, disabled: true);

        var key = cut.Find("button.popup__key");
        Assert.True(key.HasAttribute("disabled"));
        Assert.Equal("why", key.GetAttribute("aria-describedby"));
        Assert.Equal("Plan", key.QuerySelector(".popup__label")!.TextContent);
        Assert.Null(key.GetAttribute("title"));
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void A_comparer_matches_a_value_written_in_another_case()
    {
        var cut = RenderComponent<DeskPopUp<string>>(p => p
            .Add(x => x.Label, "Plan")
            .Add(x => x.Options, Plans)
            .Add(x => x.Value, "SOLO")
            .Add(x => x.Comparer, StringComparer.OrdinalIgnoreCase));

        Assert.Equal("Personal", cut.Find(".popup__label").TextContent);
    }
}
