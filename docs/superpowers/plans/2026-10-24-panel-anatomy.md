# Panel Anatomy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every window on the desk renders its parts in one fixed order — title, toolbar, note, metrics, sections, forms — with spacing owned by four new Desk components rather than by inline styles in twenty panels.

**Architecture:** Four Razor components in `LineOps.Desk/Primitives` (`PanelTitle`, `PanelToolbar`, `PanelSection`, `PanelForm`) plus one CSS block in `lineops.css`. They take render fragments and strings only, and follow the existing splat rule (class/style held out and merged). Every panel in `LineOps.Web/Components/Panels` is then rewritten onto them in four small sweeps, each a commit, each checked in the browser.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.7, bUnit on `DeskTestContext`, Windows PowerShell for build/test.

**Spec:** `docs/superpowers/specs/2026-10-24-panel-anatomy-design.md`

## Global Constraints

- Branch `LineX_Development`; one commit per task; developer identity only, **no `Co-Authored-By` trailer** (CLAUDE.md); push after every commit.
- Commit subjects: `type(scope): what changed, as a sentence`; body says why.
- `LineOps.Desk` references no domain type; the new components take `RenderFragment` and `string` only.
- Splat rule: `class` and `style` are held out of `@attributes` and merged after the component's own, never substituted (see `DeskLayout.MergeStyle`, `DeskLayout.WithoutClassAndStyle`, `DeskLayout.ExtraClass`).
- No new tokens. Spacing values are `--space-1..6`; type is `--text-title2`, `--text-subheadline`, `--text-footnote`; weights `--weight-semibold`; colours `--text-primary`, `--text-secondary`.
- CSS: single-class, `.a + .b` or `.win__body > .a` selectors only; no `!important`; the block goes after the `.stack--end` rule (`lineops.css` ~line 2828) and before `.metrics`.
- Spec slot order inside a panel: Title → Toolbar → Note → Metrics → Sections (forms inside a section). Toolbar order: pickers → search → message → actions; the one Filled button is last in actions.
- Form footer order: `Destructive` far left, then Cancel, then the primary at the right edge.
- No behaviour change in any panel: same queries, same handlers, same aria attributes.
- Build/test commands (from repo root, PowerShell):
  `dotnet build src/LineOps.Web`, `dotnet build tests/LineOps.Web.Tests`, `dotnet build tests/LineOps.Tests`, `dotnet test tests/LineOps.Web.Tests`.
- A `LineOps.Web` process already runs on port 5264 from this checkout; it locks `bin/`. Before any `dotnet build`, stop it (`Get-Process LineOps.Web | Stop-Process`) and restart it after the task via `preview_start` name `lineops-web` (port 5263) for the browser check.

## Review Focus

1. A panel rendered for a visitor (`Manager.Access.CanOperate == false`) has no actions; the toolbar must still render pickers with no empty right-hand box and no stray gap. → Task 2 test `Omits_the_actions_slot_when_none_is_given`.
2. A `PanelSection` nested inside another renders `h4`, a top-level one `h3`, so screen-reader outlines do not flatten. → Task 3 test `Nested_sections_step_down_a_heading_rank`.
3. A caller passing `class="x" style="margin:0"` to any of the four components must extend, not erase, the component's own class and style. → Tasks 1–4, test `Merges_a_callers_class_and_style`.
4. `PanelTitle` with no `Meta` and no `Keys` must not render an empty meta row that adds 16px of nothing. → Task 1 test `Renders_no_meta_row_without_meta_or_keys`.
5. A `PanelForm` with no `Destructive` fragment must keep Cancel and the primary pinned right. → Task 4 test `Footer_pins_right_without_a_destructive_slot`.

---

### Task 1: `PanelTitle` and the anatomy CSS block

**Files:**
- Create: `src/LineOps.Desk/Primitives/PanelTitle.razor`
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (insert after `.stack--end`, ~line 2828)
- Test: `tests/LineOps.Web.Tests/PanelTitleTests.cs`

