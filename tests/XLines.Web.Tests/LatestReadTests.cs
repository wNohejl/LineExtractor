using XLines.Web.Components.Panels;

namespace XLines.Web.Tests;

/// <summary>
/// The helper every panel's load goes through, on its own: the panels' tests prove each uses it,
/// and these prove what it promises them — the next read cancels the last, a closed panel's reads
/// are all cancelled, including one that starts after it closed, and only the latest hands over.
/// </summary>
public class LatestReadTests
{
    [Fact]
    public void The_next_read_cancels_the_one_before_it_and_no_other()
    {
        var reads = new LatestRead();

        var first = reads.Next();
        var second = reads.Next();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
        Assert.False(reads.Gone.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_stops_the_read_in_flight_and_the_reads_that_are_not_a_load()
    {
        var reads = new LatestRead();
        var load = reads.Next();

        reads.Cancel();
        reads.Cancel();

        Assert.True(load.IsCancellationRequested);
        Assert.True(reads.Gone.IsCancellationRequested);
    }

    [Fact]
    public void A_read_started_after_the_panel_has_gone_is_cancelled_as_it_starts()
    {
        // A refresh whose own-game check came back as the window closed goes on to load: with
        // nothing left to cancel it, that read ran to its end for a window nobody has.
        var reads = new LatestRead();
        reads.Cancel();

        Assert.True(reads.Next().IsCancellationRequested);
    }

    [Fact]
    public async Task Only_a_read_that_is_still_the_latest_hands_over()
    {
        var reads = new LatestRead();
        var handed = new List<int>();

        var slow = new TaskCompletionSource<int>();
        var first = reads.Next();
        var superseded = LatestRead.HandOverAsync(first, () => slow.Task, handed.Add);

        await LatestRead.HandOverAsync(reads.Next(), () => Task.FromResult(2), handed.Add);

        // The slower read lands last, and anyway: it was superseded, so it hands over nothing.
        slow.SetResult(1);
        await superseded;

        Assert.Equal([2], handed);
    }

    [Fact]
    public async Task A_cancelled_read_returns_quietly_and_any_other_failure_propagates()
    {
        var reads = new LatestRead();

        var cancelled = reads.Next();
        reads.Next();
        await LatestRead.HandOverAsync<int>(cancelled,
            () => Task.FromCanceled<int>(cancelled), _ => Assert.Fail("A superseded read handed over."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => LatestRead.HandOverAsync<int>(reads.Next(),
            () => Task.FromException<int>(new InvalidOperationException("The database refused.")), _ => { }));
    }
}
