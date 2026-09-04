using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// What the desk claims will fit, against what its own layout does.
///
/// <para>
/// The guidance used to be <c>DeskWidth / 420</c> — a uniform division that assumed every
/// column takes an equal share. The layout does not work that way: the primary window takes
/// its configured share off the top first and the rest divide what is left. At 1920 with the
/// default 45% primary, four columns leave the other three 345px each, under the 420 several
/// windows declare as their minimum. Those clamp back up to their minimum, the row sums past
/// the viewport, and the desk scrolls with the last window off-screen.
/// </para>
///
/// <para>
/// Guidance that recommends an arrangement its own layout cannot draw is worse than no
/// guidance, because it is the number the ceiling defaults to.
/// </para>
/// </summary>
public class DeskCapacityTests
{
    private static WindowManager Desk(double width, double primaryShare = 0.45, string? primary = "ops")
    {
        var manager = new WindowManager();
        manager.SetViewport(width, 900);
        manager.Settings.PrimaryWindowKey = primary;
        manager.Settings.PrimaryShare = primaryShare;
        return manager;
    }

    [Fact]
    public void A_recommended_column_is_never_narrower_than_a_window_can_be_read_at()
    {
        var desk = Desk(1920);

        // Three columns at 45%/27.5%/27.5% of 1888 usable gives the narrowest 519px. Four
        // gives it 345px, which is the arrangement that was overflowing.
        Assert.Equal(3, desk.RecommendedCapacity);
    }

    [Fact]
    public void Without_a_primary_the_columns_divide_evenly_and_more_of_them_fit()
    {
        var desk = Desk(1920, primary: null);

        // Nothing takes a share off the top, so 1888 usable divides four ways at 472px each.
        Assert.Equal(4, desk.RecommendedCapacity);
    }

    [Fact]
    public void A_wider_desk_earns_another_column()
    {
        Assert.True(Desk(2560).RecommendedCapacity > Desk(1920).RecommendedCapacity);
    }

    [Fact]
    public void A_narrow_desk_still_offers_one_column_rather_than_none()
    {
        // A single window is always allowed to be the whole desk, however narrow — the
        // alternative is a desk that recommends showing nothing.
        Assert.Equal(1, Desk(600).RecommendedCapacity);
    }

    [Fact]
    public void A_larger_primary_share_leaves_room_for_fewer_columns()
    {
        // The share is the operator's, and the honest response to spending more of the desk
        // on one window is to say that fewer others fit beside it.
        Assert.True(Desk(1920, primaryShare: 0.7).RecommendedCapacity
                    < Desk(1920, primaryShare: 0.3).RecommendedCapacity);
    }
}
