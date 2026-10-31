using Bunit;
using LineOps.Desk.Primitives;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Desk.Tests;

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
        => Services.GetRequiredService<ISnackbar>().ShownSnackbars.Select(s => s.Message ?? string.Empty).ToList();

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

        var toast = Assert.Single(Services.GetRequiredService<ISnackbar>().ShownSnackbars);
        Assert.Equal(summary, toast.Message);
        Assert.Equal(result switch
        {
            DeskJobResult.Done => Severity.Success,
            DeskJobResult.Nothing => Severity.Info,
            _ => Severity.Warning
        }, toast.Severity);
    }

    [Fact]
    public void A_run_that_throws_says_it_did_not_finish()
    {
        _jobs.Throws = true;
        var cut = Open(Menu());

        cut.FindAll("[role=menuitem]")[0].Click();

        Assert.Equal(["Refresh the index did not finish. Try again."], Toasts());
        Assert.Equal(Severity.Error, Services.GetRequiredService<ISnackbar>().ShownSnackbars.Single().Severity);
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

        public Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default)
        {
            Described++;

            IReadOnlyList<DeskJob> jobs = Only is { } only
                ? [only]
                :
                [
                    new("index", "Refresh the index", "Reads every record again", "4 units", true, null),
                    new("archive", "Copy to the archive", "Keeps a copy elsewhere", "1 unit", false, "No archive is configured."),
                    new("tidy", "Tidy up", "Clears what has expired", null, true, null)
                ];

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
