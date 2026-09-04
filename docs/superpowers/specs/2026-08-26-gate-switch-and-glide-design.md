# Gate switch & glide highlight — design

Date: 2026-08-26
Brief: bring Apple-style sliding tab indicators ("marble") and action highlights to the
desk, with reusability first.

## Translation, not imitation

The desk's design language is physical: keys with travel, hue as state, gloss as the
affordance channel. Apple's sliding pill was translated into that vocabulary rather than
copied:

- **The gate switch** — a segmented control whose selected position is a raised key-cap
  that *slides along a channel cut into the panel*. The channel wears the field's light
  rules (recessed, unlit — you cannot press it); the cap wears the key's (crown sheen,
  seat shadow — you are holding it). Travel uses a slightly overshooting ease
  (`cubic-bezier(.32,1.32,.36,1)`, 260ms) so the cap lands with a settle. Reduced motion
  removes the travel, never the state.
- **The glide** — one shared highlight plate per cluster of flat controls, slid between
  them by pointer or focus, instead of each control flashing its own background. The
  macOS menu/toolbar behaviour, on the desk's graphite.

## Components

### `DeskSwitch<TValue>` (`Components/Desk/DeskSwitch.razor`)
- `Options` (`DeskSwitchOption<TValue>(Value, Label, Title?)`), `Value`/`ValueChanged`
  (bindable), `Label` (group aria-label, required), `Size` (reuses `DeskKeySize`),
  `Mono` (tabular figures for numeric positions).
- `role="radiogroup"`; options are `role="radio"` with roving tabindex; arrows/Home/End
  move the cap and focus follows.
- Layout: CSS grid `repeat(var(--n), 1fr)`; the cap is one absolutely positioned span at
  `translateX(calc(var(--i) * 100%))` — no JS, no measurement.
- `SelectedIndex == -1` (value matches no option, e.g. a custom resolution) renders no
  cap: no position pretends to be current.
- `GateOptions.LookbackDays` (14d/30d/90d) is shared so the same question reads the same
  in every window.
- When to use: 2–5 fixed positions. Past that, `select.field` stays the honest control.

### The glide (`wwwroot/js/desk-glide.js` + CSS in `lineops.css`)
- Opt-in per container with `data-glide`; zero per-component wiring. Document-level
  delegation (`pointerover/out`, `focusin/out`) means Blazor re-renders cost nothing —
  a plate diffed away is rebuilt on next hover.
- A child can tone the plate with `data-glide-tone="stop"` (the close button turns it
  drift). While the plate is live, per-control hover backgrounds stand down; ink changes
  stay.
