using Bunit;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Desk.Tests;

/// <summary>
/// A panel that throws stops inside its own window.
///
/// <para>
/// An exception in one panel's lifecycle used to reach the circuit, and Blazor Server answers an
/// unhandled exception by ending the circuit: every window on the desk went with it, half-typed
/// entries included. The window shell now holds an error boundary, so the fault is drawn as that
/// window's error state with a key to build the panel again, and nothing else on the desk moves.
/// </para>
/// </summary>
public class WindowErrorBoundaryTests : DeskTestContext
{
    /// <summary>
    /// Burns once: the first panel built throws, every one after renders. A service rather than a
    /// static, so no other test sharing the process can find it already spent.
    /// </summary>
    private sealed class Fuse
    {
        public bool Spent { get; set; }
    }

    private sealed class Explodes : ComponentBase
    {
        [Inject] private Fuse Fuse { get; set; } = default!;

        // Present so the window can turn to another page of the same view.
        [Parameter] public string? Subject { get; set; }

        protected override void OnInitialized()
        {
            if (Fuse.Spent)
                return;

            Fuse.Spent = true;
            throw new InvalidOperationException("boom");
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "class", "ok");
            builder.AddContent(2, "fine");
            builder.CloseElement();
        }
    }

    // A subject window that is not a singleton follows its links in place, which is what lets a
    // test turn its page.
    private static readonly WindowDefinition Volatile = new()
    {
        Key = "volatile",
        Title = "Volatile",
        Icon = "",
        ComponentType = typeof(Explodes),
        RequiresSubject = true,
        Singleton = false
    };

    private (IRenderedComponent<AppWindow> Window, WindowManager Manager, WindowInstance Instance) Render()
    {
        Services.AddSingleton(new Fuse());
        Services.AddSingleton<IDeskAlerts>(new FakeAlerts());

        var manager = Services.GetRequiredService<WindowManager>();
        var instance = manager.Open(Volatile, new Dictionary<string, object> { ["Subject"] = "first" });
        var window = RenderComponent<AppWindow>(p => p.Add(x => x.Window, instance));

        return (window, manager, instance);
    }

    [Fact]
    public void A_panel_that_throws_shows_the_error_state_and_try_again_rebuilds_it()
    {
        var (window, _, _) = Render();

        var error = window.Find(".win__body .empty--error");
        Assert.Equal("Volatile stopped. Nothing you typed elsewhere is lost.",
            error.TextContent.Replace("Try again", "").Trim());
        Assert.DoesNotContain("boom", window.Markup);

        var retry = window.Find(".win__body .empty--error button");
        Assert.Equal("Try again", retry.TextContent.Trim());
        retry.Click();

        Assert.NotNull(window.Find(".win__body .ok"));
        Assert.Empty(window.FindAll(".win__body .empty--error"));
    }

    [Fact]
    public void The_window_bar_stays_working_while_its_panel_is_stopped()
    {
        var (window, _, _) = Render();

        Assert.NotNull(window.Find(".win__body .empty--error"));
        Assert.Equal("Volatile", window.Find(".win__title").TextContent);
        Assert.NotNull(window.Find(".win__ctl--close"));
    }

    [Fact]
    public void Turning_the_page_builds_a_fresh_boundary()
    {
        var (window, manager, instance) = Render();
        Assert.NotNull(window.Find(".win__body .empty--error"));

        manager.Follow(instance.Id, Volatile, new Dictionary<string, object> { ["Subject"] = "second" });
        window.Render();

        Assert.NotNull(window.Find(".win__body .ok"));
        Assert.Empty(window.FindAll(".win__body .empty--error"));
    }
}
