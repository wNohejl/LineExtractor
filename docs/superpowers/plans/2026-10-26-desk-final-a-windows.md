# Desk finalisation, Phase A: finish the windows — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every window on the desk holds to the HIG rulebook: grouped forms with no nested frames, verbs on buttons, a name on every control, a loading and an error state everywhere, an exception that stays inside its window, and the right theme from the first frame.

**Architecture:** Panels in `src/LineOps.Web/Components/Panels` already share `PanelBase` (load, pulse, `LoadFailure`) and the Desk's grouped-form CSS (`.wsection` > `.wgroup` > `.wgroup__row--form`). This phase brings the stragglers onto those two rails, adds an `ErrorBoundary` to the window shell, and stamps the theme attribute before first paint. No new primitive; no CSS split (that is Phase B).

**Tech Stack:** .NET 10 Blazor Server, MudBlazor 9.7, bUnit + xUnit (`tests/LineOps.Web.Tests`), the Desk CSS in `src/LineOps.Desk/wwwroot/css`.

**Spec:** `docs/superpowers/specs/2026-10-26-desk-finalisation-design.md` (Phase A).

## Global Constraints

- CLAUDE.md: commits by the developer's identity, no `Co-Authored-By`, `type(scope): sentence` subject with a why-body, push after every commit, two branches only.
- The house rule on edges: one 1px hairline on a group or card is right; a bordered thing inside a bordered thing is not.
- Copy in sentence case; buttons are verbs; no "Yes," prefix; no exclamation marks.
- No literal colour (`#…`, `rgb(…)`, named colours) outside a token block; no px font size in Razor.
- Another session may share the checkout: build with `dotnet build src/LineOps.Web -o <scratchpad>/webout`; never `Stop-Process`; subagents do not run git — the controller commits with `git commit -- <paths>`.
- Before "done": `dotnet build` clean on `src/LineOps.Web`, `src/LineOps.Worker`, both test projects; `dotnet test tests/LineOps.Web.Tests` green; `dotnet format --verify-no-changes` clean in `mcr.microsoft.com/dotnet/sdk:10.0`.

## Review Focus

1. A panel whose load throws after it has already shown rows must keep the rows and show `LoadFailure` above them (PanelBase contract) — Task 6 test covers Line movement's first load only; the reviewer checks the refresh case by reading.
2. The error boundary must reset when the window turns a page (`PageVersion` key) — Task 5 test.
3. `theme-boot` must agree with `ThemeService.Attribute` for every family and mode, including High contrast when `prefers-contrast: more` and no family chosen — Task 2 test pins the slugs; the reviewer reads the mode logic against `ThemeService.IsDark`.
4. A write that fails must leave the operator's typed input in place — Task 7 tests assert the field value survives.
5. A `.wgroup` moved inside a `section.wsection` must not gain a second edge from an ancestor (`.pform` non-grouped) — Task 1 removes that edge; Task 8 confirms in the browser.

---

## Wave 1 (independent files; may run in parallel)

### Task 1: The CSS drift — one form look, one toolbar rule, no literal colours

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css:1757` (palette scrim), `:3189–3195` (toolbar container query), `:3230–3243` (`.pform`), `:4587–4592` (group comment), `:5223` (group border)
- Modify: `src/LineOps.Desk/Layout/ReconnectModal.razor.css:24,30,44,93–104,115`
- Modify: `src/LineOps.Desk/Primitives/PanelHeader.razor:4`, `src/LineOps.Desk/Primitives/PanelTitle.razor:7–8` (doc comments)
- Test: `tests/LineOps.Web.Tests/DeskCssConventionTests.cs` (create)

**Interfaces:** none produced. `.pform` keeps its class name; `.pform--grouped` keeps working.

- [ ] **Step 1: Write the failing tests**

```csharp
public class DeskCssConventionTests
{
    private static string Css(string name) => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", name));

