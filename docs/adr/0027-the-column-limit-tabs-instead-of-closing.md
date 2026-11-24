# ADR 0027 — The column limit tabs instead of closing

**Status:** Accepted. Supersedes the "ceiling evicts rather than refuses" section of
[ADR 0007](0007-window-manager-ui.md).

## Context

The desk is one row of columns up to a limit the operator sets in the Window manager
(ADR 0007). Opening a window at the limit **closed** the least recently used one, and the header
offered to bring it back. It was never silent, and a window holding unsaved input was passed by,
but it still meant a window the operator had chosen went away because they chose another, and a
desk of all-dirty windows refused to open anything.

The owner asked for the opposite: at the limit, windows group as tabs, the tab at the front of a
group is the page last shown, the ones after it are the pages shown before it, and the groups
fill left to right.

## Decision

**The limit is the number of columns, not the number of windows.** When every column is in use,
an opened window joins a column as a tab. Nothing closes.

### Where a window goes

One rule, applied left to right, by `WindowManager.Pick`:

1. never the column the operator is working in (the focused one), while there is another — the
   window they are reading stays in view beside the new one;
2. a collapsed column only when every column is collapsed — what was set aside stays aside;
3. the primary window's column last, so the larger share keeps showing the window it was given to;
4. otherwise the column with the fewest tabs, the leftmost of those first.

Rules 3 and 4 make the groups fill left to right and stay balanced: with three columns, the
fourth window tabs into the first, the fifth into the second, the sixth into the third. A person
can predict it from the strip they are looking at, which an LRU rule (which window did I last
touch?) does not allow. The header's count says where the next window will go before the click
(`NextTabTarget`, `DeskHeader.CapacitySentence`), as it used to say which window would close.

Lowering the limit folds the columns past it into the ones that remain, the leftmost extra first,
each into the column the same rule picks; the tabs that move go **behind** what the column shows,
so nothing in view changes. A workspace or a restored layout longer than the limit folds the same
way. The limit is never raised behind the operator's back — applying a workspace used to raise it.

### The strip

A column with more than one window draws a tab strip **as its title bar**: the leading tab is the
window showing and carries its name, icon and status; the window's own title is not drawn a
second time, and the bar keeps its height, so a group costs no vertical space. The tabs after it
are the other windows in the order they were last shown (`WindowInstance.Recency`, a counter, not
a clock, so two windows opened in one tick still have an order). Selecting a tab brings its window
forward and makes it the leading tab; the one that was showing becomes the second. The leading
tab slides in from where it stood (`ArrivedFrom`), and stops under reduced motion.

- Each tab closes without being selected; closing the showing tab reveals the next most recent;
  a group left with one window is a plain window again, with its title and Close key back.
- A tab moves to a column of its own from the strip's menu, only while a column is free, just
  right of the one it left. The limit is the operator's; moving out does not raise it.
- **Overflow:** the server knows each column's width, so it decides how many tabs fit at a
  readable width (`WindowTabs.VisibleTabs`: 120px a tab after 188px of bar keys). The rest go
  into the trailing menu, which counts them ("+2"). The strip never scrolls and never squeezes a
  label to nothing. The menu lists **every** tab in full whether or not it fits, with its status,
  so a label cut by an ellipsis is always readable whole somewhere that takes focus — besides the
  tab's own `title` and accessible name.
- **State:** a tab behind that goes Warn or Critical carries its window's pulse as a dot, and says
  it in its accessible name ("…, needs attention"); the menu key carries the worst of the tabs it
  hides. The column's pulse strip is the showing window's.
- **Minimise, maximise, reorder, resize** act on the column: a column collapses whole, maximising
  a tab brings it forward, dragging a bar moves the column with its tabs, and every tab shares the
  column's width and minimum (the widest tab's), so a tab switch never reflows the row.

### Keyboard and accessibility

The strip is a WAI-ARIA tablist with **manual activation**: one Tab stop on the leading tab, the
arrows (wrapping), Home and End move focus along the strip, Enter or Space selects, Delete closes
the tab with focus. Manual because selecting reorders the strip — an arrow that selected as it
moved would pull the tab out from under the keyboard. Focus follows a selection onto the new
leading tab (`RequestTabFocus`), since the strip it was pressed in goes behind with its window.
Each tab controls its window's body (`aria-controls`, `role="tabpanel"`). The per-tab close glyph
is the pointer's duplicate of Delete and is left out of the accessibility tree; the menu's Close
and Move are the labelled commands. Focus rings, 28px targets and the reduced-motion block are the
Desk's own.

The command palette, the header strip's keys and a follow-up for a singleton all reach a window
through `Open`/`Focus`, which brings a tab behind forward rather than opening a second copy. The
desk's chords include tabbed windows: Ctrl+Alt+←/→ moves the focused column, Ctrl+[ and ] page
the window showing.

### Tabs stay mounted

