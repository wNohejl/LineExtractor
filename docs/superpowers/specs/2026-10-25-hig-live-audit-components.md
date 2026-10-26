# HIG live audit — Components — 25 October 2026

A read of Apple's live Human Interface Guidelines (the `.md` rendering of each page, fetched
25 October 2026) against the desk's primitives, windowing, desk-level components, their CSS
and the panels that use them. Components only; Foundations and Patterns are a sibling audit.

Scope rules carried in, not re-litigated: the wager window (`BoardWager.razor`, `.w*`) is out
of scope; one 1px hairline edge on a group or card is the house rule; no outlined or nested
boxes; macOS density (13px body, 28px controls); one accent for interaction; sentence case.

Paths: CSS lines are `src/LineOps.Desk/wwwroot/css/lineops.css` unless `mud-bridge.css` is
named; Razor paths are under `src/`. Commit at read: `195e41e`.

Group indexes read: Components, Layout and organization, Menus and actions, Navigation and search,
Presentation, Selection and input, Status. Their children not audited below are touch, watch,
vision or system pages (activity rings, page controls, ornaments, quick actions, dock menus…).

## The earlier audit (2026-10-25-hig-desk-audit.md), verified

Done and not repeated here: §1 all-caps (only the brand wordmark `.hdr__brand` 470/494 and
`.desk-empty__mark` 1299 keep `uppercase`, as ruled); §2 boxes (`baa1222`); §3 accent
(`0da1038`); §4 the ladder inside `lineops.css` (no raw `calc(Npx …)` outside the token block);
§5 28px controls (`61ee7db`, `c380976`); §6 every wrong-control row (`266deba`: the +EV
checkbox, the count field with a stepper, the Tinted pull menu; `RowActions` view switch;
theme swatches are a `radiogroup`, `WindowManagerPanel.razor:102`); §7 load failure (`e2d029e`).

Still open from it:
- "an inline reset widths button mid-sentence" — still there, `WindowManagerPanel.razor:215–221`,
  and a second one, "Reset to 100%", inside a paragraph at `:158`.
- §4 never reached `mud-bridge.css`: `.mud-dialog-title` tracks `.05em` (395), `.mud-tooltip`
  `.02em` (425), `.mud-chart text` 10px raw (548), `.deskchart__headline` 12.5px / 600 (575),
  the chart legend 10.5px tracked `.04em` (594–599), `.mud-list-item` raw 13px (132).

---

## Buttons

Change log: Dec 16 2025 (Liquid Glass), Jun 9 2025 (styles and content).

- A prominent style for the most likely action; one or two prominent buttons per view.
- Use style, not size, to mark the preferred choice; equal sizes in a set.
- Always a press state on a custom button.
- Primary role = the Return-key button; never primary on a destructive action.
- Append an ellipsis when a push button opens another window or view that asks for input.
- macOS tooltip on hover explains an icon button; text label when a word is clearer.
- Image/square buttons: in a view, not the window frame; about 10px padding round the image.
- One help button per window, in a lower corner.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| One prominent per view | Met | `DeskEmphasis` Plain/Tinted/Filled, `.desk-btn--filled` 2689; Board's Filled went Tinted (`BoardPanel.razor:72–77`) | — |
| Style not size | Met | one height 28px `.desk-btn` 2618; `--sm` 24px only inside grid rows (2725), 28px in toolbars (2727) | — |
| Press state | Met | `.desk-btn:active … transform: scale(.97)` | — |
| Destructive never primary | Met | `DeskAlert.razor:63–67` focuses Cancel when `Destructive` | — |
| Ellipsis when it opens input | Partly | "Settle…", "Book…", "Choose…" are select placeholders (`JournalPanel.razor:219,707`, `SlipPanel.razor:154`), where an ellipsis means nothing; keys that open an edit form carry none (`JournalPanel.razor` Edit icon key, Title "Edit this entry") | Keep ellipses off placeholders ("Result", "Book"), put one on any text key that opens a form or sheet. |
| Icon buttons explained | Met | `DeskButton.Title` "required" for icon-only (`DeskButton.razor:43`), squared by `.desk-btn--icon` 2733 | — |
| Icon-as-child loses the square | Not met | the stepper keys pass `<Glyph>` as ChildContent, not `Icon=`, so `desk-btn--icon` never applies and they are 12px-padded rectangles (`WindowManagerPanel.razor:171–178`) | Pass `Icon=` (or build the Stepper below). |
| Keys in prose | Not met | `DeskButton` inside `<p class="empty">` at `WindowManagerPanel.razor:158,219–221` | Move each to the end of the row it acts on, after the control. |
| Busy feedback | Partly | sheen over the label (`.desk-btn--busy::after`); Pull data relabels to the job name (`PullMenu.razor:32`) | Keep; add a 12px spinner glyph before the label in `--busy` so the state is not only a moving gradient. |

## Pull-down buttons

Change log: Sep 14 2022 (menu length).

- For commands directly related to the button's purpose; a list of states is a pop-up instead.
- Don't put all of a view's actions in one; three or more items makes it worthwhile.
- Title only if it adds meaning; destructive items red and confirmed.
- Icons after the label only when they add value.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Commands, not states | Met | `PullMenu.razor` lists jobs ("Pull data") | — |
| Trigger looks like a menu | Met | Tinted + `ExpandMore` chevron (`PullMenu.razor:26–33`) | — |
| ≥3 items | Met | `IngestionJobs.DescribeAsync` jobs | — |
| Keyboard and semantics | Not met | items are bare `<button>`s, no `role="menuitem"`, no arrow keys, no Escape handler, focus stays on the trigger (`PullMenu.razor:40–62`) | Build on the Menu primitive proposed under Missing components. |
| Unavailable items say why | Met | blocked item keeps its label in tertiary and the reason in warning (`.pullmenu__item--blocked` 2469–2473) | — |