**Interfaces:**
- Produces: `PanelTitle` with parameters `RenderFragment? ChildContent` (the name), `RenderFragment? Tags`, `RenderFragment? Meta`, `RenderFragment? Keys`, `string? Class`, `string? Style`, `[Parameter(CaptureUnmatchedValues = true)] IReadOnlyDictionary<string, object>? Extra`.
- Markup:
  ```html
  <div class="ptitle [Class] [splat class]" style="[own; caller]" ...rest>
    <div class="ptitle__row"><h2 class="ptitle__name">@ChildContent</h2>@Tags</div>
    <div class="ptitle__meta">  <!-- only when Meta or Keys is non-null -->
      <span class="ptitle__text">@Meta</span>
      <span class="ptitle__keys">@Keys</span>  <!-- only when Keys non-null -->
    </div>
  </div>
  ```
- CSS block header comment `/* ---- Panel anatomy (spec 2026-10-24) ---- */`. Rules for this task:
  `.ptitle { margin: 0 0 var(--space-4); }`
  `.ptitle__row { display:flex; align-items:baseline; flex-wrap:wrap; gap: var(--space-2); }`
  `.ptitle__name { margin:0; font-size: var(--text-title2); font-weight: var(--weight-semibold); line-height: var(--leading-title); color: var(--text-primary); }`
  `.ptitle__meta { display:flex; align-items:center; flex-wrap:wrap; gap: var(--space-2); margin-top: var(--space-1); font-size: var(--text-subheadline); color: var(--text-secondary); }`
  `.ptitle__text strong, .ptitle__text .ptitle__em { color: var(--text-primary); font-weight: var(--weight-regular); }`
  `.ptitle__keys { margin-left:auto; display:flex; align-items:center; gap: var(--space-1); }`

- [ ] **Step 1: Write the failing tests** in `PanelTitleTests : DeskTestContext`

```csharp
[Fact] public void Renders_the_name_as_an_h2_with_the_title_class()
  // RenderComponent<PanelTitle>(p => p.AddChildContent("Mets at Braves"))
  // Assert.Equal("Mets at Braves", cut.Find("h2.ptitle__name").TextContent)
[Fact] public void Renders_no_meta_row_without_meta_or_keys()
  // Assert.Empty(cut.FindAll(".ptitle__meta"))
[Fact] public void Pins_keys_in_their_own_slot_on_the_meta_row()
  // Meta "7:10 PM ET", Keys "<button>Season</button>" → cut.Find(".ptitle__keys button").TextContent == "Season"
[Fact] public void Merges_a_callers_class_and_style()
  // AddUnmatched("class","x"), AddUnmatched("style","margin:0")
  // root.ClassList contains "ptitle" and "x"; root.GetAttribute("style") contains "margin:0"
```

- [ ] **Step 2: Run to verify they fail**
Run: `dotnet test tests/LineOps.Web.Tests --filter PanelTitleTests`
Expected: build error, `PanelTitle` not found.