Every window in a group stays rendered. A tab behind is the same element in the same place in the
render tree, hidden with `visibility: hidden`, `inert` and `aria-hidden`, and draws no bar. So it
keeps its component state, its scroll position (a hidden box is still laid out; a detached or
`display: none` one loses it), its unsaved input and its own Back and Forward, and it keeps
refreshing on its data topics, which is what lets its tab report a breach truthfully. Unmounting
would have lost unsaved input — the one thing the desk promises never to lose. Windows are
rendered in the order they were opened, never the row's or a strip's, because a keyed element
Blazor moves in the page loses its scroll too.

The cost is circuit memory and refresh work for every window, so the desk has a **ceiling**:
`DeskSettings.MaxOpenWindows`, 24 — far past what a person keeps in mind across at most twelve
columns. At it an open is **refused**, the header says why ("The desk holds 24 windows, its
most. Close one to open …"), and nothing closes; the operator is left where they were.

### Eviction goes

`EnforceCeiling`, the blocker that refused when every window was dirty, and the "Closed X to make
room for Y" sentence are gone. The header notice remains for what is still true: a window closed
by its own key ("Closed Notes." with "Reopen Notes", which steps Back and returns a tab to its
group) and a refusal at the ceiling. `WindowManager.LastEviction`/`LastEvictedTitles`/
`ClearEviction` are now `Notice`/`NoticeTitles`/`ClearNotice`.

**Follow-ups still float when every column is in use** (`FollowUpLauncher`): a follow-up is a look
taken from a row the operator is still working in, and as a tab it would cover another column's
window to show it. Floating keeps every column as it was, as it did when the alternative was
closing one. A follow-up whose singleton is already on the desk is brought forward and re-aimed.

### Persistence

`DeskLayoutWindow` gains `Column` (its column's index) and `Rank` (its place in the strip, 0 for
showing), and saved workspaces gain `Columns` beside their keys. Both are optional, added without
a version bump as the scales were: a layout written before tabs reads them as null, every window
gets a column of its own, and a row longer than the limit folds. A visitor's desk drops the
operator-only windows from a layout or a group, as it drops them everywhere (ADR 0018).

## Consequences

- The Window manager's limit is labelled **Columns** ("Columns side by side"), its reading says
  columns in use and windows open, and a Tab groups section lists each column's windows in order
  while any are tabbed. The footer says "3 columns, 5 windows"; the header says "5 windows" (amended 2026-11-21,
  below; it said "3/3 +2 tabbed").
- The application needed no change: panels know nothing about being a tab.
- A tab behind keeps refreshing. If a desk of many heavy windows turns out to cost too much, the
  lever is the ceiling, or pausing refreshes for tabs behind — not unmounting them.
- Maximise still renders only the maximised column (its tabs included), as before. (Superseded
  by the amendment of 2026-11-21, below.)

## Amendment — 2026-11-20: the strip became a menu

**What changed.** A group's title bar no longer draws its tabs side by side. It shows only the
window showing — its icon and name, as a plain window's bar does — as a pop-up key carrying the
column's count and the up-and-down chevron ("Journal · 3"). The windows behind are listed in the
key's menu, most recently shown first, each with its icon, name, status line and, for a warning
or a breach, an attention dot; after a hairline come "Move … to its own column" and "Close …".

**Why.** The strip gave every tab behind at least 120px of the bar, so a group of three in a
narrow column spent most of its title on windows nobody was looking at, squeezed the title and the
window's own keys, and needed its own width fitting (`WindowTabs.VisibleTabs`, the browser's
measured widths, `WindowInstance.MeasuredWidth`/`ShownWidth`, `WindowManager.SetMeasuredWidth`)
and a "+N" overflow menu that duplicated half the strip. The owner asked for the nested tabs to
drop from the header instead. A pop-up is the HIG's control for "one of several mutually exclusive
states, where space is limited": the key says the current one, the list says the rest, and the
two commands sit after a separator as a pop-up's "Custom" items do.

**What holds.** Everything above: nothing closes at the limit, every tab stays mounted, choosing a
window makes it first and the one that was showing second, Close on a row with unsaved input asks
first. The keyboard is the WAI-ARIA menu button's: the key opens with Enter, Space, Down or Up,
landing on the window showing; arrows, Home, End and typeahead walk the menu; Enter chooses; Delete
or Backspace closes the focused row and keeps the menu open on the next; Escape returns to the key.
The rows are `menuitemradio` (the showing one checked), the commands `menuitem`; the row's close
glyph is the pointer's way to Delete and is out of the accessibility tree. A warning or a breach on
any window behind is a dot on the closed key and words on its name ("…; Alerts needs attention").
The bar's Close key now closes the window showing in a group too. The header's "2/3 +3 tabbed" and
the footer's "3 columns, 5 windows" are unchanged. (Both superseded by the amendment of 2026-11-21, a key that reads as a stack.)

**Removed.** The strip's markup and styles (`.wtab*`, `.wtabs__more*`), its width fitting and the
whole measuring path (`observeWindows` in windowing.js and `Desk.OnWindowsMeasured`), and the
tablist/tabpanel roles on the body. `DeskMenuItem` gained `OnRemove`, `RemoveLabel` and `Attention`
to carry a closable row and its dot, so the menu stays the Desk's one menu.

