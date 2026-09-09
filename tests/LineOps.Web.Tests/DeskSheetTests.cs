using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// A sheet is for a task that is self-contained and whose only goal is completing it.
/// These tests check the frame it puts around such a task — title, body, footer, and a
/// way out — not the task itself.
/// </summary>
public class DeskSheetTests : DeskTestContext
{
    [Fact]
    public void Renders_its_title_and_body()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p>body</p>"))));

        Assert.Contains("New wager", cut.Markup);
        Assert.Contains("body", cut.Markup);
    }

    [Fact]
    public void Carries_the_sheet_class_so_css_can_find_it()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        Assert.Contains("desk-sheet", cut.Markup);
    }

    /// <summary>
    /// The surface class has to land on Mud's own dialog element, not on something inside it.
    /// The desk's material, radius, shadow and entrance are all declared on `.mud-dialog.desk-sheet`
    /// so they out-specify the bridge's plain `.mud-dialog`; if the class slid onto an inner
    /// wrapper instead, every one of those rules would silently stop applying.
    /// </summary>
    [Fact]
    public void The_sheet_class_lands_on_muds_own_dialog_surface()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        var surface = cut.Find(".mud-dialog");

        Assert.Contains("desk-sheet", surface.GetAttribute("class"));
    }

    [Fact]
    public void A_subtitle_is_optional()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        Assert.DoesNotContain("desk-sheet__sub", cut.Markup);
    }

    [Fact]
    public void A_subtitle_renders_when_given()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.Subtitle, "Board 12"));

        Assert.Contains("Board 12", cut.Markup);
    }

    [Fact]
    public void The_footer_holds_the_tasks_own_buttons()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Place</button>"))));

        Assert.Contains("Place", cut.Markup);
    }

    /// <summary>
    /// The way out is not optional. A sheet that cannot be cancelled is a trap, and
    /// "help people recover from mistakes" is the whole reason modality is allowed here.
    /// </summary>
    [Fact]
    public void Cancelling_reports_it()
    {
        var cancelled = false;

        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        cut.Find(".desk-sheet__close").Click();

        Assert.True(cancelled);
    }

    /// <summary>
    /// Cancelling also dismisses the sheet. The callback tells the caller what happened; the
    /// dialog instance is what actually takes the modal down, and forgetting the second half
    /// leaves the operator staring at a sheet that has already reported itself closed.
    /// </summary>
    [Fact]
    public void Cancelling_dismisses_the_sheet()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        cut.Find(".desk-sheet__close").Click();

        Assert.DoesNotContain("desk-sheet__close", cut.Markup);
    }

    /// <summary>
    /// Renders a real <see cref="DeskSheet"/> through <see cref="IDialogService"/> inside a live
    /// <c>MudDialogProvider</c>, which is the only way to get the genuine cascaded dialog instance
    /// rather than a stand-in. The provider is what is returned, so assertions read the whole
    /// modal surface — Mud's element included.
    /// </summary>
    private IRenderedFragment RenderInDialog(Action<ComponentParameterCollectionBuilder<DeskSheet>> parameters)
    {
        var provider = RenderComponent<MudDialogProvider>();
        var service = Services.GetRequiredService<IDialogService>();

        var builder = new ComponentParameterCollectionBuilder<DeskSheet>();
        parameters(builder);

        var dialogParameters = new DialogParameters();

        foreach (var p in builder.Build())
            dialogParameters.Add(p.Name!, p.Value);

        // The show is observed rather than discarded: a fire-and-forget task would swallow any
        // failure inside ShowAsync into an unobserved exception, and the test would go green on
        // a sheet that never rendered. Waiting on the markup is what makes it synchronous here.
        Task<IDialogReference>? shown = null;

        provider.InvokeAsync(() => shown = service.ShowAsync<DeskSheet>(string.Empty, dialogParameters));

        provider.WaitForState(() => shown is { IsCompleted: true });
        shown!.GetAwaiter().GetResult();

        return provider;
    }
}