- [ ] **Step 3: Create `PanelTitle.razor`** per the Interfaces block, with a header comment in the voice of `PanelHeader.razor` saying why titles stopped being section rules. Use `DeskLayout.WithoutClassAndStyle(Extra)` for the splat, `DeskLayout.ExtraClass(Extra)` folded into classes, and `Style` + splatted style appended (own style is empty here; mirror `MetricRow`'s `DeskAttributes.MergeStyle` if simpler).

- [ ] **Step 4: Add the CSS block** to `lineops.css` with the rules above.

- [ ] **Step 5: Run tests**
Run: `dotnet test tests/LineOps.Web.Tests --filter PanelTitleTests`
Expected: 4 passed.

- [ ] **Step 6: Commit**
```bash
git add src/LineOps.Desk/Primitives/PanelTitle.razor src/LineOps.Desk/wwwroot/css/lineops.css tests/LineOps.Web.Tests/PanelTitleTests.cs
git commit -m "feat(desk): a subject window names itself in a title, not a section rule"
git push origin LineX_Development
```

---

### Task 2: `PanelToolbar`

**Files:**
- Create: `src/LineOps.Desk/Primitives/PanelToolbar.razor`
- Modify: `src/LineOps.Desk/wwwroot/css/lineops.css` (same block)
- Test: `tests/LineOps.Web.Tests/PanelToolbarTests.cs`

**Interfaces:**
- Produces: `PanelToolbar` with `RenderFragment? ChildContent` (pickers and search), `string? Message`, `RenderFragment? Actions`, `string? Class`, `string? Style`, splat `Extra`.
- Markup:
  ```html
  <div class="ptoolbar [classes]" style="…" role="toolbar" ...rest>
    @ChildContent
    <span class="ptoolbar__msg">@Message</span>     <!-- only when Message non-empty -->
    <div class="ptoolbar__actions">@Actions</div>   <!-- only when Actions non-null -->
  </div>
  ```
- CSS: `.ptoolbar { display:flex; align-items:center; flex-wrap:wrap; gap: var(--space-2); margin: 0 0 var(--space-3); }`
  `.ptoolbar__msg { font-size: var(--text-subheadline); color: var(--text-secondary); }`
  `.ptoolbar__actions { margin-left:auto; display:flex; align-items:center; gap: var(--space-2); }`
  `.ptoolbar > .desk-field, .ptoolbar > .field-set { flex: 1 1 160px; min-width: 150px; }` — check the actual root class `DeskField` renders before writing this selector; the intent is "a bare search field takes the remaining width".

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] public void Omits_the_actions_slot_when_none_is_given()   // Assert.Empty(cut.FindAll(".ptoolbar__actions"))
[Fact] public void Renders_actions_in_the_right_pinned_slot()    // Actions "<button>Sync</button>" → .ptoolbar__actions button
[Fact] public void Renders_the_message_between_content_and_actions()
  // ChildContent "<select></select>", Message "Settled 3", Actions "<button/>"; children of root in order: select, span.ptoolbar__msg, div.ptoolbar__actions