## Pop-up buttons

Change log: Oct 24 2023 (artwork), Sep 14 2022.

- A flat list of mutually exclusive options; actions, multi-select or submenus → pull-down.
- A useful default selection; the button shows the current choice.
- Predictable without opening: an introductory label or a self-describing value.
- Space-efficient when options needn't all be visible.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| States, not commands | Partly | `DeskSelect` is a native `<select>` (correct); but "Settle…" commits a result the instant it changes and resets to the placeholder (`JournalPanel.razor:217–224,310–317`) — a command menu wearing a pop-up | Settle is a pull-down ("Settle" ▾ → Win/Loss/Push/Void), or a four-segment control in the edit form. |
| Shows current choice / default | Met | `Value=` bound everywhere (`BoardPanel.razor:37`, `PerformancePanel.razor:45–79`) | — |
| Predictable | Met | "All sports", "All books", "vs anyone" read as values | — |
| Right control for two leagues | Not met | sport filters are pop-ups of 3 fixed values — All/MLB/NFL — in Board (`BoardPanel.razor:37`), Journal (`:32`), Performance (`:45`), Players (`:20`); CLAUDE.md fixes the scope at two leagues | A `DeskSwitch` (All · MLB · NFL) when `Sports.Enabled` ≤ 4, the select only past that. |
| Own pop-up control | Partly | `BookPicker.razor` is a hand-built pop-up with check marks (good), Escape (good), no arrow keys, `role="listbox"` over `<button>`s that Tab one by one (49–70) | Arrow keys + `aria-activedescendant` (as `GamePicker` already does), one Tab stop. |

## Menus

Change log: Jun 8 2026 (menu-item icons), Dec 16 2025, Jul 28 2025, Jun 10 2024.

- Verb-first labels; remove articles; ellipsis when more input is needed.
- Show unavailable items dimmed; the menu itself stays openable.
- Icons sparingly and uniformly — all items in a group or none (June 2026).
- Group related items with separators; most-used first; mind the length.
- Submenus sparingly, one level, about five items.
- Toggled items: a checkmark, or a label that states the change.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Verb-first labels | Partly | workspace menu items are nouns (workspace names) — right, they are choices; "Close all" is a verb (`DeskHeader.razor:72–75`) | — |
| Icons uniform per group | Not met | every workspace row repeats the same `Dashboard` glyph and every saved one `Bookmark` (`DeskHeader.razor:47,62`) — an icon that does not tell items apart; the June 2026 note says don't show an icon you can't make meaningful | Drop the per-row glyph; keep one only where items differ (window icons in the window bar are fine). |
| Group with separators | Partly | groups are labelled headings (`.launcher__group` 1799), no separator line between Workspaces / Saved here / Clear | A hairline between groups; keep the heading only where it names something ("Saved here"). |
| Destructive last, set apart | Met | "Close all" in its own last group, undoable through Back (`DeskHeader.razor:71`) | — |
| Keyboard | Not met | `RailMenu` declares `role="menu"` but moves no focus into the panel and has no arrow keys, only Escape (`RailMenu.razor:43–48,76–80`) | One Menu primitive: focus first item on open, Up/Down/Home/End, Enter, Escape returns focus to the trigger. |
| One highlight language | Partly | menu hover is accent wash + accent text in `PullMenu` (2419) and `BookPicker` (4652), `--surface-3` in `RailMenu`/palette (1828) | Pick one for every menu (macOS: accent fill, `--on-accent` text); the palette, a list, keeps surface-3. |

## Context menus

Change log: Dec 5 2023 (hide unavailable items), Jun 21 2023, Sep 14 2022.

