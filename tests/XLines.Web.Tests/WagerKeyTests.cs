using System.Text.RegularExpressions;
using Bunit;
using XLines.Desk.Primitives;
using XLines.Web.Components.Desk;
using XLines.Web.Tests.Support;

namespace XLines.Web.Tests;

/// <summary>
/// The wager key: one component wherever a wager is started or committed, with its label, glyph
/// and emphasis decided by what it does and its words by the state it is in. And the convention
/// that keeps it the only one: no wager surface draws its own wager key or picks a price with
/// anything but the shared picker.
/// </summary>
public class WagerKeyTests : DeskTestContext
{
    private IRenderedComponent<WagerKey> Key(WagerKind kind, Action<ComponentParameterCollectionBuilder<WagerKey>>? more = null)
        => RenderComponent<WagerKey>(p =>
        {
            p.Add(x => x.Kind, kind);
            more?.Invoke(p);
        });

    private static string Text(IRenderedFragment cut) => cut.Find("button").TextContent.Trim();

    [Fact]
    public void Log_names_its_stake_once_there_is_one()
    {
        using var dollars = new UsDollars();

        Assert.Equal("Log wager", Text(Key(WagerKind.Log)));
        Assert.Equal($"Log {25m:C0}", Text(Key(WagerKind.Log, p => p.Add(x => x.Stake, 25m))));
    }

    [Fact]
    public void Each_kind_has_its_label_and_default_emphasis()
    {
        Assert.Equal(WagerKey.PlaceLabel, Text(Key(WagerKind.Place)));
        Assert.Contains("desk-btn--tinted", Key(WagerKind.Place).Find("button").ClassName);

        Assert.Contains("desk-btn--filled", Key(WagerKind.Log).Find("button").ClassName);

        Assert.Equal("Add to slip", Text(Key(WagerKind.AddToSlip)));
        Assert.Contains("desk-btn--plain", Key(WagerKind.AddToSlip).Find("button").ClassName);

        Assert.Equal("Add as leg", Text(Key(WagerKind.AddLeg)));
    }

    [Fact]
    public void The_caller_may_set_the_emphasis_and_label()
    {
        var cut = Key(WagerKind.AddLeg, p => p.Add(x => x.Label, "Add leg").Add(x => x.Emphasis, DeskEmphasis.Filled));

        Assert.Equal("Add leg", Text(cut));
        Assert.Contains("desk-btn--filled", cut.Find("button").ClassName);
    }

    [Fact]
    public void Waiting_it_is_disabled_and_described_by_the_reason_not_a_title()
    {
        var cut = Key(WagerKind.Log, p => p.Add(x => x.Disabled, true).Add(x => x.DescribedBy, "keyrow-why"));

        var button = cut.Find("button");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("keyrow-why", button.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Busy_it_says_what_it_is_doing()
    {
        var cut = Key(WagerKind.Log, p => p.Add(x => x.Stake, 25m).Add(x => x.Busy, true));

        Assert.Equal("Logging…", Text(cut));
        Assert.Contains("desk-btn--busy", cut.Find("button").ClassName);
    }

    [Fact]
    public void Done_it_says_what_it_did_and_takes_no_press()
    {
        var log = Key(WagerKind.Log, p => p.Add(x => x.Done, true));
        Assert.Equal("Logged", Text(log));
        Assert.True(log.Find("button").HasAttribute("disabled"));

        Assert.Equal("Added", Text(Key(WagerKind.AddToSlip, p => p.Add(x => x.Done, true))));
    }

    [Fact]
    public void A_count_is_heard_with_its_noun()
    {
        var cut = Key(WagerKind.AddToSlip, p => p.Add(x => x.Count, 3).Add(x => x.CountLabel, "3 on the slip"));

        var badge = cut.Find(".slip-badge");
        Assert.Equal("3", badge.TextContent);
        Assert.Equal("3 on the slip", badge.GetAttribute("aria-label"));
    }

    [Fact]
    public void It_names_its_kind_for_the_convention()
    {
        Assert.Equal("log", Key(WagerKind.Log).Find("button").GetAttribute("data-wager-key"));
        Assert.Equal("place", Key(WagerKind.Place).Find("button").GetAttribute("data-wager-key"));
    }

    // ---- The convention ----------------------------------------------------------------------

    private static string Components => Path.Combine(Repo.Root(), "src", "XLines.Web", "Components");

    /// <summary>
    /// A key that places, logs, banks or adds a wager is a WagerKey. Read from the source: a
    /// DeskButton whose label is one of the wager keys' words is a key drawn by hand, which is how
    /// the take's and the window's keys drifted apart. The confirm inside InlineConfirm is that
    /// primitive's own key, and "Log entry" for a journal entry is not a wager key.
    /// </summary>
    [Fact]
    public void No_wager_key_is_drawn_by_hand()
    {
        var offenders = new List<string>();
        var words = new Regex(@"^\s*(Place wager|Log wager|Log @|Log \$|Add to slip|Add as leg|Add leg)", RegexOptions.Multiline);

        foreach (var file in Directory.EnumerateFiles(Components, "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match button in Regex.Matches(text, @"<DeskButton\b[^>]*>(?<label>.*?)</DeskButton>", RegexOptions.Singleline))
            {
                if (words.IsMatch(button.Groups["label"].Value))
                    offenders.Add($"{Path.GetFileName(file)}:{text[..button.Index].Count(c => c == '\n') + 1}");
            }
        }

        Assert.True(offenders.Count == 0, "A wager key drawn as a DeskButton, not a WagerKey:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Wherever a wager's price is chosen from the books quoting a side (WagerOffer.Books), it is
    /// chosen with the PricePicker: never a segmented control of books, a select of books, or a
    /// pop-up built again by hand.
    /// </summary>
    [Fact]
    public void Every_price_is_chosen_with_the_price_picker()
    {
        var offenders = new List<string>();
        var choosers = 0;

        foreach (var file in Directory.EnumerateFiles(Components, "*.razor", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name == "PricePicker.razor")
                continue;

            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"WagerOffer\.Books\(|\bBooks=""Books"""))
                continue;

            choosers++;

            if (!text.Contains("<PricePicker"))
                offenders.Add($"{name}: lists a side's books without the PricePicker");

            if (Regex.IsMatch(text, @"<(DeskSwitch|DeskSelect|DeskPopUp)\b[^>]*(Book|book)", RegexOptions.Singleline))
                offenders.Add($"{name}: chooses a book with a control of its own");

            if (text.Contains("<BookPicker"))
                offenders.Add($"{name}: the retired BookPicker");
        }

        Assert.True(choosers >= 2, "Expected the take and the wager window to choose a price.");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>The Board row's Place wager is drawn by the Desk's strip, so it takes the key's own words and glyph.</summary>
    [Fact]
    public void The_board_rows_place_wager_speaks_as_the_wager_key()
    {
        var board = File.ReadAllText(Path.Combine(Components, "Panels", "BoardPanel.razor"));

        Assert.Contains("Label = WagerKey.PlaceLabel", board);
        Assert.Contains("Icon = WagerKey.Glyph", board);
        Assert.DoesNotContain("Label = \"Place wager\"", board);
    }
}