## Amendment — 2026-11-21: collapsed and maximised keep their windows too

**What changed.** "Tabs stay mounted" held for a tab behind but not for two other ways a window
left view. A collapsed column drew only its bar, so minimising it unmounted every tab's panel —
and its unsaved input, without a question; Back to a collapsed moment and a tab folding into a
collapsed column did the same. A maximised column was the only one rendered, so every window
outside it went. Now a collapsed window keeps its body, `hidden`, `inert` and `aria-hidden`
(display, not visibility: a stub is too narrow to lay a panel out in), and a window outside the
maximised column is drawn as a tab behind is (`AppWindow.Concealed`: `win--behind`, inert,
aria-hidden, no bar). Like a tab behind, both keep refreshing on their topics. A panel that does
go — a fault, a page turn — clears its window's unsaved flag as it is disposed.

**Maximised is the column's.** Every tab in a column carries the flag alike: maximising sets it on
all of them, so the tab chosen next offers Restore and one press gives the row back; a tab joining
takes the column's state; collapsing clears it. Focus, an open, or a tab moved to its own column
landing outside the column that holds the row gives the row back, so focus is never on a window
out of view. History moments carry maximised and width, so Back puts a column back as it was.

**History and the notice.** Moving a tab to its own column is a step Back takes back. Every step
that records history clears the notice, so "Reopen …" never steps back over something newer than
the close it names. A workspace or a Back refused for unsaved input says so in the notice, naming
the window ("Nothing changed: Notes holds input that has not been saved. …"). A follow-up at the
ceiling floats, as one does when every column is in use, since `Open` would refuse it.

## Amendment — 2026-11-21: a key that reads as a stack, tabs that are keys, one window count

**What changed.** The owner: "improve the tabbing counts and visibly layer the tabs. Remove the +1
from the window manager section in the nav bar, solely display window count", and later "this
should have nice, glossy buttons for the tab selection".

- **The count is in words.** The group's key reads "Window manager · 2 tabs", not "· 2". Its name
  is "Window manager, 2 tabs in this column — show tabs", with any warning behind it said before
  "show tabs". At a column's narrowest (the bar's 300px container rule) the word goes and the figure
  stays.
- **The window's own figure stands apart.** Beside the key it stands a full `--space-3` off. The
  Window manager's own figure says "2 of 2 columns" where it said "2/2", so the two are never read
  as one run of figures.
- **The key reads as a stack.** It is a gray key with the Desk's finish: a top line and a sheen
  (`--key-highlight`, `--key-gloss`; see the buttons' "Finish" in desk.css). An edge for each window
  behind, two at most (`.wtabs__layer--1`, `--2`), steps out to its right, a little shorter each —
  the Activities stack's language (ADR 0028). The layers come before the key in the DOM, so the key
  paints over them with no z-index and its menu keeps the popover tier. The key had no fill at rest
  before; the fill is what lets the edges read.
- **The tabs in the menu are keys.** Each tab (`DeskMenuItem Class="wtabs__tab"`) is a full-width
  key: the same gray, the same finish, its name on one line cut with an ellipsis (the whole name is
  its tooltip), and the window's own reading a quiet line under it. Its close is drawn at rest at
  the control height, a target of its own; Delete does the same. The tab showing is the selected key:
  the accent's wash, its name in the accent's ink, its check kept so selection is not told by hue
  alone, and `aria-checked`. That is selection, not a Filled key, so the context's one Filled key is
  untouched. The two commands after the hairline stay plain menu rows, because they are commands,
  not tabs. The stack on the bar and the keys in its menu are one idea.
- **The header says one figure.** "3 windows" — every open window, tabbed ones included. "2/2 +1
  tabbed" is gone. A screen reader hears "3 windows open, in 2 of 2 columns", and the sentence under
  the pointer still says where the next window would go. The footer always says "2 columns, 3
  windows", tabbed or not.

**Closing a tab behind, without a pointer.** The close mark on a tab's key is the pointer's way
to what Delete does from the keyboard. It is a span with `aria-hidden`, because a menu item's
children are presentational and a button inside a button is not HTML. So a screen reader, or a
touch user without a pointer's precision, does not get it as a target of its own. Their route is
the one every tab has:
- choose the tab, which brings it forward and closes the menu;
- then close it with the menu's "Close …" command or the bar's Close key.

Delete is still announced on the row (`aria-keyshortcuts="Delete"`). A "Close …" command for every
tab would double the menu, so the limitation is accepted and recorded here. The key no longer
declares `aria-keyshortcuts="ArrowDown"`: Down and Up are a menu button's own keys, not shortcuts.
A script (`popover.js`, `opensOnArrows`) keeps them from also scrolling the box the key sits in.
**What holds.** The menu, its order (most recent first), its heading ("3 tabs, most recent
first"), its keyboard (arrows, Home, End, typeahead, Enter, Delete), the attention dots and their
words, and the commands. High contrast and forced colours draw the keys flat; reduced motion stops
their transitions.
