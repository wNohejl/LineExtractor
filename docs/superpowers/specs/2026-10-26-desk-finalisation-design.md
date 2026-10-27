# Finishing the Desk — design

26 October 2026. The design for the last pass of the Apple-HIG-on-MudBlazor front-end rewrite:
the pass after which the desk reads as a finished product in the browser and `src/LineOps.Desk`
has the shape of a package that could be lifted out of this repository without dragging LineOps
with it.

## Intent

LineOps is the design home for a reusable Blazor UI system. The system is built on MudBlazor
today because MudBlazor is what this project set out to learn; other frameworks may follow, and
nothing here plans for them. The desk's windows are the system's first consumer and its proof.
"Finished" therefore has two faces that are done together:

1. **Every window holds to the HIG rulebook** in both appearances on a laptop or desktop screen:
   one anatomy, grouped forms with no nested frames, one filled key per view, named controls,
   quiet success, specific and recoverable errors, no flash of the wrong theme.
2. **The Desk is separable.** Its stylesheet holds no window of this app; its namespace holds no
   type of this app; its tests do not need this app; a consumer wires it with one service call,
   one head component and one script component; a README says how.

Four surveys of the code on 26 October (boundary, window audit, primitive inventory, rendering
cost) are the evidence behind each item below. Where this document names a file and line it is
from those surveys at commit `55d43e6`.

## Goals

- Every window passes the HIG review checklist (apple-hig-web SKILL.md) at 1280×800 and
  1920×1080, light and dark, with a screenshot of each kept as the proof.
- `src/LineOps.Desk/wwwroot/css` contains only system CSS; app CSS lives in `src/LineOps.Web`.
- `dotnet pack src/LineOps.Desk` produces a package, and a fresh Blazor Server project can show
  the Parts bin with the wiring the README gives — no further hand-copying of links.
- A `tests/LineOps.Desk.Tests` project covers every primitive without a reference to LineOps.Web.
- The desk's chrome re-renders once per status change, not the whole desk twice per window load;
  a browser resize costs one layout per animation frame; the head carries no unused CSS.
- The primitive set gains the six HIG controls it lacks: toggle, badge, spinner, tooltip, radio
  group, slider.

## Non-goals

- **Phone and tablet layouts.** The floor is a laptop at 1280×800. Nothing under 1024px wide is
  designed for; the existing 720px fallback stays as it is.
- **Renaming the assembly or namespace.** `LineOps.Desk` keeps its name and its `_content` path
  until the day it is exported; the plan only gathers the path into one constant.
- **Replacing MudBlazor** or wrapping its data-grid column API. `PropertyColumn`/`TemplateColumn`
  inside `DeskGrid` and `ChartSeries<double>` into `DeskChart` stay the documented seam (ADR 0008).
- **Trimming MudBlazor's own CSS** (611 KB). It is fingerprinted and cached in this pass, not cut.
- **The wager window's cards.** Commit `e36c3b6` made the wager window two bordered cards by the
  operator's choice. The 25 October audit ruled it out of scope and this document keeps that.
- **An LLM analyst, UFC work, data changes.** Other specs own those.

## Anti-goals

- A window that looks finished in dark and wrong in light. Both are checked, every time.
- A "system" stylesheet that still knows what a board or a book is.
- A primitive added for completeness and used nowhere: every new primitive replaces an existing
  hand-built control in a window.
- Rewriting history, adding worktrees, or leaving a branch behind (CLAUDE.md).

## Constraints

- CLAUDE.md governs: two branches, every commit pushed, developer identity, `type(scope):`
  subjects with a why-body; `dotnet build` clean, `dotnet test` green, the Worker starts; and
  `dotnet format --verify-no-changes` clean in the `mcr.microsoft.com/dotnet/sdk:10.0` container
  because CI runs on Linux and `LineX_Development` gets no CI.
- The HIG rulebook is `~/.claude/skills/apple-hig-components` (components, forms, audit,
  type-color-layout) and the apple-hig-web skill's checklist. Where they disagree with an older
  spec, the skill wins (the 25 October audit set that precedent).
- **The house rule on edges:** one 1px hairline on a group or card is correct; a bordered thing
  inside a bordered thing is not. `.wgroup`'s border at lineops.css:5223 is the rule, and the
  comment at 4587–4592 that calls a group "never a frame" is the stale text to fix.
