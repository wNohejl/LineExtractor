# HIG Live Audit — Foundations, Patterns, Components Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the gaps the two live-HIG audits of 25 October found between the desk and Apple's current guidance — menus that work from the keyboard, a toolbar that overflows instead of wrapping, alerts whose default button is the safe *and* likely one, no silent loss of typed input, contrast that passes in every theme, the OS contrast and transparency settings honoured, one focus ring, and the HIG components the desk still improvises (stepper, disclosure, search field, pull-down, popover).

**Architecture:** New Desk primitives (`DeskPopover`, `DeskMenu`, `DeskPullDown`, `DeskStepper`, `DeskDisclosure`, `DeskSearch`) with keyboard behaviour in a small JS module, adopted by the three hand-rolled menus and the panels that improvise. Foundations work is tokens and stylesheet rules (`lineops.css`, `themes.css`, `mud-bridge.css`, `theme.js`). Pattern work is behaviour in `WindowManager`, `AppWindow`, `windowing.js` and the Journal form, each pinned by a bUnit test.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.7, bUnit on `DeskTestContext` (`tests/LineOps.Web.Tests`), PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-10-25-hig-live-audit-foundations-patterns.md` and `docs/superpowers/specs/2026-10-25-hig-live-audit-components.md` (line numbers at commit `195e41e`). Rulebook: `.claude/skills/apple-hig-components` (SKILL.md, `references/*`). The sibling plan `2026-10-25-hig-desk-quality.md` Tasks 11–14 are being implemented in another session at the same time; see "Two waves".

## Two waves, because another session shares this checkout

A second session is finishing Tasks 11–14 of the earlier plan in this same working copy (not a worktree). Until it reports that Task 14 is pushed, these files are **its**, and no task here may edit them:

`lineops.css` (all of it), `GamePanel.razor`, `BoardPanel.razor`, `BoardWager.razor`, `TeamPanel.razor`, `HeadToHeadPanel.razor`, `Components/Snippets/*`, `Tag.razor`, `SnippetShell.razor`, `DeskGrid.razor`, `RowActions.razor`, `Desk/StakeRow.razor`, `Desk/Scoreboard.razor`, `Desk/BookPicker.razor`, and the wager/RowActions test files.

- **Wave 1 (Tasks 1–5)** touches none of those: behaviour, markup, `mud-bridge.css`, `themes.css`, JS, new primitives. New primitives that need styling put it in a **new file `src/LineOps.Desk/wwwroot/css/controls.css`**, linked in `src/LineOps.Desk/Layout/MainLayout.razor` (or wherever `lineops.css` is linked — grep for it) immediately after `lineops.css`, using only existing tokens. Wave 2 keeps that file; it is the controls' home, not a stopgap.
- **Wave 2 (Tasks 6–11)** starts only after the controller says the other session's Task 14 is pushed. It owns `lineops.css` and the files above.

Tasks within a wave run in parallel, one subagent each; no two tasks in a wave share a file. **Subagents never run git commands**; the controller stages each task's files by name and commits.

## Global Constraints

- Branch `LineX_Development`, direct; one commit per task by the developer's own identity; **no `Co-Authored-By` trailer of any kind** (CLAUDE.md). Push after every commit. `git add` by file name, never `-A`: another session commits to the same tree.
- Build/test from the repo root in PowerShell: `dotnet test tests/LineOps.Web.Tests -nologo -v q` (builds Desk and Web transitively). A failure that is a file lock or a missing `obj` artefact is another session building; wait 20 s and retry once before reporting. Never `Stop-Process` another LineOps.Web; never start the web app (the controller previews on port 5271 from a scratch output).
- House rules: one 1px `--separator` edge per group or card, never nested; hairlines and fills inside; no vertical rules in tables. Type only on the ladder `--text-largetitle … --text-caption`, weights only `--weight-*`; nothing under `--text-caption`. Spacing `--space-1..6`. Colour only by token; the accent on interactive things, focus, selection and the one primary key. Sentence case; verbs on keys; no exclamation marks; errors say what happened and what to do, never `ex.Message`.
- Density is macOS: 28px controls in toolbars and form rows, 24px keys inside grid rows, 29px grid rows. Do not adopt 44pt targets, iOS switches, title-case labels, uppercase headers, Liquid Glass toolbars, a help key per window, per-window sheets, or a menu bar (spec "Do not adopt").
- CSS selectors: single class, `.a + .b`, `.a > .b`, `.a .b`; no `!important`; no inline `style` for size, spacing or type in panels.
- No new NuGet packages. No MudBlazor components beyond those already in use (`MudDataGrid`, `MudChart`, `MudDialog`, snackbar).
- Keyboard rules for every popover-like thing (menu, pull-down, book pop-up): focus moves into it on open, arrows move, Home/End jump, typing a letter jumps to the next item starting with it, Enter/Space activate, Escape closes and returns focus to the opener, outside click closes. One popover open at a time.

## Review Focus

1. **A menu opened by keyboard, then a second one opened by pointer.** The first must close; focus must land in the second. → Task 1 test `Opening_a_second_menu_closes_the_first`.
2. **Closing a window with a half-typed Journal entry, including by eviction and by Close all.** It must ask, and eviction must skip it. → Task 2 tests `Close_asks_when_a_window_holds_unsaved_input`, `Eviction_skips_a_window_with_unsaved_input`.
3. **Return pressed on a destructive alert.** It must do nothing (no default) rather than confirm or cancel. → Task 5 test `Destructive_alert_has_no_default_button`.
4. **Every light theme's positive, warning and negative text.** ≥ 4.5:1 on `--surface-1`; the test computes it from the token values. → Task 6 test `State_ink_tokens_pass_contrast_in_every_theme`.
5. **A 300px-wide Board window.** Its toolbar must stay on one line with a "More" pull-down, every control still reachable. → Task 8 test `Overflow_items_move_into_the_more_menu`.

---

## Wave 1

### Task 1: One popover, one menu, with a keyboard

**Files:**
- Create: `src/LineOps.Desk/Primitives/DeskPopover.razor`, `src/LineOps.Desk/Primitives/DeskMenu.razor`, `src/LineOps.Desk/Primitives/DeskMenuItem.razor`, `src/LineOps.Desk/Primitives/DeskPullDown.razor`, `src/LineOps.Desk/wwwroot/js/popover.js`, `src/LineOps.Desk/wwwroot/css/controls.css`
- Modify: `src/LineOps.Desk/Windowing/RailMenu.razor` (panel becomes a `DeskPopover` + `DeskMenu`; keep its `Trigger="brand"`, `AlignEnd`, `Class`, `Close()` surface), `src/LineOps.Web/Components/Desk/PullMenu.razor:21–62` (becomes a `DeskPullDown` whose items are the jobs: label, cost as detail, description, `Disabled` with the `Unavailable` sentence as reason). **Do not edit `DeskHeader.razor` or `CommandPalette.razor`** (Task 3 owns the header this wave): `DeskMenu`'s keyboard handling must therefore work on any descendant `[role="menuitem"]` button, not only on `DeskMenuItem`, so the existing `launcher__item` buttons keep working unchanged.
- Link `controls.css` after `lineops.css` where that is linked (grep `lineops.css` under `src/`)
- Test: `tests/LineOps.Web.Tests/DeskMenuTests.cs`

**Interfaces:**
- Produces `DeskPopover`: parameters `bool Open`, `EventCallback<bool> OpenChanged`, `RenderFragment Anchor` (the opener; the component wires `aria-haspopup`, `aria-expanded` and `aria-controls` onto its first element via `popover.js`), `RenderFragment ChildContent`, `DeskPopoverAlign Align { Start, End }`, `string? Class`. Behaviour: renders `<div class="popover" role="presentation">` + `<div class="popover__panel" id=…>`; on open calls `popover.js` `open(panelEl, anchorEl)`; the module keeps a single `current`, closes the previous one (invoking its `.NET` `OnDismiss`), listens for outside `pointerdown` and `Escape` on `document`, flips the panel to open upward when it would cross `window.innerHeight`, and returns focus to the anchor on close.
- Produces `DeskMenu`: `RenderFragment ChildContent`, `string Label` (aria-label). Renders `role="menu"`; on first render focuses the first enabled item; handles ArrowUp/Down (wrap), Home, End, typeahead (first letter, 500 ms buffer), Enter/Space on the focused item. Roving `tabindex`: focused item `0`, others `-1`.
- Produces `DeskMenuItem`: `string Label` (required), `string? Detail` (trailing, secondary, tabular), `string? Description` (second line, footnote secondary), `string? Icon`, `bool Disabled`, `string? DisabledReason` (rendered as the description when disabled, and as `title`), `bool Destructive`, `bool Checked` (leading check glyph, `aria-checked`, `role="menuitemradio"` when the parent `DeskMenu` sets `Radio="true"`), `EventCallback OnClick`. Renders `<button type="button" role="menuitem" class="menu__item …">`.
- Produces `DeskPullDown`: `string Label` (the key's text), `string? Icon`, `DeskEmphasis Emphasis = Tinted`, `DeskKeySize Size`, `bool Busy`, `string? Title`, `RenderFragment ChildContent` (DeskMenuItems). A `DeskButton` with a trailing `chevron.down` glyph (`Icons.Material.Filled.ExpandMore`), opening a `DeskPopover` holding a `DeskMenu`. Choosing an item closes the menu.
- `controls.css` classes: `.popover`, `.popover__panel` (material `--material-regular`, blur, `--radius-panel`, `--shadow-3`, padding `var(--space-1)`, `min-width: 200px`, `max-width: 360px`, enters with `opacity 0→1, translateY(-4px)→0` over `--dur-fast --ease-standard`, reduced motion opacity only), `.menu`, `.menu__item` (28px min height, `padding: 0 var(--space-3)`, `--radius-sm`, `font-size: var(--text-body)`), `.menu__item:hover`/`:focus-visible` (`background: var(--surface-3)`; focus shows `--focus-ring` **outside**, not the inset outline), `.menu__item--destructive` (`color: var(--state-negative)`), `.menu__item[aria-disabled="true"]` (`color: var(--text-tertiary)`, no hover), `.menu__detail`, `.menu__desc`, `.menu__check`, `.menu__sep` (1px `--separator`, `margin: var(--space-1) 0`).

- [ ] **Step 1: Write the failing tests** in `DeskMenuTests.cs` on `DeskTestContext`. JS is loose, so test the markup and the key handling that lives in C#:
  - `Menu_items_carry_roles_and_roving_tabindex`: a `DeskMenu` with three `DeskMenuItem`s renders `role="menu"`, three `role="menuitem"`, exactly one `tabindex="0"`.
  - `Arrow_down_moves_focus_and_wraps`: dispatch `keydown ArrowDown` three times on the menu; the item with `tabindex="0"` is the first again.
  - `Typing_a_letter_jumps_to_the_matching_item`: items "Pull odds", "Fetch games", "Settle"; keydown `f` → "Fetch games" has `tabindex="0"`.
  - `Disabled_item_states_its_reason`: `Disabled DisabledReason="Needs an API key"` renders `aria-disabled="true"`, `title="Needs an API key"` and the reason text.
  - `Pull_down_key_announces_the_menu`: a `DeskPullDown Label="Pull data"` renders a button with `aria-haspopup="menu"`, `aria-expanded="false"`; after click `aria-expanded="true"` and a `role="menu"` exists.
  - `Opening_a_second_menu_closes_the_first`: two `DeskPullDown`s in one render; click the first then the second; exactly one `role="menu"` in the DOM. (The C# side keeps a static-per-circuit `DeskPopoverHost` scoped service — register it in `DeskTestContext` only if you make it a service; prefer a `[CascadingParameter]`-free scoped service `DeskPopovers` with `Open(DeskPopover)` that closes the previous.)
  - `Escape_closes_and_the_key_regains_focus`: keydown Escape on the menu → no `role="menu"`; the button carries `data-popover-return="true"` (the JS reads it to restore focus; assert the attribute).
- [ ] **Step 2: Run** `dotnet test tests/LineOps.Web.Tests --filter DeskMenuTests -nologo -v q`; expect FAIL (types missing).
- [ ] **Step 3: Implement** the four primitives, `popover.js`, `controls.css`, and register `DeskPopovers` as scoped in `src/LineOps.Desk/DeskHost.cs` where the other desk services are added (and in `DeskTestContext`).
- [ ] **Step 4: Adopt.** `RailMenu` keeps its public surface and renders `DeskPopover` + `DeskMenu`; the `launcher` markup its callers pass stays exactly as it is. `PullMenu` becomes a `DeskPullDown` with one `DeskMenuItem` per job, keeping `RunJobAsync`, the toast and the busy label. Existing `.railmenu__panel`/`.pullmenu__*` rules in `lineops.css` stay untouched (Wave 2 deletes them).
- [ ] **Step 5: Run the full test project; expect green.**
- [ ] **Step 6: Report** the files changed; the controller commits: `feat(desk): one popover and one menu, with arrows, typeahead and Escape`.

### Task 2: Unsaved input is never lost silently

**Files:**
- Modify: `src/LineOps.Desk/Windowing/WindowModels.cs` (`WindowInstance` gains `bool HasUnsavedInput` and `string? UnsavedInputLabel`), `src/LineOps.Desk/Windowing/WindowManager.cs:301` (`Close`), `:318` (`CloseAll`), the eviction chooser near `:145–200` (`LastEviction`, least-recently-used), `src/LineOps.Desk/Windowing/AppWindow.razor:152`, the panel base class (grep `WithActivityAsync` under `src/LineOps.Web/Components/Panels` to find it; add `protected void MarkUnsaved(bool dirty, string? label = null)` that calls `Manager.SetUnsaved(WindowId, dirty, label)`). **Do not edit `JournalPanel.razor`** (Task 4 owns it this wave and wires the Journal form to `MarkUnsaved` as its last step); prove the behaviour with the manager tests and a tiny test panel.
- Test: `tests/LineOps.Web.Tests/UnsavedInputTests.cs`

**Interfaces:**
- Produces on `WindowManager`: `bool TryClose(string id)` → returns `false` and raises `event Action<WindowInstance>? CloseNeedsConfirmation` when the window `HasUnsavedInput`; `Close(string id)` stays and closes unconditionally (what the confirmation calls). `void SetUnsaved(string id, bool dirty, string? label)`. Eviction never picks a window with `HasUnsavedInput`; if every candidate is dirty, the open is refused and `LastEviction` says `"Every window holds unsaved input; save or discard one to open {title}."`.
- `AppWindow` close key calls `TryClose`; on `false` it shows a `DeskAlert` with `Heading="Discard {UnsavedInputLabel ?? "unsaved changes"}?"`, `Message="The window has input that has not been saved."`, `ConfirmLabel="Discard"`, `Destructive="true"`, `CancelLabel="Keep editing"`; confirm → `Close`.
- Journal: `UnsavedInputLabel` is `"this journal entry"`.

- [ ] **Step 1: Tests** (`WindowManager` can be constructed with the catalogue from `DeskTestContext`'s services):
  - `Close_asks_when_a_window_holds_unsaved_input`: open a window, `SetUnsaved(id, true, "x")`, `TryClose` returns false, the window is still open, the event fired once.
  - `Close_proceeds_when_clean`: `TryClose` returns true and the window is gone.
  - `Eviction_skips_a_window_with_unsaved_input`: `MaxConcurrentWindows = 2`, open A (dirty) and B, open C → B is evicted, A remains.
  - `Eviction_refuses_when_every_window_is_dirty`: both dirty → the third open does not happen and `LastEviction` is the sentence above.
  - `Close_all_asks_once_when_any_window_is_dirty`: `TryCloseAll()` returns false and raises the event with the first dirty window; `CloseAll()` still closes everything.
- [ ] **Step 2: Run; expect FAIL.** **Step 3: Implement.** **Step 4: Full run green.**
- [ ] **Step 5: Report.** Commit subject: `feat(desk): a window with unsaved input asks before it closes, and eviction passes it by`.

### Task 3: The desk from the keyboard: reorder, settings, undo

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/js/windowing.js:22–30` (one `keydown` handler dispatching: Ctrl/Cmd+K palette as today; **Ctrl/Cmd+,** → `OnSettingsRequested`; **Ctrl/Cmd+Z** → `OnUndoRequested`; **Ctrl/Cmd+Shift+Z** and **Ctrl+Y** → `OnRedoRequested`; **Ctrl+Alt+←/→** → `OnMoveWindowRequested(direction)`; none fire while focus is in an `input`, `textarea`, `select` or `[contenteditable]` except the palette and settings chords), the `.NET` side that owns `OnPaletteRequested` (grep `JSInvokable` under `src/LineOps.Desk/Windowing`) gains the four methods: settings → `Manager.Open(...)` with the Window manager's key read from the catalogue, undo → `Manager.Back()`, redo → `Manager.Forward()`, move → compute the new order from `Manager.Windows` with the focused window shifted one place and call the existing `Manager.Reorder(IReadOnlyList<string>)`. **Do not edit `WindowManager.cs` or `WindowModels.cs`** (Task 2 owns them this wave): the move logic lives in the interop host as `static IReadOnlyList<string> Shifted(IReadOnlyList<string> order, string id, int delta)`.
- Modify: `src/LineOps.Desk/Windowing/DeskHeader.razor:36–60` (the brand menu gains, after the workspaces group, a `DeskMenuItem Label="Settings…" Detail="Ctrl+,"` opening the Window manager — if Task 1 has not landed yet, use the existing `launcher__item` button markup; the controller merges)
- Modify: `src/LineOps.Desk/Windowing/DeskFooter.razor:22–27,75` (`Undo` → label `"Reopen {name}"` where `name` is the evicted window's title; `title="Ctrl+Z"`; `Undo()` calls `Manager.Back()` instead of `history.back` — check `Back()` returns true before clearing the notice), and the `CommandPalette`/`IDeskSearch` command list gains "Move window left", "Move window right", "Settings" entries (find where "Close window" or similar commands are listed; if commands live only in `WindowCatalog`, add them there)
- Modify: `src/LineOps.Desk/wwwroot/js/theme.js` (`watch` also observes `(prefers-contrast: more)` and `(prefers-reduced-transparency: reduce)`; `OnSystemPreferenceChanged` gains two bools, and the C# `ThemeService` switches to the `contrast-*` family while `prefers-contrast` is on and the operator has not chosen a family explicitly — read `ThemeService.cs` for how "System" is stored; `reduced-transparency` sets `data-reduced-transparency="true"` on `<html>`; Wave 2 styles it)
- Test: `tests/LineOps.Web.Tests/DeskKeyboardTests.cs`, extend `ThemeServiceTests.cs`

**Interfaces:**
- Produces `Shifted(order, id, delta)` as above (±1, unchanged list at the ends).
- Produces `ThemeService.OnSystemPreferenceChanged(bool dark, bool moreContrast, bool reducedTransparency)` (keep the old one-bool overload calling it with `false, false` so nothing else breaks).

- [ ] **Step 1: Tests:** `Shifted_moves_the_window_one_place`, `Shifted_at_the_end_returns_the_same_order`, `Footer_undo_names_the_window_it_reopens` (render `DeskFooter` after an eviction; the button text is `"Reopen Journal"`), `More_contrast_switches_an_unpinned_family_to_contrast`, `An_explicit_family_is_kept_under_more_contrast`.
- [ ] **Step 2: FAIL. Step 3: Implement. Step 4: Green.**
- [ ] **Step 5: Report.** Commit subject: `feat(desk): windows move, settings open and evictions undo from the keyboard, and the OS contrast setting is honoured`.

### Task 4: Forms say what is wrong, and a fixed set of three is a segmented control

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/JournalPanel.razor` — (a) `:681–685` Selection: when `d.GameId` is set and `d.Market` is Moneyline or Spread, a `DeskSwitch<string>` of the two team abbreviations (away, home); Total → `DeskSwitch` Over/Under; free text keeps the field; (b) Price, Stake and Line `DeskField`s get `Error=` from `static string? PriceError(int price)` (`0` or between −99 and 99 → `"A price is −100 or lower, or +100 or higher."`), `StakeError(decimal stake)` (`≤ 0` → `"Enter a stake above zero."`), `LineError(string market, decimal? line)` (spread/total with no line → `"Enter the line."`; a total line below 0 → `"A total is above zero."`); errors show only after the field was touched (track a `HashSet<string> _touched` set in `ValueChanged`/`onblur`); (c) the disabled Save key's reason becomes visible text: `<span class="meta" id="save-why">Needs a selection, a stake above zero, a price and a book.</span>` beside the key, the key carries `aria-describedby="save-why"`, and the `Title=` goes; (d) `:32–38` the sport `DeskSelect` becomes `DeskSwitch Options="SportOptions" Label="Sport"` with `All · MLB · NFL` built from `_sports` (keep "All" as `""`)
- Modify: `src/LineOps.Web/Components/Panels/PerformancePanel.razor:45–52` and `src/LineOps.Web/Components/Panels/PlayersPanel.razor:20–26` — same sport switch (Players' options come from `_sports` ids and names)
- Modify: `src/LineOps.Web/Components/Panels/WindowManagerPanel.razor:158,215–221` (the two keys inside sentences move out: "Reset to 100%" becomes a Plain key in the Scale section's `.row` after the switches; "reset widths" becomes a Plain key `Reset widths` on its own row under the share slider, the sentence ends "…until you reset the widths."), `:168–178` (the −/+ keys pass `Icon="@Icons.Material.Filled.Remove"`/`Add` instead of a `Glyph` child so they take the square icon-key style; Wave 2 replaces them with `DeskStepper`), `:320–329` (drop the `0.8` interface scale; comment why: captions would fall under 10px)
- Test: `tests/LineOps.Web.Tests/JournalValidationTests.cs` (the three pure error functions as `[Theory]`), extend `JournalPanelTests.cs` if it renders the form (`Disabled_save_states_its_reason_in_text`), `WindowManagerControlsTests.cs` (`Interface_scale_starts_at_ninety_percent`)

- [ ] **Step 1: Tests. Step 2: FAIL. Step 3: Implement. Step 4: Green.** Also `Select-String 'Title="@\(EntryValid' src/LineOps.Web/Components/Panels/JournalPanel.razor` → 0.
- [ ] **Step 4b: Unsaved input.** If `WindowManager.SetUnsaved(string id, bool dirty, string? label)` and the panel base's `MarkUnsaved` exist when you reach this step (Task 2 adds them in parallel), call `MarkUnsaved(EntryIsDirty(_entry, _opened), "this journal entry")` whenever an entry field changes, and `MarkUnsaved(false)` on save and cancel, with a pure `static bool EntryIsDirty(EntryDraft current, EntryDraft opened)` under test. If they do not exist yet, write `EntryIsDirty` and its test anyway and say in your report that the wiring is left for the controller.
- [ ] **Step 5: Report.** Commit subject: `feat(journal): fields say what is wrong, a selection is a choice of two, and a sport is a segment`.

### Task 5: Alerts, charts and loading copy

**Files:**
- Modify: `src/LineOps.Desk/Primitives/DeskAlert.razor:82,100–119` — `ConfirmLabel` becomes `[EditorRequired]` with no default (every caller must name the consequence; fix callers: `Select-String "<DeskAlert|DeskAlerts\." src/ -r`), and a destructive alert focuses **neither** button: focus goes to the dialog panel itself (`tabindex="-1"` on the root, `FocusAsync` on an `ElementReference`), so Return does nothing until the operator tabs; a non-destructive alert keeps the confirm as default. Update the XML comments to say why (Alerts page, Feb 2024).
- Modify: `src/LineOps.Desk/wwwroot/css/mud-bridge.css:548,576–600` — `.mud-chart text` → `font-size: var(--text-caption)`; `.deskchart__headline` → `var(--text-subheadline)`, `var(--weight-semibold)`, no tracking; `.deskchart__takeaway` → `color: var(--text-secondary); font-size: var(--text-footnote)`; legend → `var(--text-caption)`, no tracking
- Modify: `src/LineOps.Desk/Primitives/DeskChart.razor` — `Description` gets a documented convention and a helper `static string Describe(string what, string[] labels, IReadOnlyList<ChartSeries<double>> series)` → `"{what}. {n} series over {labels.Length} points from {labels[0]} to {labels[^1]}; {series name}: {min}–{max}" …`; `src/LineOps.Web/Components/Panels/OddsPanel.razor:52` passes `Description="@DeskChart.Describe(...)"`, and the odds lines differ by more than colour: give every second series a dashed stroke — MudChart 9.7 `ChartOptions.LineStrokeWidth` is global, so instead append the book's abbreviation to the series name (it is already the legend) and set `InterpolationOption` per family only if available; record in the report what MudChart allowed. Minimum: the legend names and the description carry the books, so colour is not the only channel.
- Modify: loading copy — `src/LineOps.Web/Components/Panels/PlayerPanel.razor:29`, `BoardAllBets.razor:21`, `BoardForm.razor:15` replace the `Loading the …` `EmptyState` with a `DeskSkeleton` block shaped like the panel's first section (look at how `RunsPanel`/`JournalPanel` do it after the earlier plan's Task 8). (Game, Team and Head to head wait for Wave 2.)
- Test: `tests/LineOps.Web.Tests/DeskAlertTests.cs` (`Destructive_alert_has_no_default_button`: neither button has focus-marker class/attribute — assert the root has `tabindex="-1"` and no button carries `autofocus`; `Plain_alert_defaults_to_confirm`), `DeskChartTests.cs` new (`Describe_names_range_and_series`)

- [ ] **Step 1: Tests. Step 2: FAIL. Step 3: Implement. Step 4: Green.**
- [ ] **Step 5: Report.** Commit subject: `fix(desk): a destructive alert has no default key, charts read at ladder sizes, and three more panels load as a shape`.

---

## Wave 2 — after the other session's Task 14 is pushed

### Task 6: Contrast tokens: state inks, accent ink, tertiary off read text

**Files:** `lineops.css` token blocks (dark `:root` and the `apple-light` block), `themes.css` (all eight families), the call sites in spec F&P findings 1, 2 and 5; `tests/LineOps.Web.Tests/ThemeContrastTests.cs`

- `--state-positive-ink`, `--state-warning-ink`, `--state-negative-ink`, `--accent-ink` in every theme block. Dark values: positive `#30D158`, warning `#FFB340`, negative `#FF6961`, accent `#409CFF`. apple-light: `#008932`, `#C55300`, `#E9152D`, `#0062CC`. Other families: pick the family's own palette value that reaches ≥ 4.5:1 on that family's `--surface-1`; the test enforces it.
- Text rules move to the inks: `.up`/`.down`, `.metric--good/warn/bad .metric__value`, `.tag--*` text, `.price__edge--wide`, `.bar__key--current`, `.desklink:hover`, `.warnc`, `.wager-offer__ev--*`. Fills (`--state-*-wash`) stay.
- Tertiary → secondary at: `.launcher__desc`, `.metric__note`, `.grid .dim`, `.price--closing .price__sel`, `.snippet__note`, `.hdr__sub`, resting `.bar__key`, (`.deskchart__takeaway` done in Task 5). Tertiary stays for placeholders, disabled and watermarks only.
- Test: parse each `[data-theme=…]` block from both CSS files with a regex, compute WCAG contrast of each ink against that block's `--surface-1` (resolve `rgba` over the surface), assert ≥ 4.5 — `State_ink_tokens_pass_contrast_in_every_theme`, `Accent_ink_passes_in_every_theme`. Also `DeskTheme.cs` mirror (ADR 0016): add the inks there and keep `DeskThemeTests` green.
- Commit: `fix(desk): state and accent text carry ink tokens that pass in every theme, and tertiary is for placeholders only`.

### Task 7: One focus ring, calmer motion, honoured OS settings, a type floor

**Files:** `lineops.css` (focus at 1089, 1829, 2420, 4369 → `--focus-ring`; `--ease-spring` on `.gate__cap` 983, `.marble` 925, `pullmenu-in` 2389/4639 → `--ease-standard`; `.metric:hover` lift at 1920 removed; the `@supports not (backdrop-filter…)` fallback at ~4144 also applies under `[data-reduced-transparency="true"]` and `@media (prefers-reduced-transparency: reduce)`; `@media (prefers-contrast: more)` raises `--separator` to `.24` and `--text-secondary` to `.70` in the dark block and the light equivalents; type rungs become `max(10px, calc(Npx * var(--text-scale)))` for footnote and caption at 108 and the scale block at 361; `.desk-sheet__close:hover` 3966 → `--surface-3`, not red; delete the old `.railmenu__panel`/`.pullmenu__*` rules replaced by Task 1's `controls.css`), `windowing.js` nothing.
- Greps: `Select-String "outline: *2px" lineops.css` → 0 outside the wager block; `Select-String "ease-spring" lineops.css` → only `deskdialog-in` and the sheet entrance.
- Commit: `fix(desk): one focus ring, motion that settles, a 10px floor, and the OS contrast and transparency settings honoured`.

### Task 8: The toolbar overflows into More

**Files:** `src/LineOps.Desk/Primitives/PanelToolbar.razor` (new `RenderFragment? Overflow`; a `ResizeObserver` in `popover.js` or a new `toolbar.js` sets `data-narrow` on the toolbar when its content width exceeds its box; under `data-narrow` the `Overflow` fragment renders inside a trailing `DeskPullDown Label="More" Icon=ellipsis` instead of inline), `lineops.css:3087` (`.ptoolbar { flex-wrap: nowrap; overflow: hidden }` plus `.ptoolbar__overflow`), `BoardPanel.razor:35–78` (the sport and book pickers and the +EV checkbox move into `Overflow`; search and the primary stay), `JournalPanel.razor` toolbar (sport, book into `Overflow`), `PerformancePanel.razor`; tests `PanelToolbarTests.cs` (`Overflow_items_move_into_the_more_menu`: render with `data-narrow` forced via a parameter `ForceNarrow` used only by tests → the items are inside a `role="menu"`).
- Commit: `feat(desk): a narrow toolbar keeps one line and folds its filters into More`.

### Task 9: Stepper, disclosure, search field

**Files:** Create `DeskStepper.razor`, `DeskDisclosure.razor`, `DeskSearch.razor` in `src/LineOps.Desk/Primitives`; styles in `controls.css`; adopt: `WindowManagerPanel.razor:168–178` (stepper), `RunbookSteps.razor:20–23` and `TeamPanel.razor:84–111` (disclosure, leading triangle that rotates 90° over `--dur-fast`), `BoardPanel.razor:61` and `PlayersPanel.razor` search (`DeskSearch`: leading magnifier glyph, clear key when text, Escape clears, `role="search"`, `type="search"`); tests `SharedControlsTests.cs` additions.
- `DeskStepper`: `int Value`, `EventCallback<int> ValueChanged`, `int Min`, `int Max`, `int Step = 1`, `string Label`; one capsule (`--surface-2` fill, one `--separator` edge, `--radius`), two 28×28 halves divided by a hairline, `aria-label="{Label}: one fewer"`/`"one more"`, disabled at the bounds; sits beside its field.
- `DeskDisclosure`: `bool Open`, `EventCallback<bool> OpenChanged`, `RenderFragment Summary`, `RenderFragment ChildContent`; renders `<details>`/`<summary>` with the leading triangle (`Icons.Material.Filled.ArrowRight` rotated), `aria-expanded`.
- Commit: `feat(desk): a stepper, a disclosure triangle and a search field replace the loose keys, hidden markers and plain fields`.

### Task 10: Grid, rows and book pop-up

**Files:** `DeskGrid.razor:51–67` (`ColumnResizeMode="ResizeMode.Column"` and a `Resizable` parameter default true for grids with more than five columns), `DeskSwitch.razor:163–164` + `RowActions.razor:134–138` (clicking the lit segment closes the open snippet: `DeskSwitch` gains `bool Deselectable`; `RowActions` sets it), `BookPicker.razor` (re-based on `DeskPopover` + `DeskMenu Radio="true"` with `DeskMenuItem Checked`; rows keep book, price and "best" detail; one Tab stop), `JournalPanel.razor:217–224,310–317` (Settle becomes `DeskPullDown Label="Settle" Emphasis=Plain Size=Small` with Win/Loss/Push/Void items), `BoardWager.razor` copy (`"Your bet"` → `"Bet"`, `"Enter my own price"` → `"Enter a price"`, spec F&P finding 15) and `MarkUnsaved` from Task 2 when a side or stake is set; tests in `RowActionsTests.cs` (`Clicking_the_open_view_closes_it`), `JournalPanelTests.cs` (`Settle_is_a_pull_down`), `WagerWindowBooksTests.cs` (book menu roles).
- Commit: `feat(desk): grid columns resize, an open view closes on click, and the book and settle choices are menus`.

### Task 11: Remaining skeletons, the eviction notice, window controls

**Files:** `GamePanel.razor:30`, `TeamPanel.razor:32`, `HeadToHeadPanel.razor:25` (skeleton shaped like the first section instead of "Loading the …"); `DeskFooter.razor:16–35` + `DeskHeader.razor` (the eviction notice and its "Reopen {name}" key move beside `.hdr__count` in the header; the footer keeps stats only), `lineops.css` `.ftr__refusal` → `.hdr__notice`; `AppWindow.razor:70–91` + `lineops.css:1500–1517` (window controls: keep the trailing Windows order but draw them as the desk's own 24px plain icon keys — glyphs always visible at `--text-secondary`, hover `--surface-3`, close hover **not** red — rather than grey dots that imitate macOS traffic lights); tests `WindowBarTests.cs`/`DeskLayoutTests.cs` (`Eviction_notice_renders_in_the_header`, `Window_controls_are_labelled_keys`).
- Commit: `fix(desk): every window loads as a shape, the eviction notice sits in the header, and window controls are keys, not dots`.

---

## Verification before "done" (controller)

- `dotnet build src/LineOps.Web -nologo -v q` clean; `dotnet test tests/LineOps.Web.Tests` and `tests/LineOps.Tests` green; `dotnet run --project src/LineOps.Worker` starts (CLAUDE.md).
- Browser pass on port 5271 at 1280px and at a 300px-wide window, dark and light, `contrast-dark` family, with Windows "Reduce transparency" emulated via the `data-reduced-transparency` attribute: every menu drives from the keyboard; a dirty Journal asks before closing; the Board toolbar stays on one line; focus rings are one shape everywhere.
- `docs/superpowers/specs/2026-10-24-panel-anatomy-design.md` gains one line under "Constraints" naming `controls.css` as the home of control primitives' styles.