- Only the commands most relevant to the item; a small number; ~3 groups max.
- Every item also available in the main interface.
- Hide unavailable items (don't dim); no keyboard shortcuts shown.
- Submenus one level; destructive items at the end.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Context menus anywhere | No such component | no `oncontextmenu` handler in `src/` outside vendored Bootstrap | Optional: a right-click menu on board rows and window bars listing the `RowActions` commands — only after the Menu primitive exists. |
| Items also in main UI | N/A | — | If added, mirror `RowActions` exactly; never context-only commands. |

## Toolbars

Change log: Dec 16 2025 (Liquid Glass), Jun 9 2025 (grouping, symbols).

- Choose items deliberately; define what overflows as the view narrows; don't overflow by default.
- Leading: navigation/title; centre: common controls; trailing: important items, search, the primary.
- At most three groups; keep text-labelled actions apart from symbol actions.
- Prefer symbols over text except where a symbol is unclear; no bordered symbols.
- One prominent (`.prominent`) action, trailing.
- Reduce custom toolbar backgrounds and tinted controls.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Primary trailing | Met | `.ptoolbar__actions { margin-left: auto }` 3108; anatomy spec "The Filled one is always last" | — |
| Overflow by design | Not met | `.ptoolbar` wraps (`flex-wrap: wrap` 3088); a 400px window stacks pickers, field, message and keys on 3–4 lines (container rule 520px) instead of collapsing | Overflow, don't wrap: at the container's narrow width move secondary pickers into a "More" pull-down (ellipsis glyph) at the trailing edge. |
| ≤3 groups | Partly | Board: sport select, horizon switch, state switch, team field, +EV checkbox, Refresh, Pull data (`BoardPanel.razor:35–78`) = 7 controls, no grouping beyond the action slot | Group into scope (sport, horizon, state) · search · actions, with `--space-4` between groups and `--space-2` within. |
| Symbols, not text | Partly | "Refresh" is icon + text beside "Pull data" icon + text + chevron (`BoardPanel.razor:72–77`) | Refresh becomes an icon key with Title "Refresh (R)"; Pull data keeps its text. |
| No tinted chrome | Met | toolbar sits on the body, no fill | — |
| Search trailing | Not met | the Board team filter sits mid-row, before the checkbox (`BoardPanel.razor:61–66`) | Put the search field last in the content group, nearest the actions. |

## Toggles

Change log: Mar 29 2024 (macOS switches, checkbox titles, radio art), Sep 12 2023.

- Two opposing values that affect state; not for picking from a list.
- State obvious by shape, never colour alone.
- macOS: switches, checkboxes, radios in the window body, not the frame (toolbar, status bar).
- Checkbox for a single setting and for hierarchies (indent, mixed state); switch to emphasise a group; mini switch in a grouped form row.
- Radio buttons for 2–5 exclusive options; more → pop-up.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Shape carries state | Met | tick drawn in `.checkbox__input:checked + .checkbox__box::after` | — |
| Disabled reads disabled | Partly | only the box dims (`opacity: .40` 2954); the label stays `--text-primary` | Dim `.checkbox__label` to `--text-tertiary` when the input is disabled. |
| Mixed state | Not met | `DeskCheckbox` has no `Indeterminate` parameter (`DeskCheckbox.razor:31–43`) | Add `Mixed`, set `indeterminate` via JS, draw a dash. |
| Toggle-by-tint | Partly | quick stakes mark the chosen one by Tinted vs Plain with `aria-pressed` (`StakeRow.razor:22–24`); the single-view row key uses `desk-btn--on` (`RowActions.razor:63`) — colour as state | Quick values are actions that set a field: leave them Plain; the field shows the amount. For the row key, add the chevron that the disclosure rows use. |
| Body, not frame | Met | no toggles in `AppWindow`'s bar | — |
| Switch (iOS-style) | No such component | none — correct for the desk | — |

## Segmented controls

Change log: Jun 21 2023.

- Closely related choices that affect an object, state or view.
- Don't mix action segments and selection segments.
- About five to seven segments in a wide interface; equal widths.
- Text or images, not both; nouns, similar length.
- macOS: tooltips per segment; view switching in the main window is a tab view.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Selection only | Met | `RowActions` split views (switch) from commands (keys) with a gap (`RowActions.razor:38–89`) | — |
| No selection when none matches | Met | `SelectedIndex` −1 hides the cap (`DeskSwitch.razor:91–103`) | — |
| Can return to none | Not met | the inline-view switch opens a snippet but a second click on the lit segment returns early (`DeskSwitch.razor:163–164`), so pointer users can only close it with Escape | Give `RowActions`' snippet its own close key in `SnippetShell`'s bar, or let `DeskSwitch` accept `AllowNone`. |
| Bounded count | Partly | season switches are built from data with no cap in Team and Player (`TeamPanel.razor:318–333`, `PlayerPanel.razor:219–226`); Game caps at 3 (`GamePanel.razor:301`) | `Take(4)` newest seasons, older behind a pop-up "Earlier…". |
| Closely related | Partly | Team splits mix venue and recency in one control — All · Home · Away · Last 5 · Last 10 (`TeamPanel.razor:250–264`, labels `GameLogSplits.cs:30–37`) | Two controls: Venue (All · Home · Away) and Range (Season · Last 10 · Last 5). |
| Equal widths, tabular figures | Met | `grid-template-columns: repeat(var(--n), 1fr)` 961; `--num` 1013 | — |
| Tooltips | Partly | `DeskSwitchOption.Title` exists, set only for seasons (`GamePanel.razor:309`) | Fine for text segments; require Title only if a segment ever carries an icon. |

## Steppers

Change log: none listed.

- Sits beside a field showing the value; pair with a text field for large changes.
- macOS: Shift-click changes by a larger increment.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Beside a field | Met | count field then − + (`WindowManagerPanel.razor:168–178`) | — |
| Reads as one control | Not met | two separate Plain rectangles with a gap (`--space-2` from `.row`), not a two-segment stepper | Build `DeskStepper` (below): one 28px capsule, two 14px-tall halves, ▲/▼ or −/+. |
| Arrow keys | Not met | the field is `type="number"` with spinners hidden (`.field--count` 2839) but Up/Down do nothing custom; fine natively | Keep native Up/Down; add PageUp/PageDown or Shift-click ×5 only if a range grows. |

## Sliders

Change log: Jun 21 2023.

- Min leading, max trailing; consider a field and stepper beside it.
- Live feedback as the value changes.
- macOS: a label introduces it (sentence case, colon); tick marks for discrete values; label min and max; tooltip with the value.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A slider primitive | No such component | a raw `<input type="range">` with inline `style="flex:1; max-width:240px; accent-color: var(--accent)"` (`WindowManagerPanel.razor:209–212`) | `DeskSlider`: track, value read-out, `step` ticks, min/max labels, `aria-valuetext`. |
| Labelled | Partly | `<span class="meta" style="min-width:70px">Share</span>` is not a `<label for>`; no accessible name on the input (`:208`) | `<label>` bound to the input. |
| Live feedback, value shown | Met | `@oninput`, `NN%` beside it (`:211–213`) | — |
| Ticks / range labels | Not met | step 5 between 20 and 80, no ticks, no 20%/80% labels | `list` + `<datalist>` ticks, labels at the ends. |

## Lists and tables

Change log: Jun 21 2023, Jun 5 2023.

- Text in rows; succinct item text; truncate thoughtfully (middle ellipsis can help).
- Selection feedback: navigation tables keep the row lit; option tables mark it.
- Column headings: nouns, no end punctuation.
- macOS: click a heading to sort, click again to reverse; resizable columns; consider alternating rows in a wide table; outline view for hierarchy.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Sort on header | Met | `SortMode.Multiple` (`DeskGrid.razor:58`), arrow hung in padding (`mud-bridge.css:307–321`) | — |
| Resizable columns | Not met | `MudDataGrid` without `ColumnResizeMode` (`DeskGrid.razor:51–67`) | `ColumnResizeMode="ResizeMode.Column"` on wide grids (Board, Journal, Performance), a 1px handle in `--separator-strong`. |
| Alternating rows | Not met | `Striped="false"`, no `nth-child(even)` rule in either stylesheet | Optional `Striped` on grids wider than ~8 columns, `--surface-2` at 50% on even rows. |
| Selection kept | Met | `.deskgrid--clickable .mud-table-row.board__row--open` (`mud-bridge.css:381`) | — |
| Truncation keeps meaning | Partly | `.trunc` end-ellipsis with full text in `title` (1963–1971) | Middle-truncate matchups ("Pittsburgh … Baltimore") in the narrow-window rule. |
| Headings | Met | after `cfb49d1` stat columns carry names | — |
| Rows as disclosure | Partly | Team log rows expand with a trailing ▾ that rotates to ▴ (`TeamPanel.razor:111`, `.logrow__chev` 3756/3762) | See Disclosure controls. |

## Disclosure controls

Change log: none listed.

- Hide details until relevant.
- Disclosure triangle: leading edge, points inward (›) when hidden, down when shown; a descriptive label.
- Disclosure button (under a control): points down when hidden, up when shown; one per view; near its content.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A disclosure primitive | No such component | three improvisations: `<details>` with the marker hidden and a book glyph in its place (`RunbookSteps.razor:20–23`, `.runbook__summary::-webkit-details-marker { display: none }` 3268); Team rows' trailing rotating chevron; `DeskStep`'s folded summary with a "change" link | `DeskDisclosure`: a leading 12px triangle (›, rotating 90° down), label, `aria-expanded`, used by all three. |
| State visible | Not met | the runbook shows no open/closed state at all (marker hidden, static `MenuBook` icon) | Leading triangle that rotates. |
| Correct direction | Partly | row chevrons point down-then-up — the disclosure *button* idiom — on list rows, where the HIG asks for a leading triangle | Leading triangle on list rows; keep down/up only for a single "More options" button under a form. |

## Boxes

Change log: none listed.

- Keep a box small relative to its view.
- Padding and alignment for subgroups inside; no nested boxes.
- Optional title: brief, sentence case, above the box on macOS.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| One hairline, no nesting | Met | `.pform` 3159 one edge; `.pform--grouped` drops it (3173); `.snippet` fill only (3555) | — |
| Small relative to view | Partly | `.pform` spans the whole window body; `.pform--grouped` caps at 560px but the plain form does not | `max-width: 560px` on `.pform` as on the grouped one. |
| Metric tiles | Met | `.metric` one hairline (1878); house rule | — |
| Title above, sentence case | Met | `PanelForm` title as `PanelHeader Tight` | — |

## Labels

Change log: Jun 5 2023.

- Labels for small uneditable text; four label levels carry importance.
- Tertiary = an unavailable item; quaternary = watermark.
- Make useful label text (errors, ids) selectable.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Levels | Met | `--text-primary/secondary/tertiary` (tokens 37–39) | — |
| Tertiary only for unavailable/annotation | Not met | chart takeaways — the summary the Charts page calls essential — are `--text-tertiary` (`mud-bridge.css:582–585`); `.launcher__desc` (1831) and `.metric__note` (1903) too; `.grid .quiet` notes tertiary measures 2.70:1 | Takeaway and descriptions in `--text-secondary`. |
| Selectable | Met | no `user-select: none` on content; only drag handles, checkbox, takes (`lineops.css:678,2291,2907,4688`) | — |
| No quaternary token | Partly | three levels only | Add `--text-quaternary` (≈ .18) for watermarks (`.desk-empty__mark` uses `--surface-3` instead, 1297). |

## Split views

Change log: Jun 9 2025, Dec 5 2023, Jun 5 2023.

- Persistently highlight the selection in each pane leading to the detail.
- macOS: draggable dividers, thin (1pt) style, sensible min/max sizes so the divider stays visible.
- Let people hide a pane and offer more than one way back (button, menu, shortcut).

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Panes resize by divider | Met | the desk row of windows with dividers (`--z-divider`, `body.splitting`) and `data-min-width` per window (`AppWindow.razor:19`) | — |
| Min sizes | Met | `Window.Definition.MinWidth` | — |
| Hide and restore two ways | Met | Minimise in the bar, restore from the window bar menu, Back (Alt+Left) | — |
| Selection highlighted | Met | `.bar__key--current` (701) | — |

## Tab views

Change log: Jun 5 2023.

- Closely related panes; controls in a pane affect only that pane.
- Nouns as labels; no more than six tabs; never a pop-up to switch tabs.
- Tabbed control on the top edge; inset the tab view from the window body.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| ≤6, nouns | Met | Game, Journal, Wager tabs 2–4 each (`GamePanel.razor:208`, `JournalPanel.razor:344`) | — |
| Hidden when one | Met | `DeskTabs.razor:27,66–71` | — |
| One focus language | Not met | `.tabs__tab:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px }` (1089), same for `.launcher__item` (1829), `.pullmenu__item` (2420), `.book-row` (4369) — every other control uses `--focus-ring` | `box-shadow: var(--focus-ring)` on all four. |
| Count in label | Partly | "Slip · 2" (`BoardWager.razor:489`) — a count in the noun | Out of scope here (wager); elsewhere use a count badge (Tab bars). |
| Spacing token | Partly | `.tabs { margin-bottom: 12px }` raw (1048) | `var(--space-3)`. |

## Sidebars

Change log: Jun 8 2026 (icon colours), Jun 9 2025, Aug 6 2024.

- Navigate top-level areas; let people hide it; two levels max; familiar symbols.
- Icon colours serve a purpose; follow the accent unless a fixed colour means something.
- Don't put critical actions at the bottom.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A sidebar | No such component | navigation is the horizontal window bar with group drawers (`WindowBar.razor`) — the right call for a row-of-windows desk | — |
| Icons tinted on purpose | Met | bar glyphs neutral (`.bar__key`), accent only on current (701) | — |

## Search fields

Change log: Jun 8 2026, Jun 9 2025 (tokens, placement), Sep 12 2023.

- A search icon, a clear button and placeholder text describing what can be found.
- Search as people type; suggest recent or predicted terms.
- macOS: trailing side of the toolbar for common use; top of a sidebar when it filters it.
- Scope bars and tokens to narrow.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A search primitive | No such component | the Board team filter is a plain `DeskField` with placeholder "Team", no glyph, no clear, `style="max-width:170px"` (`BoardPanel.razor:61–62`) | `DeskSearch`: `type="search"`, leading magnifier, trailing clear (×) when non-empty, Escape clears. |
| Placeholder describes scope | Partly | "Team" vs the palette's "Go to a team, player, game or window" (`CommandPalette.razor:20`) | "Filter by team or matchup". |
| Search as you type | Met | `Immediate` on Board; palette settles at 200ms (`CommandPalette.razor:66,151`) | — |
| Results ranked and grouped | Met | palette groups and puts title-prefix matches first (`:173–179`) | — |
| Recent searches | Not met | empty palette lists windows and workspaces only (`:192–206`) | Show the last five opened subjects under "Recent". |

## Text fields

Change log: Jun 5 2023.

- Placeholder plus a separate label; width matches the expected text; consistent widths in a stack.
- Logical tab order; validate when it makes sense; number formatter for numbers.
- macOS: a field with a list of choices is a combo box; expansion tooltip for clipped text.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Label + placeholder | Met in forms | `DeskField` `Label` renders `<label for>` (`DeskField.razor:26–35`); RCA areas labelled (`IncidentsPanel.razor:187–192`) | — |
| Unlabelled in rows | Partly | Stake has only placeholder "Stake" (`StakeRow.razor:13–15`); Board "Team" (above) | `aria-label` at minimum; a leading inline label in grouped rows. |
| Width by call site | Partly | widths are inline styles at every call site — `max-width:130px` (`StakeRow.razor:14`), `170px`, `190px`, `120px`, `140px` (`BoardPanel.razor:37,61`, `PerformancePanel.razor:45–72`) | Three width tokens on the field: `--field-short` (8ch), `--field-medium` (16ch), `--field-long`. |
| Invalid state | Met | `.field[aria-invalid="true"]` red edge and ring | — |
| Height and recess | Met | 28px, `--surface-0` well, one hairline (2772–2783) | — |

## Pickers

Change log: Jun 5 2023.

- Medium-to-long lists; short list → pull-down/pop-up; very long → list or table.
- In context, near the field it edits; predictable ordering.
- macOS date picker: textual when space is short, graphical to browse.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Long list in context | Met | Odds "Games with moves…" select (`OddsPanel.razor:13–19`); `GamePicker` inline list | — |
| Date picker | No such component | dates are chosen by season switch and horizon switch; no free date entry | None needed now; if a date range filter arrives, a textual `DeskDate` before any calendar. |

## Combo boxes

Change log: none listed.

- Text field plus a list; a meaningful default from the list.
- An introductory label; list items no wider than the field.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Combo semantics | Met | `GamePicker`: `role="combobox"`, `aria-autocomplete`, `aria-activedescendant`, arrow keys (`GamePicker.razor:41–50`) | — |
| Meaningful default | Met | this week's game first (`c05945b`) | — |
| Reusable | Not met | the only combo is panel code; Book entry in the Journal edit form is a free `DeskField` beside a separate `DeskSelect` of books (`JournalPanel.razor:705–712`) | Lift `GamePicker`'s list into `DeskCombo` and use it for Book. |

## Sheets

Change log: Mar 24 2026 (button placement), Mar 29 2024, Dec 5 2023.

- A scoped task close to the current context; one sheet at a time.
- Pair Done with Cancel (or Back); never all three.
- macOS: dims only its parent; reasonable default size; other windows stay usable.
- A panel, not a sheet, when people need to watch results.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Used | Partly | `DeskSheet` exists and is used only in the Parts bin (`PartsPanel.razor`) | Fine; it waits for a real job. |
| Close affordance | Not met | the round × turns solid red on hover (`.desk-sheet__close:hover { background: var(--state-negative) }` 3966) — dismissal is drawn as destruction | Hover `--surface-3` and `--text-primary`; red is for the window's close dot only. |
| Cancel + primary | Met | `Footer` doc: "Exactly one Filled… Cancel Plain beside it" (`DeskSheet.razor:79–83`); `.desk-sheet__foot` trailing (3972) | — |
| Modal to the desk | Partly | MudDialog scrim covers the whole desk, so every window is blocked — macOS sheets block only their parent | Accept for now (the desk is one "app window"); document it in the sheet's comment. |

## Popovers

Change log: none listed.

- Small amounts of information or functionality; one at a time; nothing on top except an alert.
- Arrow points at the source; don't cover it; size to content.
- Close on outside click; save work on auto-close; never a warning in a popover.
- macOS: may detach into a panel.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A popover primitive | No such component | three hand-built anchored layers with three scrims: `.pullmenu__backdrop`/`__panel` (2357–2368), `.book-picker__scrim`/`__panel` (4624–4625), `.railmenu__backdrop`/`__panel` (1680–1689); plus `MudTooltip` with an arrow in `PriceCell.razor:32` | One `DeskPopover` (anchor, edge, scrim-less outside-click, Escape, focus return) that the three menus and BookPicker sit in. |
| One at a time | Partly | each scrim catches the next click, so a second menu needs two clicks | A shared popover host that closes the open one and opens the next on one click (HIG: "close one popover and open another with a single click"). |
| Material and edge | Met | `--material-regular` + blur + one hairline (2368–2378, 1689–1697) | — |
| Mud popover drift | Partly | `.mud-popover.mud-popover-open` is opaque `--surface-1` with a `--surface-3` border (`mud-bridge.css:126–130`), unlike the desk's own layers | `--material-regular`, `--separator` edge, `--radius-panel`. |

## Alerts

Change log: Feb 2 2024 (default and Cancel), Sep 12 2023, Jun 21 2023.

- Sparingly; not for information; not for common undoable actions; not at launch.
- Title states the situation; message only if it adds value; no button explanations.
- Button titles are verbs; avoid OK unless purely informational; always "Cancel" for cancel.
- Default button trailing; Cancel leading; don't make Cancel the default — if people must read, make no button default.
- Destructive style only when the destruction wasn't what the person deliberately chose.
- Escape cancels.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Order | Met | Cancel then confirm (`DeskAlert.razor:53–69`) | — |
| Escape | Met | `CloseOnEscapeKey = true`, no backdrop dismiss (`DeskAlerts.cs:57–58`) | — |
| Cancel not default | Not met | a destructive alert focuses Cancel so Return activates it (`DeskAlert.razor:102,116–119`) | Focus the alert container (`tabindex="-1"`): Return does nothing, Escape cancels, Tab reaches either key. |
| Avoid OK | Partly | `ConfirmLabel = "OK"` default (`:82`) | Make `ConfirmLabel` `EditorRequired`; no default. |
| Destructive style judgment | Partly | "Start over" confirm is red (`SlipPanel.razor:375`, `destructive: true`) though the operator pressed Start over deliberately | Keep red only for a consequence the operator did not name; Start over's confirm is Filled normal. |
| Visual | Met | centred, 320px, title3 heading (`.desk-alert` 3995–4020) | — |

## Action sheets

Change log: none listed.

- Choices related to an intentional action (save draft / delete draft); Cancel included; destructive prominent.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Component | N/A | iOS idiom; on the desk a two-choice consequence is an alert with three buttons | If Slip ever needs "Save for later / Discard", use `DeskAlert` with a second confirm. |

## Panels

Change log: none listed.

- Quick access to controls or info about the active content; inspectors update with selection.
- Prefer simple adjustment controls; brief noun title; no minimise.
- HUD style rarely.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Floating panel | Met | `DeskDialog`, non-modal, draggable, several at once (`DeskDialog.razor`), opened by `FollowUpLayer` | — |
| Noun title, no minimise | Met | title + Close only (`DeskDialog.razor:28–42`) | — |
| Subtitle legibility | Partly | `.deskdialog__sub` is `--text-caption` in `--text-tertiary` (2315–2321) | `--text-subheadline`, `--text-secondary`, like `.win__status` (1451). |

## Windows

Change log: Jun 9 2025, Jun 10 2024, Jun 21 2023.

- Adapt fluidly to sizes; open new windows deliberately; offer "open in new window".
- Avoid custom window UI that half-imitates the system.
- Key, main and inactive windows look different.
- No critical info or actions in a bottom bar.
- Say "window".

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Key vs inactive | Met | unfocused bar drops to `--surface-1`, title to secondary (1372–1378) | — |
| Custom controls that half-imitate | Partly | traffic-light-sized grey dots that reveal glyphs on hover (`.win__ctl::before` 12px circle, 1500–1517), placed at the trailing edge in Windows order (minimise, maximise, close; `AppWindow.razor:70–91`) | Either commit to the macOS look (leading, coloured on key window) or to plain 28px icon keys; the hybrid is the HIG's "feel broken" case. |
| Bottom bar non-critical | Met | footer is status only; the eviction notice carries Undo, also on Alt+Left (`DeskFooter.razor:16–34`) | — |
| "Window" | Met | "Open window", "Concurrent windows" | — |
| Pulse as status | Partly | the 2px pulse strip says state by colour alone (`.pulse--*` 1551–1555), named only in `aria-label` | Pair warn/critical with a glyph in the bar beside the status text. |

## Scroll views

Change log: Jun 8 2026, Mar 24 2026, Jul 28 2025 (scroll edge effects).

- Default scrolling and keys; make scrollability apparent.
- Don't nest scroll views on the same axis.
- Scroll edge effect only where content passes under floating chrome; one per view.
- macOS: small scroll bars in panels.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| No same-axis nesting | Partly | a grid given `Height=` scrolls vertically inside `.win__body`'s vertical scroll (`DeskGrid.razor:61–62`, `.win__body { overflow: auto }` 1586) | Use `Height` only on grids that are the whole window's content. |
| Edge under pinned header | Partly | sticky grid header pins with `top: -14px` to undo body padding (1954) — coupled to `.win__body { padding: 14px }` (1590), off the 4px ramp | Body padding `--space-4`, sticky `top: calc(-1 * var(--space-4))`, and a hairline under the pinned header only while scrolled. |
| Thin bars | Met | `scrollbar-width: thin`, 10px webkit (1592–1598) | — |

## Progress indicators

Change log: Sep 12 2023, Jun 5 2023.

- Determinate when possible; keep it moving; don't switch circular ↔ bar.
- A description only if it adds context — avoid vague "loading".
- Consistent location; Cancel (and Pause) when halting is safe.
- macOS: spinner for background work or tight spaces, unlabelled.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Determinate first | Met | `DeskProgress` defaults determinate (`DeskProgress.razor:12–18,59`) | — |
| Used | Not met | `DeskProgress` appears only in the Parts bin; a pull that walks N books shows a button sheen and a pulse (`PullMenu.razor`) | Give `IngestionJobs` a progress callback and show a determinate `DeskProgress` under the toolbar while a pull runs. |
| No vague "loading" | Not met | `EmptyState` text "Loading the game…", "Loading the player…", "Loading the market…" (`GamePanel.razor:30`, `PlayerPanel.razor:29`, `BoardAllBets.razor:21`), against the primitive's own rule (`DeskProgress.razor:20–25`) | "Reading the box score", "Fetching prices from N books" — or a skeleton with no words. |
| Spinner | No such component | none; the busy sheen stands in | A 12px `DeskSpinner` for in-button and in-field waits. |
| Cancel | Not met | a running pull cannot be stopped | A Cancel key beside the progress bar when the job supports a token. |

## Gauges

Change log: Sep 23 2022.

- Label the current value and both ends; macOS level indicators for capacity, continuous for large ranges, colour tiers when meaningful.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Component | No such component | coverage ("Priced 41 / 52") and credits are text metrics (`BoardPanel.razor:111–113`) | Optional: a continuous level indicator under the Priced and credits metrics, ends labelled. |

## Charts

Change log: Sep 23 2022.

- Mark type by message; fixed vs dynamic axes; familiar tick sequences; light grid lines.
- Title and summary that state the main message before the detail.
- Don't rely on colour alone; separate contiguous colour areas.
- Accessible: labels per meaningful element, overview with axes and bounds; keyboard path.
- Interaction optional, never required for critical information.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Message first | Partly | `Headline` + `Takeaway` slots (`DeskChart.razor:39–51`); Performance's takeaway describes the chart, not the finding ("The bankroll after each bet…", `PerformancePanel.razor:140`) | Takeaway states the result: "Up $412 over 63 bets; worst run −$180 in May". |
| Summary legible | Not met | takeaway is `--text-tertiary` 11px (`mud-bridge.css:582–585`) | `--text-secondary`, `--text-footnote` token. |
| Accessibility depth | Partly | one `role="img"` label per chart (`DeskChart.razor:54`), e.g. "Line chart of the bankroll over settled bets." — no axes, bounds or values | Compose `Description` from data: type, x range, y min/max with units, last value. |
| Not colour alone | Not met | Odds movement draws one line per book distinguished by palette only (`OddsPanel.razor:52`, `DeskChartPalette`) | Direct end-of-line labels (book name) or dashed strokes for the second family. |
| Type ramp | Not met | axis text 10px raw, legend 10.5px tracked (`mud-bridge.css:548,594–599`) | `--text-caption` / `--text-footnote`, no tracking. |

## Text views

Change log: Jun 5 2023.

- Long, editable or formatted text; keep legible; make useful text selectable.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Multiline in a text view | Met | `DeskTextArea` with label, `resize: vertical`, body leading (2811–2816) | — |
| Width | Met | fills its field-set | — |

## Tab bars (badges only)

Change log: Jun 8 2026, Dec 16 2025, Jul 28 2025.

- A badge is a red oval with white text, a number or "!", for critical new information only.
- Reserve badges so they keep their meaning.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| A badge primitive | No such component | counts ride in labels ("Slip · 2"), and the window bar marks every open window with a coloured 2px pulse, healthy green included (`.bar__pulse` 722, `.bar__group-pulse` 789) | `DeskBadge`: `--state-negative` capsule, `--on-accent` figure, shown only for warn/critical counts (open alerts, failed runs). |
| Reserved for critical | Not met | green "healthy" marks are always on in the bar | Drop the healthy and idle marks from the bar; keep warn/critical (and Active's sweep). |

---

## Missing components

| HIG component | What the desk does instead | Where it improvises | Build |
|---|---|---|---|
| Menu (with keyboard) | three hand-rolled menus, one with `role="menu"` and no arrows, two with no roles | `RailMenu.razor:43–48`, `PullMenu.razor:40–62`, window-bar drawers (`WindowBar.razor:94–119`, hover-opened) | `DeskMenu` + `DeskMenuItem` (label, detail, icon, disabled reason, destructive, checked); focus in on open, arrows, typeahead, Escape returns focus. |
| Popover | three scrims and three anchored panels | `.pullmenu__*` 2357, `.book-picker__*` 4624, `.railmenu__*` 1680 | `DeskPopover` host (one open at a time, single-click switch, edge flip at window bounds). |
| Pop-up button (custom) | native `<select>` (good) and `BookPicker` (rich rows) | `BookPicker.razor` | `DeskPopUp` for rich rows on `DeskMenu`, with checkmark, arrows and one Tab stop. |
| Pull-down button | `PullMenu` is one job list | `PullMenu.razor` | `DeskPullDown` = `DeskButton` (Tinted/Plain, chevron) + `DeskMenu`; Journal's Settle becomes one. |
| Toolbar overflow ("More") | wrapping | `.ptoolbar` 3087 | `PanelToolbar` overflow slot: items marked `Overflow` move into a trailing `…` pull-down under the container's narrow width. |
| Stepper | two loose keys | `WindowManagerPanel.razor:168–178` | `DeskStepper` beside `DeskField`: one capsule, −/+ halves, disabled at bounds, Shift ×5 optional. |
| Slider | raw `<input type="range">` with inline styles | `WindowManagerPanel.razor:207–214` | `DeskSlider` with label, ticks, end labels, value read-out. |
| Disclosure control | `<details>` with hidden marker; rotating trailing chevrons | `RunbookSteps.razor:20`, `TeamPanel.razor:84–111` | `DeskDisclosure` (leading triangle) for rows and sections. |
| Search field | plain field | `BoardPanel.razor:61` | `DeskSearch` (glyph, clear, Escape clears, placeholder of scope). |
| Combo box | panel-local `GamePicker` | `GamePicker.razor`, Journal Book entry `JournalPanel.razor:705` | `DeskCombo` lifted from `GamePicker`. |
| Badge | label counts, colour dots | `BoardWager.razor:489`, `.bar__pulse` 722 | `DeskBadge`. |
| Spinner | button sheen | `.desk-btn--busy::after` | `DeskSpinner` (12/16px). |
| Level indicator | text metrics | `BoardPanel.razor:111` | optional `DeskLevel`. |
| Radio group (vertical) | segmented control only | — | Not needed while every choice is 2–5 short nouns. |

## Top findings

Ranked by what the operator sees or hits.

1. **Menus have no keyboard and no shared shape.** `RailMenu` claims `role="menu"` but never
   moves focus or handles arrows (`RailMenu.razor:43–48,76–80`); `PullMenu` has no roles, no
   arrows and no Escape (`PullMenu.razor:40–62`); hover colour differs by menu (2419 vs 1828).
   One `DeskMenu` + `DeskPopover` fixes all three and `BookPicker`.
2. **The toolbar wraps instead of overflowing.** `.ptoolbar { flex-wrap: wrap }` (3087–3088):
   Board's seven controls (`BoardPanel.razor:35–78`) become three or four lines in a 400px
   window. Group into three, and move secondary pickers into a trailing "More" pull-down.
3. **A destructive alert makes Cancel the default button.** `DeskAlert.razor:102,116–119`
   focuses Cancel so Return cancels; the HIG (Feb 2024) says don't make Cancel default — make
   no button default. Also `ConfirmLabel = "OK"` by default (`:82`).
4. **Settle is a command dressed as a pop-up.** "Settle…" commits on change and snaps back
   (`JournalPanel.razor:217–224,310–317`); make it a pull-down button.
5. **No disclosure control; the runbook shows no state.** Marker hidden, static book glyph
   (`RunbookSteps.razor:20–23`, 3268); Team rows use a trailing down/up chevron (3756–3762)
   where a list row wants a leading triangle. Build `DeskDisclosure`.
6. **Sport filters are pop-ups for three fixed values.** All/MLB/NFL in Board, Journal,
   Performance, Players (`BoardPanel.razor:37`, `JournalPanel.razor:32`,
   `PerformancePanel.razor:45`, `PlayersPanel.razor:20`) — a segmented control's job.
7. **Grid columns cannot be resized.** `DeskGrid.razor:51–67` sets no `ColumnResizeMode`;
   wide Board/Journal/Performance grids clip what the operator came to read.
8. **The inline view in a row cannot be closed by pointer.** Clicking the lit segment returns
   early (`DeskSwitch.razor:163–164`); only Escape closes the snippet (`RowActions.razor:134–138`).
9. **Chart summaries are tertiary and colour carries the series.** Takeaway `--text-tertiary`
   (`mud-bridge.css:582–585`), axis/legend type off the ladder (548, 594–599), multi-book lines
   by colour only (`OddsPanel.razor:52`), descriptions without axes or bounds (`DeskChart.razor:54`).
10. **The sheet's close button turns red.** `.desk-sheet__close:hover { background:
    var(--state-negative) }` (3966) draws dismissal as destruction.
11. **Window controls half-imitate macOS.** 12px grey dots revealing glyphs (1500–1517) in
    Windows order at the trailing edge (`AppWindow.razor:70–91`) — the HIG's "custom window
    UI" warning. Pick one idiom.
12. **The stepper is two loose rectangles.** Glyphs passed as content skip `desk-btn--icon`
    (`WindowManagerPanel.razor:171–178`); build `DeskStepper`.
13. **Keys inside sentences, still.** "reset widths" and "Reset to 100%" sit in `<p>`
    (`WindowManagerPanel.razor:158,215–221`) — the one open row of the earlier audit.
14. **Focus is drawn two ways.** 2px inset outline on tabs, launcher rows, menu items and book
    rows (1089, 1829, 2420, 4369) against `--focus-ring` everywhere else.
15. **Progress exists but is not used; loading text says "Loading…".** `DeskProgress` only in
    Parts; pulls show a sheen; empty states read "Loading the game…" (`GamePanel.razor:30`,
    `PlayerPanel.razor:29`, `BoardAllBets.razor:21`).

## Do not adopt

- **44×44pt hit regions and 44pt rows.** The Buttons page states 44pt for all input; the desk
  is pointer-first at 28px controls and 29px grid rows (SKILL.md "When not to bend it").
- **iOS switches and "switch only in a list row".** On/off on the desk is a checkbox
  (`DeskCheckbox`); no `MudSwitch` — keep it that way.
- **Title-style capitalisation** for buttons, menu items, segments, tabs and column headings.
  The HIG asks for it throughout; the operator chose sentence case.
- **Uppercase or tracked group headers.** Not in the HIG anyway, and ruled out by the earlier
  audit; the brand wordmark is the only exception.
- **Liquid Glass toolbars and scroll edge effects.** The toolbar sits in an opaque body;
  materials stay on floating layers only (tokens 76–80). A soft edge effect under the sticky
  grid header would be decoration.
- **Bordered square buttons under tables** (macOS gradient buttons). They are boxes in boxes.
- **A help button per window.** The desk explains with tooltips and notes; a `?` key would add
  a control with no help book behind it.
- **Sheets that block only their parent window.** MudDialog's scrim covers the desk; matching
  macOS per-window sheets would mean a modal per column, which the desk's comparison workflow
  argues against (ADR 0007). Floating `DeskDialog` already covers the non-modal case.
- **Trailing primary in grouped forms.** The HIG puts the default button trailing; the wager
  reference leads with it, aligned to the groups (`.pform--grouped .pform__keys` 3184). Keep
  the house order, but say it once in `PanelForm`'s comment so the two footer orders
  (plain trailing, grouped leading) are a decision, not drift.
- **Menu-bar parity** ("every toolbar item also in the menu bar"). There is no menu bar; the
  command palette is the parity surface — make sure toolbar actions are reachable from it.
- **Context-menu-only commands, or showing shortcuts in context menus.** If context menus come,
  they mirror `RowActions`, nothing more.
