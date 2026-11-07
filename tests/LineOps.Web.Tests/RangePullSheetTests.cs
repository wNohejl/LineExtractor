using Bunit;
using LineOps.Desk;
using LineOps.Desk.Primitives;
using LineOps.Ingestion.Services;
using LineOps.Web.Components.Desk;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// The Pull data menu's "Pull a date range…" sheet: Pull waits, with its reason on the row, until
/// the answers are a range a pull takes — a league named and From not after To — and a sheet
/// answered completes with the run the menu then makes.
/// </summary>
public class RangePullSheetTests : DeskTestContext
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    private readonly List<RangePullRequest> _asked = [];

    private Task<RangePullSummary> Pull(RangePullRequest r, IProgress<RangePullProgress> p, CancellationToken ct)
    {
        _asked.Add(r);
        p.Report(new RangePullProgress(r.Sport, r.From, 0, 1, 0, 0));
        return Task.FromResult(new RangePullSummary(r.Sport, r.From, r.To, 1, 1, true, 1, 0, 2, 2, 40, 8, 4, 0, null));
    }

    private (IRenderedFragment Provider, IDeskSheetHandle Handle) Open(IReadOnlyList<RangeLeague> leagues)
    {
        Services.AddScoped<IDeskSheets, DeskSheets>();
        var provider = RenderComponent<MudDialogProvider>();
        var sheets = Services.GetRequiredService<IDeskSheets>();

        IDeskSheetHandle? handle = null;
        provider.InvokeAsync(async () => handle = await sheets.ShowAsync(new DialogParameters<RangePullSheet>
        {
            { x => x.Leagues, leagues },
            { x => x.Pull, Pull },
            { x => x.Today, Today }
        }));

        provider.WaitForState(() => handle is not null && provider.FindAll(".desk-sheet").Count > 0);
        return (provider, handle!);
    }

    private static AngleSharp.Dom.IElement PullKey(IRenderedFragment provider)
        => provider.FindAll("button").Single(b => b.TextContent.Trim() == "Pull");

    private static string? Reason(IRenderedFragment provider)
        => provider.FindAll(".keyrow__reason").SingleOrDefault()?.TextContent;

    [Fact]
    public void A_range_ready_to_pull_has_no_reason_and_an_enabled_key()
    {
        var (provider, _) = Open([new RangeLeague("nfl", "NFL")]);

        Assert.Null(Reason(provider));
        Assert.False(PullKey(provider).HasAttribute("disabled"));
    }

    [Fact]
    public void From_after_To_waits_and_says_why()
    {
        var (provider, _) = Open([new RangeLeague("nfl", "NFL")]);

        provider.Find("input[aria-label=From]").Change("2024-09-30");
        provider.Find("input[aria-label=To]").Change("2024-09-01");

        Assert.Equal("From (2024-09-30) is after To (2024-09-01).", Reason(provider));
        Assert.True(PullKey(provider).HasAttribute("disabled"));
    }

    [Fact]
    public void No_league_waits_and_says_why()
    {
        var (provider, _) = Open([]);

        Assert.Equal("Name a league.", Reason(provider));
        Assert.True(PullKey(provider).HasAttribute("disabled"));
    }

    [Fact]
    public void Choosing_no_league_waits_too()
    {
        var (provider, _) = Open([new RangeLeague("nfl", "NFL"), new RangeLeague("mlb", "MLB")]);

        provider.Find("select[aria-label=League]").Change("");

        Assert.Equal("Name a league.", Reason(provider));
    }

    [Fact]
    public async Task Pull_completes_the_sheet_with_the_run_for_the_answers_given()
    {
        var (provider, handle) = Open([new RangeLeague("nfl", "NFL"), new RangeLeague("mlb", "MLB")]);

        provider.Find("select[aria-label=League]").Change("mlb");
        provider.Find("input[aria-label=From]").Change("2025-04-01");
        provider.Find("input[aria-label=To]").Change("2025-04-30");
        provider.Find("input[role=switch]").Change(true);
        PullKey(provider).Click();

        var result = await handle.Result;
        var run = Assert.IsType<DeskJobRun>(result.Data);
        var phrases = new List<string>();
        var outcome = await run(new SyncProgress(phrases.Add), CancellationToken.None);

        Assert.Equal(new RangePullRequest("mlb", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), true), Assert.Single(_asked));
        Assert.Equal(["MLB Apr 1 · 1 of 1"], phrases);
        Assert.Equal(DeskJobResult.Done, outcome.Result);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