- Another Claude session may share the checkout. Build to a scratch output folder, preview on
  port 5271 with the scheduler off, never stop another session's process, commit with an
  explicit pathspec (`git commit -- <paths>`), and treat `lineops.css` as single-owner while a
  task holds it.
- MudBlazor stays at 9.7.0 (`Directory.Packages.props`).

## The work, in four phases

The phases run in this order because each one leaves the next a stable target: the windows are
fixed before the CSS that styles them is split; the CSS is split before the package is packed;
new primitives land in a package that already has a gallery and a test project; performance is
measured last against a desk that is no longer changing shape.

### Phase A — finish the windows

**Forms.** Every form on the desk is a `PanelForm Class="pform--grouped"` holding
`section.wsection` groups. The ones that are not yet: Incidents' RCA (`IncidentsPanel.razor:253`,
a bordered `.pform` of stacked text areas), Performance's bankroll (`PerformancePanel.razor:147`,
a field in a section's key slot with a hidden label), Ops' Scanning and Drill
(`OpsPanel.razor:120, 366`, rows without a section), History's Run group (`HistoryPanel.razor:109`),
Place wager's groups (`BoardWager.razor:230–402`, rows without `.wsection` — the cards stay),
and the Journal parlay form's first group (`JournalPanel.razor:168`, a `div.wsections` where a
`section.wsection` belongs). The non-grouped `.pform` border (lineops.css:3234–3243) goes: a form
is either grouped or it is a bare key row, never a third box.

**Boxes.** `.loglist` (3838) and `.metric` tiles (1901) carry one hairline each, which is the
rule; they stay. What changes is anything nested inside them that draws its own edge — the
builder checks each in the browser and removes the inner edge.

**Copy.** "Yes, scan automatically" (`OpsPanel.razor:135`) becomes "Scan automatically";
"Yes, re-walk" (`HistoryPanel.razor:173`) becomes "Re-walk". Buttons are verbs.

**Names for controls.** The stake field (`StakeRow.razor:15`) is labelled by its group label
(`aria-labelledby`), not a placeholder. The Team window's opponent select
(`TeamPanel.razor:238`) gets a label. The Parts bin's search gets one.

**Errors that reach the operator.** Each window body renders inside an `ErrorBoundary` whose
error content is `EmptyState Kind="Error"` with the window's "Try again" key, so an exception
in a write action no longer drops the circuit. The write actions the audit found uncaught
(Journal save/delete/settle, Incidents note/mitigate, Ops actions, Performance bankroll) catch
and show the failure inline, as Place wager already does ("The entry was not logged. Try
again."). Line movement, Every book and Recent form load through `WithActivityAsync` so they
show the skeleton while loading and `LoadFailure` on failure, as the other windows do.

**Both appearances, from the first frame.** The theme attribute is stamped on `<html>` before
first paint by a tiny inline script in `App.razor` that reads the stored theme and, for
"system", `prefers-color-scheme` — the same resolution `theme.js` does later. `ReconnectModal.
razor.css` and the palette scrim (lineops.css:1757) move to tokens; after this there is no
literal colour outside a token block.

**Small drift.** The toolbar container query that `controls.css:474` overrides
(`lineops.css:3192`) is resolved to one rule in one file. Stale doc comments in
`PanelHeader.razor:4` and `PanelTitle.razor:7` that still describe uppercase rules are
rewritten. The league-key `.ToUpperInvariant()` calls stay: MLB, NFL and UFC are initialisms.

**Proof.** A browser walk of all nineteen windows at 1280×800 and 1920×1080, dark and light,
with screenshots saved under `docs/superpowers/evidence/2026-10-desk-final/`, and an axe-core
run in each window reporting no serious or critical violation. The walk is the acceptance test
for this phase and is repeated at the end of Phase D.

### Phase B — the package shape

**Two stylesheets.** `lineops.css` is split along the survey's section map. The system file,
renamed `desk.css`, keeps the tokens, scale, shell, window strip, marble, glide, windows,
dialogs, buttons, fields, panel anatomy, loading vocabulary, a11y, sheet, alert, swatches and
material fallbacks. A new `src/LineOps.Web/wwwroot/css/lineops.css` takes the price cell and
spread rail, runbook, splits strip, line movement, board rhythm, Performance, Parts, the wager/
slip/journal block, operations, game lines, and the app fragments inside mixed sections
(`.teammark`, `.scoreboard`, `.game-search`, the odds and form snippets, `.logrow__opp/score`,
`.price__sel`). The one app selector in `mud-bridge.css:467` moves with them. The Window
manager's `.wm-*` block moves with its panel (next item). Order stays load-bearing and is
asserted by a test that reads both files for the selectors each side may own.

**The Window manager belongs to the Desk.** It is the desk's own settings window (appearance,
scale, workspaces, capacity). If it references no LineOps.Web type beyond the catalogue
interface the Desk already defines, `WindowManagerPanel` and its CSS move into
`src/LineOps.Desk/Windowing`; if it does, the app-specific part is split off first. The
builder confirms the dependency list before moving it.

