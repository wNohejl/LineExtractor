using Bunit;
using LineOps.Desk.Windowing;
using LineOps.Desk.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Desk.Tests;

/// <summary>
/// Ctrl+K: one field that reaches anything. Before anything is typed it is a launcher over the
/// windows; typing asks the application's sources after a pause; arrows and Enter pick; Escape
/// closes.
/// </summary>
public class CommandPaletteTests : SampleDeskTestContext
{
    private sealed class FakeSearch : IDeskSearch
    {
        public int Opened;
        public int Calls;

        public Task<IReadOnlyList<DeskSearchResult>> SearchAsync(string text, CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<DeskSearchResult> results =
                text.Contains("acme", StringComparison.OrdinalIgnoreCase)
                    ? [new DeskSearchResult("Customers", "Acme Corporation", "Customer · ACME", Icons.Material.Filled.Business, () => Opened++)]
                // A person whose name starts as a window's title does.
                : text.Contains("not", StringComparison.OrdinalIgnoreCase)
                    ? [new DeskSearchResult("People", "Notary Public", "Clerk", Icons.Material.Filled.Person, () => Opened++)]
                    : [];
            return Task.FromResult(results);
        }
    }

    private readonly FakeSearch _search = new();

    public CommandPaletteTests() => Services.AddSingleton<IDeskSearch>(_search);

    private WindowManager Manager => Services.GetRequiredService<WindowManager>();

    private IRenderedComponent<CommandPalette> Open()
    {
        var palette = RenderComponent<CommandPalette>();
        Manager.RequestPalette();
        palette.WaitForState(() => palette.FindAll("[role=dialog]").Count == 1);
        return palette;
    }

    [Fact]
    public void It_is_closed_until_asked_for()
        => Assert.Empty(RenderComponent<CommandPalette>().FindAll("[role=dialog]"));

    [Fact]
    public void Opened_empty_it_lists_the_windows_that_open_on_nothing()
    {
        var palette = Open();
        var titles = palette.FindAll("[role=option]").Select(o => o.TextContent).ToList();

        Assert.Contains(titles, t => t.Contains(SampleCatalog.Find(SampleCatalog.Notes).Title));

        // Every window that opens on nothing, and every workspace — and no window that needs a
        // subject, which opened cold would have nothing to show.
        var expected = Manager.Catalog.All.Count(d => !d.RequiresSubject) + Manager.Catalog.Workspaces.Count;
        Assert.Equal(expected, titles.Count);
    }

    [Fact]
    public void Typing_asks_the_applications_sources_and_their_results_lead()
    {
        var palette = Open();

        palette.Find("input").Input("acme");

        palette.WaitForAssertion(() =>
            Assert.StartsWith("Acme Corporation", palette.FindAll("[role=option]")[0].TextContent.Trim()),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_window_named_by_what_was_typed_leads_the_applications_results()
    {
        // The application has a person whose name starts as the Notes window's title does; the
        // window still comes first, and the person after it.
        var palette = Open();

        palette.Find("input").Input("not");
        palette.WaitForAssertion(() => Assert.Contains("Notary Public", palette.Markup), TimeSpan.FromSeconds(3));

        var options = palette.FindAll("[role=option]").Select(o => o.TextContent.Trim()).ToList();
        Assert.StartsWith(SampleCatalog.Find(SampleCatalog.Notes).Title, options[0]);
        Assert.StartsWith("Notary Public", options[1]);
    }

    [Fact]
    public void Enter_opens_the_highlighted_result_and_closes_the_palette()
    {
        var palette = Open();
        palette.Find("input").Input("acme");
        palette.WaitForAssertion(() => Assert.Contains("Acme Corporation", palette.Markup), TimeSpan.FromSeconds(3));

        palette.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(1, _search.Opened);
        Assert.Empty(palette.FindAll("[role=dialog]"));
    }

    [Fact]
    public void Escape_closes_without_opening_anything()
    {
        var palette = Open();

        palette.Find("input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(palette.FindAll("[role=dialog]"));
        Assert.Equal(0, _search.Opened);
    }

    [Fact]
    public void Arrows_move_the_highlight_and_wrap()
    {
        var palette = Open();
        var count = palette.FindAll("[role=option]").Count;
        var input = palette.Find("input");

        input.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal("true", palette.Find($"#palette-{count - 1}").GetAttribute("aria-selected"));
    }
}
