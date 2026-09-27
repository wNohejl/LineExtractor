using Bunit;
using LineOps.Core.Analytics;
using LineOps.Data.CrossReference;
using LineOps.Desk;
using LineOps.Desk.Primitives;
using LineOps.Desk.Windowing;
using LineOps.Web.Access;
using LineOps.Web.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// A gated desk has two people at it (ADR 0018). The operator's desk is the one there has always
/// been. A visitor's reads everything that is not personal and changes nothing — and that has to
/// hold at the catalogue, where every way of reaching a window starts, rather than at each
/// button that happens to be remembered.
/// </summary>
public class DeskAccessTests : DeskTestContext
{
    private sealed class Visitor : IDeskAccess
    {
        public bool CanOperate => false;
    }

    private static WindowManager VisitorDesk() => new(new AppWindowCatalog(), new Visitor());

    private static readonly string[] Personal =
        [WindowCatalog.Journal, WindowCatalog.Performance, WindowCatalog.Slip, WindowCatalog.Wager];

    [Fact]
    public void An_operator_desk_has_every_window()
    {
        var manager = new WindowManager(new AppWindowCatalog());

        Assert.Equal(WindowCatalog.All.Count, manager.Catalog.All.Count);
        Assert.All(Personal, key => Assert.NotNull(manager.Catalog.Find(key)));
    }

    [Fact]
    public void A_visitor_desk_has_no_personal_window_anywhere_it_could_be_reached()
    {
        var manager = VisitorDesk();

        Assert.All(Personal, key => Assert.Null(manager.Catalog.Find(key)));
        Assert.DoesNotContain(manager.Catalog.All, d => d.OperatorOnly);
        Assert.DoesNotContain(manager.Catalog.Workspaces, w => w.WindowKeys.Any(k => Personal.Contains(k)));
        Assert.All(manager.Catalog.Workspaces, w => Assert.NotEmpty(w.WindowKeys));
    }

    /// <summary>The board is what the public desk is for; reading it must survive the filter.</summary>
    [Fact]
    public void A_visitor_still_reads_the_board_the_market_and_operations()
    {
        var manager = VisitorDesk();

        foreach (var key in new[] { WindowCatalog.Board, WindowCatalog.Bets, WindowCatalog.Odds, WindowCatalog.Ops,
                     WindowCatalog.Incidents, WindowCatalog.Runs, WindowCatalog.History })
            Assert.NotNull(manager.Catalog.Find(key));

        Assert.NotNull(manager.Catalog.Find(manager.Catalog.DefaultPrimary));
    }

    [Fact]
    public void Opening_a_personal_window_on_a_visitor_desk_is_refused_not_obeyed()
    {
        var manager = VisitorDesk();

        Assert.Throws<InvalidOperationException>(() => manager.Open(WindowCatalog.Find(WindowCatalog.Journal)!));
        Assert.Empty(manager.Windows);
    }

    /// <summary>
    /// Shortcuts resolve through the manager's catalogue, so a link a panel forgot to hide
    /// does nothing on a visitor's desk rather than ending the circuit.
    /// </summary>
    [Fact]
    public void A_shortcut_to_a_personal_window_does_nothing_for_a_visitor()
    {
        var manager = VisitorDesk();

        manager.OpenJournal();

        Assert.Empty(manager.Windows);
    }

    [Fact]
    public void A_follow_up_to_the_wager_window_does_nothing_for_a_visitor()
    {
        var manager = VisitorDesk();
        var launcher = new FollowUpLauncher();

        launcher.Launch(manager, WindowCatalog.Wager, "GameId", 7, "Home v Away");

        Assert.Empty(manager.Windows);
        Assert.Empty(launcher.Open);
    }

    [Fact]
    public void A_live_price_is_only_a_reading_on_a_visitor_desk()
    {
        Services.AddSingleton<IDeskAccess>(new Visitor());
        var taken = 0;

        var cut = RenderComponent<PriceCell>(p => p
            .Add(x => x.Offer, new BestOffer("home", "draftkings", 145, null, DateTimeOffset.UnixEpoch,
                null, false, false, [new BookPrice("draftkings", 145, null, 0.408)]))
            .Add(x => x.OnTake, () => taken++));

        Assert.Empty(cut.FindAll("[role=button]"));
        cut.Find(".price").Click();
        Assert.Equal(0, taken);
    }

    // The code itself: made once, stored hashed, checked in constant time.

    [Fact]
    public void A_code_matches_the_hash_made_from_it_and_nothing_else()
    {
        var stored = AccessCode.Hash("open-sesame");

        Assert.True(AccessCode.IsWellFormed(stored));
        Assert.True(AccessCode.Matches("open-sesame", stored));
        Assert.False(AccessCode.Matches("open-sesame ", stored));
        Assert.False(AccessCode.Matches("", stored));
        Assert.False(AccessCode.Matches(null, stored));
    }

    [Fact]
    public void Two_hashes_of_one_code_differ_because_each_is_salted()
        => Assert.NotEqual(AccessCode.Hash("same"), AccessCode.Hash("same"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open-sesame")]
    [InlineData("pbkdf2-sha256.0.AAAA.AAAA")]
    [InlineData("pbkdf2-sha256.600000.not base64.AAAA")]
    [InlineData("md5.1.AAAA.AAAA")]
    public void A_missing_or_malformed_hash_matches_nothing(string? stored)
        => Assert.False(AccessCode.Matches("open-sesame", stored));

    /// <summary>
    /// Made by <c>scripts/new-access-code.ps1 -Code 'test-code-1'</c>. The script and this class
    /// are two implementations of one format; this is what keeps them one.
    /// </summary>
    [Fact]
    public void A_hash_made_by_the_setup_script_is_one_the_desk_accepts()
    {
        const string fromScript =
            "pbkdf2-sha256.600000.WS+gSSSb5yllgWZUr3o8XA==.E3SPAqf5NZGklCRLyeVnTXgvfy9d0DhRC8ZzXFUquHQ=";

        Assert.True(AccessCode.Matches("test-code-1", fromScript));
        Assert.False(AccessCode.Matches("test-code-2", fromScript));
    }
}
