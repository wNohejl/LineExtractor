# Desk finalisation, Phase D: rendering cost — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A status change re-renders one window's chrome, not the desk; a browser resize costs one pass per frame; the page carries no unused asset; the grid's extra render is bounded; and the three panels that fetched twice fetch once — each with a measurement before and after.

**Architecture:** `WindowManager` raises one `Changed` event for everything, and every piece of chrome subscribes to it; this phase splits status from layout (`WindowChanged(id)` beside `Changed`) and gives `WindowInstance` a `Version` so `AppWindow.ShouldRender` can decline a render that changes nothing. The JS handlers coalesce to one `requestAnimationFrame` pass. The rest is deletion and single reads.

**Tech Stack:** .NET 10 Blazor Server, bUnit (`RenderCount`), the Desk's JS modules, `read_network_requests` in the browser pane for byte counts.

**Spec:** `docs/superpowers/specs/2026-10-26-desk-finalisation-design.md` (Phase D).

## Global Constraints

- CLAUDE.md as before. Agents work in their own tree copy and never run git; the controller commits by pathspec and deletes `wwwroot/lib` with `git rm`.
- `DeskVocabularyTests` (code and comments), `AccessibleNameTests`, `StylesheetOwnershipTests`, `GridConventionTests` keep holding.
- Behaviour is unchanged for the operator: every existing test in both desk projects stays green without weakening; a render-count test asserts a *ceiling*, never an exact number that a later MudBlazor could move.
- Before "done": builds, all tests, `dotnet format` on Linux, `dotnet pack`, the Worker starts, and the measurements in Task 5 recorded.

## Review Focus

1. A window whose pulse changes while another is maximised must not come back when restored with a stale title bar — Task 1's `Version` must bump on every `WindowInstance` mutation the manager makes (pulse, status text, title, page, minimise/maximise, focus); the reviewer reads every `Notify()` call site in `WindowManager.cs` against the bump.
2. The window bar shows every window's pulse dot, so it must still hear status changes after the split — Task 1 test.
3. After coalescing, the *last* resize in a burst must still be reported (trailing edge), or the layout ends a pixel stale — Task 2's handler keeps the latest size and reports it once per frame.
4. `TaleOfTheTape` must still reload when its `GameId` changes (a Game window turning its page) — Task 4 test.
5. Removing `wwwroot/lib` must not remove anything referenced — Task 3 greps before deleting and asserts no `lib/` reference remains.

---

## Wave 1 (independent file sets)

### Task 1: A status change re-renders one window

**Files:**
- Modify: `src/LineOps.Desk/Windowing/WindowManager.cs` (`Changed` at :33, `Notify()` at :1299 and its ~12 call sites; `SetPulse` at :565)
- Modify: `src/LineOps.Desk/Windowing/WindowModels.cs` (`WindowInstance` gains `public int Version { get; private set; }` and `internal void Touch() => Version++;`)
- Modify: `src/LineOps.Desk/Windowing/AppWindow.razor` (subscribe to `WindowChanged` for its own id; `ShouldRender` → `Window.Version != _renderedVersion || IsFocused != _renderedFocused`, recorded in `OnAfterRender`)
- Modify: `src/LineOps.Desk/Windowing/WindowBar.razor` (subscribe to both; a status change re-renders the bar once), `Desk.razor`, `DeskHeader.razor`, `DeskFooter.razor` (stay on `Changed` only)
- Test: `tests/LineOps.Desk.Tests/WindowRenderCountTests.cs` (create)

**Interfaces produced:** `WindowManager.WindowChanged : event Action<string>?` (raised with the window id by `SetPulse` and any other change confined to one instance; `Changed` no longer fires for those); `WindowInstance.Version`.

- [ ] **Step 1: Failing tests** — render `Desk` (as `DeskLayoutTests`/`WindowBarTests` in `LineOps.Web.Tests` do, but on the fake catalogue; copy the smallest set-up) with two windows open; record `desk.RenderCount` and each `FindComponents<AppWindow>()[i].RenderCount`; call `Manager.SetPulse(idOfFirst, PulseState.Healthy, "3 items")`; assert the desk's count is unchanged, the first window's rose by at most 1, the second's by 0, and the bar's `.bar__pulse` for the first reflects the new state. Then `Manager.Relayout()`: the desk's count rose by 1 and each window's by at most 1. A third test: `SetPulse` twice with the same values renders nothing more (Version unchanged → `ShouldRender` false).
- [ ] **Step 2: Run; fail.** **Step 3: Implement** — `Touch()` in every manager method that mutates one instance; `NotifyWindow(id)` raising `WindowChanged` from `SetPulse` (and from title/status-only paths); `Notify()` keeps the layout paths. `AppWindow` subscribes in `OnInitialized`, unsubscribes in `Dispose`, calls `InvokeAsync(StateHasChanged)` when the id matches. Comments say why in the files' voice.
- [ ] **Step 4: Both test projects green** (`WindowBarTests`, `DeskLayoutTests`, `DeskKeyboardTests`, `UnsavedInputTests`, `WindowErrorBoundaryTests` especially). **Step 5: Commit** — `perf(windowing): a window's status change re-renders that window, not the desk`.