    [Fact]
    public void No_literal_colour_outside_the_token_blocks()
    {
        // Token blocks end where the Scale section starts ("/* ── Scale" or the first rule after `[data-theme="apple-light"]`'s block) — locate by the section banner that follows the second theme.
        var body = Css("lineops.css")[Css("lineops.css").IndexOf("Scale", StringComparison.Ordinal)..];
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgba?\(", body);
        var reconnect = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "Layout", "ReconnectModal.razor.css"));
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|\bwhite\b", reconnect);
    }

    [Fact]
    public void A_form_is_not_boxed()
    {
        var pform = Regex.Match(Css("lineops.css"), @"\.pform\s*\{[^}]*\}").Value;
        Assert.DoesNotContain("border:", pform);
        Assert.DoesNotContain("background:", pform);
    }

    [Fact]
    public void The_toolbar_folds_by_one_rule()
    {
        Assert.DoesNotContain("@container ptoolbar (max-width: 520px)", Css("lineops.css"));
        Assert.Contains("@container ptoolbar (max-width: 520px)", Css("controls.css"));
    }

    [Fact]
    public void A_group_carries_its_hairline_where_it_is_defined()
    {
        var css = Css("lineops.css");
        var group = Regex.Match(css, @"\.wgroup\s*\{[^}]*\}").Value;     // the first .wgroup block
        Assert.Contains("border: 1px solid var(--separator-strong)", group);
        Assert.Equal(1, Regex.Matches(css, @"^\.wgroup\s*\{", RegexOptions.Multiline).Count);
    }
}
```

- [ ] **Step 2: Run them to see them fail** — `dotnet test tests/LineOps.Web.Tests --filter DeskCssConventionTests`. Expected: 4 failures.

- [ ] **Step 3: Edit `lineops.css`**
  - `.pform` (3234): remove `padding`, `background`, `border`, `border-radius`; keep `display`, `flex-direction`, `gap`, `margin`. Rewrite the comment: "A form is its title and its grouped fields; the group is the only edge."
  - `.pform--grouped` (3259 on): drop the now-redundant `padding: 0; background: none; border: 0;` lines; keep its gap/margin/max-width.
  - Delete the `@container ptoolbar (max-width: 520px)` block at 3189–3195 and its comment; controls.css's rule stands.
  - Group comment (4587–4592): rewrite to the house rule: a group is a fill one level above the body with one hairline edge; rows inside it divide by inset hairlines; a group never sits inside another edged thing. Move `border: 1px solid var(--separator-strong);` from line 5223 into the main `.wgroup {}` block and delete the one-line rule at 5223 (keep `.wcard .wgroup { … border: 0 }` after it).
  - Palette scrim (1757): `background: color-mix(in srgb, var(--surface-0) 60%, transparent);`.

- [ ] **Step 4: Edit `ReconnectModal.razor.css`** — background `var(--surface-1)`, text `var(--text-primary)`, shadow `var(--shadow-2)`, backdrop `color-mix(in srgb, var(--surface-0) 55%, transparent)`, button `var(--accent)` / `var(--on-accent)` with hover `var(--accent-hover)` and active `var(--accent-press)`, spinner ring `var(--accent)`.

- [ ] **Step 5: Rewrite the two doc comments** so PanelHeader describes "the sentence-case rule that names a stretch of a panel" and PanelTitle no longer says "11px uppercase grey".

- [ ] **Step 6: Run the test class and the whole suite** — expected: green (ThemeContrastTests and DeskThemeTests read these files; they must still pass).

- [ ] **Step 7: Commit** — `style(desk): a form has one edge, the toolbar folds by one rule, and the reconnect sheet wears the tokens`.

### Task 2: The theme is on `<html>` before first paint

**Files:**
- Create: `src/LineOps.Desk/wwwroot/js/theme-boot.js`
- Modify: `src/LineOps.Web/Components/App.razor` (a `<script src>` in `<head>` before the stylesheets)
- Test: `tests/LineOps.Web.Tests/ThemeBootTests.cs` (create)

**Interfaces:** `theme-boot.js` is a classic script (not a module). It reads `localStorage['lineops.theme']` (mode: `Dark`/`Light`/`System`, case-insensitive) and `localStorage['lineops.theme.family']` (a `DeskThemeFamily` name), resolves exactly as `ThemeService` does — `isDark = mode==Dark || (mode!=Light && matchMedia('(prefers-color-scheme: dark)').matches)`; family slug from `DeskThemeFamilies.Slug` (`apple`, `nord`, `solarized`, `rose-pine`, `contrast`); when no family is stored and `(prefers-contrast: more)` matches, slug is `contrast` — and sets `document.documentElement.dataset.theme = slug + '-' + (isDark ? 'dark' : 'light')`. Every storage read is in try/catch; on any failure it sets `apple-dark`. Phase B renames the keys; this task reads the current ones.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ThemeBootTests
{
    private static readonly string Boot = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "js", "theme-boot.js"));

    [Theory]
    [InlineData(DeskThemeFamily.Standard)] [InlineData(DeskThemeFamily.Nord)] [InlineData(DeskThemeFamily.Solarized)]
    [InlineData(DeskThemeFamily.RosePine)] [InlineData(DeskThemeFamily.HighContrast)]
    public void The_boot_script_knows_every_family_slug(DeskThemeFamily family)
        => Assert.Contains($"'{DeskThemeFamilies.Slug(family)}'", Boot);

    [Fact]
    public void The_boot_script_reads_the_same_keys_as_theme_js()
    {
        var theme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "js", "theme.js"));
        foreach (var key in Regex.Matches(theme, @"const \w*KEY = '([^']+)'").Select(m => m.Groups[1].Value))
            Assert.Contains($"'{key}'", Boot);
    }

    [Fact]
    public void App_loads_the_boot_script_before_any_stylesheet()
    {
        var app = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "App.razor"));
        Assert.True(app.IndexOf("theme-boot.js", StringComparison.Ordinal) < app.IndexOf("<link rel=\"stylesheet\"", StringComparison.Ordinal));
    }
}
```
(Use the enum member names as they exist in `DeskThemeFamily.cs`; adjust the InlineData names if they differ.)

- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: Write `theme-boot.js`** per the Interfaces block, ≤ 40 lines, with a header comment saying why it exists (one frame of the wrong theme) and that `ThemeService` owns the policy and this mirrors it.
- [ ] **Step 4: Add to `App.razor` head**, after `<ResourcePreloader />` and before the first `<link>`: `<script src="@Assets["_content/LineOps.Desk/js/theme-boot.js"]"></script>` with a two-line comment.
- [ ] **Step 5: Tests green; build.**
- [ ] **Step 6: Commit** — `fix(theme): the stored theme is on the page before the first paint`.

### Task 3: Line movement, Every book and Recent form load like every other window

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/OddsPanel.razor` (`LoadAsync` at ~378; markup at 60–95)
- Modify: `src/LineOps.Web/Components/Panels/BoardAllBets.razor` (`OnParametersSetAsync` at 102; markup 18–30)
- Modify: `src/LineOps.Web/Components/Panels/BoardForm.razor` (`OnParametersSetAsync` at 59, `LoadAsync` 67–78; markup 18–30)
- Test: `tests/LineOps.Web.Tests/PanelLoadStateTests.cs` (extend)

**Interfaces:** consumes `PanelBase.WithActivityAsync(Func<Task> work, Func<PulseState> settle, Func<string?>? status = null)`, `Loaded`, `Failed`, `LoadError`, `RetryAsync`, `LoadSubject`, and `LoadFailure` (`Message`, `OnRetry`).

- [ ] **Step 1: Write the failing test** (same `ScriptedFactory` as the file already has):

```csharp
[Fact]
public void Line_movement_that_fails_to_load_says_so_and_offers_a_retry()
{
    Use(new ScriptedFactory(fails: true));
    var panel = RenderComponent<OddsPanel>(p => p.Add(x => x.GameId, 7));   // use the panel's real parameter names
    panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".empty--error")));
    Assert.Contains("Line movement did not load.", panel.Find(".empty--error").TextContent);
    Assert.DoesNotContain("No stored prices", panel.Markup);
}

