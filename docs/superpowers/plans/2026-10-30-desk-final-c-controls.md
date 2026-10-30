# Desk finalisation, Phase C: the missing controls — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Desk's primitive set covers the HIG controls it lacked — toggle, badge, spinner, tooltip, radio group, slider — each tested, shown in the Parts bin with its states, documented in the README, and used wherever a window had a hand-built one; the twelve primitives with no test get one; the Parts bin shows the ten it omitted.

**Architecture:** Every new control follows the shapes already in `src/LineOps.Desk/Primitives`: a native element kept in the tree for keyboard, form and screen-reader behaviour (as `DeskCheckbox` does), `Label`/`Class`/`Extra` parameters with `DeskLayout.ExtraClass`/`WithoutClassAndStyle`, sentence-case copy, CSS in `controls.css` (the newer-primitives sheet) spending the tokens. The one exception is `DeskTooltip`, which wraps `MudTooltip` the way `DeskButton` wraps `MudButton`, because `mud-bridge.css` already themes it and the HIG behaviour (hover and focus, delay, measure) is MudBlazor's to give.

**Tech Stack:** .NET 10 Blazor Server RCL, MudBlazor 9.7, bUnit + xUnit in `tests/LineOps.Desk.Tests` (bench `DeskTestContext`, fake catalogue).

**Spec:** `docs/superpowers/specs/2026-10-26-desk-finalisation-design.md` (Phase C).

## Global Constraints

- CLAUDE.md as before (identity, subjects, push, two branches). Agents work in their own tree copy and never run git; the controller commits by pathspec.
- `DeskVocabularyTests` scans the Desk's code *and comments* for this app's words: new primitives and their docs use generic examples ("items", "a count", "a note").
- `AccessibleNameTests`: every field named. `DeskCssConventionTests`: no literal colour outside token blocks; new CSS uses tokens only. `StylesheetOwnershipTests`: new system selectors go in a Desk sheet.
- One agent owns `controls.css` per wave (Task 1). Other tasks that need CSS write their block to `<tree>/scratch/<task>.css` with a banner, and the controller appends it to `controls.css`.
- Targets ≥ 28px tall on the desk; focus ring is the global `:focus-visible` rule (no `outline: none` without a replacement).
- Before "done": `dotnet build` clean on Web, Worker and the three test projects; all tests green; `dotnet format --verify-no-changes` clean in the Linux container; `dotnet pack src/LineOps.Desk` succeeds.

## Review Focus

1. A `DeskToggle` must announce as a switch and flip on Space and on click of its label — Task 1 tests both.
2. `DeskRadioGroup` must be one tab stop with arrows moving the choice and `Home`/`End` reaching the ends, matching `DeskSwitch` — Task 2 tests all four keys.
3. `DeskSlider` must report its value to a screen reader (`aria-valuenow`/`-valuetext`) and keep the visible readout in step — Task 2 test.
4. A `DeskTab` with a count must keep the old text reachable by tests that find the tab by name ("Slip") — Task 1 updates `WagerWindowTabsTests`/`JournalPanelTests` to find "Slip" and assert the badge separately.
5. `DeskTooltip` must not swallow the trigger's own accessible name — Task 3 asserts the wrapped `PriceCell` still has its `aria-label`.

---

## Wave 1 (independent file sets)

### Task 1: Toggle, spinner, badge — and the badge on a tab

