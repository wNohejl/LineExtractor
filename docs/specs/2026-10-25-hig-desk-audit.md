# HIG audit of the desk — 25 October 2026

A walk of every window in the browser at 800px and 1280px, light and dark, plus a read of
`lineops.css`, `mud-bridge.css`, the Desk primitives and every panel, against the rulebook in
`~/.claude/skills/apple-hig-components` (SKILL.md, `references/audit.md`,
`type-color-layout.md`, `components.md`). Line numbers are for
`src/LineOps.Desk/wwwroot/css/lineops.css` at commit `43ecf49`; Razor paths are under `src/`.

## Rulings that shape the fixes

- **The wager window is out of scope.** Commit `e36c3b6` made it two bordered cards with a
  3px accent base, accent-filled segments and a bordered group inside the current step,
  "the reference the operator chose". Nothing in this audit touches `.whead`, `.wcard`,
  `.wsteps`, `.wstep*`, `.wbet*`, `.selection-gate`, `.wgroup` or `BoardWager.razor`.
- **One hairline edge on a group or card is the house rule** (audit.md, and the same commit:
  "every group and card on the desk carries a 1px hairline edge"). A single bordered tile is
  therefore fine. What is not fine is a bordered thing inside a bordered thing, vertical
  rules inside a table, and chips drawn as boxes inside cells.
- **Section headers lose their capitals.** The anatomy spec (2026-10-24) kept the uppercase
  section rule as a non-goal; the HIG skill, written the same week and maintained since,
  says "no all-caps eyebrows, no new sizes". The skill wins; the spec's non-goal is amended
  by this document.

## Seen in the browser