[Fact]
public void Line_movement_holds_its_shape_while_loading()
{
    Use(new ScriptedFactory());
    var panel = RenderComponent<OddsPanel>(p => p.Add(x => x.GameId, 7));
    Assert.NotEmpty(panel.FindAll("[aria-busy=true]"));
    Assert.DoesNotContain("No stored prices", panel.Markup);
}
```
If `OddsPanel` resolves its game through a service the test context cannot script, register the fake the Journal tests use (`FakeWagerData`) as that service too.

- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: OddsPanel** — `protected override string LoadSubject => "Line movement";`. Wrap the body of `LoadAsync` in `WithActivityAsync(work, settle: () => _rows.Count == 0 ? PulseState.Idle : PulseState.Healthy, status: …)` keeping the existing `Report` sentences as the status function; remove the direct `Report` calls inside. Markup: before the market/side switches, `@if (Failed) { <LoadFailure Message="@LoadError" OnRetry="RetryAsync" /> } else if (!Loaded) { <DeskSkeleton Width="40%" /> <DeskGrid T="MovementRow" Items="@(Array.Empty<MovementRow>())" Loading="true" SkeletonRows="4" LoadingLabel="Fetching the line's moves" /> }` and the existing `_rows.Count == 0` empty state only when `Loaded`.
- [ ] **Step 4: BoardAllBets and BoardForm** — `@inherits PanelBase`, `LoadSubject` "Every book" / "Recent form", `OnParametersSetAsync => LoadAsync()`, `LoadAsync` wraps the existing read in `WithActivityAsync(…, settle: () => PulseState.Healthy)`, `RefreshAsync => LoadAsync()`; markup gains the `Failed` branch with `LoadFailure` ahead of the existing skeleton branch, which now keys on `!Loaded`.
- [ ] **Step 5: Tests green** — the whole suite (BoardAllBetsTests and OddsMovementTests must still pass).
- [ ] **Step 6: Commit** — `fix(panels): Line movement, Every book and Recent form load, wait and fail like the other windows`.

### Task 4: An exception stays inside its window

**Files:**
- Modify: `src/LineOps.Desk/Windowing/AppWindow.razor:116–126`
- Create: `tests/LineOps.Web.Tests/WindowErrorBoundaryTests.cs`

**Interfaces:** consumes `WindowInstance` (`Title`, `PageVersion`, `Definition.ComponentType`, `Parameters`), `EmptyState Kind="EmptyStateKind.Error"`, `DeskButton`.

- [ ] **Step 1: Write the failing test** — build an instance the way `UnsavedInputTests.cs:357` does, with a `WindowDefinition` whose `ComponentType` is a private test component that throws `InvalidOperationException("boom")` in `OnInitialized` the first time it is created and renders `<p class="ok">fine</p>` after:

```csharp
[Fact]
public void A_panel_that_throws_shows_the_error_state_and_try_again_rebuilds_it()
{
    var window = RenderComponent<AppWindow>(p => p.Add(x => x.Window, InstanceOf<Explodes>()));
    var error = window.Find(".win__body .empty--error");
    Assert.Contains("stopped", error.TextContent);
    Assert.DoesNotContain("boom", window.Markup);
    window.Find(".win__body .empty--error button").Click();
    Assert.NotNull(window.Find(".win__body .ok"));
}
```

- [ ] **Step 2: Run to see it fail.**
- [ ] **Step 3: In `AppWindow.razor`**, inside `.win__body`, wrap the `CascadingValue` in `<ErrorBoundary @ref="_boundary">` with `<ChildContent>` = the existing content and `<ErrorContent Context="_">` = `<EmptyState Kind="EmptyStateKind.Error"><ChildContent>@Window.Title stopped. Nothing you typed elsewhere is lost.</ChildContent><Action><DeskButton Size="DeskKeySize.Small" Icon="@Icons.Material.Filled.Refresh" OnClick="Recover">Try again</DeskButton></Action></EmptyState>`. `private ErrorBoundary? _boundary; private void Recover() => _boundary?.Recover();`. Leave the `@key="Window.PageVersion"` on `.win__body` so a page turn builds a fresh boundary. Add a four-line comment: the boundary is per window so one panel's fault never takes the desk; the exception is logged by the boundary itself.
- [ ] **Step 4: Test green; whole suite green.**
- [ ] **Step 5: Commit** — `fix(windowing): a panel that throws stops inside its own window`.

## Wave 2 (one agent per file set; after Wave 1 lands)

### Task 5: Ops and History — grouped sections, verbs, caught writes

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/OpsPanel.razor` (Scanning 120–140, Drill 366–388, copy 135, methods 433, 461, 616, 668, 680, 730)
- Modify: `src/LineOps.Web/Components/Panels/HistoryPanel.razor` (Run group 109–245, copy 173, `RewalkAsync` and the gather action)
- Test: `tests/LineOps.Web.Tests/OperationsWindowsTests.cs` (extend)

