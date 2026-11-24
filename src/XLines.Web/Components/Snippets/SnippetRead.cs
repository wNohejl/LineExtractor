using XLines.Web.Components.Panels;
using Microsoft.Extensions.Logging;

namespace XLines.Web.Components.Snippets;

/// <summary>
/// A snippet's one read, of which only the latest may hand over. Each snippet held its own
/// loading flag, failure sentence and token, and most never cancelled a read the row had moved on
/// from, so a slow read of the old game could land last and draw it under the new one. A snippet
/// holds one of these as a field and draws from <see cref="Loading"/> and <see cref="Error"/>.
/// </summary>
public sealed class SnippetRead : IDisposable
{
    private readonly LatestRead _reads = new();

    /// <summary>True until the first read has answered, and while a read that shows it runs.</summary>
    public bool Loading { get; private set; } = true;

    /// <summary>The failure sentence of the latest read, or null when it did not fail.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Cancels the read in flight and runs <paramref name="read"/>, handing its result to
    /// <paramref name="handOver"/> only if no newer read has started. A superseded read, however
    /// it ends, says nothing: the newer one decides <see cref="Loading"/> and <see cref="Error"/>.
    /// Any other failure is logged and recorded as <paramref name="failure"/>, and nothing escapes,
    /// because a snippet that throws takes the row it sits under down with it.
    /// </summary>
    /// <param name="logged">The log's message; the sentence without its stop when null.</param>
    /// <param name="showLoading">False to keep what is drawn while the read runs, as a reload does.</param>
    public async Task RunAsync<T>(
        Func<CancellationToken, Task<T>> read,
        Action<T> handOver,
        string failure,
        ILogger log,
        string? logged = null,
        bool showLoading = true)
    {
        var ct = _reads.Next();

        Loading = showLoading;
        Error = null;

        try
        {
            await LatestRead.HandOverAsync(ct, () => read(ct), handOver);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "{Failure}", logged ?? failure.TrimEnd('.'));
            Error = failure;
        }
        catch (Exception)
        {
            // Superseded or closed: whatever it threw is not news, and the newer read answers.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                Loading = false;
        }
    }

    /// <summary>
    /// The answer came from elsewhere (a host's rows, a closed market): the read in flight is
    /// cancelled, so it cannot overwrite it, and the snippet is neither loading nor failed.
    /// </summary>
    public void Settle()
    {
        _reads.Next();
        Loading = false;
        Error = null;
    }

    /// <summary>Cancels the read in flight for good: a closed snippet has nobody to hand over to.</summary>
    public void Cancel() => _reads.Cancel();

    public void Dispose() => Cancel();
}
