# XLines.Desk

The Desk is a window system for Blazor Server, drawn to Apple's Human Interface Guidelines on
top of MudBlazor: a row of tiled windows, a strip and a rail to open them, a command palette,
toasts, alerts and sheets, and the primitives a window is built from. It knows nothing about the
application it hosts — an application tells it its name and lists the windows it can open, and
gets the rest. Everything it paints is spent from one set of semantic tokens, so a second theme
is a second token block rather than a component rewrite.

## Wiring

Reference the project (or the package — the assembly, the namespaces and the
`_content/XLines.Desk` path are the same either way), then make one service call and place two
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
  It is required for a first paint without a flash: it writes the boot tables (`DeskBoot.Json`)
  just ahead of `theme-boot.js`, and the script reads every family, accent and look from them
  alone. A head that links the script and the sheets by hand, without the tables, boots the plain
  Standard desk in the stored mode — no family, no accent, no look — and shows it until the
  circuit opens and `ThemeService` repaints the chosen one: a flash on every load. Place
  `<DeskHead />`; never copy its tags.
- `<DeskScripts />` goes after `blazor.web.js`: MudBlazor's script and the Desk's one classic
  script. The Desk's other scripts are modules its components import when they need them.
- The application's `{Assembly}.styles.css` bundle carries the Desk's scoped styles —
  `ReconnectModal` relies on it.
- The layout is `XLines.Desk.Layout.MainLayout`: set it as the router's `DefaultLayout`, and
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

Add `@using XLines.Desk`, `XLines.Desk.Layout`, `XLines.Desk.Primitives` and
`XLines.Desk.Windowing` to the application's `_Imports.razor`.

### A window's base class

A window's component inherits `DeskPanelBase` (in `XLines.Desk.Windowing`), which is its one
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
- A notice can be addressed: `orders:4123` (`DeskSignals.Address(topic, id)`) is a change to one
  thing under `orders`. Watching `orders` hears it and the bare `orders`; a window that also
  returns ids from `WatchesIds` (`{ ["orders"] = { "4123" } }`) hears the bare topic and its own
  ids only. Override `RefreshAsync(IReadOnlySet<string> heard)` to be told which tokens arrived
  in the settle, and reload only what they touched; it is empty after a bare `RequestRefresh()`,
  meaning everything. A test bench publishes one with `Signal("orders", "4123")`.
- `MarkUnsaved(dirty, label)` says the window holds input not yet saved — "this draft". While it
  does, its close key — or its tab's — asks before discarding it, and a tab behind another keeps
  it untouched. Call it with false once the input is saved or cleared.
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

### Columns and tab groups

The desk is one row of columns, up to the limit the operator sets in the Window manager. Past it
nothing closes: the next window joins a column as a tab (ADR 0027), never the column the operator
is in, the primary window's last, otherwise the one with the fewest tabs, leftmost first. A group's
title is a pop-up key (`WindowTabs`): the window showing, its icon and name, with the column's
count in words ("2 tabs") and a chevron, and an edge stepped out behind it for each window behind,
two at most, so it reads as a stack. Each tab in its menu is a key with the Desk's finish, the one
showing selected in the accent's wash (selection, not a Filled key). Its menu lists the column's windows most recently shown first, each with its
status and an attention dot, closable with Delete or the row's close glyph, then Move to its own
column and Close; choosing one brings it forward and the one showing steps to second (ADR 0027,
amended 2026-11-20). The bar's width beyond the key goes to the window's status and keys. Every
window stays mounted, so a tab keeps its panel, scroll, input and Back and Forward; the desk holds
`DeskSettings.MaxOpenWindows` (24) in all and refuses the next open with a sentence. A panel needs
nothing to be a tab. `WindowManager.Columns` reads the row (`WindowColumn.Tabs`, `Showing`,
`IsGroup`), `Focus` brings a tab forward, `MoveToOwnColumn` takes one out while a column is free,
and a saved layout or workspace keeps its groups.

### Workspaces and the empty desk

A `Workspace` names a set of windows to open, with an optional `Icon` (a Material icon string),
a `Description` (one sentence saying what it is for) and a `Tag` the application defines and the
Desk carries without reading. `WindowManager.ActiveWorkspace` is the one last applied, cleared
when the desk empties or another is applied, and saved and restored with the layout by name. An
empty desk offers the catalogue's workspaces as its main choice: the first as a card with the
page's one Filled key, the rest as rows, then the ones saved on this desk, each saying which
windows it opens; then the primary window and the palette's Ctrl+K as the quieter way in, and the
desk's mechanics in a disclosure. Only what the person at the desk may open is offered.

## Hosting

