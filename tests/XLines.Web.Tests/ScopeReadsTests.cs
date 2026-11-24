using XLines.Core.Entities;
using XLines.Data;
using XLines.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XLines.Web.Tests;

/// <summary>
/// The desk seams' scope per read (<see cref="ScopeReads"/>), and the Game window's detail load
/// honouring the token it is handed, so a superseded load stops instead of running to the end.
/// </summary>
public class ScopeReadsTests
{
    private sealed class Held : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task The_scope_outlives_the_read_it_was_opened_for()
    {
        var services = new ServiceCollection().AddScoped<Held>().BuildServiceProvider();
        var gate = new TaskCompletionSource<int>();
        Held? seen = null;

        var read = services.GetRequiredService<IServiceScopeFactory>().InScopeAsync<Held, int>(held =>
        {
            seen = held;
            return gate.Task;
        });

        // A helper that returned the read's task un-awaited would already have disposed the scope.
        Assert.NotNull(seen);
        Assert.False(seen.Disposed);

        gate.SetResult(7);
        Assert.Equal(7, await read);
        Assert.True(seen.Disposed);
    }

    [Fact]
    public async Task A_cancelled_detail_load_stops_at_its_first_read()
    {
        // Nothing listens on port 1: a read that ignored the token would try to connect and fail
        // with a connection error after its retries, never with a cancellation.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LineOps"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1"
            })
            .Build();

        var services = new ServiceCollection().AddLogging().AddXLinesData(configuration).BuildServiceProvider();
        var data = new GameWindowData(services.GetRequiredService<IServiceScopeFactory>());
        var game = new Game { Id = 1, HomeTeamId = 2, AwayTeamId = 3, SeasonYear = 2026, StartsAt = DateTimeOffset.UtcNow };

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // A team sport with no bets: the first read is the home side's seasons, which took no token.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => data.DetailAsync(game, seasonYear: null, withBets: false, cancelled.Token));
    }
}