[Fact] public void Has_the_toolbar_role()                          // root.GetAttribute("role") == "toolbar"
[Fact] public void Merges_a_callers_class_and_style()
```
- [ ] **Step 2: Run, expect build failure.**
- [ ] **Step 3: Create `PanelToolbar.razor`** and add its CSS.
- [ ] **Step 4: Run tests, expect 5 passed.**
- [ ] **Step 5: Commit**
```bash
git commit -m "feat(desk): one toolbar, and the primary action always at its right edge"
git push origin LineX_Development
```

---

### Task 3: `PanelSection`

**Files:**
- Create: `src/LineOps.Desk/Primitives/PanelSection.razor`
- Modify: `src/LineOps.Desk/Primitives/PanelHeader.razor` (doc comment only: `Gap` is now the frame's job; keep the parameter)
- Modify: `lineops.css` (same block)
- Test: `tests/LineOps.Web.Tests/PanelSectionTests.cs`

**Interfaces:**
- Produces: `PanelSection` with `string? Title`, `RenderFragment? TitleContent` (when the heading needs markup, e.g. a link; wins over `Title`), `RenderFragment? Keys`, `RenderFragment? Meta`, `RenderFragment? ChildContent`, `string? Class`, `string? Style`, splat `Extra`. A `[CascadingParameter(Name = "PanelSectionDepth")] int Depth` (default 0) decides heading rank: `Level = Depth == 0 ? 3 : 4`; the body is wrapped in `<CascadingValue Name="PanelSectionDepth" Value="Depth + 1">`.
- Markup:
  ```html
  <section class="psection [classes]" style="…" ...rest>
    <div class="psection__head">
      <PanelHeader Tight Level="@Level">@(TitleContent ?? Title)</PanelHeader>
      <div class="psection__keys">@Keys</div>   <!-- only when Keys non-null -->
    </div>
    <div class="psection__meta">@Meta</div>      <!-- only when Meta non-null -->
    <div class="psection__body">@ChildContent</div>
  </section>
  ```
- CSS: `.psection { margin: 0; }` `.psection + .psection { margin-top: var(--space-6); }`
  `.psection__head { display:flex; align-items:baseline; flex-wrap:wrap; gap: var(--space-2); margin-bottom: var(--space-2); }`
  `.psection__keys { margin-left:auto; display:flex; align-items:center; gap: var(--space-1); }`
  `.psection__meta { font-size: var(--text-subheadline); color: var(--text-secondary); margin: calc(-1 * var(--space-1)) 0 var(--space-2); display:flex; flex-wrap:wrap; gap: var(--space-3); }`
  Siblings that precede a section get their own bottom margin from their own rule (`.ptitle`, `.ptoolbar`, `.metrics`, `.note`), so nothing else is needed above.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] public void Renders_the_title_through_PanelHeader_at_h3()       // cut.Find("h3.panel-h").TextContent == "Recent games"
[Fact] public void Nested_sections_step_down_a_heading_rank()          // outer h3, inner cut.Find("section section h4.panel-h")
[Fact] public void Pins_keys_beside_the_rule()                         // .psection__keys button exists
[Fact] public void Renders_no_meta_row_without_meta()                  // Assert.Empty(.psection__meta)
[Fact] public void Prefers_TitleContent_over_Title()                   // TitleContent "<a>Journal</a>" → h3 a exists
[Fact] public void Merges_a_callers_class_and_style()
```
- [ ] **Step 2: Run, expect build failure.**
- [ ] **Step 3: Create `PanelSection.razor`**, add CSS, amend `PanelHeader.razor`'s header comment (two sentences: sections now space themselves; `Gap` remains for the callers not yet moved).
- [ ] **Step 4: Run tests, expect 6 passed.**
- [ ] **Step 5: Commit**
```bash
git commit -m "feat(desk): a section spaces itself, and its keys sit on its rule"
git push origin LineX_Development
```

---

### Task 4: `PanelForm`, MetricRow and Note frame margins, Parts bin Anatomy section

**Files:**
- Create: `src/LineOps.Desk/Primitives/PanelForm.razor`
- Modify: `lineops.css` (same block, plus one `.metrics` and one `.note` margin rule)
- Modify: `src/LineOps.Web/Components/Panels/PartsPanel.razor` (new first section, after the opening Note)
- Test: `tests/LineOps.Web.Tests/PanelFormTests.cs`

**Interfaces:**
- Produces: `PanelForm` with `string? Title`, `RenderFragment? ChildContent`, `RenderFragment? Footer` (Cancel then primary, in that order, written by the caller), `RenderFragment? Destructive`, `string? Class`, `string? Style`, splat `Extra`, and `string? Id` passed to the root (callers like BoardWager label regions by id).
- Markup:
  ```html
  <div class="pform [classes]" style="…" ...rest>
    <PanelHeader Tight Level="4">@Title</PanelHeader>   <!-- only when Title non-empty -->
    <div class="pform__body stack">@ChildContent</div>
    <div class="pform__foot">                            <!-- only when Footer or Destructive non-null -->
      <div class="pform__danger">@Destructive</div>      <!-- only when Destructive non-null -->
      <div class="pform__keys">@Footer</div>
    </div>
  </div>
  ```
