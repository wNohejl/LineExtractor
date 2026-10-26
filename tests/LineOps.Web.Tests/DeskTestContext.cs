using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The bench every Desk render test sits on.
///
/// Desk primitives wrap MudBlazor, and MudBlazor components resolve services out of the
/// container and reach for JS the moment they render. Registering both here means a test
/// states only what it is checking — a role, an aria attribute, a class, a callback — and
/// never the plumbing that got the component onto the page.
///
/// JSInterop is loose on purpose: the desk's own modules (desk-glide and friends) are
/// browser behaviour, not markup semantics, and a test that had to stub each import would
/// break every time a component picked up an animation.
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

        // The desk asks its host two things — what it is called and what it can open — and
        // a render test should not have to say either. The application's own catalogue is
        // the honest answer for the second: these tests read its keys by name.
        Services.AddSingleton(new LineOps.Desk.DeskBrand("LINE", "OPS", "test desk"));
        Services.AddSingleton(new LineOps.Desk.DeskClock(
            LineOps.Core.Analytics.LeagueClock.Zone, LineOps.Web.Components.DisplayTime.Label));
        Services.AddSingleton<LineOps.Desk.IWindowCatalog, LineOps.Web.Windowing.AppWindowCatalog>();
        Services.AddSingleton<LineOps.Desk.Windowing.DeskSignals>();
        // Who is at the desk: the operator, as on the laptop, unless a test registers a visitor
        // after this. Options because the Window manager asks how access is configured.
        Services.AddSingleton<LineOps.Desk.IDeskAccess>(LineOps.Desk.OpenDeskAccess.Instance);
        Services.AddOptions();
        Services.AddScoped<LineOps.Desk.Windowing.WindowManager>();
        Services.AddScoped<LineOps.Desk.Windowing.DeskFollowUps>();
        // Every menu sits in a popover, and a popover asks which one is open so it can close it.
        Services.AddScoped<LineOps.Desk.Primitives.DeskPopovers>();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