**Namespaces.** `PriceCell.razor` and `PullMenu.razor` leave `LineOps.Desk.Primitives` for
`LineOps.Web.Components.Desk`. `SnippetFailure` and `CheckList` with its `Check` record (state,
sentence, remedy) move into the Desk, since nothing in them is this app's. `ResultKind`,
`ResultTag`, `StakeRow`, `BookPicker`, `SelectionChips`, `Scoreboard`, `TeamMark`, `TeamColors`,
`RunbookSteps`, `Checks` stay in the app.

**Words.** `CommandPalette`'s placeholder ("Go to a team, player, game or window") comes from
`IDeskSearch` (a `Placeholder` property the app's search supplies). `Tag.Live` is documented as
"something is happening now". Storage keys `lineops.theme` / `lineops.theme.family` become
`desk.theme` / `desk.theme.family`, read once from the old key so no one loses their theme.
The DOM event `lineops:rescaled` and the `__lineops*` expandos become `desk:*`. The
`_content/LineOps.Desk` path is gathered into one `DeskAssets.Root` constant. A test greps the
Desk's `.cs`, `.razor` and `.js` for the domain words (game, book, odds, wager, journal, team,
player, fight, bout, stake, parlay, slip, league) outside comments and fails on any; the ~120
doc-comment uses are rewritten by hand in the same task so the shipped XML docs read as the
package's, not this app's.

