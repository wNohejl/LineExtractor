# ADR 0004 — Global Interactive Server render mode

**Status:** Accepted

## Context

.NET 10's Blazor Web App template defaults to static server rendering, with interactivity
opted into per component. MudBlazor does not support static SSR, and as of this build does not
officially target .NET 10 — components render but do not wire up their JavaScript, producing a
page that looks right and does nothing.

## Decision

Declare interactivity once at the root:

```csharp
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
```

with `@rendermode="InteractiveServer"` on `<Routes />` and `<HeadOutlet />`. MudBlazor's CSS
and JS are referenced by static path rather than through `Assets[...]`, which resolves against
the app's own manifest and not the `_content/` static web assets of a package.

## Consequences

- Every page is interactive, so no component silently loses behaviour.
- Every page needs a circuit — irrelevant for a single-user operations tool, and the Ops
  Center wants a live connection anyway.
- Components must not hold an injected scoped `DbContext` across their lifetime, since a
  circuit outlives a request. Pages take `IDbContextFactory<T>`, and anything on a timer
  creates its own scope per tick. Getting this wrong produced a real
  `ObjectDisposedException` during development; the badge component now documents it.