- Applied to: `.win__controls` (every window's minimise/maximise/close) and both
  launcher menus in the header.

## Adoption in this change

| Where | Was | Now |
|---|---|---|
| OddsPanel market | `<select>` | gate: Moneyline / Spread / Total |
| Game/Team/Odds/BoardForm lookback | `<select>` | shared 14d/30d/90d gate |
| BoardPanel horizon | `<select>` | 12h / 24h / 48h gate |
| PlayerPanel games shown | `<select>` | 10 / 25 / 60 gate |
| WindowManagerPanel resolution mode | two `btn--primary` toggles | 2-position gate |
| WindowManagerPanel presets | four toggle buttons | 4-position gate, capless on custom size |
| PartsPanel | — | live gate documentation section |

Dead `.segment` CSS block removed (was never referenced by markup).

## Revision — tab bar, and the splat bug

### The bug this shipped with
`DeskSwitch` set `style="--n:…; --i:…"` on its root *and* splatted `@attributes`.
Blazor applies splatted attributes last, so any caller passing `style` (three panels
passed `margin-left:auto`) replaced the element's own style outright. `--n` never
arrived, `grid-template-columns: repeat(var(--n), …)` became invalid, and the control
collapsed to one column — the options rendered as a vertical stack.

Two fixes, because either alone leaves a trap:
- The component composes `style` itself, appending the caller's declarations after its
  own, and splats everything *except* `style`/`class` (`Rest`). A `Class` parameter
  matches `DeskButton`.
- `.marble-row` uses `grid-auto-flow: column` instead of an explicit template, so the
  track count follows the children. A missing `--n` can no longer collapse the layout;
  only the marble's own width depends on the number.

### Shared marble
`.marble` (travel + easing) and `.marble-row` (equal columns, stacking context) are now
the shared mechanics. `.gate__cap` and `.tabs__marble` are skins over them — one idiom,
two roles.

### `DeskTabs<TValue>` (`Components/Desk/DeskTabs.razor`)
Navigation, where the gate is a control. Flat by design: a tab is not a key, so it
catches no light. Proper `tablist`/`tab`/`tabpanel` semantics with roving tabindex,
wrapping arrows (a ring, unlike the gate's track with two ends), and generated ids
wiring `aria-controls`/`aria-labelledby`.

Answering "display tabs only if they can be clicked", as component behaviour rather
than per-caller discipline:
- `DeskTab.Available = false` → the tab is never rendered.
- Fewer than two available → no bar at all; the one section renders bare, without a
  `tabpanel` role that would have no tab to answer to.
- A held value naming a vanished section is corrected to the first available one and
  reported back through `ValueChanged` (guarded so a caller that ignores the callback
  is not asked twice).

`DeskSwitch` follows the same rule: fewer than two options renders nothing.

### Adoption
- **GamePanel** — the three stacked sections became tabs (Lines/Top bets · Team data ·
  Players/Box score), which is what the tall-panel screenshot was really about. Team
  data and Players are dropped when empty; Markets always shows, since an unpriced game
  still answers "what is the number" with the reason there isn't one. The `<h4>` per
  section is gone — the tab names it now.
- **GamePanel toolbar** — Movement and Head to head are one `data-glide` cluster.
- **TeamPanel splits** — the five split keys became a small gate. Picking an opponent is
  a filter the strip cannot express, so it goes capless rather than leaving a position
  lit under a filter it is not performing.
- Dead `.splits__key--on` removed (its only consumer was that strip).

## Revision — the desk-wide pass

### Window tabs along the toolbar
`Components/Windowing/WindowTabs.razor`. The desk's columns, laid out horizontally in
the header, in the row's own order, with the marble on whichever column holds focus.

The case it earns its place on: a collapsed window is 46px wide and cannot show its own
title, and the desk fills up exactly when knowing what is open matters most. Pressing a
tab focuses that column and restores it if collapsed — the offer the collapsed column is
too narrow to make itself. Each tab mirrors its window's pulse along the bottom edge, so
state stays readable from the toolbar.

Deliberately `role="toolbar"`, not `tablist`: every window is on screen at once, so
pressing a tab moves focus rather than swapping what is displayed. Current item is
`aria-current`; arrows walk and focus as they go, wrapping.

The header now reads like a browser's — mark, the control that opens something new, then
everything open, then the desk's own controls as one glide cluster. The tagline shows
only on an empty desk; once there are windows that space is theirs.

### Materials
`--material-chrome`, `--material-raised`, `--material-blur` (`saturate(180%) blur(20px)`).
Header, footer and menu panels are now panes held above the desk that take their colour
from what passes underneath, rather than bands painted on. Guarded by `@supports` — with
no `backdrop-filter` the translucency is dropped rather than left as a flat wash.

### Motion tokens
`--ease-marble` / `--ease-glide` / `--dur-marble` / `--dur-glide`, spent by every marble
and the glide plate, so the travel is one vocabulary rather than three animations sharing
a screen.

### Two bugs found while verifying
- **The minimise button never worked.** The window controls sit inside `.win__bar`, which
  has its own `@onclick` that expands a collapsed window. Minimise set `Minimised = true`,
  the click bubbled to the bar, the bar read the flag that had just been set and restored
  the window. Fixed with `@onclick:stopPropagation` on all three controls.
- **The marble drifted on a gapped row.** It stepped by its own width, which is correct
  only when positions are flush; with the window strip's 2px gap it was ~2px off by the
  last tab. `.marble` now steps by `100% + var(--gap)` and `.marble--track` sizes a
  column with the gaps taken out. `--gap` defaults to `0px`, so the gate and section tabs
  are arithmetically unchanged.

## Revision — the toolbar

### The launcher menu is gone
It listed all 17 catalogue entries, 7 of them disabled destinations (Game, Team, Player,
Head to head, Every book, Place wager, Recent form) that are reached by following a name
in a panel. A control you cannot press is not a control. The 12 openable windows are now
icon keys in the toolbar, split by catalogue group with hairlines; each one's tooltip
carries its name and description. Every window offered there is a `Singleton`, so
pressing an already-open key focuses it rather than opening a second — the non-singletons
are exactly the destinations that were removed. The capacity note that lived at the foot
of the menu is now the tooltip on the count.

### Three groups, evenly spread
`.bar` is `space-between`, so the groups sit at fixed places on the bar instead of
bunching left. Each `.bar__group` is a recessed channel — the gate's treatment — which is
the "marble outline": the marble runs inside a track in all three places. Only the
windows group spells names out; a window's name is what you are hunting for, an action's
icon is something you already know.

### Tabs are sized to their names
`.wintab` is `flex: 0 1 auto` with a 190px ceiling, so "Ops" is 59px and "Incidents" is
85px, and one open window no longer takes the whole bar.

That breaks the CSS-variable marble, which steps by equal columns. The toolbar therefore
uses a **measured** marble: the glide engine gained a `data-glide="marble"` flavour that
takes the size of whatever it lands on. It follows the pointer like the plate does, but
instead of vanishing it settles onto the child carrying `data-glide-rest` — the focused
window. Groups with no resting child (launch, controls) fade out like the plate. The
CSS-variable marble still drives the gate and section tabs, where positions genuinely are
equal and nothing needs measuring.

### Three bugs found while verifying this
- **Re-seating missed added nodes.** The observer watched `data-glide-rest` attribute
  changes, but a re-rendered tab arrives as a *new node* already carrying it, and a whole
  group appears as a child of `.bar`. `closest()` only looks upward, so neither was seen
  and the marble kept the geometry of a tab that no longer existed. Now watches
  `childList` and scans `addedNodes`.
- **`requestAnimationFrame` never runs in a hidden document,** so seating and the
  first-paint transition guard would hang until the tab was shown. Both moved to
  `queueMicrotask`; the observer callback already runs after the DOM is updated and
  `getBoundingClientRect` flushes layout itself.
- **A `rail__btn` inside a channel** rendered its own bordered box — a control nested in
  a control. Flattened to match `.bar__key`.

## Revision — finalize (2026-09-04)

The bar's final form is the fixed catalogue: every openable window drawn once, always, in
catalogue order, with state painted onto it — closed, open (lit, carrying its pulse),
current (accent wash), collapsed (quiet). Subject windows appear nowhere in the strip.
`WindowBar` owns that rule and `WindowBarTests` pins it. This pass finished it:

- **Comments reconciled.** The file's header and `DeskHeader`'s still described the
  abandoned two-runs design and claimed destinations appear in the strip, which the tests
  forbid. Both now describe what the markup does.
- **Keyboard walk fixed.** `OnKeyDown` derived its position from the *current window*, so
  arrows could only ever reach that window's two neighbours and Delete closed the current
  window rather than the focused key. A first fix tracked focus through `@onfocus`, which
  a hidden document never dispatches; the final fix puts `@onkeydown` on each key with its
  own index, so nothing is inferred from focus events or from where the current window is.
- **Motion.** `.bar__key` transitions background and colour on `--dur-fast`/`--ease-standard`
  so closed → open → current settles rather than snaps; the global reduced-motion block
  covers it and the bar block restates it.
- **`--bar-tight` delivered.** The comment promised a collapse that did not exist. `.bar` is
  now an inline-size container: at ≤1100px closed keys give up their names (open keys keep
  theirs), at ≤700px all names go. Group labels were already leaving at a 1500px viewport
  under an earlier rule, so the container rule no longer duplicates that.
- **Glide comment** no longer describes a tab-with-× that does not exist.

Verified in the browser: 11 keys, none disabled, order unchanged across open and close;
current key carries `aria-current` and `data-glide-rest`; marble seats on it at Δ0, resizes
onto a hovered key and settles back; wash computes to `rgba(10,132,255,.15)` once frozen
transitions are finished (see the hidden-pane note); at 1920 all names and four group
labels show with no scrolling, at 1440 names show and labels have gone, at 768 all names
collapse and nothing overflows; arrows advance Board → Line movement → Players → Journal,
End/Home land on the last and first keys; Delete on the focused Ops key closes Ops while
Incidents stays current; 254 web tests pass; console clean apart from restart noise.

## Verified

Build clean; browser-verified: cap slides to the exact position (one cap-width per
index), radiogroup semantics and roving tabindex correct, keyboard arrows move cap and
focus, capless state on custom resolution, glide plate sizes/positions to hovered
control in both horizontal (window controls) and vertical (launcher) clusters, stop
tone on close, plate clears on leave, Board reloads on horizon change, no console
errors.

Revision verified in the browser on the same game as the bug report (Red Sox at
Marlins): lookback gate lays out horizontally with both its vars *and* the caller's
`margin-left:auto` present; tab bar reports `role="tablist"` with three equal 187px
tabs and the marble exactly one tab wide; marble slides to 367.8px on selecting the
third tab and `aria-labelledby` follows; arrow key wraps from last to first with focus
following; unavailable section absent from the demo bar and restored when given
content; selecting a section then removing it falls back to the first with no orphaned
selection; five-position splits gate on one row, going capless when an opponent is
picked; glide slides between Movement and Head to head. Note: the earlier verification
only exercised gates without a `style` attribute, which is exactly why the splat bug
reached the screenshot.

Desk-wide pass verified in the browser: header carries the tagline and no strip on an
empty desk, and swaps to three equal 325px window tabs once a workspace opens; strip
reports `role="toolbar"` with `aria-current` and roving tabindex, each tab carrying its
window's real pulse state; pressing a tab focuses that column and the desk agrees
(`.win--focused` matches); minimise now genuinely collapses (46px, `win--collapsed`) and
the tab still reads its name, then restores to 562px with its body back; arrows wrap and
move DOM focus; header tools glide plate slides 0→68px across the three buttons; header
and footer compute `rgba(20,25,38,.74)` with `saturate(1.8) blur(20px)`; marble aligns to
0.00px at every position on the gapped strip and within 0.03px on the gate; no header or
body overflow at 1280, 961 or 768px; console clean apart from reconnect noise from my own
server restarts.

Toolbar verified in the browser: 12 launch keys, none disabled, 3 hairline splits, and
the old launcher menu button absent; groups spread with gaps of 105 and 106px, each with
a 1px channel border; window tabs at honest widths (Ops 59px, Incidents 85px); marble
seats on the resting tab (Δx 1px, Δwidth 0), resizes onto a hovered tab (566/59 against
that tab's 565/59) and settles back on leave; clicking a tab moves both the resting
marble and the desk's focused window together; launch group contains no text at all,
controls group only the count, windows group the only names; no header or body overflow
at 768px with all three groups visible.
