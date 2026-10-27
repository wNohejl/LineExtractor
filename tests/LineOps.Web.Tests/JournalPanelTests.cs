using Bunit;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Desk.Windowing;
using LineOps.Web.Components.Panels;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The Journal records new bets through the wager window and gives parlays to the slip; its own
/// form is for editing an entry. Mostly read from the source: the Journal loads from the
/// database the moment it renders, and this project has none. Where the behaviour is the point —
/// a write that fails — it renders against the in-memory journal the parlay tests read through
/// the panel's virtual seams.
/// </summary>
public class JournalPanelTests : WagerTestContext
{
    private readonly JournalParlayTests.JournalOnHand _journal = new();

    public JournalPanelTests()
    {
        Services.AddSingleton(_journal);
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(new JournalParlayTests.NoDatabase());
    }

    private static string Source()
        => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "JournalPanel.razor"));

    /// <summary>A straight bet on the Chiefs, $50 at -110 with DraftKings, still pending.</summary>
    private static JournalEntry Pending()
    {
        var game = new Game
        {
            Id = 21,
            Sport = new Sport { Key = "nfl", Name = "NFL" },
            AwayTeam = new Team { Name = "Buffalo Bills", Abbrev = "BUF" },
            HomeTeam = new Team { Name = "Kansas City Chiefs", Abbrev = "KC" },
            StartsAt = DateTimeOffset.UtcNow.AddHours(4),
            Status = GameStatus.Scheduled,
            SeasonYear = 2026
        };

        return new JournalEntry
        {
            Id = 7,
            GameId = game.Id,
            Game = game,
            Market = Markets.Moneyline,
            Outcome = "Kansas City Chiefs",
            PriceTaken = -110,
            Stake = 50m,
            Book = "draftkings",
            PlacedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    /// <summary>
    /// A write that fails used to escape the handler: the form stayed or went by accident, and
    /// nothing said the entry was not saved. Now the form stays open on what was typed and the
    /// toolbar says so, as the parlay form already did.
    /// </summary>
    [Fact]
    public void A_save_that_fails_keeps_the_form_open_and_says_so()
    {
        _journal.Entries.Add(Pending());
        _journal.FailWith = new InvalidOperationException("connection refused");

        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Journal)!);
        var cut = RenderComponent<JournalParlayTests.JournalUnderTest>(p => p.AddCascadingValue("WindowId", window.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[title='Edit this entry']")));

        cut.Find("button[title='Edit this entry']").Click();
        cut.Find(".pform--grouped .stake-row__field").Input("40");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Save changes").Click();

        Assert.NotEmpty(cut.FindAll(".pform--grouped"));
        Assert.Equal("40", cut.Find(".pform--grouped .stake-row__field").GetAttribute("value"));
        Assert.True(window.HasUnsavedInput);
        Assert.Equal("The entry was not saved. Try again.", cut.Find(".ptoolbar__msg").TextContent.Trim());
        Assert.DoesNotContain("connection refused", cut.Markup);
        Assert.Empty(_journal.EntryWrites);
    }

    /// <summary>The other four writes catch theirs the same way, each saying which write did not happen.</summary>
    [Fact]
    public void Every_journal_write_says_when_it_did_not_happen()
    {
        var source = Source();

        foreach (var sentence in new[]
                 {
                     "The entry was not saved. Try again.",
                     "The entry was not deleted. Try again.",
                     "The parlay was not deleted. Try again.",
                     "The result was not recorded. Try again.",
                     "Settlement did not run. Try again."
                 })
        {
            Assert.Contains($"_message = \"{sentence}\";", source);
        }
    }

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
    public void The_slip_is_a_tab_of_the_journal_not_a_key_that_opens_a_window()
    {
        var source = Source();

        Assert.DoesNotContain("Open slip", source);
        Assert.Contains("<DeskTabs", source);
        Assert.Contains("<SlipPanel", source);
    }

    /// <summary>
    /// A parlay is built in the same wager window Log entry opens, in its parlay mode, and logged
    /// from the slip: not a second parlay form here.
    /// </summary>
    [Fact]
    public void Build_parlay_opens_the_wager_window_building_legs_onto_a_parlay_slip()
    {
        var source = Source();

        Assert.Contains("Build parlay", source);
        Assert.Contains("[\"BuildParlay\"] = true", source);
        Assert.Contains("Slip.SetMode(SlipMode.Parlay)", source);
    }

    [Fact]
    public void An_entry_can_be_duplicated_into_the_wager_window_through_the_shared_door()
    {
        var source = Source();

        Assert.Contains("Duplicate this entry", source);
        Assert.Contains("Manager.OpenWagerCopy(WagerPrefill.From(", source);
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

    /// <summary>
    /// "Edit entry" used to float: a quiet group-header voice with a note between it and the
    /// fields, under a disclaimer box wider than the form. The title now carries a detail line
    /// naming the row it edits, a typed price is the group's footer, and the disclaimer is the
    /// window's footnote.
    /// </summary>
    [Fact]
    public void The_edit_form_names_its_entry_and_the_disclaimer_is_the_footnote()
    {
        var source = Source();

        Assert.Contains("<Detail>@(_editing is { } editing ? $\"Placed {Starts(editing.PlacedAt)}\" : null)</Detail>", source);
        Assert.Contains("<p class=\"wgroup__foot journal-manual-note\">", source);
        Assert.DoesNotContain("<Note Class=\"journal-manual-note\">", source);
        Assert.Contains("<p class=\"wfoot journal-foot\">", source);
        Assert.True(source.IndexOf("journal-foot", StringComparison.Ordinal) > source.IndexOf("Title=\"Entries\"", StringComparison.Ordinal),
            "The disclaimer sits under the lists, not between the toolbar and the form.");
    }

    [Fact]
    public void The_edit_form_uses_the_shared_stake_row_and_tags_a_typed_price()
    {
        var source = Source();

        Assert.Contains("<StakeRow", source);
        Assert.Contains("ManualPrice", source);
    }

    /// <summary>
    /// A disabled key's reason is text beside it, not a tooltip: a tooltip waits for a hover the
    /// operator has no reason to make, and a screen reader is told through aria-describedby.
    /// </summary>
    [Fact]
    public void Disabled_save_states_its_reason_in_text()
    {
        var source = Source();

        Assert.Contains("<span class=\"meta\" id=\"save-why\">Needs a selection, a stake above zero, a price and a book.</span>", source);
        // Pointed at only while the reason is on screen: a describedby naming a missing id says nothing.
        Assert.Contains("aria-describedby=\"@(EntryValid(_entry, true) ? null : \"save-why\")\"", source);
        Assert.DoesNotContain("Title=\"@(EntryValid", source);
    }

    [Fact]
    public void Price_stake_and_line_say_what_is_wrong_once_touched()
    {
        var source = Source();

        Assert.Contains("Error=\"@(Touched(\"price\") ? PriceError(d.PriceTaken) : null)\"", source);
        // The stake row carries its own error now, under its field, rather than a sentence the
        // Journal hung after the row.
        Assert.Contains("Error=\"@(Touched(\"stake\") ? StakeError(_entry.Stake) : null)\"", source);
        Assert.DoesNotContain("stakeError", source);
        Assert.Contains("Error=\"@(Touched(\"line\") ? LineError(d.Market, d.LineTaken) : null)\"", source);
    }

    /// <summary>
    /// Settling commits a result: a command, so a pull-down of commands rather than a pop-up
    /// of values that acted on change and snapped back to its placeholder (HIG pop-up buttons).
    /// Plain and small, because it sits in a row beside the row's other keys.
    /// </summary>
    [Fact]
    public void Settle_is_a_pull_down()
    {
        var source = Source();

        Assert.DoesNotContain("Settle…", source);
        Assert.Contains("<DeskPullDown Label=\"Settle\" Emphasis=\"DeskEmphasis.Plain\" Size=\"DeskKeySize.Small\"", source);

        foreach (var result in new[] { "Win", "Loss", "Push", "Void" })
            Assert.Contains($"<DeskMenuItem Label=\"{result}\" OnClick=\"@(() => SetResultAsync(context.Item.Id, EntryResult.{result}))\" />", source);

        // A parlay settles the same way, and offers a win only where there is a price to pay it at.
        Assert.Contains("<DeskMenuItem Label=\"Win\" OnClick=\"@(() => SetParlayResultAsync(context.Item, EntryResult.Win))\" />", source);
    }

    /// <summary>
    /// On a tracked game the selection is one of two known answers, so it is a choice of two,
    /// not a box to type a team into; a total is over or under. Free text keeps its field.
    /// </summary>
    [Fact]
    public void A_selection_on_a_tracked_game_is_a_choice_of_two()
    {
        var source = Source();

        Assert.Contains("<DeskSwitch TValue=\"string\" Options=\"@TeamSides(", source);
        Assert.Contains("<DeskSwitch TValue=\"string\" Options=\"TotalSides\"", source);
        Assert.Contains("new(\"over\", \"Over\")", source);
        Assert.Contains("new(\"under\", \"Under\")", source);
    }

    /// <summary>A half-edited entry tells the window, so closing or evicting it asks first.</summary>
    [Fact]
    public void The_edit_form_marks_the_window_unsaved_while_it_differs()
    {
        var source = Source();

        Assert.Contains("MarkUnsaved(EntryIsDirty(_entry.ToDraft(), _opened), \"this journal entry\")", source);
        Assert.Contains("MarkUnsaved(false)", source);
    }

    [Theory]
    [InlineData("JournalPanel.razor")]
    [InlineData("PerformancePanel.razor")]
    [InlineData("PlayersPanel.razor")]
    public void The_sport_filter_is_a_segment(string panel)
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", panel));

        Assert.Contains("<DeskSwitch Options=\"SportOptions\" Label=\"Sport\"", source);
        Assert.DoesNotContain("All sports", source);
    }

    /// <summary>A narrow window keeps the toolbar on one line: the sport and book pickers fold into More.</summary>
    [Theory]
    [InlineData("JournalPanel.razor")]
    [InlineData("PerformancePanel.razor")]
    public void Sport_and_book_fold_into_more(string panel)
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", panel));
        var overflow = BoardToolbarTests.Slot(source, "Overflow");

        Assert.Contains("<DeskSwitch Options=\"SportOptions\" Label=\"Sport\"", overflow);
        Assert.Contains("All books", overflow);
        Assert.Contains("class=\"ptoolbar__label\" aria-hidden=\"true\">Sport<", overflow);
        Assert.Contains("class=\"ptoolbar__label\" aria-hidden=\"true\">Book<", overflow);
    }

    /// <summary>Settle and Build parlay fold last; Log entry, the window's own key, never leaves the row.</summary>
    [Fact]
    public void Journal_side_keys_fold_and_log_entry_stays()
    {
        var source = Source();
        var secondary = BoardToolbarTests.Slot(source, "SecondaryActions");
        var actions = BoardToolbarTests.Slot(source, "Actions");

        Assert.Contains("Settle finished games", secondary);
        Assert.Contains("Build parlay", secondary);
        Assert.DoesNotContain("OnClick=\"OpenWager\"", secondary);
        Assert.Contains("OnClick=\"OpenWager\"", actions);
    }
}
