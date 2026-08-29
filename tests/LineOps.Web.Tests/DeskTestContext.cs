using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace LineOps.Web.Tests;

/// <summary>
/// The bench every Desk render test sits on.
///
/// Desk primitives wrap MudBlazor, and MudBlazor components resolve services out of the
/// container and reach for JS the moment they render. Registering both here means a test
/// states only what it is checking — a role, an aria attribute, a class, a callback — and
/// never the plumbing that got the component onto the page.
///
/// JSInterop is loose on purpose: the desk's own modules (desk-glide and friends) are
/// browser behaviour, not markup semantics, and a test that had to stub each import would
/// break every time a component picked up an animation.
/// </summary>
public abstract class DeskTestContext : TestContext
{
    protected DeskTestContext()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