- [ ] **Step 1: Failing tests** (render with the fakes OperationsWindowsTests already uses):
  - `Ops_scanning_is_a_section_with_its_cost_as_footer`: `section.wsection` contains `.wgroup__row--form` whose `.wgroup__label` is "Scanning" and a `.wgroup__foot`; the confirm key text is exactly "Scan automatically".
  - `History_run_group_is_a_section`: `section.wsection .wgroup__label` includes "Walking" or "Gather"; the re-walk confirm key text is "Re-walk".
  - `An_ops_action_that_fails_tells_the_operator`: script the DbContext factory (or the service the action uses) to throw on `SetPollingAsync`; assert a `Toasts.Fail`-style message or an inline `Note` with "did not" and "Try again", and that the panel still renders its rows.
- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: Ops** — wrap the Scanning `.wgroup` in `<section class="wsection" aria-label="Scanning">` and move the cost sentence that follows into `<p class="wgroup__foot">`; wrap the Drill group the same way with its "nothing registered" sentence as the footer. Copy: "Scan automatically". Each of the six action methods: `try { … } catch (Exception ex) { Logger.LogError(ex, …); Toasts.Fail("<Thing> did not <verb>. Try again."); }` — one sentence each, naming the thing ("Scanning was not changed.", "The evaluation did not run.", "The fault was not set.", "The drill did not run.", "The alert was not promoted.").
- [ ] **Step 4: History** — wrap the Run group in `<section class="wsection" aria-label="Run">` with the walk's description as `.wgroup__foot`; "Re-walk"; `RewalkAsync` and the gather action catch and show a `Note State="DeskState.Warning"` "The walk did not start. Try again." in the group.
- [ ] **Step 5: Tests green; suite green.**
- [ ] **Step 6: Commit** — `fix(ops): Ops and History ask in sections, answer in verbs, and say when an action did not run`.

### Task 6: Incidents and Performance — grouped forms, caught writes

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/IncidentsPanel.razor` (RCA 253–291, timeline note 193–220, `AddNoteAsync` 412, `MitigateAsync` 468)
- Modify: `src/LineOps.Web/Components/Panels/PerformancePanel.razor` (bankroll 142–157, `SaveBankrollAsync` 447)
- Test: `tests/LineOps.Web.Tests/OperationsWindowsTests.cs` or `IncidentTitleTests.cs` (Incidents), `tests/LineOps.Web.Tests/PerformancePanelTests.cs` (Performance)

- [ ] **Step 1: Failing tests**
  - `The_rca_is_a_grouped_form`: `.pform--grouped section.wsection .wgroup__row--form` ×2 with labels "Root cause" and "Corrective action"; the rule sentence is a `.wgroup__foot`; exactly one `desk-btn--filled` in the form ("Resolve incident").
  - `Bankroll_is_a_setting_row`: `section.wsection .wgroup__row--form .wgroup__label` = "Starting bankroll"; the field has an accessible name; a `.wgroup__foot` explains; "Set" is not Filled.
  - `A_note_that_fails_to_save_keeps_its_text`: script the service to throw; type "network blip"; click Add note; assert `Note` text contains "was not saved. Try again." and the field still holds "network blip".
  - `A_bankroll_that_fails_to_save_says_so`: same shape; message "The bankroll was not saved. Try again.".
- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: Incidents** — `PanelForm Title="Root cause analysis" Class="pform--grouped"` with `<section class="wsection" aria-label="Root cause analysis"><div class="wgroup">` two `wgroup__row--form` rows (label span + `DeskTextArea rows="4" aria-label=…` with no `Label`), the rule sentence as `<p class="wgroup__foot">`, the `_error` Note under the group; Footer keys unchanged. `AddNoteAsync` / `MitigateAsync` get the try/catch the file's `ResolveAsync` already has, setting `_error = "The note was not saved. Try again."` / `"The incident was not updated. Try again."`.
- [ ] **Step 4: Performance** — replace the `Keys` slot with a `<section class="wsection" aria-label="Bankroll">` body: one `.wgroup` row "Starting bankroll" → `DeskField … aria-label="Starting bankroll"` + Set key (Plain, Small), `.wgroup__foot` "The curve starts here; stored with the journal, not the browser."; `_bankrollError` Note on failure.
- [ ] **Step 5: Tests green; suite green.**
- [ ] **Step 6: Commit** — `fix(desk): the RCA and the bankroll are grouped forms, and a save that fails says so`.

### Task 7: Journal, Place wager, names on controls, and the scan that keeps them named

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/JournalPanel.razor` (parlay form 168–196; `SaveEntryAsync` 1005, `DeleteEntryAsync` 1016, `DeleteParlayAsync` 1033, `SetResultAsync` 1050, `SettleAsync` 1071)
- Modify: `src/LineOps.Web/Components/Panels/BoardWager.razor` (groups at 230–402: wrap each `.wgroup` that is not inside a `.wcard` in `section.wsection`; the cards stay)
- Modify: `src/LineOps.Web/Components/Desk/StakeRow.razor:15` (`aria-label="Stake"`), `src/LineOps.Web/Components/Panels/TeamPanel.razor:238` (`aria-label="Opponent"`), `src/LineOps.Web/Components/Panels/PartsPanel.razor:63` (`Label="Players"`)
- Create: `tests/LineOps.Web.Tests/AccessibleNameTests.cs`
- Test: `tests/LineOps.Web.Tests/JournalParlayTests.cs`, `SharedControlsTests.cs`, `JournalPanelTests.cs` (extend)

