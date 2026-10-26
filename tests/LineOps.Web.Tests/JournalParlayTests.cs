using AngleSharp.Dom;
using Bunit;
using LineOps.Core.Entities;
using LineOps.Data;
using LineOps.Desk.Windowing;
using LineOps.Ingestion.Services;
using LineOps.Web.Components.Panels;
using LineOps.Web.Services;
using LineOps.Web.Tests.Support;
using LineOps.Web.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// A logged parlay carries the keys a logged entry does: Edit opens the Journal's grouped form on
/// its money, its book and its legs' prices; Duplicate puts its legs back on the slip as a parlay
/// to log again. Rendered against a journal held in memory — the Journal's one read and the
/// parlay's write are the panel's virtual seams, because this project has no database.
/// </summary>
public class JournalParlayTests : WagerTestContext
{
    /// <summary>What the Journal under test reads, and every parlay save it made.</summary>
    public sealed class JournalOnHand
    {
        public List<Parlay> Parlays { get; } = [];
        public List<string> Books { get; } = ["draftkings", "fanduel", "pinnacle"];
        public List<(Guid Id, ParlayEdit Edit)> Writes { get; } = [];

        /// <summary>Set to make the next save fail with this.</summary>
        public Exception? FailWith { get; set; }
    }

    /// <summary>The Journal, reading and writing <see cref="JournalOnHand"/> instead of the database.</summary>
    public sealed class JournalUnderTest : JournalPanel
    {
        [Inject] private JournalOnHand OnHand { get; set; } = default!;

        protected override Task<JournalRead> FetchAsync(JournalFilter filter)
            => Task.FromResult(new JournalRead(
                [], OnHand.Parlays.ToList(), OnHand.Books, ["mlb", "nfl"],
                OnHand.Parlays.Count(p => p.Result == EntryResult.Pending)));

        protected override Task WriteParlayAsync(Guid id, ParlayEdit edit)
        {
            if (OnHand.FailWith is { } ex)
                return Task.FromException(ex);

            OnHand.Writes.Add((id, edit));
            return Task.CompletedTask;
        }
    }

    /// <summary>Registered because the Journal asks for a factory; the seams above mean it is never called.</summary>
    private sealed class NoDatabase : IDbContextFactory<LineOpsDbContext>
    {
        public LineOpsDbContext CreateDbContext() => throw new NotSupportedException("No database in a render test.");
    }

    private readonly JournalOnHand _journal = new();

    public JournalParlayTests()
    {
        Services.AddSingleton(_journal);
        Services.AddSingleton<IDbContextFactory<LineOpsDbContext>>(new NoDatabase());
    }

    private static Game Game(int id, string away, string home, DateTimeOffset startsAt) => new()
    {
        Id = id,
        Sport = new Sport { Key = "nfl", Name = "NFL" },
        AwayTeam = new Team { Name = away, Abbrev = away[..3].ToUpperInvariant() },
        HomeTeam = new Team { Name = home, Abbrev = home[..3].ToUpperInvariant() },
        StartsAt = startsAt,
        Status = startsAt < DateTimeOffset.UtcNow ? GameStatus.Final : GameStatus.Scheduled,
        SeasonYear = 2026
    };

