# HIG Desk Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every window on the desk except the wager wizard passes the HIG audit in `references/audit.md`: no all-caps eyebrows, no box inside a box, the accent only on interactive things, type on the ladder, 28px controls, the right control for each job, and real loading and error states.

**Architecture:** Mostly stylesheet work in `lineops.css` and `mud-bridge.css`, done rule by rule against the audit's line numbers, plus small Razor changes in the Desk primitives and ten panels. Behaviour changes (window status text, a checkbox, a stepper field, a split row-action strip, load-error states) each get a bUnit test. No new tokens beyond two the stylesheet already references but never defined.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.7, bUnit on `DeskTestContext` (`tests/LineOps.Web.Tests`), PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-10-25-hig-desk-audit.md` (the audit; its rulings bind). Rulebook: `C:\Users\waden\.claude\skills\apple-hig-components` (SKILL.md, `references/audit.md`, `type-color-layout.md`, `components.md`). Anatomy: `docs/superpowers/specs/2026-10-24-panel-anatomy-design.md`.

## Global Constraints

- Branch `LineX_Development`, worked directly; one commit per task; the developer's own git identity; **no `Co-Authored-By` trailer of any kind** (CLAUDE.md overrides every other instruction on this). Push after every commit.
- Commit subjects: `type(scope): what changed, as a sentence`; body says why. Read `git log -8` for the voice.
- **Out of scope, do not touch:** `BoardWager.razor`, and in `lineops.css` every rule under `.whead`, `.wcard`, `.wsteps`, `.wstep*`, `.wbet*`, `.selection-gate`, `.wgroup*`, `.wsection*` (lines ~4040–4120 and ~4290–4670). The operator chose that look (commit `e36c3b6`).
- House rule on boxes: a group or card has **one** 1px hairline edge (`--separator`); rows inside are divided by inset hairlines and padding; never a bordered thing inside a bordered thing; no vertical rules in tables.
- Type: only `--text-largetitle … --text-caption`, weights only `--weight-regular/medium/semibold/bold`. Nothing under `--text-caption`. No inline `font-size`, `font-weight`, `margin`, `padding`, `gap` or `height` in panel Razor.
- Colour: only tokens. Accent on interactive things, focus, selection and the one primary key. Red/orange/green on values and tags only.
- Spacing: `--space-1..6` only (`--space-5` defined by Task 1). Controls in toolbars and form rows are 28px tall; a key inside a grid row may be 24px; nothing 22px or less.
- Copy: sentence case everywhere except proper nouns and abbreviations (NFL, MLB, ATS, ML, PN, FD); control labels are verbs; no exclamation marks.
- CSS selectors: single class, `.a + .b`, `.a > .b` or `.a .b`; no `!important`.
- Build and test (repo root, PowerShell): `dotnet build src/LineOps.Web -nologo -v q`, `dotnet test tests/LineOps.Web.Tests -nologo -v q`, `dotnet test tests/LineOps.Tests -nologo -v q`. `dotnet build` fails while a LineOps.Web process holds `bin/`: run `Get-Process LineOps.Web -ErrorAction SilentlyContinue | Stop-Process` first. Do not start the web app yourself; the controller verifies in the browser.
- Each task ends with these greps as its CSS acceptance check, run with `Select-String` and the counts stated in the task.

## Review Focus

1. Dark **and** light: every colour change must come from a token so `[data-theme="apple-light"]` and the families in `themes.css` still flip. → Tasks 2–4: grep for `#` and `rgba(` outside the token blocks adds nothing.
2. A 29px grid row must not grow when `.tag` loses its caps and gains `--text-footnote` (lineops.css 2586–2598 explains the measurement). → Task 2 step: assert the computed `.tag` height ≤ 18px in the note's own terms.
3. A visitor (`Manager.Access.CanOperate == false`) sees the Board's toolbar with no "+EV" checkbox gap and no actions slot. → Task 7 test `Visitor_toolbar_has_no_ev_filter`.
4. A failed load must never show the "New" empty state over stale rows. → Task 8 test `A_failed_load_renders_the_error_state_not_the_new_state`.
5. Keyboard: the theme swatches as a radiogroup must move with arrow keys or at least Tab+Space; the split RowActions strip must keep every key reachable. → Task 7 test `Swatches_are_a_radiogroup`; Task 7 test `Row_actions_keep_every_key_focusable`.