### Task 2: One pass per frame

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/js/windowing.js` (`reportViewport` at ~96 and the `resize` listener at ~17), `desk-glide.js` (the `MutationObserver` at ~148–175 and `seatAll` on `resize` at ~227), `dialogs.js` (`refitAll` on `resize` at ~66)
- Create: `tests/LineOps.Desk.Tests/DeskScriptsTests.cs`

**The change:** each `resize` handler becomes a trailing-edge `requestAnimationFrame` coalescer (`let pending = false; function onResize() { if (pending) return; pending = true; requestAnimationFrame(() => { pending = false; doTheWork(); }); }`) so a burst of N events does the work once with the final size. The glide observer keeps watching `document.body` (a strip or a window can appear anywhere) but its callback only *flags* that something was added and schedules one `requestAnimationFrame` pass that runs `document.querySelectorAll('[data-glide="marble"]')` once and reseats hosts not yet seated (mark seated hosts with a `WeakSet`), instead of a `querySelectorAll` per added element. Comments explain the coalescing in each file's voice.

- [ ] **Step 1: Failing test** — `DeskScriptsTests.Resize_work_is_coalesced_to_a_frame`: for each of the three files, the text between `addEventListener('resize'` and the end of that statement names a function whose body contains `requestAnimationFrame` (regex on the source; crude, but it pins the shape), and `desk-glide.js` contains at most one `querySelectorAll('[data-glide="marble"]')` inside the observer's callback region. **Step 2: Implement.** **Step 3: Green; commit** — `perf(desk): resize and DOM-change work runs once per frame`.

### Task 3: Nothing unused in the head, and the grid's extra render is bounded

**Files:**
- Delete: `src/LineOps.Web/wwwroot/lib/**` (Bootstrap, 8.4 MB, unreferenced — grep `lib/bootstrap` and `bootstrap` across `src/**/*.razor|cs|csproj|json|css` first and report)
- Modify: `src/LineOps.Desk/Layout/DeskHead.razor` — before the stylesheets, `<link rel="preload" href="@Assets[DeskAssets.Root + "/fonts/InterVariable.woff2"]" as="font" type="font/woff2" crossorigin />` with a comment (the regular face is on every page; the italic is fetched only where used, so it is not preloaded)
- Modify: `tests/LineOps.Desk.Tests/DeskWiringTests.cs` (the head preloads the regular face, before the first stylesheet), `tests/LineOps.Desk.Tests/DeskGridTests.cs` (add `The_alignment_pass_renders_once_more_and_then_no_more`: mount a grid with an `Align.Right` column; `RenderCount <= 2`; call `SetParametersAndRender` with the same items; the count rose by exactly 1)
- Create: `tests/LineOps.Web.Tests/StaticAssetsTests.cs` — `No_bootstrap_ships`: `src/LineOps.Web/wwwroot/lib` does not exist and no file under `src/` references `lib/bootstrap`.

- [ ] **Step 1: Failing tests.** **Step 2: Delete, preload, done.** **Step 3: Green; commit** — `perf(web): the unused Bootstrap bundle goes, the body face is preloaded, and the grid's second render is pinned to one`.

### Task 4: Panels that fetched twice fetch once

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/PerformancePanel.razor:332–341` — one `PerformanceAsync(new PerformanceFilter())` read; the filtered set is derived from it in memory (the pickers already come from the whole; if `PerformanceAsync` applies its filter server-side in a way the panel cannot reproduce, keep two calls but make the second conditional on a non-empty filter — report which)
- Modify: `src/LineOps.Web/Components/Snippets/TaleOfTheTape.razor:99` — `OnParametersSetAsync` loads only when `GameId != _loadedGameId` (the `EventCallback` parameter changes identity every parent render)
- Modify: `src/LineOps.Web/Components/Panels/PanelBase.cs` — expose `protected void RequestRefresh()` that routes through the existing 1 s debounce used for `DeskSignals`; `HistoryPanel.razor:431–439` — `OnCoordinatorChanged` calls `RequestRefresh()` instead of `LoadAsync()`
- Modify: `RunsPanel.razor:203, 215`, `HistoryPanel.razor:455`, `OddsPanel.razor:451` — `.AsNoTracking()`; `src/LineOps.Reliability/IncidentService.cs:109–113` — `AsNoTracking()` and `.Take(200)` newest first (read the callers: `IncidentsPanel` lists open and resolved; 200 covers both)
- Test: `tests/LineOps.Web.Tests/PerformancePanelTests.cs` (a scripted `JournalService` double or a counting `IDbContextFactory` shows one read per load), a `TaleOfTheTapeTests.cs` (re-rendering the parent with the same `GameId` does not reload; a new `GameId` does — count via a fake `FightService` or the factory), `OperationsWindowsTests.cs` (`Coordinator.Changed` raised five times inside 1 s loads once)

- [ ] **Step 1: Failing tests.** **Step 2: Implement.** **Step 3: Green (both projects; `tests/LineOps.Tests` builds); commit** — `perf(panels): Performance, the tale of the tape and History read once, and read-only queries stop tracking`.

## Wave 2 (controller)

### Task 5: Measure, record, verify

- [ ] Browser, port 5271, 1280×800 dark: `read_network_requests` for the head's CSS bytes excluding MudBlazor (target < 350 KB uncompressed — note the desk is now `desk.css` + `controls.css` + `themes.css` + `mud-bridge.css` + app `lineops.css`), no `lib/` request, the font preloaded before the first stylesheet; a resize burst via `resize_window` three times quickly then `preview_logs`/console shows one `OnViewportChanged` per frame (instrument by reading `performance.getEntries`… or accept the test as the proof and say so); open two windows and change one's pulse (a refresh) — `read_console_messages` shows no error; append a "Phase D" section to `docs/superpowers/evidence/2026-10-desk-final.md` with the numbers.
- [ ] Builds, all tests, `dotnet pack`, `dotnet format` in the Linux container, the Worker starts; push.
