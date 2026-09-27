using LineOps.Desk.Primitives;
using LineOps.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests.Support;

/// <summary>The desk bench plus what a window that logs wagers needs: the data seam, the slip, toasts and confirms.</summary>
public abstract class WagerTestContext : DeskTestContext
{
    protected FakeWagerData Data { get; } = new();
    protected FakeAlerts Alerts { get; } = new();

    protected WagerTestContext()
    {
        Services.AddSingleton<IWagerData>(Data);
        Services.AddScoped<WagerSlip>();
        Services.AddScoped<DeskToasts>();
        Services.AddSingleton<IDeskAlerts>(Alerts);
    }

    protected WagerSlip Slip => Services.GetRequiredService<WagerSlip>();
}
