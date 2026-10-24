# Panel anatomy — one order for every window

*2026-10-24.* Design for making every window on the desk open, read and act the same way.

## Intent

The desk library (`LineOps.Desk`) gives every panel the same buttons, fields, tags, metrics
and section rules, and the panels still read as twenty different layouts. The cause is not
the parts; it is that nothing says what order the parts go in or how far apart they sit, so
each panel decided for itself:

- Ops opens with a lone Filled "Evaluate now" on its own right-aligned row, then metrics.
  Incidents opens with metrics. Runs and Journal open with a note, then selects, then the
  action. Players puts its Filled action in the middle of the filter row. Board opens with
  filters and no action at all.
- Game, Team, Player and Head to head set the window's own subject — "Yankees at Red Sox" —
  with `PanelHeader Tight`, the 11px uppercase grey *section* rule. The name of the thing is
  the smallest type in the window.
- Spacing is hand-written at every call site: `style="margin-bottom:12px"`, `margin-top:10px`,
  `gap:8px` on raw `div.row`, while `DeskRow`, `DeskStack` and the spacing ramp sit unused
  outside the Parts bin. MetricRow margins differ per panel (14px, 16px). Meta lines are
  `span.dim` at `calc(11.5px * var(--text-scale))`, copied by hand into five panels.
- Form footers disagree: Journal puts Save before Cancel; Incidents puts Resolve before
  Mark mitigated; History puts Gather history first and a destructive re-walk after.
- OddsPanel still writes an `h4.panel-h` by hand.

The fix is a **fixed anatomy**: six slots, in one order, each optional, each a component that
owns its own spacing so a panel never writes a margin. An operator who learns one window has
learned them all.

## Goals

- Every window body renders its slots in the same order: title, toolbar, note, metrics,
  sections, with forms as inset cards inside a section.
- The panel's single Filled action is in the same place in every window: last in the
  toolbar, pinned to the right edge.
- A subject window's name is set as a title, not a section rule.
- No panel writes a margin, a gap or a font-size inline. Spacing is the frame's business.
- Form footers end with the primary action at the right edge, Cancel beside it on the left,
  a destructive action at the far left.
- The Parts bin shows the anatomy, so the next panel is copied from it rather than invented.

## Non-goals

- No palette, type ramp, radius or token changes. ADR 0016 and the theme families stand.
- No change to what any panel does, shows or queries. This is layout and markup only.
- No change to the window chrome (`AppWindow`, `WindowBar`), the rail, the footer or the
  command palette.
- Grids, charts, tags, metrics, buttons, fields and switches keep their current look.
- The uppercase section rule stays. It is the system's established convention for a
  *section*; this design reserves it for that job and takes it off titles.

## Anti-goals

- A sweep that changes behaviour. If a panel's markup move alters which query runs, which
  key is default, or what a screen reader announces, that is a bug in the sweep.
- A frame that needs its own escape hatches. If a panel cannot be expressed in the six slots
  the slots are wrong, not the panel; come back to this document rather than adding a seventh
  slot in one place.
- A second way to do what an existing primitive does. `DeskRow`, `DeskStack`, `MetricRow`,
  `Note`, `EmptyState` and `PanelHeader` stay; the new components arrange them.

## The anatomy

Every window body, top to bottom. A slot a panel does not need is simply absent; the order
of the ones present never changes.

```
┌ win__bar   ⌂ Title                     status ──────────────────────  – □ × ┐
│                                                                              │
│ 1 Title      Yankees at Red Sox   [Final] [2026 season]     ← subject windows only
│              Sat Oct 24 · 7:10 PM ET · Cole v Crochet               [Season ▾]
│                                                                              │
│ 2 Toolbar    [Sport ▾] [Horizon ░░▓░] [Team…        ]  ………  [● Sync stats] │
│                                                                              │
│ 3 Note       ▌ A standing note or a warning, if the panel has one.           │
│                                                                              │
│ 4 Metrics    ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐                      │
│              │ 2/2    │ │ 0      │ │ 16,835 │ │ 0      │                      │
│              └────────┘ └────────┘ └────────┘ └────────┘                      │
│                                                                              │
│ 5 Sections   SECTION RULE                                   [small] [keys]   │
│              body — a grid, a stack, a chart, an EmptyState                  │
│                                                                              │
│              SECTION RULE                                                    │
│              body                                                            │
│                                                                              │
│ 6 Form       ┌ New entry ──────────────────────────────────────────────────┐ │
│   (inset)    │ fields …                                                    │ │
│              │ [Delete]                                 [Cancel] [● Save]  │ │
│              └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────┘
```