---

### Task 1: Window status, palette rows, undefined tokens, dead CSS

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/GamePanel.razor:337`, `src/LineOps.Web/Components/Panels/TeamPanel.razor:326`
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (`.launcher__item` ~1803; token blocks ~100–140 and the light block; delete `.splits__key` 3557–3571, `.launcher__open` 1837–1844, `.board__actions-label` 2254–2261; stale comment 1913–1917)
- Modify: `src/LineOps.Desk/wwwroot/css/themes.css` (each family block that defines `--space-4` gets `--space-5` and `--weight-bold`, if those blocks define spacing/weights at all — check first; if spacing lives only in `lineops.css :root`, define there only)
- Modify: `src/LineOps.Desk/Primitives/RowActions.razor:30` (remove the `Label` rendering and parameter if no caller passes it)
- Test: `tests/LineOps.Web.Tests/SubjectWindowStatusTests.cs`

**Interfaces:**
- Produces: `--space-5: 20px` and `--weight-bold: 700` in `:root`; Game status text `"{Phase} · {score}"` for finals and live, `"{Starts}"` for scheduled; Team status `"{W}-{L} · {season}"`.

- [ ] **Step 1: Write the failing tests** in `SubjectWindowStatusTests.cs`, modelled on `WindowNavigationTests.cs` and the existing panel tests that render a panel inside a `WindowManager` (find one with `grep -l "SetPulse\|StatusText" tests/LineOps.Web.Tests/*.cs`; if none renders a real panel, test the pure formatter you extract in Step 3 instead):
  - `Game_status_is_state_not_the_matchup`: a final game `Mets at Braves` 4–2 → status `"Final · NYM 4–2 ATL"` (reuse the Board's score string if there is a helper; otherwise `"Final · 4–2"`), and the status never contains the matchup string.
  - `Team_status_is_the_record`: team with 5 wins, 0 losses in 2026 → `"5-0 · 2026"`.
- [ ] **Step 2: Run the test file; expect FAIL.**
- [ ] **Step 3: Implement.** In `PanelBase` or the two panels add `protected static string StateStatus(...)` as needed; pass it as `status:` in both `WithActivityAsync` calls. The matchup/team name is already the window title.
- [ ] **Step 4: CSS.** `.launcher__item { justify-content: flex-start; }`. Define the two tokens. Delete the three dead rules and the stale `.metric--action` comment. Grep: `Select-String "splits__key|launcher__open|board__actions-label" src/LineOps.Desk/wwwroot/css/lineops.css` → 0 matches; `Select-String "var\(--space-5\)|var\(--weight-bold" ` still resolve to defined tokens.
- [ ] **Step 5: Build, test, commit, push.** Subject: `fix(desk): a subject window's bar says its state, and palette rows sit flush left`.

### Task 2: Retire the all-caps eyebrows

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` — audit §1 rows 1–14 (except 10 and 15, done in Task 1) and the tracked-but-not-uppercase list
- Modify: `src/LineOps.Desk/wwwroot/css/mud-bridge.css:471–483` (`.mud-chip` in step with `.tag`)
- Modify: `src/LineOps.Web/Components/Panels/IncidentsPanel.razor:104` (the incident subject becomes `<PanelTitle>` with the incident's title as the name and `#id · severity · opened` as `Meta`; remove the `psection--subject` use if it is then unused, and its CSS at 3010)
- Modify: `docs/superpowers/specs/2026-10-24-panel-anatomy-design.md` — under Non-goals, strike the "uppercase section rule stays" line with a one-line note pointing at the audit's ruling
- Test: `tests/LineOps.Web.Tests/PanelHeaderTests.cs` if it asserts on class names only — no test change expected; the acceptance is the grep

- [ ] **Step 1: Rewrite each rule** per the audit's Fix column: sentence case means removing `text-transform` and `letter-spacing`; sizes become the named rung; colours `--text-secondary` (section rules, labels) or `--text-tertiary` where the audit says.
- [ ] **Step 2: `.tag` row height.** After the change, compute from the rule: font `--text-footnote` (11px) × line-height × padding must stay ≤ 18px so a 29px grid row holds. Adjust vertical padding to `2px` if needed; update the comment at 2586–2598 to the new numbers.
- [ ] **Step 3: Grep.** `Select-String "text-transform: *uppercase" src/LineOps.Desk/wwwroot/css/lineops.css src/LineOps.Desk/wwwroot/css/mud-bridge.css` → only `.hdr__brand`, `.hdr__brand--menu`, `.desk-empty__mark` remain (3 matches, all the wordmark) plus any rule inside the wager block (out of scope, leave). `Select-String "letter-spacing" lineops.css` → only the wordmark and the wager block.
- [ ] **Step 4: Copy check.** Section titles passed to `PanelSection`/`PanelHeader` across `src/LineOps.Web/Components/Panels/*.razor` are already sentence case ("Recent games"); fix any that relied on CSS to look right (e.g. `Title="ODDS FEEDS"`): `Select-String 'Title="[A-Z][A-Z ]+"' src/LineOps.Web/Components/Panels/*.razor` → 0.
- [ ] **Step 5: Build, test, commit, push.** Subject: `feat(desk): section rules, labels and tags read in sentence case, not capitals`.

### Task 3: The accent only on interactive things

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` — audit §3 table (1901, 2618, 1200, 2053–2061, 3110, 3517)
- Modify: `src/LineOps.Web/Components/Panels/PartsPanel.razor:717`

- [ ] **Step 1: Apply the §3 table.** `.metric--info .metric__value` → `--text-primary`; `.tag--info` → `background: var(--surface-3); color: var(--text-secondary)`; `.ftr__flag` → the `.ftr__stat` look; `.price__book` → `color: var(--text-secondary); border: 0; background: none; letter-spacing: 0; font-size: var(--text-caption)`; `.runbook` accent edge removed; `.formrun__latest` ring → `--separator-strong`.
- [ ] **Step 2: Grep.** `Select-String "var\(--accent" src/LineOps.Desk/wwwroot/css/lineops.css` — every remaining match outside the wager block is a focus ring, marble, current key, active palette item, chosen book row, progress fill, link, Filled/Tinted button, `.rail__tick--best`, or the brand. List any other in the report.
- [ ] **Step 3: Build, commit, push.** Subject: `fix(desk): the accent marks what can be pressed, nothing else`.

### Task 4: Boxes become fills; tables lose their rules

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` — audit §2 table: `.runbook`/`.runbook__step`, `.snippet`, `.snippet__open`, `.logrow`, `.price__sel`, `.deskgrid--board` 3733–3740, `.ftr__refusal`/`.ftr__undo`
- Modify: `src/LineOps.Web/Components/Panels/TeamPanel.razor:81,147` — wrap each `.logrow` list in one `<div class="loglist">` so the group carries the single hairline and rows carry only an inset top hairline
- Modify: `src/LineOps.Web/Components/Desk/PriceCell.razor` only if `.price__sel` markup needs a class rename; prefer CSS only

- [ ] **Step 1: `.loglist`** — `background: var(--surface-2); border: 1px solid var(--separator); border-radius: var(--radius); overflow: hidden`. `.logrow` — no border, no radius, no margin; `.logrow + .logrow { border-top: 1px solid var(--separator); }` inset by the row's horizontal padding (use a pseudo-element or `margin-inline` on the hairline as `.wgroup__row` does at ~4073). Chevron and expand behaviour unchanged.
- [ ] **Step 2: The rest of §2** per its Fix column. The board table: replace the three `border-left` rules with `padding-left: var(--space-4)` on the same cells.
- [ ] **Step 3: Grep.** `Select-String "border-left: *1px|border-right: *1px" lineops.css` → none outside the wager block and `.divider`. `Select-String "border: *1px solid currentColor"` → 0.
- [ ] **Step 4: Build, test, commit, push.** Subject: `feat(desk): lists, snippets and price cells are fills and hairlines, not boxes in boxes`.

### Task 5: Type on the ladder

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` — audit §4 line list; add `.meta { font-size: var(--text-subheadline); color: var(--text-secondary); }` in the Panel anatomy block
- Modify: the panels in audit §4 "Inline font-size" list; `span.dim style="font-size: calc(11.5px …)"` → `span.meta`; `GamePanel.razor:454,507` hand-written `<h5>` → `PanelSection`/`PanelHeader`
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css:4247–4248` tabular figures on `.book-row__price`, `.book-row__line`, `.metric__note`

- [ ] **Step 1: Replace** each `calc(Npx * var(--text-scale))` outside the wager block with the nearest rung (9/9.5/10/10.5 → `--text-caption`; 11/11.5 → `--text-footnote`; 12/12.5 → `--text-subheadline`; 13 → `--text-body`; 15 → `--text-title3`; 16/17 → `--text-title2`; 19 → `--text-title1`). Numeric weights → tokens.
- [ ] **Step 2: Panels.** Remove every inline `font-size`/`font-weight` in the listed files; use `.meta`, `.num`, or a `PanelSection`.
- [ ] **Step 3: Grep.** `Select-String "px \* var\(--text-scale\)" lineops.css` → only inside the wager block (lines > 4290) and `.desk-empty__mark`'s clamp. `Select-String "font-size" src/LineOps.Web/Components/Panels/*.razor` → 0 outside `BoardWager.razor` and `PartsPanel.razor`'s own type-ramp specimen.
- [ ] **Step 4: Build, test, commit, push.** Subject: `fix(desk): every size is a rung on the type ladder`.

### Task 6: Hit regions and rhythm, and the anatomy sweep

**Files:**
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` — audit §5: `.desk-btn--sm` 2743 → 24px (and a `.desk-btn--sm` in `.ptoolbar`/`.toolbar` → 28px), `.gate__opt` 990 → 28px, `.gate--sm .gate__opt` 1014 → 24px, `.tabs__tab` 1064 → 28px, `.win__nav-btn` 1399 → 28px, `.desk-sheet__close` 3826 → 28px, `.ftr__dismiss`/`.ftr__undo` → 24px; off-grid paddings snapped to `--space-*`
- Modify: `JournalPanel.razor` (205,214,218,220,298,309), `WindowManagerPanel.razor` (86,143,149,170,202,206,269,280,33,40), `OpsPanel.razor:60,81,107`, `HistoryPanel.razor:72`, `GamePanel.razor:44,46,53,371`, `TeamPanel.razor:114,162`, `BoardAllBets.razor:41,47`, `BoardPanel.razor:122,365`, `OddsPanel.razor:93`, `SlipPanel.razor:35,54`, `PartsPanel.razor` (`p.empty` paddings), `IncidentsPanel.razor:164–170` (label each RCA textarea with `DeskTextArea Label=` or a `field-lbl`)
- Modify: Board and Window manager — move the `Note` above the `MetricRow` (anatomy slot order)
- Modify: `HeadToHeadPanel.razor:65` wrap the grid in `<PanelSection Title="Meetings">`; `RunsPanel.razor:44` in `<PanelSection Title="Runs">`

- [ ] **Step 1: CSS heights and paddings** as listed; `.empty` padding `var(--space-3) 0`, so `p.empty style=` can go.
- [ ] **Step 2: Razor sweep.** Remove every inline `style` that sets margin/padding/gap/height in the listed files; where a gap is needed use `DeskStack`/`DeskRow` or an existing class.
- [ ] **Step 3: Grep.** `Select-String 'style="[^"]*(padding|margin|gap|height|font)' src/LineOps.Web/Components/Panels/*.razor` → 0 outside `BoardWager.razor`; PartsPanel may keep its deliberate spacing-demo bars.
- [ ] **Step 4: Build, test, commit, push.** Subject: `fix(desk): controls are 28px, spacing is on the grid, and no panel writes a margin inline`.

### Task 7: The right control for each job

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/BoardPanel.razor:68–75` — "+EV · n" → a checkbox row `Only +EV (n)` (use `DeskSwitch`? no: a checkbox; if no `DeskCheckbox` primitive exists, create `src/LineOps.Desk/Primitives/DeskCheckbox.razor` wrapping `<input type="checkbox">` + label at 28px, styled in `lineops.css` after `.field`)
- Modify: `src/LineOps.Desk/Primitives/RowActions.razor:37–55` — selection keys become one `DeskSwitch` (segments = the snippet views, value = the open snippet or none); action keys follow as Plain `DeskButton`s
- Modify: `src/LineOps.Web/Components/Panels/WindowManagerPanel.razor:98–122` swatches → `role="radiogroup"` with `role="radio" aria-checked`; `:156–166` → `DeskField Type="number"` + `−`/`+` keys beside it + Plain "Match this desk (n)"
- Modify: `src/LineOps.Web/Components/Desk/PullMenu.razor:23` → `Emphasis="DeskEmphasis.Tinted"` with a trailing chevron glyph
- Test: `tests/LineOps.Web.Tests/BoardToolbarTests.cs` (`Visitor_toolbar_has_no_ev_filter`, `Ev_filter_is_a_checkbox`), `tests/LineOps.Web.Tests/RowActionsTests.cs` (`Row_actions_keep_every_key_focusable`, `Selecting_a_view_opens_that_snippet`), `tests/LineOps.Web.Tests/WindowManagerControlsTests.cs` (`Swatches_are_a_radiogroup`, `Concurrent_windows_is_a_field_with_a_stepper`)

- [ ] **Step 1: Write the six failing tests** (find the existing RowActions/Board test files first and extend them if present).
- [ ] **Step 2: Run; expect FAIL.**
- [ ] **Step 3: Implement** each change; RowActions keeps its `RowAction` model and every `OnClick`.
- [ ] **Step 4: Run; expect PASS. Full test run green.**
- [ ] **Step 5: Commit, push.** Subject: `feat(desk): a filter is a checkbox, a count is a field with a stepper, and a menu key is tinted`.

### Task 8: Loading and error states

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/PanelBase.cs` — `WithActivityAsync` catches, records `LoadError` (a `string?` with a sentence for the operator, never `ex.Message`), reports `PulseState.Critical, "load failed"`, and does **not** rethrow; expose `protected bool Failed`, `protected string? LoadError`, `protected Task RetryAsync()` that re-runs the last work
- Modify: Board, Journal, Performance, Ops, Incidents, Runs, Players, History panels — before the first load completes render `<DeskGrid Loading>`/`DeskSkeleton` (whatever the panel's body is) instead of the New empty state; when `Failed` render `<EmptyState Kind="EmptyStateKind.Error" Action="RetryAsync">` with `LoadError`
- Modify: `SlipPanel.razor:351`, `IncidentsPanel.razor:348`, `PlayersPanel.razor:219` — replace raw exception text with a sentence ("The entry was not logged. Try again.") and log the exception
- Test: `tests/LineOps.Web.Tests/PanelLoadStateTests.cs` — `A_failed_load_renders_the_error_state_not_the_new_state`, `Retry_reruns_the_load`, `First_load_shows_a_skeleton_not_the_new_state` against one real panel (Runs is the simplest)

- [ ] **Step 1: Tests; expect FAIL. Step 2: Implement. Step 3: PASS; full run green.**
- [ ] **Step 4: Commit, push.** Subject: `feat(desk): a panel that fails to load says so and offers a retry, and never shows "new" over stale rows`.

### Task 9: Player and Team windows read like a sports page

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/PlayerPanel.razor` — column titles and averages labels through one `static string StatLabel(string key)` map: `C/ATT` → `Comp/Att`, `passing.AVG` → `Yds/Att`, `passing.YDS` → `Pass yds`, `CAR` → `Carries`, `INT` → `Int`, `LONG` → `Long`, `QBR` → `QBR`, `RTG` → `Rating`; unknown keys → the last dotted segment in sentence case. MLB keys follow the same rule (`AB` → `At bats`, `H` → `Hits`, `HR` → `HR`, `RBI` → `RBI`, `AVG` → `Avg`, `ERA` → `ERA`, `IP` → `IP`, `SO` → `SO`, `BB` → `BB`).
- Modify: `src/LineOps.Web/Components/Panels/TeamPanel.razor` roster rows — when the per-player stat is missing, omit the dash and show only `5 GP`; the row's trailing column is the games count alone
- Test: `tests/LineOps.Web.Tests/StatLabelTests.cs` — the map above as `[Theory]` rows plus `Unknown_key_uses_its_last_segment`

- [ ] **Step 1: Tests; FAIL. Step 2: Implement. Step 3: PASS.**
- [ ] **Step 4: Commit, push.** Subject: `fix(player): stat columns carry names, not keys`.

---

## Addendum, 25 October 2026 evening — expression

The operator's verdict after Tasks 1–8: the row actions Odds, H2H and Form did nothing (fixed
in 72722ca: the strip's clicks reached the grid row and closed it), the wager wizard showed no
change (it was ruled out of scope; it is in scope now, since both sessions that owned it are
idle), and "everything still lacks expression". Tasks 1–8 were subtractive: capitals, boxes and
stray accent came off, and nothing was put in their place. These tasks put expression back
without breaking the rulebook. The rulebook keeps the accent for interaction and forbids the
price as a hero figure, so expression comes from **identity** (team colour), **contrast of
weight** (the winner reads heavier than the loser), **scale where the content earns it** (a
score, a record), **shape** (a scoreboard, not a sentence), and **small, purposeful motion**.

Global constraints above still bind, with two amendments:
- **Team colour is content, not chrome.** It appears only as a team's own mark (a disc beside
  that team's name or score). It never colours text, controls, fills or selection. Hex values
  live in one C# data map, never in CSS. Each mark carries a 1px `--separator-strong` ring so a
  dark team colour still reads on a dark surface.
- **The wager window is in scope** (`BoardWager.razor` and the wager block of `lineops.css`),
  following `~/.claude/skills/apple-hig-components/references/wizards.md` ("Reconciling the
  card look" and "The bet-and-price group recipe") exactly.

### Task 10: Team marks

**Files:** Create `src/LineOps.Web/Components/Desk/TeamMark.razor`, `src/LineOps.Web/Components/Desk/TeamColors.cs`; modify `lineops.css` (a `.teammark` rule in the anatomy block); use it in `GamePanel.razor` (title), `TeamPanel.razor` (title), `BoardPanel.razor` (matchup cell), `BoardWager.razor` (header), `Snippets/FormSnippet.razor`, `Snippets/HeadToHeadSnippet.razor`; test `tests/LineOps.Web.Tests/TeamMarkTests.cs`.

**Interfaces:** `TeamColors.For(string sportKey, string abbrev) -> string?` (hex like `#E31837`, null when unknown); `<TeamMark Sport="..." Abbrev="..." Size="TeamMarkSize.Small|Large" />` renders `<span class="teammark teammark--sm|lg" style="--team:#hex" aria-hidden="true">`, or nothing when the colour is unknown.

- [ ] Tests: `Known_team_has_its_primary_colour` (NFL `KC` -> `#E31837`, MLB `NYY` -> `#0C2340`; for every club use its most recognisable colour and say which in a comment per row), `Unknown_team_renders_nothing`, `Mark_is_hidden_from_assistive_tech`, `Every_abbrev_the_desk_uses_has_a_colour` (all 32 NFL and 30 MLB abbreviations as ESPN spells them; list them in the test).
- [ ] `.teammark`: inline-block; `--sm` 8px disc, `--lg` 12px disc; `background: var(--team)`; `box-shadow: 0 0 0 1px var(--separator-strong)`; centred on the cap height; `margin-right: var(--space-1)`.
- [ ] Commit: `feat(desk): every team carries its colour as a mark beside its name`.

### Task 11: The Game window's title is a scoreboard

**Files:** `GamePanel.razor` title; `lineops.css` anatomy block (`.scoreboard*`); create `src/LineOps.Web/Components/Desk/Scoreboard.razor` (GamePanel cannot render in bUnit without a database); test `tests/LineOps.Web.Tests/ScoreboardTests.cs`.

**Interfaces:** `<Scoreboard Game="..." OnTeam="EventCallback<int>" Compact="bool" />` — two rows, away then home. Each row: `TeamMark Large`, the team name as a `DeskLink` at `--text-title3` semibold, the score trailing at `--text-title1` semibold tabular. Final: the loser's row is `--text-secondary` at `--weight-regular`. Live or tied: both rows primary. Scheduled: no scores; the start time trails the home row in `--text-subheadline` secondary. `Compact`: one line, `away mark name score · at · home mark name score`, `--text-title3`, for the wager header. The status tag, season tag and probable pitchers stay in `PanelTitle`'s `Tags` and `Detail`.

- [ ] Tests: `Final_game_dims_the_loser`, `Live_game_keeps_both_rows_primary`, `Scheduled_game_shows_no_scores`, `Team_names_link_to_their_windows`, `Compact_reads_on_one_line`.
- [ ] Commit: `feat(game): the title is a scoreboard, and the winner reads heavier than the loser`.

### Task 12: The Board row reads as two teams, and its snippet spans the row

**Files:** `BoardPanel.razor` (matchup and score columns; where `RowActions` renders); `DeskGrid.razor` (a child-row slot if MudDataGrid 9.7 supports `ChildRowContent`; read the MudBlazor package's API before deciding; if it does not, render the snippet in a cell spanned by CSS and record which in the report); `lineops.css`.

- [ ] Matchup cell: two lines, away over home, each `TeamMark Small` plus the team's short name (`Steelers`, not `Pittsburgh Steelers a...`). The score cell has two lines aligned to them: the winner semibold primary, the loser secondary. No truncation at the Board's default width.
- [ ] The open row's strip stays in the matchup cell. The snippet it opens renders full-width under the row, indented to the matchup column's leading edge, max-width 720px.
- [ ] Tests on `RowActions`/`DeskGrid` for the child slot if added; otherwise a bUnit assertion that the snippet's container carries the spanning class.
- [ ] Commit: `feat(board): a row reads as two teams, and its snippet opens across the row`.

### Task 13: The wager wizard, by the recipe

**Files:** `BoardWager.razor`, the wager block of `lineops.css`, `Desk/StakeRow.razor`; tests in the existing wager test files (`grep -l BoardWager tests/LineOps.Web.Tests/*.cs`).

- [ ] Header card: the matchup through `Scoreboard Compact` (Task 11), so "at" no longer floats between wrapped names; the inline `style="margin:0 4px"` goes.
- [ ] Price row per recipe rule 3: the chosen side at 13px semibold over an 11px secondary line; the price trailing at `--text-title2` semibold tabular primary; the book pop-up after it.
- [ ] Stake: no native number spinner (`appearance: textfield`, hide the WebKit spin buttons); quick values beside the field as Plain keys; "To win $X" as the group's footer, live as the stake is typed.
- [ ] Keys: one Filled primary (Log wager) leading the step's foot; Add to slip and Add as leg Plain; a key that cannot be used yet is hidden until it can be, or carries a one-line reason beside it; Duplicate moves out from under the keys to the header's trailing edge as a Plain key.
- [ ] Status note per recipe rule 7: the closing-line warning becomes a symbol plus one sentence under the steps card, not an orange paragraph.
- [ ] Commit: `feat(wager): the wizard follows the recipe: a scoreboard header, the price row, one key`.

### Task 14: Live and result states, and the snippet's arrival

**Files:** `src/LineOps.Desk/Primitives/Tag.razor`, `lineops.css`, `src/LineOps.Desk/Primitives/SnippetShell.razor`, and the result-tag call sites.

- [ ] A Live tag carries a 6px `--state-warning` dot that pulses (opacity 1 to .35, 1.2s ease-in-out, infinite); under `prefers-reduced-motion` the dot holds still.
- [ ] Result capsules W, L and P: W is `--state-positive-wash` with `--state-positive` text, L the negative pair, P neutral; the same size as `.tag`; used by the Form snippet, Team recent games and H2H.
- [ ] The snippet arrives: 180ms, opacity 0 to 1 and translateY(-4px) to 0, ease-out; reduced motion: opacity only.
- [ ] Commit: `feat(desk): live games pulse, results read as capsules, and a snippet arrives rather than appears`.
