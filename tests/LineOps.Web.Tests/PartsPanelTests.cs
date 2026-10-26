using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The parts bin teaches the shipped system, so what it draws is what a panel should copy: the
/// grouped form rather than a boxed one, no Filled destructive key, the toolbar's segment and
/// search field, and a type ramp whose every token exists.
/// </summary>
public class PartsPanelTests : DeskTestContext
{
    public PartsPanelTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
    }

    /// <summary>The anatomy's form is the grouped form: labelled rows in a fill, not a field in a box.</summary>
    [Fact]
    public void The_anatomy_form_is_a_grouped_form()
    {
        var cut = RenderComponent<PartsPanel>();

        var form = cut.Find(".pform.pform--grouped");
        var rows = form.QuerySelectorAll(".wgroup > .wgroup__row--form");

        Assert.Equal(["Book", "Price", "Note"], rows.Select(r => r.QuerySelector(".wgroup__label")!.TextContent.Trim()));
        Assert.All(form.QuerySelectorAll("input, select"), control => Assert.False(
            string.IsNullOrEmpty(control.GetAttribute("aria-label")), $"{control.OuterHtml} has no name"));
    }

    /// <summary>The one prominent key is never the one that destroys.</summary>
    [Fact]
    public void No_key_is_both_filled_and_destructive()
    {
        var cut = RenderComponent<PartsPanel>();

        Assert.Empty(cut.FindAll(".desk-btn--filled.desk-btn--destructive"));
    }

    /// <summary>The anatomy's toolbar is the one list windows draw: a league segment and a search field.</summary>
    [Fact]
    public void The_anatomy_toolbar_uses_the_shipped_controls()
    {
        var cut = RenderComponent<PartsPanel>();

        var toolbar = cut.Find(".ptoolbar");
        Assert.NotNull(toolbar.QuerySelector("[role='radiogroup'][aria-label='Sport']"));
        Assert.NotNull(toolbar.QuerySelector("[role='search'] input[placeholder='Search players']"));
        Assert.Null(toolbar.QuerySelector("select"));
    }

    /// <summary>
    /// The type ramp names tokens the stylesheet defines. It listed --text-title and --text-callout
    /// after both were gone, so its samples fell back to the body size.
    /// </summary>
    [Fact]
    public void Every_type_style_shown_is_defined()
    {
        var cut = RenderComponent<PartsPanel>();
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "lineops.css"));

        var tokens = cut.FindAll(".parts-key")
            .Select(k => k.TextContent.Trim())
            .Where(t => t.StartsWith("--text-"))
            .ToList();

        Assert.Contains("--text-body", tokens);
        Assert.All(tokens, token => Assert.Contains(token + ":", css));
    }
}
