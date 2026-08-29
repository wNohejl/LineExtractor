using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// An alert is the narrowest modal there is: the operator must decide before anything
/// else happens. Its whole job is stating the consequence and offering two ways out.
/// </summary>
public class DeskAlertTests : DeskTestContext
{
    [Fact]
    public void States_its_heading_and_message()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Message, "The ingested odds stay; only the run record goes."));

        Assert.Contains("Delete this run?", cut.Markup);
        Assert.Contains("only the run record goes", cut.Markup);
    }

    [Fact]
    public void Offers_a_way_out_beside_the_confirm()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?"));

        var buttons = cut.FindAll("button");

        Assert.Equal(2, buttons.Count);
    }

    [Fact]
    public void A_single_button_alert_drops_the_cancel()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Ingest finished")
            .Add(x => x.CancelLabel, (string?)null));

        Assert.Single(cut.FindAll("button"));
    }

    /// <summary>
    /// A destructive confirm is red and is never the default. Making the dangerous
    /// button the one that answers Enter is how people delete things they meant to keep.
    /// </summary>
    [Fact]
    public void A_destructive_confirm_is_red_and_not_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Destructive, true));

        var markup = cut.Markup;

        Assert.Contains("desk-btn--destructive", markup);
        Assert.DoesNotContain("desk-alert__confirm--default", markup);
    }

    [Fact]
    public void A_normal_confirm_is_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?"));

        Assert.Contains("desk-alert__confirm--default", cut.Markup);
    }

    [Fact]
    public void Labels_can_name_the_actual_consequence()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.ConfirmLabel, "Delete run")
            .Add(x => x.CancelLabel, "Keep it"));

        Assert.Contains("Delete run", cut.Markup);
        Assert.Contains("Keep it", cut.Markup);
    }

    /// <summary>
    /// The whole point of the service is that guarding a destructive action costs one
    /// await. These run the real <see cref="DeskAlerts"/> against a live
    /// <c>MudDialogProvider</c>, so the answer travels the same path it does in the app:
    /// component to dialog instance to the caller's awaited bool.
    /// </summary>
    [Fact]
    public async Task Confirming_answers_true()
    {
        var (provider, answer) = Ask(destructive: true);

        provider.Find(".desk-alert__actions button.desk-btn--destructive").Click();

        Assert.True(await answer);
    }

    [Fact]
    public async Task Backing_out_answers_false()
    {
        var (provider, answer) = Ask();

        provider.Find(".desk-alert__actions button.desk-btn--plain").Click();

        Assert.False(await answer);
    }

    /// <summary>
    /// Escape and the backdrop never raise the component's callbacks — they go through
    /// MudBlazor's own cancel path, which returns a canceled result rather than a false
    /// one. A service that only checked <c>Data</c> would read that as a confirm, so the
    /// cancelled dialog is asserted directly.
    /// </summary>
    [Fact]
    public async Task A_dialog_dismissed_without_answering_answers_false()
    {
        var (provider, answer) = Ask();

        var instance = (IMudDialogInstance)provider.FindComponent<MudDialogContainer>().Instance;
        await provider.InvokeAsync(instance.Cancel);

        Assert.False(await answer);
    }

    /// <summary>
    /// The alert supplies its own padding, so it must not land inside a wrapper that pads
    /// it again. It renders as bare content — not a MudDialog — so Mud's title/content/
    /// actions wrappers never appear around it; this pins that, because the day it renders
    /// inside .mud-dialog-content the alert silently gains 24px it did not ask for.
    /// </summary>
    [Fact]
    public void The_alert_sits_directly_on_muds_dialog_surface()
    {
        var (provider, _) = Ask();

        var surface = provider.Find(".mud-dialog");

        Assert.DoesNotContain("mud-dialog-content", provider.Markup);
        Assert.Contains("desk-alert", surface.InnerHtml);
    }

    /// <summary>
    /// Puts a real question through the real service inside a live provider. The returned
    /// task is deliberately not awaited here — it completes only once the alert is
    /// answered, which is what each test then does.
    /// </summary>
    private (IRenderedFragment Provider, Task<bool> Answer) Ask(bool destructive = false)
    {
        var provider = RenderComponent<MudDialogProvider>();
        var alerts = new DeskAlerts(Services.GetRequiredService<IDialogService>());

        Task<bool>? answer = null;

        provider.InvokeAsync(() => answer = alerts.ConfirmAsync(
            "Delete this run?",
            "The ingested odds stay; only the run record goes.",
            confirmLabel: "Delete run",
            cancelLabel: "Keep it",
            destructive: destructive));

        provider.WaitForState(() => provider.FindAll(".desk-alert").Count > 0);

        return (provider, answer!);
    }
}