**Wiring a consumer.** `AddDesk(...)` registers MudBlazor's services with the snackbar
configuration `Program.cs:25–48` holds today, so the consumer does not call `AddMudServices`.
A `<DeskHead />` component emits the five stylesheet links in order, each through `@Assets` so
they are fingerprinted (MudBlazor's included), and a `<DeskScripts />` emits MudBlazor's script
and `desk-glide.js`. `App.razor` uses both. The ten sheets a consumer currently copies by hand
reduce to those two components, `AddDesk`, the layout and the `ReconnectModal`.

**Sheets without MudBlazor in the caller.** `PartsPanel.razor:1047–1062` opens a `DeskSheet`
through `IDialogService` directly. An `IDeskSheets.ShowAsync<TSheet>(parameters, width)` on the
Desk, beside `IDeskAlerts`, takes over, and no file in `LineOps.Web` injects `IDialogService`.

**A test project of its own.** `tests/LineOps.Desk.Tests` references only `src/LineOps.Desk`,
with a `DeskTestContext` that registers a `FakeWindowCatalog`. The twenty-odd primitive and
theming test files that use only Desk types move there unchanged; the ones that render app
components (DeskDisclosureTests via RunbookSteps, DeskAccessTests via PriceCell, the windowing
tests on `AppWindowCatalog`, LiveAndResultTests, DisplayTimeTests, UnsavedInputTests,
WindowManagerControlsTests) stay in `LineOps.Web.Tests`. The format gate and CI run both
projects.

**Packing.** `LineOps.Desk.csproj` gains `IsPackable`, `PackageId=LineOps.Desk`, `Version=0.1.0`,
`Description`, `PackageLicenseFile` for the Inter licence already shipped, and
`GenerateDocumentationFile`. `dotnet pack` is part of the verification. A `README.md` in
`src/LineOps.Desk` covers: what it is, wiring (the four lines), the token families and how a
theme family is added, the primitive table with one line each, the MudBlazor seam and its
rules, and the Parts bin as the living gallery.

### Phase C — the missing controls

Six primitives, each replacing a hand-built control the survey found, each with a test, a Parts
bin section and a README row:

| Primitive | Semantics | Replaces |
|---|---|---|
| `DeskToggle` | `role="switch"`, label leading, on/off trailing, 28pt row | `DeskCheckbox` used for on/off in the Window manager and Ops Scanning |
| `DeskBadge` | a count on a key or tab; `aria-label` "n items" | counts written into labels ("Slip · 2") |
| `DeskSpinner` | indeterminate activity, 16/20pt, `role="status"` | nothing today for a short wait inside a key or row; `DeskButton Busy` keeps its shimmer |
| `DeskTooltip` | hover and focus, 300ms delay, `role="tooltip"`, escape closes | the one `MudTooltip` in `PriceCell.razor:32`; everywhere else `title` stays |
| `DeskRadioGroup` / `DeskRadio` | `role="radiogroup"`, arrow keys, one tab stop | the raw `role="radio"` swatches in the Window manager, and `SelectionChips`'s hand-built gate if it is a radio and not a segmented control (the builder decides from its behaviour) |
| `DeskSlider` | `<input type="range">` with label, value and ticks | the raw range input for UI scale in the Window manager |

Also: tests for the twelve primitives that have none (DeskCheckbox, DeskSelect, DeskTextArea,
DeskPopover, DeskProgress, DeskRow, DeskStack, DeskStep, Metric, MetricRow, RailMenu, Glyph);
`DeskStepper` handles arrow keys; the Parts bin shows the ten primitives it omits (DeskCheckbox,
DeskDisclosure, DeskMenu, DeskPullDown, DeskPopover, DeskStepper, DeskSparkline, DeskLink,
RowActions, SnippetShell).

### Phase D — rendering cost

Each item has a measurement before and after, kept in the plan's evidence folder.

- **Chrome re-renders.** One `WindowManager.Notify()` today re-renders the desk, header, bar,
  footer and every window's chrome, and each window load calls it twice. After: a status change
  re-renders that window's title bar and the bar's pulse; a layout change re-renders the desk
  row once. The mechanism (a per-window signal, a version on `WindowInstance`, `ShouldRender`)
  is the builder's; the measure is bUnit render counts on `AppWindow` and `Desk` per `Notify`.
- **Resize.** `windowing.js:17`, `desk-glide.js:227` and `dialogs.js:66` run on every `resize`
  event. After: one `requestAnimationFrame`-coalesced pass, and `.NET` hears about the viewport
  at most once per frame.
- **The body-wide observer.** `desk-glide.js:148–175` watches the whole document and queries
  every added node. After: marble hosts register themselves (a `data-glide-host` root per
  container), and only those subtrees are observed.
- **Grid double render.** `DeskGrid` decides alignment and resizability in `OnAfterRender` and
  calls `StateHasChanged`. After: decided in `OnParametersSet`, one render.
- **Head weight.** Remove `wwwroot/lib/bootstrap` (1.7 MB, unreferenced). Every link and script
  through `@Assets` (fingerprinted, immutable). Preload the regular Inter face; drop the italic
  face's two uses or keep it lazy. Target: head CSS excluding MudBlazor under 350 KB
  uncompressed, measured with `read_network_requests`.
- **Panels that fetch twice.** `PerformancePanel` derives its pickers from the one unfiltered
  read rather than calling `PerformanceAsync` twice. `TaleOfTheTape` no longer reloads on every
  Game render (cache the `EventCallback`, or key the load on the bout id). `HistoryPanel` goes
  through the 1 s debounce and single-flight that `PanelBase` already offers instead of
  reloading per `BackfillCoordinator.Changed`. Read-only queries in Runs, Line movement,
  Incidents and History use `AsNoTracking`, and `IncidentService.GetAllAsync` takes a limit.

## What is left to the builder

- Exact mechanics of the chrome re-render fix and the glide observer, within the measures above.
- Whether `SelectionChips` is a radio group or a segmented control, after reading how it is used.
- The split point for any mixed CSS section not named above, following the rule "named after a
  window or a domain → app".
- How the evidence screenshots are taken (the browser pane at a set viewport is fine).
- Task ordering inside a phase; the phases themselves stay in order.

## Verification, whole project

`dotnet build` on `src/LineOps.Web`, `src/LineOps.Worker` and all three test projects;
`dotnet test` green; the Worker starts; `dotnet format --verify-no-changes` clean in the Linux
container; `dotnet pack src/LineOps.Desk` succeeds; the browser walk of Phase A repeated with
the screenshots refreshed; the Phase D numbers recorded. Each task commits on
`LineX_Development` and pushes.
