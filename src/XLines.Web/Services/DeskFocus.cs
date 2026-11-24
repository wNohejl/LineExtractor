using Microsoft.JSInterop;

namespace XLines.Web.Services;

/// <summary>
/// Moves focus to a control once the render that drew it has reached the browser (focus.js).
/// For a press that replaces the focused control with another — the take's typed price and its
/// way back — where the browser would otherwise drop focus to the page.
/// </summary>
public static class DeskFocus
{
    public const string Module = "./js/focus.js";

    public const string Function = "focusWhenDrawn";

    public static async Task WhenDrawnAsync(IJSRuntime js, string selector)
    {
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", Module);

            // A runtime with no browser behind it (a render test that set nothing up) hands back
            // no module; there is nothing to focus there either.
            if (module is null)
                return;

            // The module is the browser's to keep (an import is cached); the reference to it is
            // this call's, and goes when the call does.
            await using (module)
                await module.InvokeVoidAsync(Function, selector);
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away with the press; there is no page to focus.
        }
        catch (InvalidOperationException)
        {
            // Prerendering: no browser yet, and nothing to move focus from. An ObjectDisposedException
            // is one of these too: the runtime or the module reference went with the component.
        }
        catch (JSException)
        {
            // The module would not load — a stale asset after a deploy — or threw. Focus is a
            // nicety; escaping here, out of a render's OnAfterRenderAsync, it ended the circuit.
        }
        catch (OperationCanceledException)
        {
            // The interop call timed out (a TaskCanceledException) or was called off.
        }    }
}