**Files:**
- Create: `src/LineOps.Desk/Primitives/DeskToggle.razor`, `DeskSpinner.razor`, `DeskBadge.razor`
- Modify: `src/LineOps.Desk/Primitives/DeskTab.cs` (add `int? Count = null` to the record), `DeskTabs.razor` (render `<DeskBadge Count="tab.Count" Label="…" />` after the tab's text when set)
- Modify: `src/LineOps.Web/Components/Panels/BoardWager.razor:550` and `JournalPanel.razor:485` — `new(WagerTab.Slip, "Slip", Count: Slip.Items.Count)` (named, past `Available`)
- Modify: `src/LineOps.Desk/wwwroot/css/controls.css` — append three sections, "Toggle", "Spinner", "Badge"
- Create: `tests/LineOps.Desk.Tests/DeskToggleTests.cs`, `DeskSpinnerTests.cs`, `DeskBadgeTests.cs`; extend `DeskTabsTests.cs`
- Modify: `tests/LineOps.Web.Tests/WagerWindowTabsTests.cs`, `JournalPanelTests.cs` (wherever "Slip · " is asserted)

**Interfaces produced:**
- `DeskToggle`: `[Parameter] bool Checked; EventCallback<bool> CheckedChanged; string Label (EditorRequired); bool Disabled; string? Title; string? Class; Extra`. Markup: `<label class="toggle"><input type="checkbox" role="switch" class="toggle__input" aria-checked=…><span class="toggle__track" aria-hidden="true"><span class="toggle__thumb"/></span><span class="toggle__label">@Label</span></label>`; label leading, control trailing inside a `wgroup__row--form` is the caller's layout.
- `DeskSpinner`: `[Parameter] DeskKeySize Size = Medium` (16px Small / 20px Medium); `string Label = "Loading"`; `Class`; `Extra`. Markup: `<span class="spinner spinner--{size}" role="status" aria-label="@Label"><span class="spinner__ring" aria-hidden="true"/></span>`; reduced-motion block stops the rotation and shows a static ring.
- `DeskBadge`: `[Parameter] int Count (EditorRequired); string Label (EditorRequired, the noun: "items on the slip"); int Max = 99; Class; Extra`. Renders nothing when `Count == 0`; `<span class="badge num" aria-label="@($"{Count} {Label}")">@(Count > Max ? $"{Max}+" : Count)</span>`.
- `DeskTab<TValue>(TValue Value, string Label, bool Available = true, int? Count = null)` — `Count` goes after the existing `Available`.

- [ ] **Step 1: Failing tests** — `DeskToggleTests`: `Announces_as_a_switch_and_flips_on_change` (input has `role=switch`, `aria-checked` follows `Checked`, changing the input invokes `CheckedChanged` with the new value), `Is_named_by_its_label`, `Disabled_does_not_flip`. `DeskSpinnerTests`: `Is_a_status_with_a_name`, `Small_and_medium_sizes`. `DeskBadgeTests`: `Zero_renders_nothing`, `Counts_and_caps` (5 → "5", 120 → "99+"), `Names_its_count_for_a_reader` ("5 items on the slip"). `DeskTabsTests.A_tab_with_a_count_shows_a_badge_beside_its_name` (tab text still "Slip", a `.badge` inside with "2").
- [ ] **Step 2: Run; fail.**
- [ ] **Step 3: Write the three primitives, the tab change, the CSS** (toggle track 26×16 with a 12px thumb, accent fill when on, `--surface-3` off, 150ms `--ease-standard` transition; badge a 16px-tall pill in `--text-secondary` on `--surface-3`, `--text-caption`, tabular digits; spinner a 2px ring in `--separator` with an accent arc, `spin 0.8s linear infinite`, `prefers-reduced-motion` → no animation).
- [ ] **Step 4: Update the two app tab labels and their tests; both test projects green.**
- [ ] **Step 5: Commit** — `feat(desk): a toggle, a spinner and a badge, and a tab carries its count as one`.

### Task 2: Slider and radio group — the Window manager uses both

**Files:**
- Create: `src/LineOps.Desk/Primitives/DeskSlider.razor`, `DeskRadioGroup.razor` (generic `<TValue>`), `DeskRadioOption.cs`
- Modify: `src/LineOps.Desk/Windowing/WindowManagerPanel.razor` — the `wm-share` range input (~239–245) becomes `<DeskSlider Label="The primary window's share of the row" Min="20" Max="80" Step="5" Value="…" ValueChanged="…" Format="@(v => $"{v:0}%")" />`; the theme swatches (`role="radiogroup"` ~85–110 and `OnSwatchKeyAsync` ~479) become `<DeskRadioGroup TValue="DeskThemeFamily" Options="_families" Value="…" ValueChanged="…" Label="Theme" Class="swatches"><ItemTemplate>…the existing swatch panel markup…</ItemTemplate></DeskRadioGroup>`; the hand-written key handler and `_swatches` refs go
- Create: `<tree>/scratch/task2.css` — sections "Slider" and "Radio group" for the controller to append to `controls.css` (do **not** edit `controls.css`); move the `.wm-share`/`.wm-value` rules' intent into `.slider*` and leave `.wm-*` swatch rules where they are (they style the template)
- Create: `tests/LineOps.Desk.Tests/DeskSliderTests.cs`, `DeskRadioGroupTests.cs`; extend `WindowManagerControlsTests.cs` (the share row is a `.slider`, the themes are a `[role=radiogroup]` of `[role=radio]` with one `tabindex=0`)

**Interfaces produced:**
- `DeskSlider`: `[Parameter] double Value; EventCallback<double> ValueChanged; double Min = 0, Max = 100, Step = 1; string Label (EditorRequired); Func<double, string>? Format; bool Disabled; Class; Extra`. Markup: `<div class="slider"><input type="range" class="slider__input" min max step value aria-label="@Label" aria-valuetext="@Text" @oninput=…><output class="slider__value num" aria-hidden="true">@Text</output></div>` where `Text = Format?.Invoke(Value) ?? Value.ToString("0.##")`.
- `DeskRadioOption<TValue>(TValue Value, string Label, string? Title = null)`.
- `DeskRadioGroup<TValue>`: `[Parameter] IReadOnlyList<DeskRadioOption<TValue>> Options (EditorRequired); TValue? Value; EventCallback<TValue> ValueChanged; string Label (EditorRequired); RenderFragment<DeskRadioOption<TValue>>? ItemTemplate; bool Disabled; Class; Extra`. Markup: `<div class="radiogroup" role="radiogroup" aria-label="@Label" @onkeydown=…>` of `<button type="button" role="radio" aria-checked tabindex="(selected ? 0 : -1)" class="radio radio--on" title="@Title">` with the template or the label. Keys: ArrowRight/ArrowDown next, ArrowLeft/ArrowUp previous, Home/End ends; the moved-to option is selected and focused (as `DeskSwitch.OnKeyDown` does — copy its approach, including the `ElementReference` focus).

- [ ] **Step 1: Failing tests** — `DeskSliderTests`: `Reports_its_value_to_a_reader` (`aria-valuetext` "45%" with `Format`), `Input_moves_the_value` (`oninput` 60 → `ValueChanged(60)`), `Is_named_by_its_label`. `DeskRadioGroupTests`: `Renders_a_radiogroup_with_one_tab_stop`, `Arrows_move_the_choice` (Right from first → second selected and `ValueChanged`), `Home_and_End_reach_the_ends`, `A_template_draws_each_option`. `WindowManagerControlsTests`: the two assertions above.
- [ ] **Step 2: Run; fail.** **Step 3: Write both primitives and the CSS block.** **Step 4: Rewire the Window manager; both test projects green.**
- [ ] **Step 5: Commit** — `feat(desk): a slider and a radio group, and the Window manager's share and themes use them`.

### Task 3: Tooltip, the untested twelve, and the stepper's arrows

**Files:**
- Create: `src/LineOps.Desk/Primitives/DeskTooltip.razor` — `[Parameter] string Text (EditorRequired); RenderFragment ChildContent; DeskTooltipPlacement Placement = Top` (an enum `Top, Bottom, Start, End` mapped to `MudBlazor.Placement`); renders `<MudTooltip Text="@Text" Placement="@Mapped" Arrow="true" Delay="300" ShowOnFocus="true" ShowOnHover="true">@ChildContent</MudTooltip>` with a header comment saying why it wraps rather than draws (the bridge themes `.mud-tooltip`; hover-and-focus with a delay and a measure is the behaviour the HIG asks for and MudBlazor supplies); `Text` is the only API so a test can reach it
- Modify: `src/LineOps.Web/Components/Desk/PriceCell.razor:27–34` — `<DeskTooltip Text="@Why">@Body(true)</DeskTooltip>`; no `MudTooltip` remains under `src/LineOps.Web`
- Modify: `src/LineOps.Desk/Primitives/DeskStepper.razor` — `@onkeydown` on the capsule: ArrowUp/ArrowRight → `Value + Step`, ArrowDown/ArrowLeft → `Value - Step`, Home → `Min`, End → `Max`, clamped as the keys clamp; `tabindex="0"` on the group if the keys are not themselves focusable (read the markup first)
- Create tests in `tests/LineOps.Desk.Tests`: `DeskTooltipTests` (text reaches the markup; the child keeps its own `aria-label`), `DeskCheckboxTests` (named by label; change invokes; disabled), `DeskSelectTests` (renders options; change invokes with the typed value; `Label` names it), `DeskTextAreaTests` (binds; `rows` passes through; `Error` renders the error line), `DeskPopoverTests` (closed renders no panel; `Open` renders one; `DeskPopovers` closes the other open one), `DeskProgressTests` (`role=progressbar` with `aria-valuenow`/`max`; indeterminate drops them; label), `DeskRowTests`/`DeskStackTests` (gap class from `DeskSpace`; align/justify classes), `DeskStepTests` (state class per `DeskStepState`; open renders `ChildContent`; summary when folded), `MetricTests`/`MetricRowTests` (label, value, note, numeric class, click invokes), `RailMenuTests` (trigger named by `Label`; opens its menu), `GlyphTests` (an `<svg>` with the icon path and `aria-hidden`); extend `DeskStepperTests` with the four key cases; `tests/LineOps.Web.Tests/PriceCellTests.cs` — the closing cell's tooltip text is still `Why`
- Write the tests in the voice of `DeskSwitchTests` (a doc comment saying what the primitive promises, one fact per promise).

- [ ] **Step 1: Failing tests** (the tooltip, stepper and PriceCell ones fail; the twelve new classes should mostly pass first time — if one fails, it has found a bug: fix the primitive, not the test, and report it).
- [ ] **Step 2: Implement the tooltip, rewire PriceCell, add the stepper keys.** **Step 3: Both test projects green; `grep -rn MudTooltip src/LineOps.Web` is empty.**
- [ ] **Step 4: Commit** — `feat(desk): a tooltip of the Desk's own, arrows on the stepper, and a test for every primitive`.

## Wave 2

### Task 4: The gallery and the README show everything

**Files:**
- Modify: `src/LineOps.Desk/Windowing/PartsPanel.razor` — new sections, each in the file's existing voice (a paragraph of why, then the control in its states): "Toggle" (on, off, disabled), "Badge" (on a key's label, on a tab via `DeskTab.Count`, capped), "Spinner" (both sizes beside a sentence), "Tooltip" (on a `DeskLink` and on plain text), "Radio group" (plain labels; with a template), "Slider" (with `Format`); and the ten omitted: "Checkbox", "Disclosure", "Menu" (`DeskPullDown` with `DeskMenuItem`s incl. a destructive and a disabled one), "Popover" (a `DeskPopover` with an anchor key), "Stepper", "Sparkline", "Linked cell" already shows `DeskLink` — add `RowActions` and `SnippetShell` under "In a grid" (a row that opens its actions and a snippet)
- Modify: `src/LineOps.Desk/README.md` — a row per new primitive in the primitives table, in alphabetical order with the rest; `DeskTab`'s `Count` in the `DeskTabs` row
- Modify: `tests/LineOps.Desk.Tests/PartsPanelTests.cs` — `Every_primitive_has_a_section`: for every `.razor` in `src/LineOps.Desk/Primitives` whose name starts with `Desk` or is in {`Metric`, `MetricRow`, `Note`, `Tag`, `EmptyState`, `RowActions`, `SnippetShell`, `CheckList`, `PanelForm`, `PanelSection`, `PanelTitle`, `PanelToolbar`}, the Parts bin's source mentions `<Name` (a repo-scan test, in the style of `Every_type_style_shown_is_defined`); `Every_primitive_is_in_the_readme` likewise against the README table.

- [ ] **Step 1: Failing tests** (the two scans). **Step 2: Add the sections and rows.** **Step 3: Green; commit** — `docs(desk): the Parts bin and the README show every primitive`.

### Task 5: Verification (controller)

- [ ] Builds, all tests, `dotnet pack`, `dotnet format` in the Linux container from the pushed branch, the Worker starts, and a browser pass of the Parts bin's new sections and the Window manager's slider and themes at 1280×800 in dark and light; `git worktree list` shows one entry.
