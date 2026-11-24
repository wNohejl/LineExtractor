using Bunit;
using XLines.Desk.Primitives;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace XLines.Desk.Tests;

/// <summary>
/// The job menu: a key that opens the application's named runs, says what each costs before it
/// is chosen, keeps an unavailable one in view with its reason, and reports how a run went as a
/// toast. The runs themselves are a fake here — the menu knows nothing of what they do.
/// </summary>
public class DeskJobMenuTests : DeskTestContext
{
    private readonly FakeJobs _jobs = new();

    public DeskJobMenuTests()
    {
        Services.AddScoped<DeskToasts>();
        Services.AddScoped<DeskJobRunner>();
        Services.AddSingleton<IDeskJobs>(_jobs);
    }

    private IRenderedComponent<DeskJobMenu> Menu(Action<ComponentParameterCollectionBuilder<DeskJobMenu>>? more = null)
        => RenderComponent<DeskJobMenu>(p =>
        {
            p.Add(x => x.Label, "Run");
            more?.Invoke(p);
        });

    private static IRenderedComponent<DeskJobMenu> Open(IRenderedComponent<DeskJobMenu> cut)
    {
        cut.Find("button.desk-btn").Click();
        return cut;
    }

    private List<string> Toasts()
        => Services.GetRequiredService<DeskToasts>().Shown.Select(s => s.Message).ToList();

    [Fact]
    public void Lists_each_run_with_its_cost()
    {
        var cut = Open(Menu());

        var rows = cut.FindAll("[role=menuitem]");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Refresh the index", rows[0].QuerySelector(".menu__label")?.TextContent);
        Assert.Equal("4 units", rows[0].QuerySelector(".menu__detail")?.TextContent);
        Assert.Equal("Reads every record again", rows[0].QuerySelector(".menu__desc")?.TextContent);
        Assert.Null(rows[2].QuerySelector(".menu__detail"));
    }

    [Fact]
    public void An_unavailable_run_is_dimmed_with_its_reason()
    {
        var cut = Open(Menu());

        var row = cut.FindAll("[role=menuitem]")[1];
        Assert.Equal("true", row.GetAttribute("aria-disabled"));
        Assert.Equal("No archive is configured.", row.QuerySelector(".menu__desc")?.TextContent);
    }

    [Theory]
    [InlineData(DeskJobResult.Done, "Refreshed 12 records.")]
    [InlineData(DeskJobResult.Nothing, "Nothing new.")]
    [InlineData(DeskJobResult.Failed, "One source failed.")]
    public void Each_result_toasts_its_own_way(DeskJobResult result, string summary)
    {
        _jobs.Next = new DeskJobOutcome(result, summary);
        var cut = Open(Menu());

        cut.FindAll("[role=menuitem]")[0].Click();

        var toast = Assert.Single(Services.GetRequiredService<DeskToasts>().Shown);
        Assert.Equal(summary, toast.Message);
        Assert.Equal(result switch
        {
            DeskJobResult.Done => DeskState.Positive,
            DeskJobResult.Nothing => DeskState.Info,
            _ => DeskState.Warning
        }, toast.State);
    }

    [Fact]
    public void A_run_that_throws_says_it_did_not_finish()
    {
        _jobs.Throws = true;
        var cut = Open(Menu());

        cut.FindAll("[role=menuitem]")[0].Click();

        Assert.Equal(["Refresh the index did not finish. Try again."], Toasts());
        Assert.Equal(DeskState.Negative, Services.GetRequiredService<DeskToasts>().Shown.Single().State);
    }

    [Fact]
    public async Task RunAsync_on_an_unavailable_key_opens_the_menu()
    {
        var cut = Menu();
        Assert.Empty(cut.FindAll("[role=menu]"));

        await cut.InvokeAsync(() => cut.Instance.RunAsync("archive"));

        Assert.Single(cut.FindAll("[role=menu]"));
        Assert.Empty(_jobs.Ran);
    }