- CSS: `.pform { padding: var(--space-3); background: var(--surface-2); border: 1px solid var(--separator); border-radius: var(--radius); margin: 0 0 var(--space-4); display:flex; flex-direction:column; gap: var(--space-2); }`
  `.pform__foot { display:flex; align-items:center; flex-wrap:wrap; gap: var(--space-2); margin-top: var(--space-1); }`
  `.pform__keys { margin-left:auto; display:flex; gap: var(--space-2); }`
  Frame margins: `.metrics { margin: 0 0 var(--space-4); }` (append to the existing `.metrics` rule) and `.win__body > .note { margin: 0 0 var(--space-4); }` — `.note` has no margin today and an inline `Note` renders a `span`, so the rule is scoped to a block note sitting directly in a window body. The space above a note that follows a toolbar or title comes from their own bottom margins; no extra rule.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] public void Renders_the_title_as_a_tight_h4_rule()
[Fact] public void Footer_pins_right_without_a_destructive_slot()   // Assert.Empty(.pform__danger); .pform__keys exists
[Fact] public void Destructive_sits_before_the_keys()               // children of .pform__foot in order: .pform__danger, .pform__keys
[Fact] public void Renders_no_footer_without_footer_or_destructive()
[Fact] public void Forwards_Id_to_the_root()                        // root.Id == "wager-form"
[Fact] public void Merges_a_callers_class_and_style()
```
- [ ] **Step 2: Run, expect build failure.**
- [ ] **Step 3: Create `PanelForm.razor`, add CSS, adjust `.metrics`/`.note` margins.**
- [ ] **Step 4: Add the Parts bin section.** Directly after the opening `<Note>` in `PartsPanel.razor`, a `<PanelSection Title="Anatomy">` whose body is: a one-paragraph `.empty` note naming the six slots in order; then a `PanelTitle` ("Mets at Braves", Tags: two `Tag`s, Meta: "Sat Oct 24 · 7:10 PM ET", Keys: a small `DeskSwitch` or two Plain `DeskButton`s); a `PanelToolbar` (a `DeskSelect`, a `DeskField` placeholder "Search…", Message "3 updated", Actions: a Plain key and one Filled "Sync stats"); a `MetricRow` of two `Metric`s; a nested `PanelSection Title="Recent games"` with Keys (a small Tinted key) and Meta ("12-9 · 8-10 ATS") and an `EmptyState` body; a `PanelForm Title="New entry"` with one `DeskField`, Destructive "Delete", Footer "Cancel" + Filled "Save changes". Remove the `PanelHeaderGap.Section` usages elsewhere in PartsPanel by wrapping each existing block in a `PanelSection` — optional in this task; required by Task 8.
- [ ] **Step 5: Build Web, run tests.** Expected: `dotnet build src/LineOps.Web` clean; PanelFormTests 6 passed; full `dotnet test tests/LineOps.Web.Tests` green.
- [ ] **Step 6: Browser check.** Stop the running web process, `preview_start` `lineops-web`, open Parts bin from the System menu, screenshot the Anatomy section at 1600 px wide. Confirm: title at 17px (`getComputedStyle` on `.ptitle__name`), Filled key at the toolbar's right edge, 24px between the two sections, form footer right-pinned.
- [ ] **Step 7: Commit**
```bash
git commit -m "feat(desk): an inset form with one footer order, and the anatomy shown in the Parts bin"
git push origin LineX_Development
```

---

### Task 5: Sweep — Ops, Runs, Players, Incidents (the toolbar rule)

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/OpsPanel.razor`, `RunsPanel.razor`, `PlayersPanel.razor`, `IncidentsPanel.razor`

**Interfaces:** Consumes Tasks 1–4.

