# LineOps.Desk

The Desk is a window system for Blazor Server, drawn to Apple's Human Interface Guidelines on
top of MudBlazor: a row of tiled windows, a strip and a rail to open them, a command palette,
toasts, alerts and sheets, and the primitives a window is built from. It knows nothing about the
application it hosts — an application tells it its name and lists the windows it can open, and
gets the rest. Everything it paints is spent from one set of semantic tokens, so a second theme
is a second token block rather than a component rewrite.

## Wiring

Reference the project (or the package — the assembly, the namespaces and the
`_content/LineOps.Desk` path are the same either way), then make one service call and place two
head components. `AddDesk` registers MudBlazor's services too, so the application does not call
`AddMudServices`; anything it wants to change there goes in `mud:`, which runs after the Desk's
defaults.

```csharp
// Program.cs
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDesk(
    new DeskBrand("ACME", "DESK", "what the product does", "Go to a customer or window"),
    new DeskClock(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"), "UK"));
builder.Services.AddSingleton<IWindowCatalog, AcmeWindowCatalog>();
builder.Services.AddScoped<IDeskJobs, AcmeJobs>();   // optional: the runs the job menu offers
// ...
app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// ...
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
```

```razor
@* App.razor *@
<head>
    <base href="/" />
    <DeskHead />
    <link rel="stylesheet" href="@Assets["css/acme.css"]" />
    <link rel="stylesheet" href="@Assets["Acme.Web.styles.css"]" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <ReconnectModal />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <DeskScripts />
</body>
```

```razor
@* Pages/Error.razor: the page the exception handler re-executes *@
@page "/Error"
@using System.Diagnostics

<PageTitle>Error</PageTitle>
<DeskErrorPage RequestId="@(Activity.Current?.Id ?? HttpContext?.TraceIdentifier)" />

@code {
    [CascadingParameter] private HttpContext? HttpContext { get; set; }
}

@* Pages/NotFound.razor: the page the status-code pages re-execute *@
@page "/not-found"
@layout MainLayout

<PageTitle>Not found</PageTitle>
<DeskNotFound />
```

- `<DeskHead />` emits the theme boot script and the Desk's stylesheets in their load-bearing
  order. The application's own sheet goes after it, so it wins a tie with any Desk rule.
- `<DeskScripts />` goes after `blazor.web.js`: MudBlazor's script and the Desk's one classic
  script. The Desk's other scripts are modules its components import when they need them.
- The application's `{Assembly}.styles.css` bundle carries the Desk's scoped styles —
  `ReconnectModal` relies on it.
- The layout is `LineOps.Desk.Layout.MainLayout`: set it as the router's `DefaultLayout`, and
  put `<Desk />` on the one page the application has. `MainLayout` renders MudBlazor's
  providers and the theme, so the application renders neither.
- `<ReconnectModal />` is the Desk's circuit-lost notice; it sits in the body beside `Routes`.
- The catalogue is the application's one implementation of `IWindowCatalog`: every
  `WindowDefinition` it can open (a key, a title, an icon, a component), the `Workspace`s it
  names, and the `DefaultPrimary` an empty desk opens. Register it as a singleton.
- `DeskBrand` is the wordmark, split where the accent falls, the tagline on an empty desk, and
  the palette's placeholder. `DeskClock` (optional) is the zone the Desk tells time in; left
  out, it is UTC and says so. `IDeskAccess` (optional) says who is at the desk; left out, one
  person who may do everything.
- `DeskTime` is how a window writes a moment on that clock, so the zone is the one the
  application named rather than the host's: `clock.Stamp(at)` for a date and time with the
  clock's label ("Sep 23 18:40 UK"), `clock.Day(at)` for the day alone, and
  `clock.Format(at, "HH:mm")` for any other shape, unlabelled, where the caller names the zone
  once — a chart's axis, say. Inject `DeskClock` and read it; never `DateTime.Now`.
- `IDeskJobs` (optional) is the named runs the operator may start by hand: `DescribeAsync` lists
  each `DeskJob` with what it costs and, when it cannot run here, why; `RunAsync` runs one and
  returns a `DeskJobOutcome`. Register it scoped. A `DeskJobMenu` with no `Jobs` of its own reads
  it, and `DeskJobRunner` runs it the one way the Desk reports a run — refused for a visitor, a
  toast in the outcome's own state, an exception logged and told as "did not finish" — so a
  run started from an empty state's key toasts like one chosen from the menu. Left out, a menu
  with no `Jobs` has nothing to offer.