    /// <summary>
    /// Four legs at DraftKings, $25 at the book's +1200, logged yesterday morning: the first two
    /// games have since started, so their prices could only have been taken before.
    /// </summary>
    private static Parlay FourLegs(EntryResult result = EntryResult.Pending)
    {
        var placed = DateTimeOffset.UtcNow.AddDays(-1);
        var parlay = new Parlay
        {
            Book = "draftkings",
            Stake = 25m,
            PriceQuoted = 1200,
            PlacedAt = placed,
            Result = result,
            Payout = result == EntryResult.Win ? 325m : null,
            SettledAt = result == EntryResult.Pending ? null : DateTimeOffset.UtcNow
        };

        var legs = new (Game Game, string Market, string Outcome, decimal? Line, int Price)[]
        {
            (Game(11, "Buffalo Bills", "Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(-3)), Markets.Moneyline, "Kansas City Chiefs", null, -128),
            (Game(12, "Dallas Cowboys", "Philadelphia Eagles", DateTimeOffset.UtcNow.AddHours(-2)), Markets.Spread, "Philadelphia Eagles", -3.5m, -110),
            (Game(13, "Detroit Lions", "Green Bay Packers", DateTimeOffset.UtcNow.AddHours(4)), Markets.Total, "over", 47.5m, -105),
            (Game(14, "Miami Dolphins", "New York Jets", DateTimeOffset.UtcNow.AddHours(5)), Markets.Moneyline, "Miami Dolphins", null, 145)
        };

        var id = 101;
        foreach (var (game, market, outcome, line, price) in legs)
        {
            parlay.Legs.Add(new JournalEntry
            {
                Id = id++,
                GameId = game.Id,
                Game = game,
                Market = market,
                Outcome = outcome,
                LineTaken = line,
                PriceTaken = price,
                Book = "draftkings",
                PlacedAt = placed,
                ParlayGroupId = parlay.Id,
                Result = result == EntryResult.Win ? EntryResult.Win : EntryResult.Pending
            });
        }

        return parlay;
    }

    private (IRenderedComponent<JournalUnderTest> Cut, WindowInstance Window) Render(params Parlay[] parlays)
    {
        _journal.Parlays.AddRange(parlays.Length > 0 ? parlays : [FourLegs()]);

        var window = Services.GetRequiredService<WindowManager>().Open(WindowCatalog.Find(WindowCatalog.Journal)!);
        var cut = RenderComponent<JournalUnderTest>(p => p.AddCascadingValue("WindowId", window.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[title='Edit this parlay']")));

        return (cut, window);
    }

    private static IElement Key(IRenderedFragment cut, string text)
        => cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    private static IElement Row(IRenderedFragment cut, string label)
        => cut.FindAll(".pform .wgroup__row--form").First(r => r.QuerySelector(".wgroup__label")!.TextContent.Trim() == label);

    private static IElement LegPrice(IRenderedFragment cut, int leg)
        => Row(cut, $"Leg {leg}").QuerySelector("input")!;

    /// <summary>Every toast raised, by its words.</summary>
    private IReadOnlyList<string> Toasts()
        => Services.GetRequiredService<ISnackbar>().ShownSnackbars.Select(s => s.Message ?? string.Empty).ToList();

    // ------------------------------------------------------------------ the row ----

    /// <summary>The keys an entry's row carries, in the same order: Edit, Duplicate, then Delete.</summary>
    [Fact]
    public void A_parlay_row_carries_edit_duplicate_and_delete_in_the_entries_order()
    {
        var (cut, _) = Render();

        // The legs' games are links too; the row's own keys are the ones that name the parlay.
        var titles = cut.FindAll("tbody button[title]").Select(b => b.GetAttribute("title"))
            .Where(t => t!.Contains("parlay", StringComparison.Ordinal)).ToList();

        Assert.Equal(
            ["Edit this parlay", "Duplicate this parlay on the slip", "Delete this parlay and its legs"],
            titles);
    }

    // ------------------------------------------------------------------ edit ----

    [Fact]
    public void Edit_opens_the_grouped_form_on_the_parlays_money_and_its_legs()
    {
        using var dollars = new UsDollars();
        var (cut, _) = Render();

        cut.Find("button[title='Edit this parlay']").Click();

        var form = cut.Find(".pform--grouped");
        Assert.Equal("Edit parlay", form.QuerySelector(".panel-h")!.TextContent.Trim());

        var labels = form.QuerySelectorAll(".wgroup__label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Book", "Stake", "Book's price", "Note", "Leg 1", "Leg 2", "Leg 3", "Leg 4" }, labels);

        Assert.Equal("draftkings", SelectedBook(cut));
        Assert.Equal("25", Row(cut, "Stake").QuerySelector(".stake-row__field")!.GetAttribute("value"));
        Assert.Equal("1200", Row(cut, "Book's price").QuerySelector("input")!.GetAttribute("value"));

        // Each leg reads as the bet it was, with its game, and holds the price taken.
        var second = Row(cut, "Leg 2");
        Assert.Contains("Philadelphia Eagles -3.5 Spread", second.TextContent);
        Assert.Contains("Dallas Cowboys at Philadelphia Eagles", second.QuerySelector(".meta")!.TextContent);
        Assert.Equal("-110", LegPrice(cut, 2).GetAttribute("value"));
        Assert.Equal("145", LegPrice(cut, 4).GetAttribute("value"));

        // Returns are read at the book's price, as a clean win pays.
        Assert.Contains("$325", Row(cut, "Stake").QuerySelector(".stake-row__returns")!.TextContent);

        // Open, the form holds nothing the journal lacks, so Save waits on nothing.
        Assert.Empty(cut.FindAll("#parlay-save-why"));
        Assert.False(Key(cut, "Save changes").HasAttribute("disabled"));
    }

    /// <summary>The book the picker shows: its value, or the option marked selected, however the renderer wrote it.</summary>
    private static string? SelectedBook(IRenderedFragment cut)
    {
        var select = Row(cut, "Book").QuerySelector("select")!;
        return select.GetAttribute("value")
               ?? select.QuerySelectorAll("option").FirstOrDefault(o => o.HasAttribute("selected"))?.GetAttribute("value");
    }

    [Fact]
    public void A_graded_parlay_says_an_edit_sends_it_back_to_pending()
    {
        var (cut, _) = Render(FourLegs(EntryResult.Win));

        cut.Find("button[title='Edit this parlay']").Click();

        var note = cut.Find(".pform--grouped .note").TextContent;
        Assert.Contains("This parlay is graded.", note);
        Assert.Contains("sends it back to pending, and settlement grades it again", note);
    }

    [Fact]
    public void An_open_parlay_has_no_graded_note()
    {
        var (cut, _) = Render();

        cut.Find("button[title='Edit this parlay']").Click();

        Assert.DoesNotContain("This parlay is graded.", cut.Find(".pform--grouped").TextContent);
    }

    /// <summary>A half-edited parlay tells the window, so closing it or evicting it asks first; Cancel lets it go.</summary>
    [Fact]
    public void An_edit_marks_the_window_unsaved_and_cancel_clears_it()
    {
        var (cut, window) = Render();
        cut.Find("button[title='Edit this parlay']").Click();
        Assert.False(window.HasUnsavedInput);

        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("50");

        Assert.True(window.HasUnsavedInput);
        Assert.Equal("this parlay", window.UnsavedInputLabel);

        // Typed back to what it was, the form holds nothing new.
        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("25");
        Assert.False(window.HasUnsavedInput);

        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("40");
        Key(cut, "Cancel").Click();

        Assert.False(window.HasUnsavedInput);
        Assert.Empty(cut.FindAll(".pform--grouped"));
        Assert.Empty(_journal.Writes);
    }

    [Fact]
    public void Save_writes_the_money_the_book_and_a_corrected_leg_price_then_clears_the_window()
    {
        var parlay = FourLegs();
        var (cut, window) = Render(parlay);
        cut.Find("button[title='Edit this parlay']").Click();

        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("40");
        Row(cut, "Book").QuerySelector("select")!.Change("fanduel");
        Row(cut, "Book's price").QuerySelector("input")!.Change("1100");
        LegPrice(cut, 3).Change("-115");
        Assert.True(window.HasUnsavedInput);

        Key(cut, "Save changes").Click();

        var (id, edit) = Assert.Single(_journal.Writes);
        Assert.Equal(parlay.Id, id);
        Assert.Equal("fanduel", edit.Book);
        Assert.Equal(40m, edit.Stake);
        Assert.Equal(1100, edit.PriceQuoted);
        Assert.Equal(-115, edit.LegPrices![103]);
        Assert.Equal(-128, edit.LegPrices[101]);

        Assert.False(window.HasUnsavedInput);
        Assert.Empty(cut.FindAll(".pform--grouped"));
        Assert.Contains("Parlay saved.", cut.Markup);
    }

    /// <summary>The entry form's price rule, on the book's price and on each leg's: a number in the dead band is not a price.</summary>
    [Fact]
    public void A_price_that_is_not_a_price_says_so_and_save_says_what_it_waits_for()
    {
        var (cut, _) = Render();
        cut.Find("button[title='Edit this parlay']").Click();

        Row(cut, "Book's price").QuerySelector("input")!.Change("50");

        Assert.Equal("A price is −100 or lower, or +100 or higher.", Row(cut, "Book's price").QuerySelector(".field-err")!.TextContent.Trim());
        Assert.Equal("The book's price is −100 or lower, or +100 or higher.", cut.Find("#parlay-save-why").TextContent.Trim());
        var save = Key(cut, "Save changes");
        Assert.True(save.HasAttribute("disabled"));
        Assert.Equal("parlay-save-why", save.GetAttribute("aria-describedby"));

        // Emptied, the book's price is optional again; a leg's emptied price is not.
        Row(cut, "Book's price").QuerySelector("input")!.Change("");
        LegPrice(cut, 1).Change("");

        Assert.Equal("Enter the price this leg was taken at.", Row(cut, "Leg 1").QuerySelector(".field-err")!.TextContent.Trim());
        Assert.StartsWith("Each leg needs the price it was taken at", cut.Find("#parlay-save-why").TextContent.Trim());

        Key(cut, "Save changes").Click();
        Assert.Empty(_journal.Writes);
    }

    [Fact]
    public void A_stake_of_nothing_holds_save()
    {
        var (cut, _) = Render();
        cut.Find("button[title='Edit this parlay']").Click();

        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("0");

        Assert.Equal("Enter a stake above zero.", cut.Find(".pform--grouped .field-err").TextContent.Trim());
        Assert.Equal("Needs a stake above zero.", cut.Find("#parlay-save-why").TextContent.Trim());
    }

    [Fact]
    public void A_save_that_fails_keeps_the_form_and_what_was_typed()
    {
        var (cut, window) = Render();
        _journal.FailWith = new InvalidOperationException("connection refused");
        cut.Find("button[title='Edit this parlay']").Click();
        Row(cut, "Stake").QuerySelector(".stake-row__field")!.Input("40");

        Key(cut, "Save changes").Click();

        Assert.NotEmpty(cut.FindAll(".pform--grouped"));
        Assert.Equal("40", Row(cut, "Stake").QuerySelector(".stake-row__field")!.GetAttribute("value"));
        Assert.True(window.HasUnsavedInput);
        Assert.Contains("The parlay was not saved. Try again.", cut.Markup);
        Assert.DoesNotContain("connection refused", cut.Markup);
    }

    [Fact]
    public void The_form_marks_the_window_unsaved_through_the_entries_funnel()
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "Panels", "JournalPanel.razor"));

        Assert.Contains("MarkUnsaved(ParlayIsDirty(_parlay.ToEdit(), _parlayOpened), \"this parlay\")", source);
        Assert.Contains("<PanelForm Title=\"Edit parlay\" Class=\"pform--grouped\">", source);
    }