- [ ] **Step 1: OpsPanel.** Replace the `div.row.row--end` with `<PanelToolbar Actions=…>` (operator only; render nothing when a visitor). Remove `Style="margin-bottom:16px"` from `MetricRow`. Wrap Odds feeds, Source health, Open alerts, On-call drill each in `<PanelSection Title="…">`, replacing the `PanelHeader` lines; the `div.row style="gap:8px; margin:0 0 10px"` at line ~129 becomes `<DeskRow Gap="DeskSpace.Space2" Style="margin-bottom:10px">` only if it is not already the first thing after a rule — otherwise drop the margin. The `div.stack style="margin-top:10px"` in On-call drill becomes a bare `<DeskStack>` (the section's head already spaces it).
- [ ] **Step 2: RunsPanel.** Order becomes `PanelToolbar` (two selects → Actions: Refresh) then the `Note` then the grid. Note loses `Style`.
- [ ] **Step 3: PlayersPanel.** `PanelToolbar`: Sport select, search field (drop its inline `style="flex:1; min-width:150px"` once the toolbar CSS from Task 2 covers it — verify in browser; keep the inline style if the selector did not match), Actions: Sync stats (operator only).
- [ ] **Step 4: IncidentsPanel.** List view: `MetricRow` loses its style. Detail view: the back/tag `div.row style="margin-bottom:10px"` becomes `<PanelToolbar>` with Back + tags as ChildContent, no Actions. `#id — title` stays a `PanelHeader` but inside `<PanelSection TitleContent=…>` whose body holds the runbook steps; Timeline and Root-cause analysis each become a `PanelSection`. The RCA block (two textareas, error note, keys) becomes a `PanelForm` with no Title, `Footer` = `[Mark mitigated (when Open)] [● Resolve incident]` — Cancel has no equivalent here, so Mark mitigated takes the Cancel position; `Destructive` unused. Remove `style="margin-top:10px"` on the stacks and `Style="margin:12px 0"` on the Notes.
- [ ] **Step 5: Build, test, browser.** Open Ops, Runs, Players, Incidents side by side at 1600 px; screenshot. Check: all four Filled/Plain actions sit in the right corner of the first row; metrics follow at 16px; no `style="margin` remains in the four files (`Select-String 'style="margin' OpsPanel.razor,RunsPanel.razor,PlayersPanel.razor,IncidentsPanel.razor` returns nothing).
- [ ] **Step 6: Commit**
```bash
git commit -m "refactor(panels): Ops, Runs, Players and Incidents open the same way"
git push origin LineX_Development
```

---

### Task 6: Sweep — Team, Player, Game, Head to head (titles)

**Files:**
- Modify: `TeamPanel.razor`, `PlayerPanel.razor`, `GamePanel.razor`, `HeadToHeadPanel.razor`

- [ ] **Step 1: TeamPanel.** Lines 31–53 become one `<PanelTitle>`: ChildContent `@_team.Name`; Tags: the two `Tag`s; Meta: the record sentence (drop the `span.dim style=…` wrapper); Keys: the season `DeskSwitch` (drop `style="margin-left:auto"`). "Recent games" becomes `<PanelSection Title="Recent games" Keys=… Meta=…>` — Keys: the split switch currently inside the section (find it between lines 56–88), Meta: the record/ATS/O-U/coverage spans (drop their font-size styles; the coverage span keeps `margin-left:auto`). "Roster form" becomes a `PanelSection`. `div.stack style="gap:4px; margin-bottom:18px"` → `<DeskStack Gap="DeskSpace.Space1">`.
- [ ] **Step 2: PlayerPanel.** Same shape: `PanelTitle` with name, Tags (position, season, team link), Meta (appearances sentence incl. the "for …" span), Keys (season switch). Averages and Game log → `PanelSection`.
- [ ] **Step 3: GamePanel.** `PanelTitle`: ChildContent = the away link / score / "at" / home link / score run; Tags = status, doubleheader, season tags; Meta = start time + Starters span (Starters' names in `.ptitle__em`); Keys = season switch then the Movement / Head to head `div.row data-glide` pair (keep `data-glide`, drop `style="gap:4px"` → `DeskRow Gap="DeskSpace.Space1"`). "Your bets on this game" → `<PanelSection TitleContent=…>` with the Journal link inside TitleContent; its stack → `<DeskStack Gap="DeskSpace.Space1">`. Leave the tabs and everything below them alone except: any `PanelHeader` inside tab bodies becomes a `PanelSection`, and rows at lines ~365–412 drop `margin-bottom`/`margin-top` styles in favour of `DeskRow Gap`/`DeskStack`.
- [ ] **Step 4: HeadToHeadPanel.** `PanelTitle`: pairing, Tags (meeting count), Meta (record + coverage; drop the `p.dim`). The coverage `Note` loses its style.
- [ ] **Step 5: Build, test, browser.** Open one Game, its Team and a Player from the Board; screenshot at 1600 px. Check title computed font-size is 17px × text scale, tags on the baseline, meta 12px secondary, season switch at the right edge.
- [ ] **Step 6: Commit**
```bash
git commit -m "refactor(panels): a game, a team, a player and a pairing are titled, not labelled"
git push origin LineX_Development
```

---

### Task 7: Sweep — Journal, BoardWager, Slip (forms)

**Files:**
- Modify: `JournalPanel.razor`, `BoardWager.razor`, `SlipPanel.razor`
- Test: `tests/LineOps.Web.Tests/JournalPanelTests.cs`, `BoardWagerTests.cs` (existing — run, and update any selector that pointed at the old `div.stack` card)

- [ ] **Step 1: JournalPanel.** Order becomes: `PanelToolbar` (ChildContent: Status, Placed within, Sport, Book; Message: `_message`; Actions: Open slip, Settle finished games, Log entry — Log entry last, Tinted as today), then the firewall `Note` (no Style), then the entry `PanelForm` (Title "New entry"/"Edit entry"; body = existing fields; Footer = `Cancel` then Filled `Save changes` — **swap the current order**), then entries, then `<PanelSection Title="Parlays">`. The second filter row merges into the toolbar. Row `style="justify-content:flex-end; gap:4px"` inside grid cells → `<DeskRow Justify="DeskJustify.End" Gap="DeskSpace.Space1">`.
Spec call 1 applies: BoardWager and Slip keep `.wsections`/`.wsection`/`.wgroup`; only the footer order changes there.

- [ ] **Step 2: BoardWager.** Leave the `.wsection` structure and the `PanelHeader Tight id=…` rules alone. Find the row holding the Filled key (line ~124) and make it `<DeskRow Justify="DeskJustify.End">` with any Plain key before the Filled one. Loading `div.stack … style="gap:10px"` → `<DeskStack Gap="DeskSpace.Space2" Class="wager-loading" aria-busy="true">`.
- [ ] **Step 3: SlipPanel.** Same: structure untouched; the row with the Filled key at ~149 becomes `<DeskRow Justify="DeskJustify.End">`, Plain keys first, Filled last. `div.row style="flex-wrap:wrap"` at line 20 → `<DeskRow>` (`.row` already wraps).
- [ ] **Step 4: Build, run `dotnet test tests/LineOps.Web.Tests`.** Fix selectors in `JournalPanelTests`/`BoardWagerTests` that targeted the removed card markup; assertions about behaviour must not change.
- [ ] **Step 5: Browser.** Open Journal, press Log entry; open Place wager from a Board row. Screenshot. Check footer order Cancel → Save at the right edge.
- [ ] **Step 6: Commit**
```bash
git commit -m "refactor(panels): every form ends the same way, with the primary at the right edge"
git push origin LineX_Development
```

---

### Task 8: Sweep — Board, Odds, Performance, History, Window manager, BoardAllBets, BoardForm, Parts bin

**Files:**
- Modify: `BoardPanel.razor`, `OddsPanel.razor`, `PerformancePanel.razor`, `HistoryPanel.razor`, `WindowManagerPanel.razor`, `BoardAllBets.razor`, `BoardForm.razor`, `PartsPanel.razor`

- [ ] **Step 1: Board.** The filter `div.row style="margin-bottom:12px"` → `PanelToolbar` (no Actions; the Pull menu stays where it is). `MetricRow` loses its style.
- [ ] **Step 2: Odds.** Filter row → `PanelToolbar`. "Current market" and "Recent form" → `PanelSection`. The hand-written `h4.panel-h` at ~200 → `<PanelHeader Level="4" Gap="PanelHeaderGap.Nested">` or, if it heads a block, a nested `PanelSection`. Row at ~129 drops its margin style.
- [ ] **Step 3: Performance.** Filter row → `PanelToolbar` (drop `style="margin:12px 0"`). `MetricRow` loses style. Every `PanelHeader` → `PanelSection`; Bankroll's `div.row style="margin-bottom:8px"` becomes the section's `Keys` if it holds controls, otherwise a `DeskRow`.
- [ ] **Step 4: History.** Sources, Coverage, Run, Seasons, Recent days → `PanelSection`s. Run's control row holds the Filled action, so it stays the first row of the section body as `<DeskRow Justify="DeskJustify.Between">`: the destructive re-walk key first (far left), then a `<DeskRow>` holding Tag/Stop/Gather with the Filled key last (spec call 4). Drop `style="margin-bottom:8px"`; stacks → `DeskStack Gap`.
- [ ] **Step 5: Window manager.** Access row → `PanelToolbar` (tag + sentence as ChildContent, the small Plain Sign in/out key as Actions — a toolbar may hold zero Filled keys, spec call 4). `MetricRow` and the two `Note`s lose their styles. Each `PanelHeader` + `div.row` pair → `PanelSection` with a `DeskRow` body; `div.row style="margin-top:10px"` → `DeskStack Gap="DeskSpace.Space2"` wrapping consecutive rows.
- [ ] **Step 6: BoardAllBets, BoardForm.** `PanelHeader` per market / per team → `PanelSection`; `BoardForm`'s `PanelHeader Level=4 Gap=Nested Muted` → nested `PanelSection` (Muted colour: pass `Class="psection--subject"` and add `.psection--subject .panel-h { color: var(--text-primary); }` to the block). `EmptyState Style="padding:6px 0 12px"` stays (it is EmptyState's own business). `div.row style="margin-bottom:10px"` → `PanelToolbar`.
- [ ] **Step 7: Parts bin.** Every remaining `PanelHeader Gap="PanelHeaderGap.Section"` block → `PanelSection`. No `PanelHeaderGap` usage remains anywhere in `src/LineOps.Web` (`Select-String PanelHeaderGap src/LineOps.Web -Recurse` is empty).
- [ ] **Step 8: Sweep gate.** `Select-String 'style="(margin|gap|font-size)' src/LineOps.Web/Components/Panels/*.razor` returns only lines that are inside grid cell templates or that this plan explicitly kept; list each remaining one in the commit body.
- [ ] **Step 9: Build all three projects, `dotnet test tests/LineOps.Web.Tests`, start the Worker (`dotnet run --project src/LineOps.Worker`, confirm it logs startup, stop it).**
- [ ] **Step 10: Browser.** Open Board, Line movement, Performance, History, Window manager; screenshot each. Confirm the toolbar is the first row in each and metrics follow at 16px.
- [ ] **Step 11: Commit**
```bash
git commit -m "refactor(panels): the rest of the desk opens in the one order"
git push origin LineX_Development
```

---

### Task 9: Record the decision

**Files:**
- Create: `docs/adr/0020-one-anatomy-for-every-window.md`
- Modify: `docs/DESIGN.md` (one line under the Desk entry in §2 naming the four components)

- [ ] **Step 1: Write the ADR** — Status Accepted; Context (the five symptoms from the spec's Intent); Decision (six slots, the four components, the footer order, the toolbar's right edge); Consequences (no inline spacing in panels; `PanelHeaderGap` is legacy; the Parts bin Anatomy section is the reference); What building it found (fill in from the sweeps — any cascade loss, any test selector that broke).
- [ ] **Step 2: Commit**
```bash
git commit -m "docs: ADR 0020 says why every window opens the same way"
git push origin LineX_Development
```
