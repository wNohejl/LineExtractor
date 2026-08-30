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
    ///
    /// <para>
    /// "Default" is now literal rather than painted: the default button is the one the
    /// alert focuses on open, so Enter answers it because it is focused. The old marker
    /// class drew a ring on a button that had no focus, which is why nothing here looks
    /// for it any more — the assertion is which button carries <c>autofocus</c>, the
    /// component's own declaration of where the keyboard starts.
    /// </para>
    /// </summary>
    [Fact]
    public void A_destructive_confirm_is_red_and_not_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Destructive, true));

        Assert.Contains("desk-btn--destructive", cut.Markup);

        var confirm = cut.Find("button.desk-btn--destructive");
        var cancel = cut.Find("button.desk-btn--plain");

        Assert.False(confirm.HasAttribute("autofocus"));
        Assert.True(cancel.HasAttribute("autofocus"));
    }

    [Fact]
    public void A_normal_confirm_is_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?"));

        var confirm = cut.Find("button.desk-btn--filled");
        var cancel = cut.Find("button.desk-btn--plain");

        Assert.True(confirm.HasAttribute("autofocus"));
        Assert.False(cancel.HasAttribute("autofocus"));
    }

    /// <summary>
    /// With nothing to back out to, the confirm takes the default back — a one-button
    /// acknowledgement that focused nothing would leave Enter answering the page behind
    /// the alert.
    ///
    /// <para>
    /// This is also the one arrangement where the focus <em>target</em> is provable here
    /// rather than only the focus call: the alert renders exactly one button, so a focus
    /// invocation could not have gone anywhere else. See
    /// <see cref="The_alert_focuses_its_default_button_once_when_it_opens"/> for why the
    /// two-button case cannot say the same.
    /// </para>
    /// </summary>
    [Fact]
    public void A_destructive_alert_with_no_cancel_still_focuses_something()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "The run was deleted")
            .Add(x => x.Destructive, true)
            .Add(x => x.CancelLabel, (string?)null));

        Assert.True(cut.Find("button.desk-btn--destructive").HasAttribute("autofocus"));

        Assert.Single(cut.FindAll("button"));
        Assert.Single(FocusCalls);
    }

    /// <summary>
    /// The behaviour the marking now rests on, pinned as behaviour.
    ///
    /// <para>
    /// The <c>autofocus</c> attribute the tests above read is a declaration, not a
    /// mechanism: the browser ignores it for content the renderer inserts after load — it
    /// says so in the console — which is exactly why <c>DeskAlert.OnAfterRenderAsync</c>
    /// calls <c>FocusAsync</c>. Without this test that call could be deleted outright and
    /// every other test here would stay green while the alert silently went back to
    /// focusing nothing. <c>ElementReference.FocusAsync</c> goes through the injected
    /// <c>IJSRuntime</c>, and bUnit records the invocation even in Loose mode, so the call
    /// is observable here even though the focus itself is not.
    /// </para>
    ///
    /// <para>
    /// Once, and only on the first render: re-focusing on every re-render would drag the
    /// keyboard back to the default button while the operator was reading the other one.
    /// </para>
    ///
    /// <para>
    /// <b>What this cannot assert.</b> The recorded invocation carries an
    /// <see cref="Microsoft.AspNetCore.Components.ElementReference"/> whose <c>Id</c> is a
    /// renderer GUID, and bUnit emits <c>blazor:elementreference</c> into the markup with
    /// an empty value — so there is no route from that id back to a DOM node, and no way to
    /// say here <em>which</em> of two buttons was focused. The only route to the element is
    /// reflection into MudBlazor's private <c>_elementReference</c> field, which would break
    /// silently on an upgrade and is a worse test than an honest gap. Which button gets the
    /// focus is pinned by the <c>autofocus</c> assertions above, by the single-button case,
    /// and by the live browser check recorded in the task report — where
    /// <c>document.activeElement</c> was the cancel button and one Tab moved a real
    /// <c>:focus-visible</c> ring onto the destructive confirm.
    /// </para>
    /// </summary>
    [Fact]
    public void The_alert_focuses_its_default_button_once_when_it_opens()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Destructive, true));

        Assert.Single(FocusCalls);

        cut.SetParametersAndRender(p => p.Add(x => x.Heading, "Delete this run, really?"));

        Assert.Single(FocusCalls);
    }

    /// <summary>
    /// The other branch of the same decision. A normal alert has no reason to send the
    /// keyboard to the way out, so the confirm is what gets focused — and it is a separate
    /// test because a mistake that focused nothing when <c>Destructive</c> is false would
    /// otherwise hide behind the destructive case above.
    /// </summary>
    [Fact]
    public void A_normal_alert_focuses_on_open_too()
    {
        RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?"));

        Assert.Single(FocusCalls);
    }

    /// <summary>
    /// Every <c>ElementReference.FocusAsync</c> this test class has provoked. The identifier
    /// is the framework's own, not ours — it is what <c>FocusAsync</c> resolves to.
    /// </summary>
    private IEnumerable<JSRuntimeInvocation> FocusCalls =>
        JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus");

    /// <summary>
    /// The alert renders bare content, so it inherits neither DeskDialog's explicit role
    /// nor MudDialog's — the semantics have to be declared on its own root or a screen
    /// reader meets an unannounced div. The heading is the label, and the message, when
    /// there is one, is the description.
    /// </summary>
    [Fact]
    public void The_alert_announces_itself_as_a_dialog_labelled_by_its_heading()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Message, "The ingested odds stay; only the run record goes."));

        var root = cut.Find(".desk-alert");

        Assert.Equal("alertdialog", root.GetAttribute("role"));
        Assert.Equal("true", root.GetAttribute("aria-modal"));

        var headingId = root.GetAttribute("aria-labelledby");
        Assert.Equal("Delete this run?", cut.Find($"#{headingId}").TextContent);

        var messageId = root.GetAttribute("aria-describedby");
        Assert.Contains("only the run record goes", cut.Find($"#{messageId}").TextContent);
    }

    /// <summary>
    /// No message means nothing to point <c>aria-describedby</c> at. A dangling reference
    /// is read as an empty description rather than skipped, so the attribute has to go.
    /// </summary>
    [Fact]
    public void An_alert_with_no_message_describes_nothing()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Ingest finished"));

        Assert.False(cut.Find(".desk-alert").HasAttribute("aria-describedby"));
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
