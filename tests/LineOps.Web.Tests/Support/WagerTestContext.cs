using AngleSharp.Dom;
using Bunit;
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

    /// <summary>The newest toast the desk raised: its words, its tone and its key.</summary>
    protected DeskToast LastToast => Services.GetRequiredService<DeskToasts>().Last!;

    /// <summary>Presses the newest toast's key — View slip, Undo — as the operator would.</summary>
    protected void PressToastKey() => LastToast.Action!.Invoke();

    /// <summary>The step with this title, in the wager window's stepper.</summary>
    protected static IElement Step(IRenderedFragment cut, string title)
        => cut.FindAll(".wstep").First(s => s.QuerySelector(".wstep__title")!.TextContent.Trim() == title);

    /// <summary>Opens a finished step again, as the operator does with Change beside its summary.</summary>
    protected static void Change(IRenderedFragment cut, string title)
        => Step(cut, title).QuerySelector(".wstep__change")!.Click();
}
