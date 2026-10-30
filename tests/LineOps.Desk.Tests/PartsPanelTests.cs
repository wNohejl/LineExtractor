using System.Text.RegularExpressions;
using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// The parts bin teaches the shipped system, so what it draws is what a panel should copy: the
/// grouped form rather than a boxed one, no Filled destructive key, the toolbar's segment and
/// search field, and a type ramp whose every token exists. It is the Desk's own gallery, so it
/// draws only the Desk's parts and opens its sheet through the Desk.
/// </summary>
public class PartsPanelTests : DeskTestContext
{
    private readonly FakeSheets _sheets = new();

    public PartsPanelTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());
        Services.AddSingleton<IDeskSheets>(_sheets);
    }

    private static string Source() => File.ReadAllText(
        Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Windowing", "PartsPanel.razor"));

    /// <summary>The anatomy's form is the grouped form: labelled rows in a fill, not a field in a box.</summary>
    [Fact]
    public void The_anatomy_form_is_a_grouped_form()
    {
        var cut = RenderComponent<PartsPanel>();

        var form = cut.Find(".pform.pform--grouped");
        var rows = form.QuerySelectorAll(".wgroup > .wgroup__row--form");

        Assert.Equal(["Owner", "Amount", "Note"], rows.Select(r => r.QuerySelector(".wgroup__label")!.TextContent.Trim()));
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

    /// <summary>The anatomy's toolbar is the one list windows draw: a segment and a search field.</summary>
    [Fact]
    public void The_anatomy_toolbar_uses_the_shipped_controls()
    {
        var cut = RenderComponent<PartsPanel>();

        var toolbar = cut.Find(".ptoolbar");
        Assert.NotNull(toolbar.QuerySelector("[role='radiogroup'][aria-label='Kind']"));
        Assert.NotNull(toolbar.QuerySelector("[role='search'] input[placeholder='Search items']"));
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
        var css = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", "desk.css"));

        var tokens = cut.FindAll(".parts-key")
            .Select(k => k.TextContent.Trim())
            .Where(t => t.StartsWith("--text-"))
            .ToList();

        Assert.Contains("--text-body", tokens);
        Assert.All(tokens, token => Assert.Contains(token + ":", css));
    }

    /// <summary>
    /// The modal sheet opens through <see cref="IDeskSheets"/>, as every caller's should: the
    /// gallery holds no <c>IDialogService</c> and no MudBlazor options of its own. Its outcome is
    /// read off the result the handle returns.
    /// </summary>
    [Fact]
    public void The_sheet_demo_opens_through_the_desk()
    {
        var cut = RenderComponent<PartsPanel>();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Modal sheet").Click();

        Assert.Equal(typeof(DeskSheet), _sheets.Shown);
        Assert.Equal("Add a note", _sheets.Parameters[nameof(DeskSheet.Title)]);

        cut.InvokeAsync(() => _sheets.Last!.Close(DialogResult.Ok("Kickoff notes")));
        cut.WaitForAssertion(() => Assert.Contains("\"Kickoff notes\" — completed", cut.Markup));
    }

    /// <summary>The linked cell section draws the Desk's own parts: a link that names where it goes, and a tag.</summary>
    [Fact]
    public void The_linked_cell_section_shows_a_desk_link_and_a_tag()
    {
        var cut = RenderComponent<PartsPanel>();

        var section = cut.FindAll(".psection").Single(s => s.QuerySelector(".psection__head")?.TextContent.Trim() == "Display — linked cell");
        Assert.NotEmpty(section.QuerySelectorAll("button.desklink"));
        Assert.NotEmpty(section.QuerySelectorAll(".tag"));
        Assert.Empty(cut.FindAll(".price"));
    }

    /// <summary>The gallery is the Desk's: it reaches for no application data and no dialog service.</summary>
    [Fact]
    public void The_gallery_knows_no_application()
    {
        var source = Source();

        Assert.DoesNotContain("LineOps.Data", source);
        Assert.DoesNotContain("LineOps.Web", source);
        Assert.DoesNotContain("LineOps.Core", source);
        Assert.DoesNotContain("IDialogService", source);
        Assert.DoesNotContain("PriceCell", source);
    }

    /// <summary>
    /// The primitives the gallery and the README answer for: every component in Primitives whose
    /// name says it is the Desk's, and the older parts named for what they are. FollowUpLayer
    /// (a panel's plumbing) and SnippetFailure (drawn by a snippet that failed) are not parts a
    /// panel places on its own.
    /// </summary>
    private static readonly string[] NamedForWhatTheyAre =
        ["Metric", "MetricRow", "Note", "Tag", "EmptyState", "RowActions", "SnippetShell", "CheckList",
         "PanelForm", "PanelSection", "PanelTitle", "PanelToolbar"];

    private static IReadOnlyList<string> Primitives() =>
        Directory.EnumerateFiles(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Primitives"), "*.razor")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => n.StartsWith("Desk", StringComparison.Ordinal) || NamedForWhatTheyAre.Contains(n))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A part opened through a service is never written as a tag: the gallery shows it by making
    /// the call any panel would.
    /// </summary>
    private static readonly Dictionary<string, string> ShownThrough = new()
    {
        ["DeskAlert"] = "Alerts.ConfirmAsync("
    };

    /// <summary>
    /// The gallery shows every primitive. Ten were missing from it — the checkbox, the menu, the
    /// popover among them — so a panel built beside it could not find them and built its own.
    /// </summary>
    [Fact]
    public void Every_primitive_has_a_section()
    {
        var source = Source();

        var missing = Primitives()
            .Where(name => !Regex.IsMatch(source, $@"<{name}[\s/>]")
                           && !(ShownThrough.TryGetValue(name, out var call) && source.Contains(call)))
            .ToList();

        Assert.True(missing.Count == 0, "Not in the Parts bin: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The README's table names every primitive, in alphabetical order, so a reader who has the
    /// package and not the gallery still learns what exists.
    /// </summary>
    [Fact]
    public void Every_primitive_is_in_the_readme()
    {
        var readme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "README.md"));
        var rows = Regex.Matches(readme, @"^\| `(?<name>[A-Za-z]+)` \|", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();

        var missing = Primitives().Except(rows).ToList();

        Assert.True(missing.Count == 0, "Not in the README's primitives table: " + string.Join(", ", missing));
        Assert.Equal(rows.Order(StringComparer.Ordinal), rows);
    }
}
