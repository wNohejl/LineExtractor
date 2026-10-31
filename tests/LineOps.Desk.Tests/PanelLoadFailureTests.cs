using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.AspNetCore.Components;

namespace LineOps.Desk.Tests;

/// <summary>
/// What a window shows when its load failed: the sentence naming what did not load, as the
/// Error kind of the empty state, and the one key that loads it again.
/// </summary>
public class PanelLoadFailureTests : DeskTestContext
{
    [Fact]
    public void Shows_the_message_as_an_error_empty_state()
    {
        var cut = RenderComponent<PanelLoadFailure>(p => p
            .Add(x => x.Message, "The list did not load.")
            .Add(x => x.OnRetry, EventCallback.Empty));

        var empty = cut.Find(".empty.empty--error");

        Assert.Contains("The list did not load.", empty.TextContent);
        Assert.Equal("Try again", empty.QuerySelector(".empty__action button")!.TextContent.Trim());
    }

    [Fact]
    public void Try_again_invokes_retry()
    {
        var retried = 0;
        var cut = RenderComponent<PanelLoadFailure>(p => p
            .Add(x => x.Message, "The list did not load.")
            .Add(x => x.OnRetry, () => retried++));

        cut.Find(".empty__action button").Click();

        Assert.Equal(1, retried);
    }

    [Fact]
    public void The_key_s_label_can_be_named()
    {
        var cut = RenderComponent<PanelLoadFailure>(p => p
            .Add(x => x.Message, "The runs did not load.")
            .Add(x => x.OnRetry, EventCallback.Empty)
            .Add(x => x.RetryLabel, "Load again"));

        Assert.Equal("Load again", cut.Find(".empty__action button").TextContent.Trim());
    }
}
