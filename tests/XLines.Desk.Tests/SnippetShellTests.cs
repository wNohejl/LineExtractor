using Bunit;
using XLines.Desk.Primitives;
using XLines.Desk.Tests.Support;

namespace XLines.Desk.Tests;

/// <summary>
/// The frame around an inline answer, on the section-card contract (ADR 0032): by default the
/// shell is one card with its name in the card's header bar and the escape as the bar's one
/// trailing key; around content that draws cards of its own, an unframed pane header in the same
/// voice — never a card around cards.
/// </summary>
public class SnippetShellTests : DeskTestContext
{
    private IRenderedComponent<SnippetShell> Shell(bool cards = false, bool open = true)
        => RenderComponent<SnippetShell>(p =>
        {
            p.Add(s => s.Title, "Last five")
             .Add(s => s.Note, "5 of 12")
             .Add(s => s.OpenLabel, "Full history")
             .Add(s => s.Cards, cards)
             .AddChildContent("<div class=\"rows\">rows</div>");
            if (open)
                p.Add(s => s.OnOpen, () => { });
        });

    [Fact]
    public void By_default_the_shell_is_one_section_card_with_its_name_in_the_bar()
    {
        var cut = Shell();

        var section = cut.Find("section.snippet.wsection[role=region]");
        Assert.Equal("Last five", section.GetAttribute("aria-label"));

        var title = cut.Find("section.wsection > .wgroup > .wgroup__bar > h3.wgroup__title");
        Assert.Equal("Last five", title.TextContent);
        Assert.Single(cut.FindAll(".wgroup"));
        Assert.Contains("rows", cut.Find(".wgroup > .snippet__body").TextContent);
    }

    [Fact]
    public void The_escape_is_the_bar_s_one_trailing_key()
    {
        var keys = Shell().Find(".wgroup__bar > .wgroup__keys");

        var open = keys.QuerySelector("button.snippet__open")!;
        Assert.Equal("Full history", open.QuerySelector(".snippet__open-word")!.TextContent);
        Assert.Single(keys.Children);
    }

    [Fact]
    public void The_note_sits_beside_the_title_and_no_symbol_is_drawn()
    {
        var cut = Shell();

        Assert.Equal("5 of 12", cut.Find(".wgroup__bar > .snippet__note").TextContent);
        Assert.Empty(cut.FindAll(".snippet__icon"));
    }

    [Fact]
    public void With_nowhere_further_to_go_the_bar_holds_no_key()
    {
        var cut = Shell(open: false);

        Assert.Empty(cut.FindAll(".wgroup__keys"));
        Assert.Empty(cut.FindAll(".snippet__open"));
    }

    /// <summary>A card inside a card is forbidden, so over content of cards the shell draws none.</summary>
    [Fact]
    public void Over_cards_of_its_own_it_is_an_unframed_pane_header_in_the_same_voice()
    {
        var cut = Shell(cards: true);

        Assert.Empty(cut.FindAll(".wgroup"));
        var section = cut.Find("section.snippet.snippet--panes");
        Assert.Equal("Last five", cut.Find("section.snippet--panes > .snippet__bar > h3.wgroup__title").TextContent);
        Assert.NotNull(section.QuerySelector(":scope > .snippet__bar .wgroup__keys .snippet__open"));
        Assert.Contains("rows", section.QuerySelector(":scope > .snippet__body")!.TextContent);
    }

    [Fact]
    public void The_pane_header_has_no_fill_and_no_edge()
    {
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "XLines.Desk", "wwwroot", "css", "desk.css"));
        var rule = System.Text.RegularExpressions.Regex.Match(css, @"\.snippet--panes > \.snippet__bar\s*\{([^}]*)\}").Groups[1].Value;

        Assert.Contains("background: none", rule);
        Assert.Contains("border-bottom: 0", rule);
    }
}