- **Command palette rows stagger diagonally.** MudBlazor's `button { justify-content:
  center }` reaches `.launcher__item`, which never sets `justify-content`, so each row's
  icon and text sit centred on their own width.
- **Game and Team windows show their name twice in the bar.** `GamePanel.razor:337` and
  `TeamPanel.razor:326` report the matchup / team name as the window status, and the window
  title is already that name. Status should be state: the game's phase and score, the team's
  record.
- **Player window column headers leak stat keys**: `C/ATT`, `passing.AVG`, `passing.YDS`
  as column titles and as the averages' labels.
- **Team window**: every recent game and every roster row is its own bordered box
  (`.logrow`), stacked. Roster rows show `— 5 GP` with a dash for every player.
- **Board**: all-caps tile labels; vertical rules between columns; book monograms and
  selection abbreviations as bordered chips inside cells; the one Filled key opens a menu.
- **Window manager**: a settings window whose sections are all-caps rules, with `−  2  +`
  as three loose keys and an inline "reset widths" button mid-sentence.
- **Every catalogue window**: all-caps section rules, all-caps metric labels, all-caps tags.

## 1. All-caps eyebrows (`text-transform: uppercase`)

| # | Class (lineops.css) | Used in | Fix |
|---|---|---|---|
| 1 | `.panel-h` 1860–1868 | `PanelHeader.razor:79`, every section via `PanelSection` | Sentence case, `--text-subheadline`, `--weight-semibold`, `--text-secondary`, no tracking. The pattern already exists at 3031–3040 (`.pform--grouped > .panel-h`) and `.wgroup__head` 4108–4114. |
| 1b | `.psection--subject .panel-h` 3010 | `IncidentsPanel.razor:104` draws the incident's own name as a section header | Use `PanelTitle` |
| 2 | `.metric__label` 1903–1909 (10.5px, `.1em`) | `Metric.razor:102` | Sentence case, `--text-footnote`, `--text-secondary`, no tracking |
| 3 | `.tag` 2599–2613 (caption, `.07em`) | `Tag.razor:42`; hand-written at `HeadToHeadPanel.razor:98,121,138` | Sentence-case capsule, `--text-footnote`, `--weight-medium`, no tracking. Re-check the 29px row note at 2586–2598. |
| 3b | `.mud-chip` `mud-bridge.css:471–483` | MudChips | Change with `.tag` |
| 4 | `.bar__group-name` 668–678 | `WindowBar.razor:85`, the nav group keys | Title-case nouns, `--text-subheadline`, `--text-secondary` |
| 5 | `.launcher__group` 1794–1801 (10px, `.13em`) | `CommandPalette.razor:39`; `DeskHeader.razor:42,57,71` | Sentence case, `--text-footnote`, semibold, secondary |
| 6 | `.deskdialog__title` 2325–2332 | `DeskDialog.razor:30` | Match `.win__title` 1436–1444 |
| 7 | `.snippet__title` 3436–3441 | `SnippetShell.razor:22` | Sentence case, `--text-footnote` semibold |
| 8 | `.snippet__market-name` 3472–3478 | `Snippets/OddsSnippet.razor:90` | Sentence case, secondary |
| 9 | `.fact__label` 3526–3532 | `PlayerPanel.razor:80`; `GameResultSnippet.razor` | Sentence case, footnote, secondary |
| 10 | `.board__actions-label` 2254–2261 | `RowActions.razor:30`, no caller passes `Label` | Delete |
| 11 | `.pullmenu__cost` 2473–2480 | `Desk/PullMenu.razor:45` | Sentence case, footnote, secondary |
| 12 | `.desklink__shortcut` 2530–2542 (9.5px, accent) | `DeskLink.razor:24` | Sentence case, `--text-footnote` |
| 13 | `.ftr__flag` 1199–1205 (9.5px, accent) | `DeskFooter.razor:45` | Plain `.ftr__stat` style |
| 14 | `.stake-row__returns-label` 4194–4200 | `Desk/StakeRow.razor:26` | Sentence case base rule; drop the 4539 override only if it then does nothing |
| 15 | `.launcher__open` 1837–1844 | dead | Delete |

Leave the brand wordmark (`.hdr__brand`, `.desk-empty__mark`): a logotype, not an eyebrow.
Tracked-but-not-uppercase: `.ftr__stat` 1195, `.price__edge` 2105, `.price__ev` 2123,
`.value__bet` 2149, `.price__sel` 3681, `.price__book` 2056 — drop the letter-spacing.
Already correct: `.grid th`, `.tabs__tab`, `.field-lbl`, `.wgroup__head`.

## 2. Boxes

Acceptable (one hairline edge on a group or card): `.metric` 1883, `.pform` 3015, the
floating layers, input wells, keycaps, control tracks.

| Line | Class | Problem | Fix |
|---|---|---|---|
| 3108–3114, 3155 | `.runbook` (border + 2px accent left edge) > `.runbook__step` vertical rule | box with rules inside a section | fill only, no accent edge, indent steps without a rule |
| 3416–3422 | `.snippet` (border) inside a DeskGrid row, holding `.grid--tight` and bordered `.price__sel` chips | box in a ruled table | fill only (`--surface-2`), no border |
| 3448–3465 | `.snippet__open` border-on-hover | outlined button | plain hover fill `--surface-3` |
| 3557–3571 | `.splits__key` | dead | delete |
| 3581–3588, 3632–3636 | `.logrow` (`TeamPanel.razor:81,147`) | each row its own bordered box | one filled group per list with one outer hairline; rows divided by inset hairlines; chevron kept |
| 3678–3688 | `.price__sel` bordered chip | boxes in cells | plain `--text-secondary` text |
| 2053–2061 | `.price__book` accent-wash border + accent text | boxes in cells, accent misuse | plain footnote in `--text-secondary` |
| 3733–3740 | `.deskgrid--board` td/th 3,5,7 `border-left` | vertical rules | group market pairs with extra left padding (`--space-4`) |
| 1208–1220, 1235–1245 | `.ftr__refusal` (border + wash) > `.ftr__undo` (`border: 1px solid currentColor`) | box in box | keep the wash, drop both borders; Undo is a plain button |
| 3517 | `.formrun__latest` accent ring | accent on a tag | ring in `--separator-strong` |

## 3. Accent on non-interactive things; prominence

| Line | Rule | Fix |
|---|---|---|
| 1901 | `.metric--info .metric__value { color: var(--accent) }` | `--text-primary` |
| 2618 | `.tag--info` accent wash + text | neutral capsule: `--surface-3` fill, `--text-secondary` text |
| 1200 | `.ftr__flag` | secondary |
| 2057–2058 | `.price__book` | secondary (see §2) |
| 3110 | `.runbook` accent edge | remove (see §2) |
| 3517 | `.formrun__latest` | see §2 |
| `PartsPanel.razor:717` | demo bars in accent | `--text-tertiary` |

Correct accent uses stay: focus rings, `.tabs__marble`, `.bar__key--current`,
`.palette__item--active`, `.book-row--chosen`, `.prog__fill`, link hover, Filled/Tinted
buttons, `.rail__tick--best`.

Prominence: Board's one Filled key is `Desk/PullMenu.razor:23`, which opens a menu, not an
action — Tinted with a chevron. Parts bin has five Filled keys; it is a specimen sheet and
stays.

## 4. Type ramp

Ladder (lines 100–108): 26 / 22 / 17 / 15 / 13 / 13 / 12 / 11 / 10.

- ~50 `calc(Npx * var(--text-scale))` declarations instead of a token, at lines 1192, 1201,
  1217, 1765, 1772, 1781, 1795, 1816, 1825, 1832, 1838, 1892, 1904, 1911, 1938, 2042, 2048,
  2054, 2104, 2122, 2142, 2148, 2212, 2255, 2338, 2470, 2476, 2486, 2537, 3120, 3144, 3166,
  3273, 3277, 3431, 3443, 3457, 3473, 3486, 3493, 3500, 3527, 3534, 3539, 3563, 3571, 3600,
  3610, 3650, 3679. Off-ladder sizes: 9, 9.5, 10.5, 11.5, 12.5, 16 (`.palette__field`),
  19 (`WindowManagerPanel.razor:161`). Snap to the nearest rung; nothing under
  `--text-caption`.
- Inline `font-size` in panels: `BoardAllBets:47`, `BoardForm:29`, `BoardPanel:221`,
  `GamePanel:373,380,387,411,415,454,507`, `HistoryPanel:37,38`,
  `IncidentsPanel:116,119,149,150`, `JournalPanel:151,257,261`, `OddsPanel:133`,
  `OpsPanel:143,160`, `PlayersPanel:93`, `WindowManagerPanel:134,139,161,195,231`.
  The recurring `span.dim` at `calc(11.5px …)` becomes one class, `.meta`
  (`--text-subheadline`, `--text-secondary`).
- Numeric weights at 1291, 1796, 2055, 2143, 2159, 2256, 2538, 3121 → weight tokens.
  `--weight-bold` is used at 4311 but undefined; `--space-5` is used at 4303, 4328, 4344
  but undefined. Define both (`--weight-bold: 700`, `--space-5: 20px`) in every theme block
  that defines the others.
- Tabular figures missing on `.book-row__price`, `.book-row__line` (4247–4248),
  `.metric__note`.

## 5. Hit regions and rhythm

- Under 28px: `.desk-btn--sm` 22px (2743; toolbar uses at `BoardPanel.razor:68`,
  `WindowManagerPanel.razor:33,40`); `.gate__opt` 24px (990) and 20px for `gate--sm` (1014;
  `JournalPanel.razor:28,30`, `TeamPanel.razor:189`); `.tabs__tab` 26px (1064);
  `.win__nav-btn` 24px (1399); `.desk-sheet__close` 24px (3826); ~18px `.snippet__open`,
  `.ftr__dismiss` 1225, `.ftr__undo`. Inline `height:24px` at
  `JournalPanel.razor:205,214,218,220,298,309`; `height:22px` at
  `WindowManagerPanel.razor:149,206,280`.
- Rule: a control in a toolbar or a form row is 28px; a key inside a 29px grid row may be
  24px; nothing is 22px or less.
- Off-grid paddings: `.metric` 11/13 (1884); `.ftr` 10 (1177–1179); `.ftr__refusal` 7/3/9;
  `.launcher__item` gap 11, padding 8/10; grid cells 10px (1960, 1970, `mud-bridge.css:175`);
  `.snippet__bar` 7/6/9; `.logrow__head` 9/7/10; `.runbook` 14, 8/11; `.board__actions`
  9/10/11; `.deskdialog__body` 14; snippet gaps 18/22/20 (3470, 3496, 3522); `.empty` 14
  (3081). Snap to `--space-*`.
- Razor: `p.empty style="padding:0 0 9px"` / `"padding:9px 0 0"` ~30× in PartsPanel,
  `OpsPanel:60,81,107`, `HistoryPanel:72`; `padding-top:6px` at
  `WindowManagerPanel:86,143,170,202,269`; `GamePanel:44,46,53`; `TeamPanel:114,162`;
  `BoardAllBets:41,47`; `Note Style=` in Board 365, Game 371, Odds 93, Slip 35/54, Window
  manager, Parts.

## 6. The wrong control for the job

- `RowActions.razor:37–55` mixes selection (snippet keys with `aria-pressed`) and actions
  (open a window) in one strip → a `DeskSwitch` for the inline view, then the action keys.
- `BoardPanel.razor:68–75` "+EV · n" is an on/off drawn as a Plain/Tinted swap → a checkbox
  "Only +EV (n)" at toolbar height.
- `WindowManagerPanel.razor:98–122` theme swatches: tiles are fine, but they need
  `role="radiogroup"` and `role="radio"` / `aria-checked`.
- `WindowManagerPanel.razor:156–166` "Concurrent windows" `− 2 +` → a numeric `DeskField`
  with the stepper beside it, "Match this desk (2)" as a Plain key after.
- `Desk/PullMenu.razor:23` Filled menu trigger → Tinted, with a chevron.

## 7. Empty, loading and error states

- No load-error state anywhere: `PanelBase.WithActivityAsync` reports "load failed" to the
  pulse and rethrows. `EmptyStateKind.Error` exists but only Parts uses it.
- No loading line in Board, Journal, Performance, Ops, Incidents, Runs, Players, History,
  Window manager; Board shows its New state before the first load. `DeskGrid` has `Loading`
  and skeleton support, unused by real panels.
- Raw exception text reaches the operator: `SlipPanel.razor:351`, `BoardWager.razor:1029`
  (out of scope), `IncidentsPanel.razor:348`; `PlayersPanel.razor:219` puts the exception
  type name in the pulse.
- RCA textareas (`IncidentsPanel.razor:164–170`) have placeholders but no labels.

## 8. Anatomy drift (spec 2026-10-24)

- Note after metrics in Board (122) and Window manager (64, 70); the spec's order is note,
  then metrics.
- Incidents detail subject drawn as a section header (`IncidentsPanel.razor:104`).
- Head to head has no "Meetings" section rule; Runs has no section.
- Stale comment at `.metric--action` 1913–1917 (retired ADR 0008 gloss).
