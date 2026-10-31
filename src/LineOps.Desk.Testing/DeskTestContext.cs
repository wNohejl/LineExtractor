using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LineOps.Desk.Testing;

/// <summary>
/// The bench every Desk render test sits on — the Desk's own, and an application's.
///
/// Desk primitives wrap MudBlazor, and MudBlazor components resolve services out of the
/// container and reach for JS the moment they render. Registering both here means a test
/// states only what it is checking — a role, an aria attribute, a class, a callback — and
/// never the plumbing that got the component onto the page.
///
/// JSInterop is loose on purpose: the desk's own modules (desk-glide and friends) are
/// browser behaviour, not markup semantics, and a test that had to stub each import would
/// break every time a component picked up an animation.
///
/// An application's bench derives from this one and registers its own brand, clock and
/// catalogue in its constructor: the container answers with the last registration, so the
/// application's win and everything else here stays.
/// </summary>
public abstract class DeskTestContext : TestContext
{
    protected DeskTestContext()
    {
        Services.AddMudServices();

        // DeskChart asks which desk is showing, because a chart's series colours reach
        // MudBlazor's SVG renderer in C# and cannot be re-themed by a token block. Left
        // uninitialised on purpose: a service that has never been told otherwise reports
        // the dark desk, which is what every render assertion here was written against.
        Services.AddScoped<LineOps.Desk.Theming.ThemeService>();

        // The desk asks its host three things — what it is called, what time it keeps and what
        // it can open — and a render test should not have to say any of them. These are the
        // answers of a host that says nothing in particular: the Desk's own UTC clock, and a
        // catalogue of three windows with no application behind them.
        Services.AddSingleton(new LineOps.Desk.DeskBrand("DESK", "TEST", "test desk"));
        Services.AddSingleton(LineOps.Desk.DeskClock.Utc);
        Services.AddSingleton<LineOps.Desk.IWindowCatalog, FakeWindowCatalog>();
        Services.AddSingleton<LineOps.Desk.Windowing.DeskSignals>();
        // Who is at the desk: one person who may do everything, unless a test registers a
        // visitor (or a gated desk) after this.
        Services.AddSingleton<LineOps.Desk.IDeskAccess>(LineOps.Desk.OpenDeskAccess.Instance);
        Services.AddOptions();
        Services.AddScoped<LineOps.Desk.Windowing.WindowManager>();
        Services.AddScoped<LineOps.Desk.Windowing.DeskFollowUps>();
        // Every menu sits in a popover, and a popover asks which one is open so it can close it.
        Services.AddScoped<LineOps.Desk.Primitives.DeskPopovers>();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