- `DeskErrorPage` and `DeskNotFound` are the two pages a host shows outside the desk: what went
  wrong with the request's reference, and an address that opens nothing, each with the way back.
  Each takes a `Title`, a `ChildContent` sentence and a `HomeHref`/`HomeLabel` when the defaults
  do not fit.

Add `@using LineOps.Desk`, `LineOps.Desk.Layout`, `LineOps.Desk.Primitives` and
`LineOps.Desk.Windowing` to the application's `_Imports.razor`.

### A window's base class

A window's component inherits `DeskPanelBase` (in `LineOps.Desk.Windowing`), which is its one
coupling to the window it is shown in:

- `WithActivityAsync(work, settle, status)` runs a load with the window's pulse busy, then
  settles it on what `settle` says. A failure is logged, the pulse goes critical and `LoadError`
  holds a sentence built from `LoadSubject` ("The list did not load."); it is not rethrown,
  because an exception out of a lifecycle method ends the circuit. Reads only — a write whose
  caller must know it failed handles its own failure. Only the latest load started may settle
  the window, so an older one landing late is ignored.
- `RetryAsync()` repeats the last load exactly, filters and all; it is what
  `PanelLoadFailure`'s key calls. `Loaded` is true once a load has completed, so an empty
  result means "nothing here" only after there has been a result.
- `Watches` names the topics the window draws from. When `DeskSignals` publishes one of them,
  the window waits a second for the burst to finish and calls `RefreshAsync()` once.
  `RequestRefresh()` asks for the same from any other source of change.
- `MarkUnsaved(dirty, label)` says the window holds input not yet saved — "this draft". While it
  does, its close key asks before discarding it and eviction passes it by. Call it with false
  once the input is saved or cleared.
- `Report(state, status)` sets the pulse and status by hand, for anything that is not a load.

```razor
@inherits DeskPanelBase
@inject IItemStore Store

@if (Failed)
{
    <PanelLoadFailure Message="@LoadError" OnRetry="RetryAsync" />
}
else if (!Loaded)
{
    <DeskSkeleton Lines="3" />
}
else
{
    @foreach (var item in _items)
    {
        <p>@item.Name</p>
    }
}

@code {
    private IReadOnlyList<Item> _items = [];

    protected override IReadOnlySet<string> Watches { get; } = new HashSet<string> { "items" };
    protected override string LoadSubject => "The list";

    protected override Task OnInitializedAsync() => LoadAsync();
    protected override Task RefreshAsync() => LoadAsync();

    private Task LoadAsync() => WithActivityAsync(
        async () => _items = await Store.ListAsync(),
        () => PulseState.Healthy,
        () => $"{_items.Count} items");
}
```

## Hosting

