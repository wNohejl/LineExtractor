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
builder.Services.AddDesk(new DeskBrand("ACME", "DESK", "what the product does", "Go to a customer or window"));
builder.Services.AddSingleton<IWindowCatalog, AcmeWindowCatalog>();
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

Add `@using LineOps.Desk`, `LineOps.Desk.Layout`, `LineOps.Desk.Primitives` and
`LineOps.Desk.Windowing` to the application's `_Imports.razor`.

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

## Primitives

| Primitive | What it is for | Wraps |
| --- | --- | --- |
| `CheckList` | Advisory checks as a grouped section: a mark, one sentence and a remedy per row. | — |
| `DeskAlert` | A decision that must be made before anything else happens; confirms and destructive gates only. | `MudDialog`, through `IDeskAlerts` |
| `DeskBadge` | A count beside the name of what it counts, heard with its noun; nothing to count, no badge. | — |
| `DeskButton` | The key: the caller states how loud (`Emphasis`) and whether it destroys. | `MudButton` |
| `DeskChart` | A chart that names the kind of number it shows and headlines its takeaway. | `MudChart` |
| `DeskCheckbox` | One setting that is on or off. | — |
| `DeskDialog` | A floating, non-modal panel for comparing things beside the desk. | — |
| `DeskDisclosure` | Details hidden behind a label until they are wanted. | — |
| `DeskField` | A text or number field, with a real label and its error beneath it. | — |
| `DeskGrid` | The table: sortable columns and actionable rows in the Desk's dense rhythm. | `MudDataGrid` |
| `DeskLink` | A grid cell that opens a window of its own, naming where the click goes. | — |
| `DeskMenu` | A column of commands the keyboard can drive. | — |
| `DeskMenuItem` | One row of a menu: a label, a trailing detail, and why it is dimmed. | — |
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
| `Metric` | One number, its label, and the qualifier that stops it being misread. | — |
| `MetricRow` | The shelf a row of metrics sits on. | — |
| `Note` | Passive feedback beside the thing it describes, for as long as it is true. | — |
| `PanelForm` | The inset card for a task completed in place, with its footer pinned. | — |
| `PanelHeader` | The sentence-case rule that names a stretch of a panel. | — |
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
| Names on controls | Every field has a label, an `aria-label` or an `aria-labelledby`; an alert names its consequence. | `AccessibleNameTests`, `DeskAlertTests.Every_caller_must_name_the_confirm` |
| No literal colours | A colour is a token. Only the token blocks write one. | `DeskCssConventionTests.No_literal_colour_outside_the_token_blocks` |
| No domain | The Desk's code and its documentation name no application's records. | `DeskVocabularyTests` |
