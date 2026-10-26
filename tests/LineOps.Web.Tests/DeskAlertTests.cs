using Bunit;
using LineOps.Desk.Primitives;
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

    [Fact]
    public void A_destructive_confirm_is_red()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.ConfirmLabel, "Delete run")
            .Add(x => x.Destructive, true));

        Assert.NotNull(cut.Find("button.desk-btn--destructive"));
    }

    /// <summary>
    /// A destructive alert has no default button — neither the confirm nor the way out.
    ///
    /// <para>
    /// Apple's Alerts page (February 2024): a destructive action is never the default. The
    /// desk used to meet that by making Cancel the default, which still let Return answer an
    /// alert the operator had not read, and a reflex of Return-to-dismiss is exactly what
    /// fires on the one alert that mattered. So focus lands on the alert's own panel, which
    /// carries <c>tabindex="-1"</c> to be focusable by script without joining the tab ring,
    /// and no button declares <c>autofocus</c>. Return then does nothing until the operator
    /// tabs to a choice.
    /// </para>
    /// </summary>
    [Fact]
    public void Destructive_alert_has_no_default_button()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.ConfirmLabel, "Delete run")
            .Add(x => x.Destructive, true));

        Assert.Equal("-1", cut.Find(".desk-alert").GetAttribute("tabindex"));
        Assert.Equal(2, cut.FindAll("button").Count);
        Assert.All(cut.FindAll("button"), b => Assert.False(b.HasAttribute("autofocus")));
    }

    /// <summary>
    /// A non-destructive alert keeps the confirm as its default, because there the likely
    /// answer is also the safe one — "Apply these changes?" is asked to be answered yes.
    /// </summary>
    [Fact]
    public void Plain_alert_defaults_to_confirm()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?")
            .Add(x => x.ConfirmLabel, "Apply"));

        var confirm = cut.Find("button.desk-btn--filled");
        var cancel = cut.Find("button.desk-btn--plain");

        Assert.True(confirm.HasAttribute("autofocus"));
        Assert.False(cancel.HasAttribute("autofocus"));
    }

    /// <summary>
    /// With nothing to back out to, a destructive alert still has no default: a one-button
    /// destructive alert that answered Return would be the dangerous default the rule above
    /// forbids. It still focuses <em>something</em> — the panel — so Return stays inside the
    /// alert rather than answering the page behind it.
    /// </summary>
    [Fact]
    public void A_destructive_alert_with_no_cancel_still_has_no_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Discard the slip?")
            .Add(x => x.ConfirmLabel, "Discard")
            .Add(x => x.Destructive, true)
            .Add(x => x.CancelLabel, (string?)null));

        Assert.Single(cut.FindAll("button"));
        Assert.False(cut.Find("button.desk-btn--destructive").HasAttribute("autofocus"));
        Assert.Single(FocusCalls);
    }

    /// <summary>
    /// Every caller names the consequence. <c>ConfirmLabel</c> used to default to "OK",
    /// which is what let a call site skip saying what the button does; it is now
    /// <c>[EditorRequired]</c> with no fallback, so omitting it is a build warning in the
    /// markup that forgot rather than an "OK" on screen. Pinned by reflection because the
    /// analyzer is the enforcement and a test cannot run it.
    /// </summary>
    [Fact]
    public void Every_caller_must_name_the_confirm()
    {
        var property = typeof(DeskAlert).GetProperty(nameof(DeskAlert.ConfirmLabel))!;

        Assert.NotNull(property.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Components.EditorRequiredAttribute), inherit: false).SingleOrDefault());
        Assert.Equal(string.Empty, new DeskAlert().ConfirmLabel);
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
    /// keyboard back to where the alert started while the operator was reading a button.
    /// </para>
    ///
    /// <para>
    /// <b>What this cannot assert.</b> The recorded invocation carries an
    /// <see cref="Microsoft.AspNetCore.Components.ElementReference"/> whose <c>Id</c> is a
    /// renderer GUID, and bUnit emits <c>blazor:elementreference</c> into the markup with
    /// an empty value — so there is no route from that id back to a DOM node, and no way to
    /// say here <em>which</em> element was focused. For a destructive alert the target is
    /// the panel; that is pinned by <see cref="Destructive_alert_has_no_default_button"/>
    /// (the panel is the only focusable thing left without <c>autofocus</c>) and by a live
    /// browser check, where <c>document.activeElement</c> should be the
    /// <c>.desk-alert</c> itself and Return should leave the alert open.
    /// </para>
    /// </summary>
    [Fact]
    public void The_alert_places_focus_once_when_it_opens()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.ConfirmLabel, "Delete run")
            .Add(x => x.Destructive, true));

        Assert.Single(FocusCalls);

        cut.SetParametersAndRender(p => p.Add(x => x.Heading, "Delete this run, really?"));

        Assert.Single(FocusCalls);
    }

    /// <summary>
    /// The other branch of the same decision. A normal alert sends the keyboard to its
    /// confirm — a separate test because a mistake that focused nothing when
    /// <c>Destructive</c> is false would otherwise hide behind the destructive case above.
    /// </summary>
    [Fact]
    public void A_normal_alert_focuses_on_open_too()
    {
        RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?")
            .Add(x => x.ConfirmLabel, "Apply"));

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
