using Bunit;
using XLines.Core.Entities;
using XLines.Data.CrossReference;
using XLines.Web.Components.Snippets;
using XLines.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// A glance whose row is re-aimed or closed while it is still reading cancels that read, and
/// nothing the read brings back lands on the answer: a slow first game cannot overwrite the
/// second, and a closed row stops spending the database's time.
/// </summary>
public class SnippetSupersededReadTests : DeskTestContext
{
    /// <summary>Game 1 never answers until cancelled; game 2 is not on the board.</summary>
    private sealed class SlowFirstGame : IMatchupData
    {
        public CancellationToken FirstToken { get; private set; }

        public Task<BoardRow?> RowAsync(int gameId, CancellationToken ct = default)
        {
            if (gameId != 1)
                return Task.FromResult<BoardRow?>(null);

            FirstToken = ct;
            var pending = new TaskCompletionSource<BoardRow?>();
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        }

        public Task<IReadOnlyList<OddsSnapshot>> SnapshotsAsync(int gameId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OddsSnapshot>>([]);

        public Task<IReadOnlyList<TeamGameLogRow>> SeasonAsync(int teamId, int seasonYear, SeasonType seasonType,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TeamGameLogRow>>([]);
    }

    [Fact]
    public void A_re_aimed_form_glance_cancels_the_read_it_moved_on_from()
    {
        var reads = new SlowFirstGame();
        Services.AddSingleton<IMatchupData>(reads);

        var cut = RenderComponent<FormSnippet>(p => p.Add(x => x.GameId, 1));
        Assert.False(reads.FirstToken.IsCancellationRequested);

        cut.SetParametersAndRender(p => p.Add(x => x.GameId, 2));

        Assert.True(reads.FirstToken.IsCancellationRequested);
        cut.WaitForAssertion(() => Assert.Contains("This game is not on the board.", cut.Find(".note").TextContent));
        Assert.DoesNotContain("did not load", cut.Markup);
    }

    [Fact]
    public void A_closed_form_glance_cancels_its_read()
    {
        var reads = new SlowFirstGame();
        Services.AddSingleton<IMatchupData>(reads);

        RenderComponent<FormSnippet>(p => p.Add(x => x.GameId, 1));
        DisposeComponents();

        Assert.True(reads.FirstToken.IsCancellationRequested);
    }
}
