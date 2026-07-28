# ADR 0007 — A window manager instead of pages

**Status:** Accepted

## Context

The UI was a conventional nav-drawer application: one page at a time, MudBlazor layout,
click a link to change what you are looking at.

That fights what the product is for. Triaging an ingestion failure means reading source
health, the incident, and the runs behind it *together* — and the CLV story means watching a
line move while logging against it. On a page-based UI every one of those is a round trip
through the nav, and you can only ever hold one in view.

## Decision

The application is a **desk**: a single route with fixed **header** and **footer** chrome,
hosting draggable, resizable, minimisable windows between them. Only the taskbar strip of
window chips relocates — header and footer never move.

### Windows are always a horizontal row

The desk is **one row of full-height columns**, and there is no other mode. Each new window
takes its place at the end of the row and the existing ones give up the space; closing one
gives it back. Windows cannot be dragged or freely positioned.

That constraint is the point rather than a limitation. Arbitrary placement is what **modals
and popovers** are for, layered above the desk — a tiling row that is sometimes not a row is
just a floating-window manager with extra rules, and it forces every feature to handle both
cases forever.

The only geometry an operator controls is **where two adjacent columns meet**, via a divider
in the gutter between them. A divider drag preserves the two columns' combined weight, so the
rest of the row does not move — that locality is what makes it feel like adjusting a split
rather than relaying out the desk. Arrow keys work on a focused divider, because a
pointer-only resize would put column widths out of reach.

A layer scale is defined in CSS (`--z-window` … `--z-toast`) so the desk sits deliberately low
and future modals, popovers and toasts have room above it without competing with a window.

### A primary window

One window can be designated primary. It leads the row, takes a configurable share
(default 45%), and is what opens on an empty desk. It is marked with a ◆ on its chip, where
windows are listed, rather than only in settings — it changes how the desk behaves, so it
should be visible where you look for windows.

### Window manager

A settings window (itself a window, from the same catalog) controlling layout, the concurrent
ceiling, the primary window and its share, resolution, and taskbar dock. No Save button: the
desk behind it is the preview.

**Resolution** may follow the browser or be *declared*. Declaring one lets an operator lay a
desk out for the screen they will run it on rather than the one they are configuring from;
the desk then keeps that size and scrolls, instead of layout pretending the screen is bigger.

**The ceiling evicts rather than refuses.** Opening past it closes the **least recently
used** window. The operator asked for the new window, so they get it; the cost is that
something goes away, which is handled by making eviction impossible to miss:

- the launcher names the window that is about to close, *before* the click;
- the footer names what was closed and what it made room for, *after*;
- minimised windows are evicted first — the operator has already set them aside — and only
  then does oldest-focus decide.

**The default ceiling comes from the screen.** A fixed default (4) overflowed the row on a
laptop, pinning every column at its minimum width and making the dividers inert — technically
correct and useless. The first viewport measurement sets the ceiling to what fits
(`desk width / 420`, floor of 2); an operator's own choice then wins permanently.

- **`WindowManager`** (scoped, one per circuit) owns placement, stacking, focus and rail
  edge. State lives there rather than in components so that tiling, workspaces and keyboard
  actions can reposition windows without a drag being the only path.
- **`WindowCatalog`** is the registry. Adding a window is one entry; the rail, launcher and
  host all read the list.
- **Panels are ordinary components.** They receive their window id as a cascading value and
  know nothing else about being windowed. That inversion is what makes "any sub-page can be
  a window" literally true.
- **Workspaces** are named layouts stored as *fractions* of the desk, so one saved on a
  large monitor still makes sense on a laptop. These are the unit of "workflow".

### Drag and resize run entirely in the browser

This is the load-bearing decision. Blazor Server runs over a SignalR circuit, so a
`@onpointermove` handler would put a network round trip inside every frame of a drag —
windows would visibly lag the cursor. Instead `windowing.js` mutates the element's own style
at pointer rate and calls `.NET` exactly once, on pointer-up, with the final rectangle.

The DOM is therefore ahead of the .NET model mid-gesture. That is safe because nothing else
writes those properties during a drag, and the commit reconciles.

### The pulse strip

Each window's title bar carries a 2px band encoding that window's own state, mirrored on its
rail chip. A window that is unfocused — or minimised — still reports. Peripheral awareness is
the only reason a desk beats tabs, so it is the one element allowed to be loud; everything
around it stays quiet.

## Consequences

- Deep links are gone. One route (`/`) renders everything, and the former page components
  were deleted rather than kept in parallel — two UIs for the same data would drift.
  Restoring deep links means encoding desk state in the URL, which the manager's shape
  already permits.
- Nothing is ever refused, so no window needs exempting from the ceiling. An earlier
  refusal-based design created a catch-22 — the Window manager, where the ceiling is raised,
  was itself blocked by the ceiling — which eviction removes entirely.
- `RecommendedCapacity` (desk width / 420) is guidance shown *beside* the operator's limit,
  with a warning when the two disagree.
- A column never shrinks below its `MinWidth`; the row overflows and the desk scrolls
  instead. Honest about not fitting, but it makes dividers inert while it holds — which is
  why the default ceiling is derived from the screen.
- **Any component reading mutable manager state subscribes to it.** Relying on a parent
  re-render works until the render tree changes shape: moving the chips out of the old rail
  and into the footer left them rendering "No windows open" beside four open windows.
- `setPointerCapture` must be called *after* the move listeners are registered and inside a
  `try`. Called first, a throw on a stale pointer id aborts the gesture after the element
  has already taken its `dragging` class — the window latches into a drag it cannot perform.
  This was a real bug, found in testing.
- MudBlazor remains for charts only — superseded by [ADR 0008](0008-gloss-as-affordance-and-the-mudblazor-seam.md),
  which builds a theming seam so its components can be used broadly. The rail's menus are hand-built (`RailMenu`) because the
  launcher is the primary way windows get created and should not inherit another library's
  positioning and z-index rules — especially when it must open away from a rail that can be
  on any edge.
