using AngleSharp.Dom;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests.Support;

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

    /// <summary>
    /// How long a wait on a rendered state may take before it fails: an upper bound, not a pause.
    /// bUnit re-checks on every render, so a passing wait returns as soon as the state is drawn.
    /// Under a loaded full-solution run a press's render can land after the press returns (even a
    /// plain handler's), and the one-second default was shorter than the game search's 200ms
    /// debounce and the reads behind it took there.
    /// </summary>
    protected static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

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
