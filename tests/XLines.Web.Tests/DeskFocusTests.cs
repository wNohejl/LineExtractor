using XLines.Web.Services;
using Microsoft.JSInterop;

namespace XLines.Web.Tests;

/// <summary>
/// Focus is a nicety: whatever the browser does with the call — the module will not load, the
/// interop times out, the circuit is going — the press that asked for it goes on, and nothing
/// escapes into the render that would end the circuit.
/// </summary>
public class DeskFocusTests
{
    private sealed class Throwing(Exception ex) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromException<TValue>(ex);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromException<TValue>(ex);
    }

    public static TheoryData<Exception> Failures() => new()
    {
        new JSException("Failed to fetch dynamically imported module: ./js/focus.js"),
        new TaskCanceledException("The JS interop call timed out."),
        new OperationCanceledException(),
        new ObjectDisposedException("IJSObjectReference"),
        new JSDisconnectedException("The circuit has gone."),
        new InvalidOperationException("JavaScript interop calls cannot be issued at this time.")
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_focus_the_browser_cannot_make_is_let_go(Exception failure)
        => await DeskFocus.WhenDrawnAsync(new Throwing(failure), ".qtake input");

    [Fact]
    public async Task A_fault_that_is_not_the_browsers_still_surfaces()
        => await Assert.ThrowsAsync<ArgumentNullException>(()
            => DeskFocus.WhenDrawnAsync(new Throwing(new ArgumentNullException("selector")), ".qtake input"));
}
