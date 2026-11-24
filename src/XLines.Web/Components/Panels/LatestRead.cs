namespace XLines.Web.Components.Panels;

/// <summary>
/// A panel's reads, of which only the latest may hand over: each load takes a token from
/// <see cref="Next"/>, which cancels the one before it, so a slower, superseded read cannot land
/// last and paint the old filter. The base class's generation check kept such a read from saying
/// how the window stood, but not from handing over what it read.
/// </summary>
public sealed class LatestRead
{
    private CancellationTokenSource? _reading;

    // Cancelled once, on Cancel, for the reads that are not a load (an alert recount, an own-game
    // check): they neither cancel the load in flight nor are cancelled by the next.
    private readonly CancellationTokenSource _gone = new();

    /// <summary>Cancelled once, when the panel goes, for a read that is not a load.</summary>
    public CancellationToken Gone => _gone.Token;

    /// <summary>
    /// Cancels the read in flight, if any, and starts the token for the next. The superseded
    /// source is not disposed: its read may still be running with the token, and a disposed
    /// source's token can throw where a cancelled one only reports cancellation. A source with no
    /// timer holds nothing the collector does not reclaim.
    /// </summary>
    public CancellationToken Next()
    {
        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref _reading, next)?.Cancel();

        // A load that starts after the panel has gone — a refresh whose own-game check came back
        // as the window closed — would otherwise read under a token nothing is left to cancel.
        // Checked rather than linked: a link would hang a registration per load on the one source
        // that lives as long as the panel.
        if (_gone.IsCancellationRequested)
            next.Cancel();

        return next.Token;
    }

    /// <summary>
    /// Cancels the read in flight and <see cref="Gone"/>: a closed window's reads have nobody to
    /// hand over to. Safe to call more than once.
    /// </summary>
    public void Cancel()
    {
        // Gone first, so a Next racing this from another thread sees it and cancels its own token.
        _gone.Cancel();
        Volatile.Read(ref _reading)?.Cancel();
    }

    /// <summary>
    /// Awaits a read taken under <paramref name="ct"/> and hands its result over only if that is
    /// still the latest read. A cancellation once <paramref name="ct"/> is cancelled is a
    /// superseded read and returns quietly: the newer load hands over, and its outcome says how
    /// the window stands. A read that completes after it was superseded hands over nothing.
    /// Any other failure propagates, so the load's wrapper still says the window did not load.
    /// </summary>
    public static async Task HandOverAsync<T>(CancellationToken ct, Func<Task<T>> read, Action<T> handOver)
    {
        T value;

        try
        {
            value = await read();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        handOver(value);
    }
}
