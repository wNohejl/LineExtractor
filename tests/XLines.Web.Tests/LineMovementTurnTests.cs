using Bunit;
using XLines.Core.Entities;
using XLines.Data;
using XLines.Data.CrossReference;
using XLines.Ingestion.Services;
using XLines.Web.Components.Panels;
using XLines.Web.Services;
using XLines.Web.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// Line movement turned to another game. Its reads ran one after another and nothing was handed
/// over until the last was back, so a Board press renamed the window at once and left the last
/// game's moves and rosters in it for as long as six reads took. The reads now go out together:
/// the last game's rows leave as the window turns, the moves are drawn as soon as they are in,
/// and the form follows without holding them.
/// </summary>
public class LineMovementTurnTests : WagerTestContext
{
    public LineMovementTurnTests()
    {
        Services.AddSingleton<IDbContextFactory<XLinesDbContext>>(new UnreachableFactory());
        Services.AddSingleton<RangePull>();
        Services.AddScoped<XLines.Desk.DeskJobRunner>();
        Services.AddSingleton<ILineMovementData>(_reads);
        Services.AddSingleton<XLines.Desk.IDeskJobs>(new NoJobs());
    }

    /// <summary>Bills at Chiefs (7) and Jets at Dolphins (8); a game's moves or its form can be held open.</summary>
    private readonly FakeLineMovementData _reads = new();

    private sealed class NoJobs : XLines.Desk.IDeskJobs
    {
        public Task<IReadOnlyList<XLines.Desk.DeskJob>> DescribeAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<XLines.Desk.DeskJob>>([]);

        public Task<XLines.Desk.DeskJobOutcome> RunAsync(string key, CancellationToken ct = default)
            => Task.FromResult(new XLines.Desk.DeskJobOutcome(XLines.Desk.DeskJobResult.Done, "Done."));
    }

    /// <summary>The side switch's labels: the clubs the window's rows are about.</summary>
    private static List<string> Sides(IRenderedFragment panel)
        => panel.FindAll("[role=radio]").Select(r => r.TextContent.Trim())
            .Where(t => t is not ("Moneyline" or "Spread" or "Total")).ToList();

    private IRenderedComponent<OddsPanel> OnTheChiefs()
    {
        var panel = RenderComponent<OddsPanel>(p => p.Add(x => x.GameId, BoardRows.GameId).Add(x => x.Press, 1));
        panel.WaitForAssertion(() => Assert.Contains("Chiefs", Sides(panel)), Settle);
        return panel;
    }

    [Fact]
    public void Turned_to_another_game_the_last_game_s_moves_leave_before_the_new_ones_land()
    {
        var panel = OnTheChiefs();
        _reads.SnapshotGates[8] = new TaskCompletionSource();

        panel.SetParametersAndRender(p => p.Add(x => x.GameId, 8).Add(x => x.Press, 2));

        // Asked for and not back: the Chiefs' rows are not the Dolphins', and nothing says
        // "No stored prices" of a game still being read.
        Assert.DoesNotContain("Chiefs", Sides(panel));
        Assert.NotEmpty(panel.FindAll("[aria-busy=true]"));
        Assert.DoesNotContain("No stored prices", panel.Markup);

        _reads.SnapshotGates[8].SetResult();

        panel.WaitForAssertion(() => Assert.Contains("Dolphins", Sides(panel)), Settle);
        Assert.Empty(panel.FindAll("[aria-busy=true]"));
    }

    [Fact]
    public void The_moves_are_drawn_without_waiting_for_the_form()
    {
        var panel = OnTheChiefs();
        _reads.FormGates[8] = new TaskCompletionSource();

        panel.SetParametersAndRender(p => p.Add(x => x.GameId, 8).Add(x => x.Press, 2));

        // The form is still being read; the moves are in and shown.
        panel.WaitForAssertion(() => Assert.Contains("Dolphins", Sides(panel)), Settle);
        Assert.False(_reads.FormGates[8].Task.IsCompleted);

        _reads.FormGates[8].SetResult();
    }
}
