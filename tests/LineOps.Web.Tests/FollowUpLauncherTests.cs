using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// Follow-ups opened from a row: a desk window while there is room, a floating dialog once
/// there is not.
///
/// The floating half of that switch is the half with no other owner. A window announces
/// itself through <see cref="WindowManager.Changed"/> and the desk redraws; a floating
/// follow-up is drawn by the panel that launched it, and the panel finds out only if the
/// launcher says so. The row that opens one lives inside a grid cell, so the press is
/// handled — and re-rendered — by the strip, not by the panel holding the layer. Without an
/// announcement the dialog sits in the list unpainted until something unrelated redraws the
/// panel, which reads as a dialog that did not open.
/// </summary>
public class FollowUpLauncherTests
{
    /// <summary>A desk with no room left, so follow-ups take the floating path.</summary>
    private static WindowManager FullDesk()
    {
        var manager = new WindowManager();

        manager.UpdateSettings(s => s.MaxConcurrentWindows = 1);
        manager.Open(WindowCatalog.Find(WindowCatalog.Board)!);

        Assert.True(manager.AtCapacity);

        return manager;
    }

    private static WindowManager EmptyDesk() => new();

    /// <summary>
    /// The bug this suite exists for: the dialog must announce itself the moment it is added.
    /// </summary>
    [Fact]
    public void Floating_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(FullDesk(), WindowCatalog.Odds, "GameId", 7, "Home v Away");

        Assert.Single(launcher.Open);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A follow-up that became a window is the desk's business, not the floating layer's.
    /// The manager announces that one itself, and repeating it here would redraw every host
    /// panel for a change none of them rendered.
    /// </summary>
    [Fact]
    public void Opening_as_a_window_leaves_the_floating_layer_alone()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(EmptyDesk(), WindowCatalog.Odds, "GameId", 7, "Home v Away");

        Assert.Empty(launcher.Open);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// Re-opening the same view for the same subject raises what is already there. The stack
    /// order is rendered, so the raise has to be announced too.
    /// </summary>
    [Fact]
    public void Re_launching_the_same_subject_raises_it_and_announces_that()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");
        launcher.Launch(desk, WindowCatalog.Bets, "GameId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");

        Assert.Equal(2, launcher.Open.Count);
        Assert.Equal(1, changes);

        // The re-launched one is now nearest the front.
        Assert.Equal(WindowCatalog.Odds, launcher.Open.MaxBy(f => f.Order)!.Key);
    }

    /// <summary>The same view against a different subject is a second dialog, not a raise.</summary>
    [Fact]
    public void A_different_subject_is_its_own_follow_up()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");
        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 8, "Other v Team");

        Assert.Equal(2, launcher.Open.Count);
    }

    [Fact]
    public void Raising_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Raise(launcher.Open[0]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Closing_a_follow_up_announces_the_change()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Close(launcher.Open[0]);

        Assert.Empty(launcher.Open);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// Closing every follow-up of one kind is what an action does once it settles. It only
    /// announces when it actually took something down.
    /// </summary>
    [Fact]
    public void Closing_a_whole_kind_announces_only_when_something_went()
    {
        var launcher = new FollowUpLauncher();
        var desk = FullDesk();

        launcher.Launch(desk, WindowCatalog.Wager, "GameId", 7, "Home v Away");
        launcher.Launch(desk, WindowCatalog.Odds, "GameId", 7, "Home v Away");

        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.CloseAll(WindowCatalog.Wager);

        Assert.Single(launcher.Open);
        Assert.Equal(1, changes);

        launcher.CloseAll(WindowCatalog.Wager);

        Assert.Equal(1, changes);
    }

    /// <summary>An unknown key is a no-op, and a no-op has nothing to announce.</summary>
    [Fact]
    public void An_unknown_view_changes_nothing()
    {
        var launcher = new FollowUpLauncher();
        var changes = 0;
        launcher.Changed += () => changes++;

        launcher.Launch(FullDesk(), "not-a-window", "GameId", 7, "Home v Away");

        Assert.Empty(launcher.Open);
        Assert.Equal(0, changes);
    }
}