`LineOps.Desk.Hosting` is the companion package for what a Desk needs from the ASP.NET Core
host it runs in, kept out of the Desk so the component library has no opinion about how it is
exposed: an access gate (a code in, a cookie out; an optional guest code opens a desk that
reads and changes nothing) and the edge (six security headers on every response, and trust in a
reverse proxy's forwarded headers when there is one). It references `LineOps.Desk` and nothing
else; its sign-in page takes its wordmark and tagline from the `DeskBrand` given to `AddDesk`.

```csharp
using LineOps.Desk.Hosting;

builder.Services.AddDeskAccess(builder.Configuration);   // optionally: o => o.CookieName = "acme.access"
builder.Services.AddDeskEdge(builder.Configuration);
// ...
app.UseDeskEdge();          // first in the pipeline
// ... exception handler, status-code pages, HTTPS redirect unless configuration.IsBehindProxy()
app.UseDeskAccess();        // before UseAntiforgery
app.UseAntiforgery();
app.MapDeskAccess();        // the sign-in page, its form, and sign-out
```

`AddDeskAccess` binds `DeskAccessOptions` from the `Access` section: `Mode` (`Open`, the
default, registers nothing but the options, so one person on their own machine runs as before;
`Gated` lets a code decide), `OperatorCodeHash` (required when gated — the host refuses to start
without it), `GuestCodeHash`, `AllowAnonymousVisitors`, `SessionLifetime`, `CookieName`,
`SignInPath` and `SignOutPath`. `AddDeskEdge` reads `DeskEdgeOptions` (`ContentSecurityPolicy`,
`ExtraImageSources`) and `Hosting:BehindProxy`. The hash format, every default and the two pure
functions it tests by (`DeskEdge.Headers`, `DeskSignInPage.Render`) are in the package's own
[README](../LineOps.Desk.Hosting/README.md).

## Tokens

Every rule in the Desk spends a token named for a role, never a colour. The families, all
declared in the first token block of `wwwroot/css/desk.css`:

- `--surface-*` — the elevation ramp, from the desk's ground to hover fills.
- `--separator`, `--separator-strong` — hairlines.
- `--text-primary`, `-secondary`, `-tertiary` — the ink hierarchy.
- `--text-largetitle` … `--text-caption` — the HIG text-style ramp, scaled by `--text-scale`.
- `--accent*`, `--on-accent` — the one accent, its states and washes, and what sits on it.
- `--state-*` — positive, negative and warning, each with a wash and an `-ink` that reads as text.
- `--chart-*` — the neutral series colours.
- `--material-*` — the translucent fills and blur for anything that floats.
- `--face-ui` — the one type family.
- `--leading-*`, `--weight-*` — line heights and weights.
- `--space-*` — the spacing ramp.
- `--rail-size`, `--titlebar-h` — the chrome's fixed dimensions.
- `--radius*` — radii that grow with the surface.
- `--dur-*`, `--ease-*` — motion.
- `--shadow-*` — elevation shadows.
- `--sheen*` — the shimmer over a busy control or a loading skeleton.
- `--focus-ring*` — the focus ring, written in `var()`s so every theme re-resolves it.
- `--z-*` — the layers, from windows to toasts.

A theme family is three things that must agree: a dark and a light token block in
`wwwroot/css/themes.css` (each redeclaring every colour token of the Standard dark block and
nothing else), a member of `DeskThemeFamily` with its name and slug in `DeskThemeFamilies`, and
its two palettes in `DeskPalettes`, which MudBlazor and the charts read. `DeskThemeTests` holds
every block to the token list and to its C# mirror and computes its text contrast;
`ThemeContrastTests` holds its inks to 4.5:1; `ThemeBootTests` holds the boot script to the
family slugs.

The Standard family alone also takes a chosen accent (`DeskAccent`: Blue, Teal, Indigo, Purple,
Graphite). Blue is what its blocks already declare; each other hue is a compound block at the end
of `themes.css`, `[data-theme="apple-dark"][data-accent="teal"]` and its light twin, redeclaring
exactly the six accent tokens (`--accent`, `-hover`, `-press`, `-wash`, `-wash-strong`, `-ink`),
mirrored in `DeskAccents`. `ThemeService.Accent` persists the choice under `desk.accent` and
stamps `data-accent` only while Standard is showing, and `Palette` and `MudTheme` carry it then,
so MudBlazor and the charts wear it too. The Window manager offers it beneath the theme tiles.

## Primitives

| Primitive | What it is for | Wraps |
| --- | --- | --- |
| `CheckList` | Advisory checks as a grouped section: a mark, one sentence and a remedy per row. | — |
| `DeskAccentPicker` | The Standard desk's accent: one circle per hue, a radiogroup whose arrows wrap. | — |
| `DeskAlert` | A decision that must be made before anything else happens; confirms and destructive gates only. | `MudDialog`, through `IDeskAlerts` |
| `DeskBadge` | A count beside the name of what it counts, heard with its noun; nothing to count, no badge. | — |
| `DeskButton` | The key: the caller states how loud (`Emphasis`) and whether it destroys. | `MudButton` |
| `DeskChart` | A chart that names the kind of number it shows and headlines its takeaway. | `MudChart` |
| `DeskCheckbox` | One setting that is on or off. | — |
| `DeskDialog` | A floating, non-modal panel for comparing things beside the desk. | — |
| `DeskDisclosure` | Details hidden behind a label until they are wanted. | — |
| `DeskErrorPage` | The page a host shows when a request failed: what happened, its reference, and the way back. | — |
| `DeskField` | A text or number field, with a real label and its error beneath it. | — |
| `DeskGrid` | The table: sortable columns and actionable rows in the Desk's dense rhythm. | `MudDataGrid` |
| `DeskJobMenu` | A key that opens a menu of named runs, each saying what it costs before it is chosen. | — |
| `DeskLink` | A grid cell that opens a window of its own, naming where the click goes. | — |
| `DeskMenu` | A column of commands the keyboard can drive. | — |
| `DeskMenuItem` | One row of a menu: a label, a trailing detail, and why it is dimmed. | — |
| `DeskNotFound` | The page a host shows for an address that opens nothing, with the way back. | — |
| `DeskPopover` | A panel anchored to the control that opened it, one at a time. | — |
| `DeskProgress` | A slim bar for named work that takes long enough to report. | — |
| `DeskPullDown` | A key that opens a menu of related commands. | — |
| `DeskRadioGroup` | One choice among options that need room: a sentence each, or a picture of what choosing it does. | — |
| `DeskRow` | A horizontal group with its gap named from the spacing ramp. | — |
| `DeskSearch` | A search field with a magnifier, a clear key and a scoped placeholder. | — |
| `DeskSelect` | A field that picks from a list. | — |
| `DeskSheet` | A modal sheet for one self-contained task. | `MudDialog`, through `IDeskSheets` |
| `DeskSkeleton` | A placeholder in the shape of content that has not arrived. | — |
| `DeskSlider` | A value chosen by dragging, when its place between two ends matters more than its figure. | — |
| `DeskSparkline` | A word-sized line chart of one series, inside a table row. | — |
| `DeskSpinner` | Work under way with no denominator, where there is no room for a bar. | — |
| `DeskStack` | A vertical group with its gap named. | — |
| `DeskStep` | One step of a `DeskSteps`: marker, title, guidance and controls. | — |
| `DeskStepper` | One capsule control that nudges a value by a step. | — |
| `DeskSteps` | A vertical sequence of steps joined by a rail. | — |
| `DeskSwitch` | A gate switch: one visible choice among two to five. | — |
| `DeskTabs` | A window's sections, with the selection sliding between them; a `DeskTab`'s `Count` draws a badge after its name. | — |
| `DeskTextArea` | A field with room for a paragraph. | — |
| `DeskToggle` | A setting that takes effect the moment it flips; announced as a switch. | — |
| `DeskTooltip` | A sentence beside the control it explains, for what a `title` cannot carry. | `MudTooltip` |
| `EmptyState` | What a panel says when there is nothing to show — first run, no match, or failure. | — |
| `FollowUpLayer` | The floating follow-ups a panel has open, drawn once per panel. | — |
| `InlineConfirm` | A question asked in place, with Cancel first and the confirm last; Escape answers Cancel. | — |
| `KeyRow` | Secondary keys, the reason the primary waits, then the one Filled key, which the reason describes. | — |
| `Metric` | One number, its label, and the qualifier that stops it being misread. | — |
| `MetricRow` | The shelf a row of metrics sits on. | — |
| `Note` | Passive feedback beside the thing it describes, for as long as it is true. | — |
| `PanelForm` | The inset card for a task completed in place, with its footer pinned. | — |
| `PanelHeader` | The sentence-case rule that names a stretch of a panel. | — |
| `PanelLoadFailure` | What a window shows when its load failed: the sentence and the key that loads it again. | — |
| `PanelSection` | A stretch of a panel under one rule, spacing itself from the next. | — |
| `PanelTitle` | The title block a subject window opens with. | — |
| `PanelToolbar` | The one row of controls that scope or act on a whole panel. | — |
| `RowActions` | The action strip on an open row, and the snippet it reveals. | — |
| `SnippetFailure` | What a snippet shows when its load failed, with the key to retry. | — |
| `SnippetShell` | The frame around an inline answer, with its one way out. | — |
| `Tag` | A small state word. | — |

Toasts are a service rather than a component: `DeskToasts` raises them, over MudBlazor's
`ISnackbar`, with the snackbar configuration `AddDesk` sets.

## The MudBlazor seam

MudBlazor arrives with Material's opinions. The Desk keeps its components and spends its own
tokens on them:

- `wwwroot/css/mud-bridge.css` re-points MudBlazor's custom properties at the Desk's tokens, so
  a Mud component nobody has styled still lands looking like the Desk, and removes the Material
  tells MudBlazor hard-codes (ripples, layered shadows, Roboto). It loads after the token
  blocks and wins on specificity, never `!important`.
- `DeskGrid` takes ordinary `PropertyColumn` and `TemplateColumn` children — the columns are
  MudDataGrid's, the appearance is the Desk's.
- `DeskChart` takes its `Series` as MudBlazor's `ChartSeries<double>`; the colours come from the
  chart's family and the theme it is drawn on, not from MudBlazor's twenty-colour ramp.
- `MainLayout` renders the four providers — `MudThemeProvider` (fed the theme by
  `ThemeService`), `MudPopoverProvider`, `MudDialogProvider` and `MudSnackbarProvider` — so a
  consuming application renders none of them.

## The Parts bin

The Parts bin is the gallery: every primitive, live, on the same surface and under the same
theme as the window being built beside it. It is the Desk's own component (`PartsPanel`, in
`LineOps.Desk.Windowing`); an application lists it in its catalogue under the key `parts`, and
a fresh project that wires the Desk can open it from the strip or the palette.