    // ------------------------------------------------------------------ duplicate ----

    [Fact]
    public void Duplicate_puts_each_leg_on_an_empty_slip_as_a_parlay_at_the_prices_taken()
    {
        var parlay = FourLegs();
        var (cut, _) = Render(parlay);

        cut.Find("button[title='Duplicate this parlay on the slip']").Click();

        Assert.Empty(Alerts.Asked);
        Assert.Equal(SlipMode.Parlay, Slip.Mode);
        Assert.Equal(25m, Slip.ParlayStake);
        Assert.Equal(1200, Slip.ParlayPriceQuoted);
        Assert.Equal("draftkings", Slip.ParlayBook);

        Assert.Equal(4, Slip.Items.Count);
        Assert.All(Slip.Items, i => Assert.True(i.AsLeg));
        Assert.All(Slip.Items, i => Assert.True(i.ManualPrice));
        Assert.All(Slip.Items, i => Assert.Equal("draftkings", i.Book));

        // A leg on a game that has started is the price taken, not a closing line: all four are legs.
        Assert.All(Slip.Items, i => Assert.False(i.IsClosing));
        Assert.Equal(4, Slip.Legs.Count);
        Assert.Equal(new[] { -128, -110, -105, 145 }, Slip.Items.Select(i => i.PriceAmerican));
        Assert.Equal(new[] { "Kansas City Chiefs", "Philadelphia Eagles", "over", "Miami Dolphins" }, Slip.Items.Select(i => i.Outcome));
        Assert.Equal(-3.5m, Slip.Items[1].Line);
        Assert.Equal("Dallas Cowboys at Philadelphia Eagles", Slip.Items[1].Matchup);
        Assert.Equal("nfl", Slip.Items[1].SportKey);

        // What the slip would log is the parlay again, at the book's price.
        var draft = Slip.ToParlayDraft();
        Assert.Equal(4, draft.Legs.Count);
        Assert.Equal(1200, Slip.CombinedPrice);

        Assert.Equal(new[] { "Copied the 4-leg parlay to the slip" }, Toasts());

        // The Journal turns to its Slip tab, where the copy is.
        Assert.NotEmpty(cut.FindAll(".slip"));
        Assert.Equal(4, cut.FindAll(".slip-leg").Count);
    }

