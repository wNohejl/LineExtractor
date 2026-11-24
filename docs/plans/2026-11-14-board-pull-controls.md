# The Board's pull controls — 2026-11-14

The operator's report: the Board's Pull data menu is one long list; the date range sits at its
foot as "Pull a date range…", whose detail ("1 req a game day + 1 a final") breaks the label into
pieces at the menu's width, and choosing it opens a second thing, a modal sheet, before anything
can be chosen. Asked for: date-range pulling optimised and brought up out of the menu's foot,
separate keys where a separate task deserves one, and related runs grouped.

## What is drawn (apple-hig-components)

The toolbar's actions become three keys, one job each:

| Key | Component | Why |
|---|---|---|
| Refresh | Plain icon key, as now | Reads; spends nothing |
| **Pull data** | Pull-down (Tinted, chevron) of **grouped** runs | Commands related to the key's purpose; the spend is the entry chosen |
| **Date range** | Tinted key with the calendar symbol, opening a **popover** anchored to it | One self-contained task with a few answers; a popover scales from its anchor and completes in place — no second surface, nothing at the foot of a list |

Nothing else gets a key: a fourth would crowd a 300px window, and the lines pulls belong with
the other pulls as a group, not as a second menu.

### The grouped menu

`DeskMenu` gains sections. A section has an optional heading (footnote, secondary ink, sentence case,
not focusable, `role="group"` + `aria-labelledby`) and is parted from the one before by the
existing `.menu__sep` hairline. Groups, in order:

1. **Today** — Today's games · Games in play
2. **Catch up** — Results owed · Closing lines owed · Play-by-play owed · Fighter profiles owed
3. **Lines** — Pull lines — MLB · NFL · UFC (the credits spenders, kept together so the eye finds the cost column)
4. **Housekeeping** — Settle finished games

`IngestionJob.Group` / `DeskJob.Group` carry the section (`JobGroups`); `DeskJobMenu` draws one
`DeskMenuGroup` per distinct group in order of first appearance. The date range leaves the menu.

Menu row fix: a row's detail never wraps into its label (`.menu__row` no-wrap, `.menu__detail`
`white-space: nowrap; flex: none`, `.menu__label` `min-width: 0` with ellipsis), and a menu whose
rows carry details and descriptions opens at least 320px wide (`menu--wide` on the panel).

### The date-range popover

`RangePullPopover` (XLines.Web, Components/Desk), hosted by the Board toolbar's Date range key
through `DeskPopover` (Align End). Inside, one grouped form, no card inside a card:

- **League** — a `DeskSwitch` (segmented) when the enabled leagues are 2–5, a `DeskSelect` when
  more; one league pre-chosen (the Board's current sport filter if it is one, else the first).
- **Days** — a `DeskSwitch` of quick ranges: *Last 7 days* · *Last 30 days* · *Custom*. The first
  two set From/To; Custom reveals them. From and To are two `type=date` `DeskField`s side by side
  on one row, sharing a width (forms.md: fields in a group share widths).
- **Re-pull complete days** — `DeskToggle`, off by default, with its one-line reason beneath.
- Footer line (secondary): what will be asked — "≈ 7 game days · at most 200 days" — using
  `RangePull.Check` for the refusal text where the answers are not a range a pull takes.
- `KeyRow`: Cancel (Plain) · **Pull** (the one Filled key, last), waiting with its reason while
  the answers do not make a range.

Pressing Pull closes the popover and runs the range exactly as the menu ran it: the **Date range
key goes busy** and carries the progress phrase ("NFL Sep 8 · 3 of 4"), the outcome toasts through
`DeskJobRunner.RunAsync(job, run, progress)`, and the Board reloads. `RangePullSheet` and the menu
entry that opened it are removed (the Desk's generic `DeskJob.Sheet` mechanism stays).

Keyboard: the popover's first field takes focus on open; Escape closes and returns focus to the
key (the popover already does this); Enter in a date field presses Pull when it is enabled.

### Audit before "done" (references/audit.md)

- One Filled key in the popover; the toolbar keeps none (its keys are Plain/Tinted).
- One hairline per group: the popover panel's own edge, the form group's edge; rows inside are
  inset separators only.
- Sentence case on labels and copy alike (the Desk's house rule), no all-caps.
- Hit regions ≥ 28px rows; fields share widths.
- Both themes screenshotted; narrow window (300px) checked: Refresh, Pull data and Date range fit
  on one line, Date range collapsing to its symbol with a title if it must.

## Packages

### D — Desk (src/XLines.Desk/**, tests/XLines.Desk.Tests/**)
- `DeskMenuGroup` primitive (`Label?`, `ChildContent`): heading + separator rules as above; keyboard
  order unaffected (items still register with the `DeskMenu`); typeahead skips headings.
- `controls.css`: `.menu__heading`, the row no-wrap fix, `.menu--wide`.
- `DeskJobMenu`: group its `DeskJob`s by `Group` in first-appearance order; ungrouped first.
- README primitives table row; Parts bin menu demo shows a grouped menu; tests in
  `DeskMenuTests` (heading rendered, not focusable, arrows skip it, separator between groups) and
  `DeskJobMenuTests` (grouped order, ungrouped first).

### W — Web (src/XLines.Web/**, src/XLines.Ingestion/Services/IngestionJobs.cs catalogue only,
IngestionDeskJobs.cs, tests/XLines.Web.Tests/**)
- Catalogue: each `IngestionJob` names its `JobGroups` group; `IngestionDeskJobs.Describe` passes
  it through; the range entry is no longer appended.
- `RangePullPopover.razor` as designed; Board toolbar gets the Date range key, its busy/progress
  state and the popover; `RangePullSheet.razor` and `RangePullSheetTests` go, replaced by
  `RangePullPopoverTests` (ready state, waits-and-says-why cases, quick ranges set the dates,
  Pull closes the popover and runs with the answers, key busy with the phrase, visitor sees no key).
- `BoardToolbarTests`: the three keys, in order; Date range key absent for a visitor.
- `xlines.css` only for the popover's two-field row if the Desk has no row for it.

## Checklist (verifier)

- [ ] Pull data menu is four headed groups in the order Today · Catch up · Lines · Housekeeping,
      hairlines between, no range entry.
- [ ] A menu row's detail never breaks its label; the job menu opens ≥ 320px wide.
- [ ] Date range is its own toolbar key; it opens a popover anchored to it, not a sheet.
- [ ] Popover: league segmented (or select above 5), quick ranges set From/To, Custom shows the
      fields, Re-pull toggle, one Filled Pull key that waits with its reason.
- [ ] Pull runs the range with the key busy and the progress phrase on it, toasts, reloads.
- [ ] Visitor: no Pull data, no Date range.
- [ ] 300px window: Refresh, Pull data, Date range on one line.
- [ ] Both themes screenshotted; `dotnet build` clean; all four test projects green;
      `DeskConventions`/CSS convention tests pass.