- [ ] **Step 1: Failing tests**
  - `AccessibleNameTests.Every_desk_field_has_a_name`: for every `src/**/*.razor` (the `GridConventionTests` walk), each `<DeskField`, `<DeskSelect`, `<DeskTextArea`, `<DeskSearch` tag (regex to its closing `>` or `/>`, multiline) contains one of `Label=`, `aria-label=`, `aria-labelledby=`, or — for `DeskSearch` — `Placeholder=`. Report the file and tag on failure.
  - `JournalParlayTests.The_parlay_form_is_sections`: `.pform--grouped > … section.wsection` count ≥ 2, the first labelled "Parlay".
  - `SharedControlsTests.The_stake_field_is_named`: the `.stake-row__field` input has `aria-label` "Stake".
  - `JournalPanelTests.A_save_that_fails_keeps_the_form_open_and_says_so`: script the journal service to throw; assert `.pform--grouped` is still present and the toolbar message reads "The entry was not saved. Try again.".
- [ ] **Step 2: Run; the first fails on StakeRow, Team, Parts (and anything else it finds — fix those too).**
- [ ] **Step 3: Journal** — `div.wsections > div.wgroup.jform` becomes `section.wsection aria-label="Parlay" > div.wgroup.jform`; the five write methods get `try/catch` setting `_message` to "The entry was not saved." / "The entry was not deleted." / "The parlay was not deleted." / "The result was not recorded." / "Settlement did not run." + " Try again." and not calling `CloseForm()` on failure.
- [ ] **Step 4: Place wager** — each standalone `.wgroup` in the step bodies gets a `section.wsection` parent with an `aria-label` naming the group ("Bet", "Stake", "Book"); nothing inside `.wcard` changes.
- [ ] **Step 5: Names** — the three one-line edits.
- [ ] **Step 6: Tests green; suite green.**
- [ ] **Step 7: Commit** — `fix(journal): the parlay form is sections, every field has a name, and a save that fails stays open`.

## Wave 3 (controller)

### Task 8: The walk, and its record

**Files:**
- Create: `docs/superpowers/evidence/2026-10-desk-final.md`

- [ ] **Step 1:** Build to the scratch output and start the preview on port 5271 (`lineops-web-fable`).
- [ ] **Step 2:** For each of the 19 windows, at 1280×800 and 1920×1080, dark then light: open it, screenshot to the scratchpad, run the checklist (grouped forms, one filled key, no nested edge, names, empty/loading/error where reachable), and run axe-core by pasting its source through the browser pane's script tool; record `window | size | theme | result | defects` rows.
- [ ] **Step 3:** Fix any defect found as a follow-up commit on the owning file (`fix(<window>): …`), re-check, update the row.
- [ ] **Step 4:** Commit the table — `docs(desk): the Phase A walk of every window, both sizes, both appearances`.
- [ ] **Step 5:** Full verification: builds, tests, Worker starts, `dotnet format` in the Linux container; push.