Left-aligned throughout. Nothing on the desk is centred except an `EmptyState` that already
is.

### 1 · Title — `PanelTitle`

For a window about one thing: a game, a team, a player, a pairing. Catalogue windows (Board,
Ops, Journal …) have no title slot; the window bar already names them.

- **Name** in `--text-title2` (17px), `--weight-semibold`, `--text-primary`,
  `--leading-title`. Links inside it (team names in a matchup) keep `DeskLink`'s look.
- **Tags** sit on the name's baseline, after it, with `--space-2` between them.
- **Meta** is one line under the name: `--text-subheadline` (12px), `--text-secondary`.
  This replaces every `span.dim` at `calc(11.5px …)`. Inline emphasis inside it (a starter's
  name) is `--text-primary`.
- **Keys** is a right-pinned slot on the meta line for the one control that scopes the whole
  window (the season switch; Game's Movement / Head to head pair). Nothing Filled lives here.
- Margin below: `--space-4` (16px). The component owns it.

Parameters: `ChildContent` (the name), `Tags`, `Meta`, `Keys` render fragments. Renders an
`<h2 class="ptitle">` so the outline is title → section rules, which is what the uppercase
rule's `Level` parameter was trying to express.

### 2 · Toolbar — `PanelToolbar`

The one row of controls that scope or act on the whole panel.

- Order inside, left to right: **pickers** (selects, switches), **search** (a field that
  takes the remaining width), then the **action** slot.
- The action slot is pinned right with `margin-left:auto`. It holds **at most one Filled
  button** and may hold Plain or Tinted keys beside it (Journal's Open slip, Settle). The
  Filled one is always last.
- A message the toolbar reports ("Settled 3 games") renders as `.ptoolbar__msg`, between
  search and action, in `--text-secondary`.
- Wraps. Gap `--space-2`. Margin below `--space-3` (12px).
- A panel that only reads (visitor access) renders the toolbar without its action slot;
  nothing moves.

Parameters: `ChildContent` (pickers and search), `Message` (string), `Actions` fragment.

### 3 · Note

`Note` as it exists, with its margins now set by the frame: `--space-3` above when it
follows a toolbar or title, `--space-4` below. Every `Note Style="margin:12px 0"` call
loses its style. The Journal's firewall sentence ("LineOps does not place wagers …") moves
from above the toolbar to this slot, under it; it is still the first prose in the window.
*(My call: one slot order beats one panel's exception, and the sentence stays above the
fold either way.)*

### 4 · Metrics

`MetricRow` as it exists, margin below `--space-4` (16px) set by the frame. The per-panel
`Style="margin-bottom:14px"` / `16px` goes.

### 5 · Sections — `PanelSection`

```razor
<PanelSection Title="Recent games" Keys="@split">
    …body…
</PanelSection>
```

- Renders `PanelHeader` (unchanged look) and, when `Keys` is given, a right-pinned group of
  small Plain/Tinted keys or a switch on the same line, baseline-aligned. Never Filled.
- A `Meta` fragment renders a quiet line under the rule (Team's "12-9 · 8-10 ATS · 14 of 21
  with a closing line") in the same style as the title's meta.
- Sections are separated by `--space-6` (24px) using `.psection + .psection`. The
  `PanelHeaderGap.Section`/`Nested` parameters and the `.panel-h + .panel-h` rule become
  unnecessary at every call site this design touches; `PanelHeader` keeps them so untouched
  callers do not move, and the Parts bin stops showing them.
- `Level` is inferred: a section inside a `PanelTitle`d window renders `h3`; a nested
  `PanelSection` renders `h4` (cascading depth). Nothing looks different.

### 6 · Form — `PanelForm`

An inset card for a task the operator completes in place (Journal entry, incident RCA,
Board's wager). Not a sheet: the operator needs the rows behind it.

- Surface `--surface-2`, hairline `--separator`, `--radius` (7px), padding `--space-3`.
  This is the card Journal already draws by hand.
- `Title` renders as a `PanelHeader Tight` at the top.
- Body is a `DeskStack` at `--space-2`.
- `Footer` is a `DeskRow`: a `Destructive` fragment at the far left, then
  `margin-left:auto`, then **Cancel, then the primary**. The primary is the only Filled
  button in the card. Pattern: `[Delete] ………… [Cancel] [● Save changes]`.
- Margin below `--space-4`.

Parameters: `Title`, `ChildContent`, `Footer` (Cancel + primary), `Destructive`.

### Spacing, in one place

| Between | Token |
|---|---|
| Title block → next slot | `--space-4` |
| Toolbar → next slot | `--space-3` |
| Note → next slot | `--space-4` |
| Metrics → next slot | `--space-4` |
| Section → section | `--space-6` |
| Section rule → body | `--space-2` (PanelHeader's own) |
| Form → next slot | `--space-4` |
| Controls in a row | `--space-2` |
| Items that belong together (value + unit) | `--space-1` |

All in one CSS block, `/* ---- Panel anatomy ---- */` in `lineops.css`, after the `.row`
and `.stack` rules. No `!important`. Every selector is a single class or a `.a + .b`
sibling pair.

### Type, in one place

| Role | Size | Weight | Colour |
|---|---|---|---|
| Panel title | `--text-title2` | semibold | `--text-primary` |
| Meta line (title or section) | `--text-subheadline` | regular | `--text-secondary` |
| Section rule | `--text-footnote`, uppercase | semibold | `--text-secondary` (unchanged) |
| Toolbar message | `--text-subheadline` | regular | `--text-secondary` |

## Where each panel lands

| Panel | Title | Toolbar (pickers → search → action) | Note | Metrics | Sections / Form |
|---|---|---|---|---|---|
| Board | — | Sport, Horizon, Game state, Team, +EV → (Pull menu stays where it is) | — | yes | rows |
| Every book (BoardAllBets) | — | — | — | — | one section per market |
| Place wager (BoardWager) | — | — | — | — | Form: Selection / Price / Your wager; footer `[Cancel] [● Log it]` |
| Slip | — | — | — | — | sections Logged as, Wagers, Parlay; footer in a Form |
| Recent form (BoardForm) | — | window picker | — | — | section per team |
| Game | matchup + tags; meta start/starters; keys Season, Movement, Head to head | — | — | — | Your bets, then tabs |
| Team | name + tags; meta record; keys Season | — | — | — | Recent games (keys: split; meta: records), Roster form |
| Player | name + tags + team link; meta appearances; keys Season | — | — | — | Averages, Game log |
| Head to head | pairing + meeting count; meta record and coverage | — | coverage note | — | meetings |
| Line movement (Odds) | — | Game, GamePicker, Market | — | — | Current market, Recent form (fix the hand-written h4) |
| Players | — | Sport, Search → **Sync stats** | — | — | grid |
| Journal | — | Status, Placed within, Sport, Book, msg → Open slip, Settle, **Log entry** (Tinted; it opens a form, which is why it is not Filled) | firewall | — | Form (entry), Entries, Parlays |
| Performance | — | Settled within, Sport, Season, Book, Market | — | yes | Bankroll, By market, By sport, By book, What CLV / value was measured against |
| Ops | — | → **Evaluate now** | — | yes | Odds feeds, Source health, Open alerts, On-call drill |
| Incidents | — | (list) — / (detail) All incidents, severity, status tags | — | yes (list) | Timeline, Root-cause analysis with Form footer `[Mark mitigated] [● Resolve incident]` |
| Runs | — | Source, State → Refresh | standing | — | grid |
| History | — | — | — | — | Sources, Coverage (metrics inside), Run (keys: Gather/Stop; destructive re-walk at far left), Seasons, Recent days |
| Window manager | — | access row → Sign in/out | capacity, standing | yes | Appearance, Scale, Concurrent windows, Primary window, Resolution, Workspaces |
| Parts bin | — | — | standing | — | existing sections, plus a new first section **Anatomy** that renders every slot once |

Ops's toolbar is the action alone. That is the design, not an exception: the slot is where
the action lives whether or not there are pickers beside it, and the operator's hand goes to
the same corner in every window.

## Constraints

- Blazor Server, MudBlazor 9.7, the `DeskButton`/`DeskRow`/`DeskStack` splat conventions
  (class and style held out of the splat and merged, never replaced).
- `LineOps.Desk` knows no domain types. The four components take render fragments and
  strings only.
- Every component gets bUnit tests in `tests/LineOps.Web.Tests` on `DeskTestContext`,
  asserting structure: the slot order, the right-pinned action container, heading rank,
  and that a caller's `class`/`style` extend rather than replace.
- `dotnet build` clean on Web and both test projects, `dotnet test` green, the Worker host
  still starts (it does not reference Desk, but CLAUDE.md asks for the check).
- Verified in the browser, on the real screen, per ADR 0016's lesson: a rule that loses the
  cascade is not findable by grep. Screenshot Ops, Runs, Journal, Game and Team before and
  after.
- Commits go on `LineX_Development`, one per task, developer identity only, pushed.

## Left to the builder

- Exact class names under the `p*` prefix (`ptitle`, `ptoolbar`, `psection`, `pform`),
  and whether `PanelSection` infers heading rank with a cascading parameter or a counter.
- Whether `PanelHeader`'s `Gap` parameter is marked obsolete now or left for a later pass.
- The order of the panel sweep. Suggested: Ops, Runs, Players (small, show the toolbar
  rule) → Team, Player, Game, Head to head (titles) → Journal, Incidents, BoardWager, Slip
  (forms) → the rest.
- Whether GamePanel's Movement / Head to head pair reads better in the title's `Keys`
  beside the season switch or as the first section's keys. Default: the title's keys.

## Builder check — the calls made

A fresh reader planned from this document and asked eight questions. None needed the
operator; each is settled here so the plan argues from one text.

1. **Place wager and Slip keep their own grouped-list system.** `.wsections` / `.wsection` /
   `.wgroup` already give those two windows sections `--space-6` apart with filled, unoutlined
   groups, and a `PanelForm` card around filled groups would be boxes in boxes. They are out
   of scope for `PanelSection` and `PanelForm`; only the **footer order rule** applies to them
   (Plain keys, then the one Filled key, pinned right). `PanelSection`'s spacing is built
   beside `.wsection`, not on it.
2. **The heading outline change is the accepted exception** to "what a screen reader
   announces must not change": a title at h2 over sections at h3 is the outline the windows
   should have had. So is the Journal note's move below the toolbar. Everything else — roles,
   aria attributes, labels, default keys — is unchanged.
3. **No form on the desk submits on Enter.** There is no `EditForm`, no `<form>`, no
   `ButtonType.Submit` in `LineOps.Web`; every key is a click handler. DOM order is visual
   order, so the footer reorder changes tab order from Save→Cancel to Cancel→Save and nothing
   else. `PanelForm` renders the footer in the order the caller writes it.
4. **Destructive keys never sit in a section's `Keys`.** They live in a form footer's
   `Destructive` slot or, where there is no form (History's re-walk), at the far left of the
   body's first `DeskRow` with `Justify="Between"`. Window manager's Sign in / Sign out is a
   small Plain key in the toolbar's actions slot; **a toolbar may hold zero Filled keys**.
5. **A key that opens a form in place is Tinted; the key that commits the form is Filled.**
   That is why Journal's toolbar has no Filled key and its entry form has one. Board, Slip
   and Performance follow the same rule.
6. **The Parts bin shows the four components in one Anatomy section and stops demonstrating
   `PanelHeader`'s `Gap` and `Level`.** The parameters stay on `PanelHeader` for callers this
   design does not touch; no test references the Parts bin's headers.
7. **The global rule `.win__body :not(.panel-h) + .panel-h { margin-top: 18px }` stays.**
   `PanelSection` renders its header `Tight`, which writes `margin:0` inline and wins; section
   spacing comes from `.psection + .psection` alone. Untouched callers keep the 18px.
8. **Screenshots are evidence, not a gate.** The expected visible differences are exactly:
   slot reordering, the spacing table, the title type, and the footer order. Any other
   change is a cascade loss to be fixed before the task commits.
