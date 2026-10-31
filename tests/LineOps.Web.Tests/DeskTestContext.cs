using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Desk's bench (<see cref="LineOps.Desk.Testing.DeskTestContext"/>) with this application's
/// answers to what the desk asks its host: its name, its clock and what it can open. The
/// application's own catalogue is the honest answer for the last, because these tests read its keys
/// by name. Registered after the base, so these are the answers the container gives.
/// </summary>
public abstract class DeskTestContext : LineOps.Desk.Testing.DeskTestContext
{
    protected DeskTestContext()
    {
        Services.AddSingleton(new LineOps.Desk.DeskBrand("LINE", "OPS", "test desk"));
        Services.AddSingleton(new LineOps.Desk.DeskClock(
            LineOps.Core.Analytics.LeagueClock.Zone, LineOps.Web.Components.DisplayTime.Label));
        Services.AddSingleton<LineOps.Desk.IWindowCatalog, LineOps.Web.Windowing.AppWindowCatalog>();
    }
}
