# ADR 0020 — One anatomy for every window

**Status:** Accepted

**Builds on:** [ADR 0016](0016-apple-feel-design-system.md), which gave the desk its parts —
one button with a weight, one section rule, metrics, notes, empty states — and said nothing
about the order they go in.

## Context

The desk library gave every panel the same parts, and the panels still read as twenty
different layouts. The cause was not the parts; it was that nothing said what order they go
in or how far apart they sit, so each panel decided for itself:

- Ops opened with a lone Filled key on a right-aligned row of its own, then metrics.
  Incidents opened with metrics. Runs and Journal opened with a note, then selects, then the
  action. Players put its Filled key in the middle of the filter row. Board opened with
  filters and no action at all. The operator's hand went to a different corner in every
  window.
- Game, Team, Player and Head to head set the window's own subject — "Yankees at Red Sox" —
  with `PanelHeader Tight`, the 11px uppercase grey *section* rule. The name of the thing was
  the smallest type in the window.
- Spacing was hand-written at every call site: `margin-bottom:12px`, `margin-top:10px`,
  `gap:8px` on raw `div.row`, while `DeskRow`, `DeskStack` and the spacing ramp sat unused
  outside the Parts bin. `MetricRow` margins differed per panel. The quiet meta line under a
  subject was a `span.dim` at `calc(11.5px …)`, copied by hand into five panels.
- Form footers disagreed: Journal put Save before Cancel, Incidents put Resolve before Mark
  mitigated, History put the destructive re-walk after the primary.

Spec: `docs/superpowers/specs/2026-10-24-panel-anatomy-design.md`.

## Decision

Every window body renders its parts in one order — **title, toolbar, note, metrics,
sections, with forms as inset cards inside a section** — and each part owns the air beneath
it, so a panel never writes a margin. Four components in `LineOps.Desk/Primitives` carry
it:

- **`PanelTitle`** — a subject window's name in `--text-title2`, tags on its baseline, one
  quiet `Detail` line, and the window-scoping keys (a season switch) pinned right. Catalogue
  windows have no title; the window bar already names them.
- **`PanelToolbar`** — pickers, then a search field that takes what is left, then a quiet
  message, then the actions slot at the right edge. **The one Filled key is always last in
  that slot.** A key that opens something elsewhere — a window, a form — is Tinted; the key
  that commits the form is the Filled one, so a toolbar may hold no Filled key at all. The
  slot is not drawn when a panel has nothing to put in it, so a visitor's window reserves no
  empty box.
- **`PanelSection`** — the existing uppercase rule, now spacing itself (`.psection +
  .psection` is `--space-6`), with small keys and a detail line on the rule, and heading rank
  inferred from nesting: h3 straight in a body, h4 inside another section. Never a Filled
  key on a rule, never a destructive one.
- **`PanelForm`** — the inset card the Journal already drew by hand, with the footer pinned:
  anything destructive at the far left, then Cancel, then the primary at the right edge.

Notes and metric rows in a window body, or in a section body, own `--space-4` beneath them.
Place wager and the Slip keep their own grouped-list system (`.wsection`), which already
spaces sections the same way; only the footer order applies to them.

The Parts bin opens with an **Anatomy** section that renders every slot once, in order, so
the next panel is copied from it rather than invented.

## Consequences

- **No panel writes a slot margin.** What inline spacing remains is inside grid cells, tab
  bodies and row expansions — content, not anatomy — and is listed in the commit that
  finished the sweep.
- **`PanelHeader`'s `Gap` and `Level` are legacy.** They stay for the callers this did not
  touch and for nothing new; nothing in `LineOps.Web` references `PanelHeaderGap` any more.
  The global rule that gives a heading after content 18px stands, because `PanelSection`
  renders its rule `Tight` and that inline `margin:0` wins.
- **The heading outline changed on purpose.** A title at h2 over sections at h3 and
  subsections at h4 is the outline the windows should have had; that is the one thing a
  screen reader now announces differently.
- **The detail slot is `Detail`, not `Meta`.** Razor refuses `<Meta>` as a child-content
  tag because `meta` is an HTML void element. The CSS classes keep `__meta`.
- **A toolbar's select is sized to its words.** `.field` is `width:100%` for the forms it
  sits in; a picker that took the whole toolbar pushed everything after it onto the next
  line, so `.ptoolbar > select.field` is `width:auto`. Callers that want a wider picker still
  say so with `max-width`.

## What building it found

- **A named slot cannot be conditional in markup.** `@if` around `<Actions>` is a Razor
  error. Players hands the toolbar `Actions="@(CanOperate ? SyncKey : null)"` from a
  `RenderFragment` in `@code`; Ops wraps the whole toolbar in the `@if` because it has no
  pickers. The spec's promise — no empty slot for a visitor — holds either way.
- **The JournalPanel test pinned a title.** `Log_entry_opens_the_wager_window…` asserts the
  panel source never says "New entry", because the Journal's form only edits; the sweep had
  put a "New entry / Edit entry" ternary on the card and the test caught it.
- **Verify in the browser, again.** The first Parts bin render showed the toolbar's select
  spanning the row and the metrics inside a section hugging the next rule — two rules that
  were right in the stylesheet and wrong on the screen, exactly as ADR 0016 warned.