`XLines.Desk.Hosting` is the companion package for what a Desk needs from the ASP.NET Core
host it runs in, kept out of the Desk so the component library has no opinion about how it is
exposed: an access gate (a code in, a cookie out; an optional guest code opens a desk that
reads and changes nothing) and the edge (six security headers on every response, and trust in a
reverse proxy's forwarded headers when there is one). It references `XLines.Desk` and nothing
else; its sign-in page takes its wordmark and tagline from the `DeskBrand` given to `AddDesk`.

```csharp
using XLines.Desk.Hosting;

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
[README](../XLines.Desk.Hosting/README.md).

## Testing

`XLines.Desk.Testing` is what an application's tests take from the Desk: the Desk's source
conventions as calls, a bUnit bench, and fakes for the seams the Desk asks its host for.
Reference it beside xunit, and the conventions are one fact:

```csharp
[Fact]
public void The_desk_conventions_hold() => DeskConventions.All(
    Path.Combine(DeskConventions.FindAbove("Acme.sln"), "src"), "Acme.Web/Components/App.razor", "Acme.Web/wwwroot/css/acme.css");
```

`All` reads the source under the first path and reports every broken rule at once: a Desk field
with no accessible name, a `TemplateColumn` with a sort key that cannot sort, Bootstrap shipped,
a literal colour or an undersized target in the application's sheet, a second toolbar fold, a
Desk component restyled by the application, and a stylesheet linked above `<DeskHead />`. Each is
a method of its own as well, and `SelectorLivesInAppSheet` checks the other side of ownership —
that a window's selector is in the application's sheet and in none of the Desk's, which the
package carries inside it.

`DeskTestContext` registers MudBlazor, the theme, a brand, a UTC clock, an open desk, the window
manager, the follow-ups and the popovers, with JS interop loose. An application's bench derives
from it and registers its own brand, clock and catalogue; the last registration wins.
`FakeWindowCatalog` (three windows, or the definitions you pass), `FakeAlerts` and `FakeSheets`
stand in for the rest. The Desk's own tests run on the same bench, so what ships is what they
prove; the package's [README](../XLines.Desk.Testing/README.md) has the whole list.

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
- `--face-ui`, `--face-display` — the type family, and the titles' (the same until a Typeface splits them).
- `--control-h`, `-sm`, `-lg`, `--row-h`, `--cell-pad-y`, `--cell-inset` — target heights and table cells, which Density moves.
- `--backdrop-tint` — how much accent the Gradient backdrop mixes into the ground.
- `--leading-*`, `--weight-*` — line heights and weights.
- `--space-*` — the spacing ramp.
- `--rail-size`, `--titlebar-h`, `--header-h`, `--footer-h` — the chrome's fixed dimensions
  (`--header-h` is the header bar's height).
- `--radius-xs` … `--radius-sheet` — radii that grow with the surface, which Shape moves.
- `--dur-*`, `--ease-*` — motion.
- `--shadow-*` — elevation shadows.
- `--sheen*` — the shimmer over a busy control or a loading skeleton.
- `--focus-ring*` — the focus ring, written in `var()`s and declared on `:root`, so it re-resolves
  to whichever block `<html>` carries; inside a swatch with its own `data-theme` it is `<html>`'s ring.
- `--z-*` — the layers, from windows to the framework's connection banner (`--z-toast`).
- `--key-highlight`, `--key-gloss`, `--key-press` — a key's finish: its top line, the sheen that
  fades out above its label, and its inner shade while held. Every family declares the shade (its
  own shadow ink); the line and the sheen are derived. High contrast sets their alphas to 0, so its
  keys are flat.

**Derived tokens.** Eight colour tokens are computed, not declared per block: `--accent-wash`,
`--accent-wash-strong` and the three `--state-*-wash` (each its opaque fill at an alpha token, by
`color-mix(in srgb, <fill> calc(<alpha> * 100%), transparent)`, which an older browser than relative
colour reads too — a wash it could not parse would be transparent and take `--focus-ring` with it),
`--key-highlight` and `--key-gloss` (the family's `--sheen` by night, white by day, at theirs: by
night relative colour replaces the translucent sheen's alpha exactly, inside
`@supports (color: rgb(from white r g b))`, and a browser without it mixes the sheen, fainter) and
`--material-blur` (an optical constant). desk.css declares them once, under "Derived
tokens", on `:root, [data-theme]` and the two appearance rules `[data-theme$="-dark"]` and
`[data-theme$="-light"]`, which default the alphas to Standard's — washes .15/.24 by night and
.12/.20 by day, states .15, the line and sheen .16/.09 and .55/.18. They sit on the attribute, not on
`:root` alone, because a custom property inherits already resolved: a swatch carrying its own
`data-theme` and `data-accent` recomputes from its own fill. A block states an alpha
(`--accent-wash-alpha`, `--accent-wash-strong-alpha`, `--state-positive-wash-alpha`,
`--state-negative-wash-alpha`, `--state-warning-wash-alpha`, `--key-highlight-alpha`,
`--key-gloss-alpha`) only where it departs from that default, and never a derived token itself.
Reduce Transparency's and the forced-colours block's blur outweigh the rule. The text tiers and the
hovers and presses are tuned per family and are never derived.

The geometry the operator can change — `--face-ui`/`--face-display`, `--space-*`,
`--titlebar-h`, `--footer-h`, `--control-h` (`-sm`, `-lg`), `--row-h`, `--cell-pad-y`,
`--cell-inset`, `--radius-xs` … `--radius-sheet` and `--backdrop-tint` — is declared in a plain
`:root` block after the theme blocks, never in one, so a swatch's own `data-theme` cannot reset it.

**The families**, in the picker's order and under the catalogue's four headings
(`DeskThemeGroups`, which the picker offers as a filter, not as shelves), each with its accents,
its own first:

| Heading | Family (slug) | Accents |
|---|---|---|
| Built in | Standard (`apple`) | Blue, Teal, Indigo, Purple, Graphite |
| | High contrast (`contrast`) | Blue |
| Character | Paper (`paper`) | Ink blue, Sepia, Plum, Verdigris |
| | Midnight (`midnight`) | Electric, Violet, Magenta, Mono |
| | Obsidian (`obsidian`) | Sky, Violet, Slate |
| | Synthwave (`synthwave`) | Magenta, Cyan, Ultraviolet, Chrome |
| | Terra (`terra`) | Clay, Slate, Heather, Stone |
| Soft | Nord (`nord`) | Frost, Frost teal, Aurora purple |
| | Rosé Pine (`rose-pine`) | Iris, Subtle |
| | Catppuccin (`catppuccin`) | Mauve, Pink, Blue, Sapphire |
| | Everforest (`everforest`) | Blue, Purple, Aqua, Graphite |
| | Kanagawa (`kanagawa`) | Blue, Violet, Aqua, Graphite |
| | Zenburn (`zenburn`) | Blue, Sky, Magenta, Graphite |
| | Palenight (`palenight`) | Purple, Blue, Cyan, Pale blue |
| | Solarized (`solarized`) | Blue, Violet, Cyan, Slate |
| | Gruvbox (`gruvbox`) | Blue, Purple, Graphite |
| Vivid | Dracula (`dracula`) | Purple, Cyan, Comment |
| | Tokyo Night (`tokyo-night`) | Blue, Magenta, Purple, Cyan |
| | Monokai (`monokai`) | Cyan, Purple, Yellow, Graphite |
| | One (`one`) | Blue, Purple, Cyan, Graphite |
| | Night Owl (`night-owl`) | Blue, Purple, Teal, Slate |
| | Ayu (`ayu`) | Gold, Blue, Purple, Teal |
| | Primer (`primer`) | Blue, Purple, Pink, Graphite |

A family's grey is called Graphite, as Standard's is, unless the palette names it (Dracula's
Comment, Rosé Pine's Subtle, Midnight's Mono, Synthwave's Chrome, Terra's Stone, Slate).

**Adding a family** is a row and its blocks; the picker, the boot script's tables and every
palette and contrast test read the row. Literally:

1. `Theming/DeskThemeFamily.cs` — a member of `DeskThemeFamily` (its name is what is stored), and
   a row in `DeskThemeFamilies.Catalog`, where its group's families sit:
   `new(DeskThemeFamily.Kanagawa, "Kanagawa", "kanagawa", DeskThemeGroups.Soft, DeskPalettes.KanagawaDark, DeskPalettes.KanagawaLight, DeskFamilyAccents.Kanagawa)`.
   The last argument is its accent list, its own accent first, with no palettes — a list in
   `DeskFamilyAccents` (`Theming/DeskAccent.cs`), or `[new("blue", "Blue")]` inline for a family
   that offers only its own. A new heading is a constant in `DeskThemeGroups`.
2. `Theming/DeskPalette.cs` — `KanagawaDark` and `KanagawaLight`, value for value from its blocks
   (and `Overlay`, the scrim, which has no CSS token).
3. `wwwroot/css/themes.css` — `[data-theme="kanagawa-dark"]` and `[data-theme="kanagawa-light"]`,
   each redeclaring every colour token of the Standard dark block, in its order, and nothing else:
   `color-scheme`, `--surface-0` … `-3`, `--separator`, `-strong`, `--text-primary`, `-secondary`,
   `-tertiary`, `--accent`, `-hover`, `-press`, `--on-accent`, `--state-positive`, `-negative`,
   `-negative-hover`, `-warning`, the four inks (`--state-*-ink`, `--accent-ink`), `--chart-neutral`,
   `-dim`, the four `--material-*` tiers, `--shadow-1` … `-sheet`, `-inset`, `--sheen`, `-soft`,
   `--knob` and `--key-press`; plus, beside the fill each thins, any wash or key alpha that departs
   from the default. The washes, the key's line and sheen and the blur are derived (above).
   `--on-accent` is the family's own and need not be white: Ayu, Midnight and Synthwave dark, Paper
   and Terra carry dark or parchment labels.

`theme-boot.js` needs nothing: `DeskHead` writes its tables (`DeskBoot.Json` — every family's
slug by its stored name, the accents with a block, every look) from the catalogue into a
`<script type="application/json" id="desk-boot">` block ahead of it, and
`ThemeBootTests.The_boot_script_names_no_family_and_no_accent` keeps the script from growing a
copy again. `DeskThemeTests` (`Every_desk_redeclares_every_colour_token_and_invents_none`,
`Every_palette_mirrors_its_block`, `No_family_is_Standard_under_another_name`,
`The_catalogue_holds_every_family_once_grouped_together`) and `ThemeContrastTests` (every ink at
4.5:1, the gradient backdrop) check the rest, all by iterating the catalogue.

**Adding a family's accents** is two halves that must agree, and nothing else changes — the
picker row appears once a family offers more than its own, and the boot tables carry it:

1. `themes.css`, after that family's accents or at the end of the accent section — two compound
   blocks per accent, `[data-theme="<slug>-dark"][data-accent="<key>"]` and `-light`, declaring
   exactly `--accent`, `--accent-hover`, `--accent-press` and `--accent-ink` (the family's
   `--on-accent` is kept, so every fill must carry it at 4.5:1), and `--accent-wash-alpha` or
   `--accent-wash-strong-alpha` only where the hue wants another strength than its family's — the
   washes are recomputed from the new fill. The key is lower-case and hyphenated.
2. `DeskAccent.cs` — append to the family's list in `DeskFamilyAccents`:
   `new("teal", "Teal", Dark: new AccentPalette("#…", "#…"), Light: new AccentPalette("#…", "#…"))`,
   the fill and the hover in the block's formatting — all that crosses into C#.

`DeskThemeTests.Every_family_s_accents_are_its_blocks` fails if the blocks and the row disagree,
`Every_accent_block_redeclares_the_four_accent_tokens_and_only_its_wash_alphas` if a block declares a
different set or restates its family's alpha, `Every_accent_block_mirrors_its_catalogue_row` if the
fill or hover drifts, `Every_accent_travels_the_way_its_ground_demands` if hover and press go the
wrong way, and `Every_derived_token_resolves_to_its_source_at_its_alpha` if a wash is not its fill
at its alpha. `ThemeContrastTests` holds every accent's ink, label-on-fill, wash and
selection to 4.5:1 on its own family's panel, raised surface and ground.

Every family takes a chosen accent from its own list (`DeskThemeFamilyInfo.Accents`; Standard's is
`DeskAccent`'s Blue, Teal, Indigo, Purple and Graphite). The first is what its blocks declare and
stamps nothing. `ThemeService` keeps the choice per family under `desk.accent.<slug>` — Standard's
falling back to the old `desk.accent` while its own is empty — holds a stored key to that family's
list, stamps `data-accent` for the showing family, and `Palette` and `MudTheme` carry it, so
MudBlazor and the charts wear it too. The Window manager offers the showing family's row beside
Mode, in the split row that opens its Appearance card (a family with only its own accent has no
cell, and Mode takes the row), and each theme tile wears its family's remembered accent.

The glass and four makes belong to every family. Each is a **look**: one row of `DeskLooks.All`
(`Theming/DeskLook.cs`) naming its row label, storage key, attribute on `<html>`, positions (stored
name, stamped value or null for the default, gate label) and the machine setting it yields to, if
any. `ThemeService` holds every look in one table (`Chosen`, `AttributeOf`, `Offers`,
`SetLookAsync`; `Glass`, `SetShapeAsync` and the rest are typed views of it), compares and stamps
them in one `applyLooks` call, and forgets them in one reset; `theme.js` only reads, writes and
stamps the keys and attributes it is handed; the boot tables carry each look; and the Window
manager draws one gate per row. One block per position in `desk.css`, keyed on the attribute
alone, absent at the default (ADR 0030):

- **Shape** (`DeskShape`: Standard, Sharp, Round; `data-shape`, `desk.shape`) rescales the five
  radii together. Circles and capsules — switches, radios, dots, swatches — stay round.
- **Density** (`DeskDensity`: Standard, Compact, Roomy; `data-density`, `desk.density`) rescales
  the spacing ramp, the chrome, the control and row heights and a table cell's padding, which
  MudDataGrid reads through the bridge. Compact keeps the toolbar control, the small key and a
  cell's padding at Standard's and brings only the large key, a settings row and the title bar
  down, to 30px: nothing Standard draws at 28px or more falls under 28px.
- **Backdrop** (`DeskBackdrop`: Dots, Plain, Gradient, Grid; `data-backdrop`, `desk.backdrop`) draws
  the desk's ground behind the windows from tokens; Dots is the default grid. Plain under a
  contrast theme.
- **Typeface** (`DeskTypeface`: System, Humanist, Serif, Mono; `data-font`, `desk.font`) sets
  `--face-ui` and `--face-display` from local stacks only. Titles spend `--face-display`.

**Adding an appearance setting** — a ninth — is three edits, about twenty lines:

1. `Theming/` — an enum for its positions, default first (or none: a look's options are strings).
2. `Theming/DeskLook.cs` — one property in `DeskLooks` and its place in `All`:
   `Of("Contrast", "desk.contrast", "data-contrast", DeskContrast.Standard, [(DeskContrast.Standard, "Standard"), (DeskContrast.More, "More")])`,
   with `YieldsTo` one of `DeskLook.MachineSettings` if the machine can take it off — a closed set,
   today Reduce Transparency alone, because a query is only worth naming if `theme.js` watches it
   and `ThemeService` answers it; any other throws when `DeskLooks` is built. A typed
   member on `ThemeService` is optional.
3. `wwwroot/css/desk.css` — a block per position that stamps: `[data-contrast="more"] { … }`.

The gate, the storage, the stamp, the cross-fade, the reset, the boot script and the "at its
default" check follow from the row. `DeskLookTests` fails until every stamped position has a block
and the row is well formed, `WindowManagerControlsTests.Every_look_is_a_gate_in_the_appearance_group_in_its_order`
until it is a gate, and `ThemeBootTests.The_boot_tables_restore_every_look_ThemeService_stamps`
until the boot tables carry it.

The Window manager is a `.wflow` of section cards (see Primitives, "Section cards"). Its Appearance
card holds Mode beside Accent in one split row, then Glass, Corners, Density, Backdrop and Typeface
as form rows, and a "Reset appearance" key (`ThemeService.ResetAppearanceAsync`), dimmed
(`aria-disabled`, keeping its focus and Tab stop) and saying why on its row while nothing is off its
default; the reset puts the theme back too. The theme tiles are a card of their own, Themes, beside
it (`Span="DeskCardSpan.Rest"`), with a filter over the catalogue's headings in its bar that opens on
All, and a footer naming the family showing and its heading. The tiles
tick the showing family (`ThemeService.ShowingFamily`), so under Increase Contrast an unchosen desk
ticks High contrast, and pressing Standard there pins it.

Every family takes a chosen glass (`DeskGlass`: Clear, Tinted — macOS's Liquid Glass setting).
Clear is the materials as each block declares them; Tinted raises them halfway to the opaque
tier Reduce Transparency uses and keeps their blur. It is derived, not declared: under
`[data-glass="tinted"]` the end of `desk.css` mixes each `--material-*` from the showing family's
own value toward its `--surface-2` by `--glass-tint`, so no family block carries a tinted tier and
none is named. It is the look `DeskLooks.Glass`: kept under `desk.glass`, stamped as `data-glass`
only for Tinted, and — `YieldsTo` Reduce Transparency, which is fully opaque and wins — neither
stamped nor offered while the machine reduces transparency. The Window manager offers it as the
first row under Mode and Accent.

Three things follow the theme past the page. **The browser's chrome:** `DeskHead` declares an
empty `<meta name="theme-color">`, and `theme-boot.js` (once the sheets have parsed) and every
`theme.js` stamp fill it from the showing block's `--surface-0`, read back from the computed
token, so no palette lives in a script and a host's head needs nothing of its own. Selected text
is `::selection` in `--accent-wash-strong` behind `--text-primary` (`DeskThemeTests` holds it to
4.5:1 on every desk), and the caret is `--accent-ink`: a one-pixel stroke, like the focus ring's
and an open key's ring, so it spends the ink held to 4.5:1 on the panel rather than the fill.
**The switch:** an operator's change of any
appearance setting cross-fades the root in a view transition on `--dur-base` and
`--ease-standard`; `ThemeService` opens it (`beginTransition`), stamps and raises `Changed` inside
it, then settles it, and `theme.js` takes the new picture once the document has gone still, or
shortly after the settle (`SETTLE_LIMIT`) on a desk that never goes still, so MudBlazor's
re-render is never caught half-done and a ticking desk is not held to `HOLD_LIMIT`. A choice that
moves none of the attributes on `<html>` (System when the machine already answers it, an
accent for a family not showing, a glass under Reduce Transparency, a reset of a default desk) is stored and announced with no
transition. The first paint and the machine's own changes
(sunset, Increase Contrast, Reduce Transparency) stay instant, and so does everything under
`prefers-reduced-motion` or without the API. **Contrast themes:** one `@media (forced-colors:
active)` block at the end of `desk.css` restores what a Windows contrast theme drops — the focus
ring as a 2px `Highlight` outline, selection and "on" as `Highlight`/`HighlightText`, a 1px
system edge on controls and cards (the one place a control carries an edge at rest), `GrayText`
(on a switch's drawn track, thumb and words too, since its input is invisible),
`LinkText`, opaque materials — and only the theme and accent swatches keep their own colours
(`forced-color-adjust: none`).

## Primitives

| Primitive | What it is for | Wraps |
| --- | --- | --- |
| `CheckList` | Advisory checks as a grouped section: a mark, one sentence and a remedy per row. | — |
| `DeskAccentPicker` | A family's accent: one circle per hue its catalogue row offers, a radiogroup whose arrows wrap. | — |
| `DeskAlert` | A decision that must be made before anything else happens; confirms and destructive gates only. | `MudDialog`, through `IDeskAlerts` |
| `DeskBadge` | A count beside the name of what it counts, heard with its noun; nothing to count, no badge. | — |
| `DeskButton` | The key: the caller states how loud (`Emphasis`) and whether it destroys. Every weight wears the same finish — a top line and a sheen above the label (`--key-*`), an inner shade while held; Plain only while pointed at — so weight still carries the hierarchy. | `MudButton` |
| `DeskChart` | A chart that names the kind of number it shows and headlines its takeaway. | `MudChart` |
| `DeskCheckbox` | One setting that is on or off. | — |
| `DeskDialog` | A floating, non-modal panel for comparing things beside the desk. | — |
| `DeskDisclosure` | Details hidden behind a label until they are wanted. | — |
| `DeskErrorPage` | The page a host shows when a request failed: what happened, its reference, and the way back. | — |
| `DeskField` | A text or number field, with a real label and its error beneath it. | — |
| `DeskGrid` | The table: sortable columns and actionable rows in the Desk's dense rhythm. | `MudDataGrid` |
| `DeskJobMenu` | A key that opens a menu of named runs, each saying what it costs before it is chosen, grouped under their `DeskJob.Group` headings in first-appearance order. | — |
| `DeskLink` | A grid cell that opens a window of its own, naming where the click goes. | — |
| `DeskMenu` | A column of commands the keyboard can drive. | — |
| `DeskMenuGroup` | Related rows of a menu under an optional sentence-case heading, parted from the group before by one hairline; the heading is never a row, so the arrows and typeahead pass over it. | — |
| `DeskMenuItem` | One row of a menu: a label, a trailing detail that never wraps into it, and why it is dimmed; for an option, a `Figure` in its own right-aligned column, a `Mark`, and a `DetailFigure` under it; `Command` keeps a command in a radio menu from being heard as a choice. Sits in a `DeskMenu` or a `DeskMenuGroup`. | — |
| `DeskNotFound` | The page a host shows for an address that opens nothing, with the way back. | — |
| `DeskPopUp` | A pop-up button: a key whose face is the current choice (name, mark, figure), over a radio menu of `DeskPopUpOption`s, each with a right-aligned figure, a mark, a detail line and a second figure in its state's ink; `Commands` end the list behind a hairline, `Footer` says once what the figures are measured against, one option and no commands is a statement, and disabled it keeps its place, described by the visible reason. | — |
| `DeskPopover` | A panel anchored to the control that opened it, one at a time; held inside its window and any box that clips, scrolling within the room it has. | — |
| `DeskProgress` | A slim bar for named work that takes long enough to report. | — |
| `DeskPullDown` | A key that opens a menu of related commands; a long one in `DeskMenuGroup`s, and `Wide` (at least 320px) when its rows carry details and descriptions. | — |
| `DeskRadioGroup` | One choice among options that need room: a sentence each, or a picture of what choosing it does. | — |
| `DeskRow` | A horizontal group with its gap named from the spacing ramp. | — |
| `DeskSearch` | A search field with a magnifier, a clear key and a scoped placeholder; given a `ListId` it heads a popover list as a combobox. | — |
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
| `DeskTabs` | A window's sections, with the selection sliding between them; a `DeskTab`'s `Count` draws a badge after its name, and `Enabled: false` keeps an empty section in the bar, disabled, until it has something behind it. | — |
| `DeskTextArea` | A field with room for a paragraph. | — |
| `DeskThemePicker` | Every theme family as a tile painted by its own blocks, in one grid; `Group` filters it to one of the catalogue's headings (null or `""` for all), `ThemeFilters` is the segments a `DeskSwitch` over it offers (All, then each heading) and `GroupOf(family)` the heading a family is filed under. One radiogroup, arrows across the tiles showing, the choice marked by ring, tick and weight; when the filter hides the choice, the first tile showing holds the Tab stop. | — |
| `DeskToastStack` | The desk's notices, drawn — "Activities" to the operator: a capsule in the header's free stretch, newest in front with the count's key and older ones as edges stepped out to the right, fanned into a list beneath on hover, focus or the count, with Clear all; the key alone where the stretch is narrow. The header renders the one stack; panels raise through `DeskToasts`. | `MudSnackbarProvider` |
| `DeskToggle` | A setting that takes effect the moment it flips; announced as a switch. | — |
| `DeskTooltip` | A sentence beside the control it explains, for what a `title` cannot carry — `Text` alone. Or the structured shape, for an explanation that is a small table: a semibold `Title`, a `Caption` held to a 20rem measure (on `.desk-tip`, inside the popover, since Mud writes `max-width: none` inline on the popover itself), `Rows` of `DeskTooltipRow` (label leading, value trailing in tabular numerals, a quiet `Qualifier`, the one `Emphasis` row by weight), a `Footnote`, or free `Content`. The drawn shape is hidden from the accessibility tree and `Text` (or `Compose` of the parts) is what a screen reader hears. It holds no controls: say where the rest are in the footnote. | `MudTooltip` |
| `EmptyState` | What a panel says when there is nothing to show — first run, no match, or failure; `Compact` when it sits inside something small, an inline answer or a disclosure's body. | — |
| `FollowUpLayer` | The floating follow-ups a panel has open, drawn once per panel. | — |
| `FormGroup` | The grouped form, drawn as a section card: one filled group with one edge, its `Title` in a header bar inside it with one quiet thing trailing (`Keys`), its rows, a `Notice` and the explaining `Footer` under the card; `Span` (a `DeskCardSpan`) says how much of a `.wflow` row it takes. | — |
| `FormRow` | One row of a `FormGroup`: the label leading in the shared column, the control taking the rest; `LabelContent` when the label carries markup, `Lead` when the leading column is not a label. | — |
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
| `Roving` | The radiogroup keyboard contract for a group that draws its own radios: `Roving.Next(current, count, key, wrap)` says where an arrow, Home or End takes the choice (null for any other key), and a group carrying `data-roving` has those keys held from scrolling the page. | — |
| `RowActions` | The action strip on an open row, and the snippet it reveals. | — |
| `SnippetFailure` | What a snippet shows when its load failed, with the key to retry. | — |
| `SnippetShell` | The frame around an inline answer, on the section-card contract (ADR 0032): by default one card (`section.wsection > .wgroup`) with the `Title` in its header bar (`h3.wgroup__title`, the headline voice), the `Note` beside it and the escape (`OpenLabel`, "Open window" by default) as the bar's one trailing key, the answer as its rows. `Cards` (a `RowAction`'s `SnippetCards`) when the content draws section cards of its own: the shell is then an unframed pane header — same title, same key, no fill, no edge — because a card inside a card is forbidden. It draws no symbol; `Icon` is kept for callers. | — |
| `Tag` | A small state word. | — |

**Section cards.** A window's sections are `FormGroup` cards (ADR 0032). `FormGroup` renders
`section.wsection > div.wgroup`: the `--surface-2` fill with one 1px `--separator-strong` edge. Its
parameters:

- `Title` — the card's name, `h3.wgroup__title` in the header bar (`.wgroup__bar`) at the card's
  top: headline style, sentence case, and the section's accessible name. With neither `Title` nor
  `Keys`, there is no bar.
- `Keys` — what trails the title in the bar (`.wgroup__keys`): one small Plain or Tinted key, a
  count, or a `DeskSwitch` filter at `DeskKeySize.Small`; never the window's Filled key.
- `ChildContent` — the rows: `FormRow`s, split rows, or any `.wgroup__row`.
- `Notice` — a `Note` about the card's state, under the card and before its footer; never inside.
- `Footer` — the sentence that explains the card, `p.wgroup__foot`, under it.
- `Span` — a `DeskCardSpan`: `Track` (the default, one track: every card of form rows), `Row` (the
  whole row: a card whose body is a grid of its own) or `Rest` (the rest of the first row once two
  tracks fit, the whole row below that; it must be the flow's second card).
- `LabelWidth` — the group's label column (`--form-label`) as a CSS length; `Class`, `GroupClass`
  and `Style` for the section, the group and the section; other attributes land on the section.

The bar's one full-width hairline is a divider inside the one card, not a second edge; nothing in
the bar, a cell or a row draws a border or a shadow. Its fill is a `color-mix` of `--surface-2` and
`--surface-1`, so it is right in every family and appearance.

- **The flow.** A window of two or more sections puts its cards in `.wflow`: tracks of at least
  420px sharing the width (`auto-fill`), `align-items: start`, so a wide window gets more cards
  across, never wider ones, and a short card keeps its own height. 420px because form rows stack
  their labels under 400px of card: at 1600px that is three tracks of about 520px with labels beside
  their controls, and about 680px is one column. `Rest` moves beside the first card from 856px of
  flow (two tracks and the gap). Never cap such a window with a max-width column.
- **The split row.** Two compact, related controls in one row: `div.wgroup__row.wgroup__split`
  holding two `div.wgroup__cell`s, each a `span.wgroup__label` and its control, divided by an inset
  vertical hairline. The first cell's label takes `--form-label`. Under 720px of card the cells
  stack; a cell never wraps, its label staying beside its control until 400px, when both cells put
  their labels above their controls at once. Two full form rows are two `FormRow`s, not a split row.
- **What is not a row.** A `DeskGrid`, a lone `EmptyState` or an `InlineConfirm` goes straight in
  the card, and the card styles it: the table insets so its text starts where the bar's title
  does, and its last row's hairline stands down for the card's edge (no second frame, no doubled
  bottom edge); the sentence and the confirm take the rows' inset. A window writes none of this
  in its own sheet.
- **Narrow is the card's.** The group is a container: form rows stack their labels under 400px of
  card, split cells under 720px, and the bar, rows and cells tighten their inset under 480px.
- **Spelled by hand**, only where `FormGroup` cannot be used: `section.wsection` (with
  `aria-labelledby`) `> div.wgroup > div.wgroup__bar > h3.wgroup__title` (and `div.wgroup__keys`),
  the rows, then `p.wgroup__foot` after the `.wgroup`. `.wgroup__head`, a quiet line above the
  group, is the old spelling, kept for sections not yet moved; nothing new uses it.

Toasts are raised through a service, `DeskToasts` (`Success`, `Info`, `Warn`, `Fail`, `Added`,
`Removed`, `Show`; `Raise` returns the notice so its owner can take it down), and drawn by the
header's one `DeskToastStack` (ADR 0028). A success, an info or a neutral notice goes after
`BriefLife` (5s), a warning after `WarningLife` (8s); a failure, and anything with a key, stays
until it is pressed or dismissed; every clock stops while the pointer or focus is on the stack.
The window manager's notice (a window closed, with Reopen; the ceiling's refusal) is said through
the same stack, so nothing is drawn in the header. MudBlazor's snackbar is not rendered.

The stack lives in the header bar (`DeskHeader` renders it), in the stretch the window strip
leaves free before the desk's own keys. It takes only that stretch, so it never covers or pushes
the wordmark, the strip, search or the window count. It is one capsule a title bar tall:
- the count's key leads;
- the newest notice is in front, its words and its key cut to one line with an ellipsis (the whole
  of each is its tooltip);
- up to two older ones show as edges stepped out to its right.

Pointing at it, focusing into it or pressing the count fans it: the capsule reads "Activities · 4"
with Clear all, and the list drops beneath it as a popover, newest first, every notice whole and
wrapped. Where the stretch is too narrow, the capsule folds to its key alone ("Activities · 4",
"Activity · 1"), which drops the same list. A notice slides in along the bar and settles (a fade
under reduced motion).

To the operator it is **Activities**:
- the region is named "Activities, 4 activities";
- the count's key is "Show all 4 activities" / "Fold all 4 activities" ("Show 1 activity" for one);
- Clear all is "Clear all activities" to a screen reader.

Being part of the header, it lies under an open alert's or sheet's scrim and the palette, and its
fanned list (no z-index of its own) under the header's own menus and drawers.

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
- `MainLayout` renders MudBlazor's three providers — `MudThemeProvider` (fed the theme by
  `ThemeService`), `MudPopoverProvider` and `MudDialogProvider` — and the desk's own
  `DeskToastStack` in place of MudBlazor's snackbar, so a consuming application renders none of
  them.

## The Parts bin

The Parts bin is the gallery: every primitive, live, on the same surface and under the same
theme as the window being built beside it. It is the Desk's own component (`PartsPanel`, in
`XLines.Desk.Windowing`); an application lists it in its catalogue under the key `parts`, and
a fresh project that wires the Desk can open it from the strip or the palette.

## The rules

| Rule | What it means | Held by |
| --- | --- | --- |
| Edges | A control or group has one edge. A form is not boxed around a bordered group, and a group carries its hairline where it is defined. A section card's name is in its header bar, whose full-width hairline divides it from the rows inside the one card; the footer is under the card. | `DeskCssConventionTests` (`A_form_is_not_boxed`, `A_group_carries_its_hairline_where_it_is_defined`) |
| Sections are cards in a flow | A window's section is one card named in its bar, nothing inside it draws a box, and a window of several puts them in `.wflow`: more width is more columns, never wider cards. | `FormGroupTests` (`The_bar_is_divided_from_the_rows_by_one_full_width_hairline_and_draws_no_edge_of_its_own`, `Nothing_inside_a_card_draws_a_box`, `The_flow_adds_columns_rather_than_widening_cards_and_aligns_them_to_the_start`) |
| One Filled key | A context has at most one Filled key, last in its row; everything else is Tinted or Plain. A selected choice in the accent's wash (a column's showing tab, a checked toggle) is selection, not a Filled key. | `PanelToolbarTests.Keeps_every_key_in_one_actions_item_after_the_pickers` (the Filled key last); the count is held in review |
| Sentence case | Titles, headers, keys and tags read in sentence case. | `PanelHeader` and `PanelSection` set the rule's type; no test reads copy yet |
| Keys explain themselves | A key that waits says why in text on its row, not in a title a disabled key never shows, and the key is described by that text; secondary keys come first and the Filled key last, and a confirm asks Cancel first. | `KeyRowTests`, `KeyRowAdoptionTests` (in the host's tests), `InlineConfirmTests` |
| Names on controls | Every field has a label, an `aria-label` or an `aria-labelledby`; an alert names its consequence. | `DeskConventions.EveryFieldHasAName` (run by the host's `DeskConventionTests`), `DeskAlertTests.Every_caller_must_name_the_confirm` |
| No literal colours | A colour is a token. Only the token blocks write one. | `DeskCssConventionTests.No_literal_colour_outside_the_token_blocks`; an application's sheet, `DeskConventions.AppSheetSpendsTokens` |
| No domain | The Desk's code and its documentation name no application's records. | `DeskVocabularyTests` |

## Releasing

The packages are published to GitHub Packages by `.github/workflows/desk-packages.yml`. Every
push to `main` packs them as a dry run (the `.nupkg` files are the run's artifact); a tag
publishes them.

1. Raise `<Version>` in `XLines.Desk.csproj`, and in `XLines.Desk.Hosting.csproj` if Hosting
   changed. Raise the same versions in the template's
   `src/XLines.Desk.Templates/content/deskapp/Directory.Packages.props`, and the template's own
   `<Version>` with them (`DeskTemplateTests` fails until the two agree). The HIG skills a new
   application's agent reads are packed from this repository's `.claude/skills` as the template
   builds, so a skill edit needs no copy step (`DeskTemplateSkillsTests` holds every Desk skill
   there to being packed; the application's own `xlines-*` skills are left out). Commit on
   `LineX_Development`, merge to `main`, and push.
2. Tag the merge with the Desk's version and push the tag:

   ```bash
   git tag desk-v0.5.0 main
   git push origin desk-v0.5.0
   ```

3. The workflow refuses a tag that disagrees with `XLines.Desk.csproj`, packs the Desk, Hosting,
   `XLines.Desk.Testing` and `XLines.Desk.Templates`, and pushes each with `--skip-duplicate`: a package whose version
   is already on the feed — an unchanged Hosting — is passed over, not failed on. A version once
   published is never replaced; a fix is a new version and a new tag.

A consumer restores from the feed with [`docs/desk/nuget.config`](../../docs/desk/nuget.config)
beside its solution, and `DESK_FEED_USER` and `DESK_FEED_TOKEN` (a token with `read:packages`) in
its environment. A new application starts from the template, which brings that file with it:
`dotnet new install XLines.Desk.Templates`, then `dotnet new deskapp` (its
[README](../XLines.Desk.Templates/README.md) lists the options).
