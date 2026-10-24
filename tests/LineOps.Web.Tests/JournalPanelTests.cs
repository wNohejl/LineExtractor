using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;

namespace LineOps.Web.Tests;

/// <summary>
/// The Journal records new bets through the wager window and gives parlays to the slip; its own
/// form is for editing an entry. Read from the source: the Journal loads from the database the
/// moment it renders, and this project has none.
/// </summary>
public class JournalPanelTests
{
    private static string Source()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "JournalPanel.razor"));

    [Fact]
    public void Log_parlay_is_gone()
    {
        var source = Source();

        Assert.DoesNotContain("Log parlay", source);
        Assert.DoesNotContain("ParlayForm", source);
        Assert.DoesNotContain("SaveParlayAsync", source);
        Assert.DoesNotContain("FormMode.Parlay", source);
    }

    [Fact]
    public void Open_slip_opens_the_slip_window()
    {
        var source = Source();

        Assert.Contains("Open slip", source);
        Assert.Contains("Manager.Open(WindowCatalog.Find(WindowCatalog.Slip)!)", source);

        var window = new WindowManager(new AppWindowCatalog()).Open(WindowCatalog.Find(WindowCatalog.Slip)!);
        Assert.Equal(typeof(SlipPanel), window.Definition.ComponentType);
    }

    [Fact]
    public void Log_entry_opens_the_wager_window_and_there_is_no_second_new_entry_form()
    {
        var source = Source();

        Assert.Contains("Manager.Open(WindowCatalog.Find(WindowCatalog.Wager)!)", source);
        Assert.DoesNotContain("New entry", source);
        Assert.DoesNotContain("ShowEntryForm", source);

        var window = new WindowManager(new AppWindowCatalog()).Open(WindowCatalog.Find(WindowCatalog.Wager)!);
        Assert.Equal(typeof(BoardWager), window.Definition.ComponentType);
    }

    [Fact]
    public void The_edit_form_uses_the_shared_stake_row_and_tags_a_typed_price()
    {
        var source = Source();

        Assert.Contains("<StakeRow", source);
        Assert.Contains("ManualPrice", source);
    }
}
