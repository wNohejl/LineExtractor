using Bunit;
using LineOps.Desk.Primitives;

namespace LineOps.Desk.Tests;

/// <summary>
/// A question asked in place, with its two answers: Cancel first, the confirm last, as every
/// confirm on the desk orders them. The group is named by the question, and Escape answers no.
/// </summary>
public class InlineConfirmTests : DeskTestContext
{
    private IRenderedComponent<InlineConfirm> Confirm(
        Action? confirmed = null, Action? cancelled = null, bool destructive = false) =>
        RenderComponent<InlineConfirm>(p => p
            .AddChildContent("Archive the three reviewed items?")
            .Add(x => x.ConfirmLabel, "Archive")
            .Add(x => x.Destructive, destructive)
            .Add(x => x.OnConfirm, () => confirmed?.Invoke())
            .Add(x => x.OnCancel, () => cancelled?.Invoke()));

    [Fact]
    public void Cancel_comes_before_the_confirm()
    {
        var cut = Confirm();

        var keys = cut.FindAll(".confirm__keys button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Cancel", "Archive"], keys);
    }

    [Fact]
    public void Is_a_group_named_by_its_question()
    {
        var cut = Confirm();

        var group = cut.Find(".confirm");
        Assert.Equal("group", group.GetAttribute("role"));

        var ask = cut.Find(".confirm__ask");
        Assert.Equal("Archive the three reviewed items?", ask.TextContent.Trim());
        Assert.False(string.IsNullOrEmpty(ask.Id));
        Assert.Equal(ask.Id, group.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Two_confirms_never_share_an_id()
    {
        Assert.NotEqual(Confirm().Find(".confirm__ask").Id, Confirm().Find(".confirm__ask").Id);
    }

    [Fact]
    public void Confirm_and_cancel_invoke()
    {
        int confirmed = 0, cancelled = 0;
        var cut = Confirm(() => confirmed++, () => cancelled++);

        var keys = cut.FindAll(".confirm__keys button");
        keys[0].Click();
        cut.FindAll(".confirm__keys button")[1].Click();

        Assert.Equal(1, cancelled);
        Assert.Equal(1, confirmed);
    }

    [Fact]
    public void Escape_cancels()
    {
        int cancelled = 0;
        var cut = Confirm(cancelled: () => cancelled++);

        cut.Find(".confirm").KeyDown(key: "Enter");
        Assert.Equal(0, cancelled);

        cut.Find(".confirm").KeyDown(key: "Escape");
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void A_destructive_confirm_is_marked()
    {
        Assert.Empty(Confirm().FindAll(".desk-btn--destructive"));

        var keys = Confirm(destructive: true).FindAll(".confirm__keys button");
        Assert.DoesNotContain("desk-btn--destructive", keys[0].ClassList);
        Assert.Contains("desk-btn--destructive", keys[1].ClassList);
        Assert.DoesNotContain("desk-btn--filled", keys[1].ClassList);
    }

    [Fact]
    public void Busy_holds_the_confirm()
    {
        var cut = RenderComponent<InlineConfirm>(p => p
            .AddChildContent("Archive it?")
            .Add(x => x.ConfirmLabel, "Archive")
            .Add(x => x.Busy, true));

        Assert.Equal("true", cut.FindAll(".confirm__keys button")[1].GetAttribute("aria-busy"));
    }
}