    [Fact]
    public void Duplicate_asks_before_replacing_a_slip_that_holds_wagers_and_replaces_it_on_yes()
    {
        Slip.Add(new SlipItem(Guid.Empty, 7, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, "Buffalo Bills", null, "fanduel", 120, DateTimeOffset.UtcNow, false, 50m, null, null, null));
        Alerts.Answer = true;
        var (cut, _) = Render();

        cut.Find("button[title='Duplicate this parlay on the slip']").Click();

        Assert.Equal(new[] { "Replace the slip?" }, Alerts.Asked);
        Assert.Equal(new[] { "Replace" }, Alerts.ConfirmLabels);
        Assert.Equal(4, Slip.Items.Count);
        Assert.DoesNotContain(Slip.Items, i => i.Outcome == "Buffalo Bills");
        Assert.Equal(new[] { "Copied the 4-leg parlay to the slip" }, Toasts());
    }

    [Fact]
    public void Declining_the_replace_leaves_the_slip_as_it_was()
    {
        var banked = Slip.Add(new SlipItem(Guid.Empty, 7, "Buffalo Bills at Kansas City Chiefs", DateTimeOffset.UtcNow.AddHours(3), "nfl",
            Markets.Moneyline, "Buffalo Bills", null, "fanduel", 120, DateTimeOffset.UtcNow, false, 50m, null, null, null));
        Alerts.Answer = false;
        var (cut, _) = Render();

        cut.Find("button[title='Duplicate this parlay on the slip']").Click();

        Assert.Single(Alerts.Asked);
        Assert.Equal(banked.Id, Assert.Single(Slip.Items).Id);
        Assert.Equal(SlipMode.Straight, Slip.Mode);
        Assert.Empty(Toasts());
        Assert.Empty(cut.FindAll(".slip"));
    }

    /// <summary>
    /// The slip holds bets on tracked games at a price. A parlay with a described leg says it
    /// cannot be copied rather than putting a card on the slip that is not the bet.
    /// </summary>
    [Fact]
    public void A_parlay_with_a_described_leg_is_not_copied_and_says_why()
    {
        var parlay = FourLegs();
        parlay.Legs.Add(new JournalEntry
        {
            Id = 105,
            Market = "other",
            FreeTextMarket = "Player X 2+ hits",
            Outcome = "—",
            PriceTaken = 180,
            Book = "draftkings",
            ParlayGroupId = parlay.Id,
            PlacedAt = parlay.PlacedAt
        });
        var (cut, _) = Render(parlay);

        cut.Find("button[title='Duplicate this parlay on the slip']").Click();

        Assert.Empty(Slip.Items);
        Assert.Empty(Toasts());
        Assert.Contains("a leg of this parlay is not one. Nothing was copied.", cut.Markup);
    }
}