    [Fact]
    public async Task RunAsync_on_an_unknown_key_opens_the_menu()
    {
        var cut = Menu();

        await cut.InvokeAsync(() => cut.Instance.RunAsync("nowhere"));

        Assert.Single(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public async Task RunAsync_on_an_available_key_runs_it()
    {
        var cut = Menu();

        await cut.InvokeAsync(() => cut.Instance.RunAsync("index"));

        Assert.Equal(["index"], _jobs.Ran);
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void A_visitor_sees_no_menu()
    {
        Services.AddSingleton<IDeskAccess>(new Visitor());

        var cut = Menu();

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Opening_rereads_the_runs()
    {
        var cut = Menu();
        var before = _jobs.Described;

        Open(cut);

        Assert.True(_jobs.Described > before);
    }

    [Fact]
    public void OnRan_and_OnBusyChanged_fire()
    {
        var ran = 0;
        var busy = new List<bool>();
        var cut = Open(Menu(p => p
            .Add(x => x.OnRan, () => ran++)
            .Add(x => x.OnBusyChanged, (bool b) => busy.Add(b))));

        cut.FindAll("[role=menuitem]")[0].Click();

        Assert.Equal(1, ran);
        Assert.Equal([true, false], busy);
    }

    [Fact]
    public void Jobs_given_as_a_parameter_win_over_the_registered_ones()
    {
        var other = new FakeJobs { Only = new DeskJob("other", "Other run", "Somewhere else", null, true, null) };

        var cut = Open(Menu(p => p.Add(x => x.Jobs, other)));

        Assert.Equal("Other run", cut.Find("[role=menuitem] .menu__label").TextContent);
    }

    [Fact]
    public async Task The_runner_runs_an_available_key_and_toasts_it()
    {
        _jobs.Next = new DeskJobOutcome(DeskJobResult.Done, "Refreshed 12 records.");

        Assert.True(await Services.GetRequiredService<DeskJobRunner>().RunAsync("index"));

        Assert.Equal(["index"], _jobs.Ran);
        Assert.Equal(["Refreshed 12 records."], Toasts());
    }

    [Fact]
    public async Task The_runner_says_why_an_unavailable_key_did_not_run()
    {
        Assert.False(await Services.GetRequiredService<DeskJobRunner>().RunAsync("archive"));

        Assert.Empty(_jobs.Ran);
        Assert.Equal(["No archive is configured."], Toasts());
    }

    [Fact]
    public async Task The_runner_runs_nothing_for_a_visitor()
    {
        Services.AddSingleton<IDeskAccess>(new Visitor());

        Assert.False(await Services.GetRequiredService<DeskJobRunner>().RunAsync("index"));

        Assert.Empty(_jobs.Ran);
        Assert.Empty(Toasts());
    }

    /// <summary>A run that asks for something first: its entry opens the sheet that asks.</summary>
    private static DeskJob Asking => new("ranged", "Choose a range…", "Asks which first", null, true, null,
        Sheet: sheets => sheets.ShowAsync(new DialogParameters<DeskSheet>()));

    [Fact]
    public void A_run_that_asks_first_opens_its_sheet_and_runs_nothing_yet()
    {
        var sheets = new FakeSheets();
        Services.AddSingleton<IDeskSheets>(sheets);
        var cut = Open(Menu(p => p.Add(x => x.Jobs, new FakeJobs { Only = Asking })));

        cut.Find("[role=menuitem]").Click();

        Assert.Equal(typeof(DeskSheet), sheets.Shown);
        Assert.Empty(cut.FindAll("[role=menu]"));
        Assert.Empty(Toasts());
    }

    [Fact]
    public void The_run_a_sheet_completes_with_runs_as_a_chosen_entry_does()
    {
        var sheets = new FakeSheets();
        Services.AddSingleton<IDeskSheets>(sheets);
        var ran = 0;
        var busy = new List<bool>();
        var cut = Open(Menu(p => p
            .Add(x => x.Jobs, new FakeJobs { Only = Asking })
            .Add(x => x.OnRan, () => ran++)
            .Add(x => x.OnBusyChanged, (bool b) => busy.Add(b))));
        cut.Find("[role=menuitem]").Click();

        DeskJobRun run = (progress, _) =>
        {
            progress.Report("Step 2 of 3");
            return Task.FromResult(new DeskJobOutcome(DeskJobResult.Done, "Ranged over 3 days."));
        };
        cut.InvokeAsync(() => sheets.Last!.Close(DialogResult.Ok(run)));

        cut.WaitForAssertion(() => Assert.Equal(["Ranged over 3 days."], Toasts()));
        Assert.Equal(1, ran);
        Assert.Equal([true, false], busy);
    }

    [Fact]
    public void A_sheet_dismissed_runs_nothing()
    {
        var sheets = new FakeSheets();
        Services.AddSingleton<IDeskSheets>(sheets);
        var busy = new List<bool>();
        var cut = Open(Menu(p => p
            .Add(x => x.Jobs, new FakeJobs { Only = Asking })
            .Add(x => x.OnBusyChanged, (bool b) => busy.Add(b))));
        cut.Find("[role=menuitem]").Click();

        cut.InvokeAsync(() => sheets.Last!.Close());

        Assert.Empty(busy);
        Assert.Empty(Toasts());
    }

    [Fact]
    public void Runs_are_drawn_under_their_group_headings_in_the_order_the_groups_first_appear()
    {
        var jobs = new FakeJobs
        {
            Many =
            [
                new("a", "Read the inbox", "", null, true, null, Group: "Now"),
                new("b", "Rebuild the index", "", null, true, null, Group: "Upkeep"),
                new("c", "Tidy up", "", null, true, null),
                new("d", "Read the outbox", "", null, true, null, Group: "Now"),
                new("e", "Copy to the archive", "", "1 unit", true, null, Group: "Upkeep")
            ]
        };

        var cut = Open(Menu(p => p.Add(x => x.Jobs, jobs)));

        var groups = cut.FindAll("[role=group]");
        Assert.Equal(3, groups.Count);

        // The ungrouped run leads, unheaded; then each named group, its runs in their order.
        Assert.Empty(groups[0].QuerySelectorAll(".menu__heading"));
        Assert.Equal(["Tidy up"], Labels(groups[0]));
        Assert.Equal("Now", groups[1].QuerySelector(".menu__heading")?.TextContent);
        Assert.Equal(["Read the inbox", "Read the outbox"], Labels(groups[1]));
        Assert.Equal("Upkeep", groups[2].QuerySelector(".menu__heading")?.TextContent);
        Assert.Equal(["Rebuild the index", "Copy to the archive"], Labels(groups[2]));

        // The arrows follow what is drawn, not the order the runs were described in.
        Assert.Equal(["Tidy up", "Read the inbox", "Read the outbox", "Rebuild the index", "Copy to the archive"],
            cut.FindAll("[role=menuitem] .menu__label").Select(l => l.TextContent));
    }

    [Fact]
    public void A_menu_of_ungrouped_runs_draws_no_heading_and_no_hairline()
    {
        var cut = Open(Menu());

        Assert.Single(cut.FindAll("[role=group]"));
        Assert.Empty(cut.FindAll(".menu__heading"));
        Assert.Empty(cut.FindAll(".menu__sep"));
    }

    [Fact]
    public void The_job_menu_opens_wide_enough_for_its_costs()
    {
        var cut = Open(Menu());

        Assert.Contains("menu--wide", cut.Find("[role=menu]").ClassList);
    }

    // ---- A run that asks first ----------------------------------------------------------------

    private static DeskJob Spend => new("spend", "Spend units", "Uses the metered service", "9 units", true, null,
        Confirm: "This spends 9 units and leaves 41. Spend them?", Hint: "Calls the metered service; units come back monthly.");

    [Fact]
    public void A_run_that_asks_first_asks_beside_the_key_and_spends_nothing_until_confirmed()
    {
        var jobs = new FakeJobs { Many = [Spend, new("free", "Read the list", "Costs nothing", "free", true, null)] };
        var cut = Open(Menu(p => p.Add(x => x.Jobs, jobs)));

        cut.FindAll("[role=menuitem]")[0].Click();

        Assert.Empty(jobs.Ran);
        var confirm = cut.Find(".confirm");
        Assert.Contains("This spends 9 units and leaves 41. Spend them?", confirm.TextContent);
        Assert.Equal(["Cancel", "Spend units"], confirm.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));

        confirm.QuerySelectorAll("button")[1].Click();

        Assert.Equal(["spend"], jobs.Ran);
        Assert.Empty(cut.FindAll(".confirm"));
    }

    [Fact]
    public void Cancel_runs_nothing_and_puts_the_question_away()
    {
        var jobs = new FakeJobs { Only = Spend };
        var cut = Open(Menu(p => p.Add(x => x.Jobs, jobs)));

        cut.FindAll("[role=menuitem]")[0].Click();
        cut.Find(".confirm button").Click();

        Assert.Empty(jobs.Ran);
        Assert.Empty(cut.FindAll(".confirm"));
    }

    [Fact]
    public void A_run_that_asks_nothing_runs_on_the_press()
    {
        var jobs = new FakeJobs { Only = new("free", "Read the list", "Costs nothing", "free", true, null) };
        var cut = Open(Menu(p => p.Add(x => x.Jobs, jobs)));

        cut.FindAll("[role=menuitem]")[0].Click();

        Assert.Equal(["free"], jobs.Ran);
        Assert.Empty(cut.FindAll(".confirm"));
    }

    [Fact]
    public void A_runs_hint_is_its_rows_tooltip_and_a_dimmed_rows_is_its_reason()
    {
        var jobs = new FakeJobs { Many = [Spend, Spend with { Key = "off", Available = false, Unavailable = "None left this month." }] };
        var cut = Open(Menu(p => p.Add(x => x.Jobs, jobs)));

        var rows = cut.FindAll("[role=menuitem]");
        Assert.Equal("Calls the metered service; units come back monthly.", rows[0].GetAttribute("title"));
        Assert.Equal("None left this month.", rows[1].GetAttribute("title"));

        // The price is text in the row as well: the tooltip explains, it never carries the cost alone.
        Assert.Equal("9 units", rows[0].QuerySelector(".menu__detail")?.TextContent);
    }

    [Fact]
    public async Task A_key_outside_the_menu_never_starts_a_run_that_asks_first()
    {
        Services.AddSingleton<IDeskJobs>(new FakeJobs { Only = Spend });
        var runner = Services.GetRequiredService<DeskJobRunner>();

        Assert.False(await runner.RunAsync("spend"));
        Assert.Contains(Toasts(), t => t.Contains("asks before it runs"));
    }

    private static IEnumerable<string> Labels(AngleSharp.Dom.IElement group)
        => group.QuerySelectorAll(".menu__label").Select(l => l.TextContent);

    private sealed class Visitor : IDeskAccess
    {
        public bool CanOperate => false;
    }

    private sealed class FakeJobs : IDeskJobs
    {
        public int Described { get; private set; }
        public List<string> Ran { get; } = [];
        public DeskJobOutcome Next { get; set; } = new(DeskJobResult.Done, "Done.");
        public bool Throws { get; set; }
        public DeskJob? Only { get; init; }
        public IReadOnlyList<DeskJob>? Many { get; init; }

        public Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
        {
            Described++;

            IReadOnlyList<DeskJob> jobs = Many ?? (Only is { } only
                ? [only]
                :
                [
                    new("index", "Refresh the index", "Reads every record again", "4 units", true, null),
                    new("archive", "Copy to the archive", "Keeps a copy elsewhere", "1 unit", false, "No archive is configured."),
                    new("tidy", "Tidy up", "Clears what has expired", null, true, null)
                ]);

            return Task.FromResult(jobs);
        }

        public Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
        {
            Ran.Add(key);

            if (Throws)
                throw new InvalidOperationException("the run broke");

            return Task.FromResult(Next);
        }
    }
}