## The rules

| Rule | What it means | Held by |
| --- | --- | --- |
| Edges | A control or group has one edge. A form is not boxed around a bordered group, and a group carries its hairline where it is defined. | `DeskCssConventionTests` (`A_form_is_not_boxed`, `A_group_carries_its_hairline_where_it_is_defined`) |
| One Filled key | A context has at most one Filled key, last in its row; everything else is Tinted or Plain. | `PanelToolbarTests.Keeps_every_key_in_one_actions_item_after_the_pickers` (the Filled key last); the count is held in review |
| Sentence case | Titles, headers, keys and tags read in sentence case. | `PanelHeader` and `PanelSection` set the rule's type; no test reads copy yet |
| Keys explain themselves | A key that waits says why in text on its row, not in a title a disabled key never shows, and the key is described by that text; secondary keys come first and the Filled key last, and a confirm asks Cancel first. | `KeyRowTests`, `KeyRowAdoptionTests` (in the host's tests), `InlineConfirmTests` |
| Names on controls | Every field has a label, an `aria-label` or an `aria-labelledby`; an alert names its consequence. | `AccessibleNameTests`, `DeskAlertTests.Every_caller_must_name_the_confirm` |
| No literal colours | A colour is a token. Only the token blocks write one. | `DeskCssConventionTests.No_literal_colour_outside_the_token_blocks` |
| No domain | The Desk's code and its documentation name no application's records. | `DeskVocabularyTests` |

## Releasing

The packages are published to GitHub Packages by `.github/workflows/desk-packages.yml`. Every
push to `main` packs them as a dry run (the `.nupkg` files are the run's artifact); a tag
publishes them.

1. Raise `<Version>` in `LineOps.Desk.csproj`, and in `LineOps.Desk.Hosting.csproj` if Hosting
   changed. Raise the same versions in the template's
   `src/LineOps.Desk.Templates/content/deskapp/Directory.Packages.props`, and the template's own
   `<Version>` with them (`DeskTemplateTests` fails until the two agree). Commit on
   `LineX_Development`, merge to `main`, and push.
2. Tag the merge with the Desk's version and push the tag:

   ```bash
   git tag desk-v0.3.0 main
   git push origin desk-v0.3.0
   ```

3. The workflow refuses a tag that disagrees with `LineOps.Desk.csproj`, packs the Desk, Hosting
   and `LineOps.Desk.Templates`, and pushes each with `--skip-duplicate`: a package whose version
   is already on the feed — an unchanged Hosting — is passed over, not failed on. A version once
   published is never replaced; a fix is a new version and a new tag.

A consumer restores from the feed with [`docs/desk/nuget.config`](../../docs/desk/nuget.config)
beside its solution, and `DESK_FEED_USER` and `DESK_FEED_TOKEN` (a token with `read:packages`) in
its environment. A new application starts from the template, which brings that file with it:
`dotnet new install LineOps.Desk.Templates`, then `dotnet new deskapp` (its
[README](../LineOps.Desk.Templates/README.md) lists the options).
