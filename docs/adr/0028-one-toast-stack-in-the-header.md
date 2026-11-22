# ADR 0028 — One toast stack, called Activities, in the header bar

**Status:** Accepted, 2026-11-20. Amended twice. The same day it moved from bottom-centre to
top-centre below the header and took the name "Activities" (the owner: "move the toasts to
top-centre and label as activities"). On 2026-11-21 it moved into the header bar itself (the owner:
"move the toasts to the nav bar where they can layer horizontally rather than take up unnecessary
space"). Replaces the header notice of [ADR 0027](0027-the-column-limit-tabs-instead-of-closing.md)
and the snackbar placement in `AddDesk`.

## Context

The desk had two notice systems in one corner. `DeskToasts` raised MudBlazor snackbars pinned
top-right under the header, listed one under another; and the window manager's `Notice` ("Closed
Incidents." with "Reopen Incidents", or the 24-window ceiling's refusal) was drawn inside the
header's tools, beside the column count. Both covered or pushed the header's search, reset-widths
and count — the controls an operator reaches for straight after the thing that raised the notice —
and a burst of snackbars read as a list to work through rather than news.

A floating stack fixed that, at a cost: bottom-centre it lay over the last rows of a column,
top-centre over the centre column's title bar and over whatever page was showing, and a stack of
several took a hand's width of the desk to say a sentence. The header, meanwhile, has an empty
stretch between the window strip and the desk's own keys.

## Decision

**One stack.** `DeskToasts` keeps the list and the clocks itself; `DeskToastStack` draws it, once,
in the header (`DeskHeader` renders it; the layout no longer does). MudBlazor's snackbar is not
rendered or configured. The public calls are unchanged (`Success`, `Info`, `Warn`, `Fail`, `Added`,
`Removed`, `Show`, `Last`), and `Raise` returns the notice so an owner can take it down. The
manager's notice goes through the same stack (`DeskNoticeToasts`, in the desk): a new sentence
replaces its toast, the manager clearing its sentence (an open, a step back, a workspace) takes the
toast down, and dismissing or expiring the toast clears the sentence. "Reopen …" walks the
browser's history, as Ctrl+Z does. The header draws no notice of its own.

**In the header, in the free stretch only.** The stack is a flex item between the window strip
(`.bar`) and the desk's keys (`.hdr__tools`). It grows into the space left over (`flex: 9 1 0`, so
nearly all of it) and has no basis of its own, so it never shrinks the strip, the wordmark, search
or the window count, and never covers them. The first design drew notices over a useful part of the
bar, and the owner rejected that; this one takes only what nothing else wanted. The slot is a size
container (`container: activities / inline-size`), so what it draws follows the width it was
actually given, not the viewport's.

**One capsule, layered horizontally.** A title bar tall (`--titlebar-h`). The count's key leads: a
bare figure in secondary ink while folded, not a badge (HIG, Notifications). The newest notice is in
front, with its icon, its words cut to one line with an ellipsis, its key, and dismiss. Up to two
more show as edges stepped out to its right, a little shorter each (`--toast-step`); the rest are
folded away. The capsule is one material with one hairline.

**Fanned, the list drops beneath.** Pointing at the stack, focusing into it, or pressing the count
fans it. The capsule becomes the line "Activities · 4" with Clear all, and the list drops beneath it
as a popover would: newest at the top, every notice whole, its words wrapped by words, its key on a
line of its own when both are long. The list is held to half the screen and scrolls past that, and
it opens on the newest. Its rows are parted by inset hairlines and the list's own edge is the one
frame.

**What is heard, and where.** The pointer and focus are heard on the capsule (`.toasts__capsule`:
the count, Clear all and the list), not on the slot. The slot spans the header's whole free
stretch, and crossing empty header would fan the list and hold every clock.
- The gap between the capsule and the list beneath it belongs to the capsule (a bridge, its
  `::after`), so a pointer on its way down never leaves.
- A leave is heard only after `LeaveGrace` (300ms), so slipping off an edge and back is not
  leaving.
- Empty, the slot takes no room and no gap (`flex: 0 0 0`), so the strip keeps the header to itself
  until there is something to say.

The rules for fanning and folding are unchanged:
- Hover and focus fan it; the count pins it open; Escape or the count folds it. It stays folded
  while the pointer or focus is still there, until both have left.
- Hovering has already fanned the stack by the time the pointer presses the count, so that press
  pins it, and a press on a pinned count folds it. From the keyboard the count does what
  `aria-expanded` says, folding a fanned stack.
- Focus moving from key to key inside the stack is no change (`toasts.js` drops the focusout whose
  next stop is inside), so Tab neither folds the stack nor undoes an Escape's fold.
- An Escape that folds the stack is spent there: an open popover elsewhere stays open. Folding
  hides every notice but the front one, so focus on an older notice's key moves to the count's
  key first (`toasts.js`), never to <body>.
- Whether it is fanned is decided once, in the component (`Fanned`), and the stylesheet and
  `aria-expanded` both read that one answer.

The DOM is in the drawn order: the count's key, Clear all, then the notices newest first, so Tab
walks it as the eye does.

**Where the stretch is narrow** (a narrow window, or a header whose strip has wrapped), the
capsule folds to its key alone, in words — "Activities · 4", "Activity · 1". Pressing it drops the
same list; nothing floats over the desk again. The slot keeps room for that key at its narrowest.
One notice fans only from its key, which is only drawn there; pointing at a lone notice in a wide
capsule shows it already.

**Long words.** In the capsule the words keep at least 8ch and are cut with an ellipsis, and the
key gives way first (at most 45% of the row, also cut). The whole of each is its tooltip, the key's
whole label is its accessible name, the dismiss key names the whole message, and the fanned list
shows both in full. Words never break letter by letter: `overflow-wrap: anywhere` once left a long
message one character wide beside a long key, in a notice 800px tall.

**Layers.** The stack is part of the header, inside `.shell`, so it needs no tier of its own:
- an open alert's or sheet's scrim lies over it as it lies over the header, and the palette
  (`--z-modal` inside the shell) lies over the header too;
- the fanned list has no z-index, so the header's own menus and drawers — the workspace menu at
  `--z-popover`, a window bar's drawer inside its group — draw over it, and an open menu is never
  covered by it;
- the capsule's layers are isolated inside the list, so their order never reaches the header's.

What the floating stack needed to stay out of the way is gone, not kept: the measured
`--header-edge` and its script, the `--z-under` tier with the rules that sent the stack beneath
open popovers, and the rule that hid the stack while the connection banner showed. The banner
(`#blazor-error-ui`) has its own place again, bottom-centre above the footer.

**Called Activities.** The C# names stay `DeskToasts` and `DeskToastStack`; the words the operator
hears and reads say Activities:
- the region is named "Activities, 1 activity" / "Activities, 4 activities";
- the count's key reads "Activities · 4" when fanned or alone, and is named "Show all 4
  activities" / "Fold all 4 activities", or "Show 1 activity" for one;
- Clear all is "Clear all activities" to a screen reader.

**Clocks and focus.** Every clock stops while the pointer or focus is on the stack, and a notice
gets at least 2s once they leave. The container is always on the page, so leaving it is always
heard; a stack that empties ends the hold whatever it last heard. A notice dismissed or acted on
from the keyboard moves focus to the next notice's dismiss key, or back where focus came from when
none is left — unless an action put focus somewhere real outside the stack (a window it opened, a
field it focused), which is left where it is. With nowhere to go back to, focus goes to the header's
first key, else the desk (`main`, `tabindex="-1"`). Focus the pointer put there is not focus on the stack: the browser focuses the key it
clicks, and that neither holds the clocks nor keeps the stack fanned once the pointer leaves
(`toasts.js` passes on only `:focus-visible` focus, and a key pressed with the pointer — a click
that counts its presses — moves no focus). The window manager's toast leaves with the desk, and so
does the stack now, since the desk's header draws it.

**Lifetimes.**
- A success, an info, a neutral notice or a removal without Undo goes after 5s.
- A warning goes after 8s.
- A key that only takes back what was just done — Undo, Reopen — is timed too, at 8s (`UndoLife`,
  raised with `RaiseUndo` or `Removed(…, undo)`). The way back is also Ctrl+Z and Back.
- A failure, and a key that is the only way forward, stays until pressed or dismissed: such a key on
  a timer is a key the operator was not given time to press (HIG, Alerts and Feedback: match the
  interruption to the significance).
- At most twelve are kept. The oldest timed one goes first, then the oldest that is not a failure;
  only when every other notice is a failure does the oldest of any go. A failure is never dropped
  silently while anything else could go. The one being raised is never dropped, since nobody has
  read it yet: twelve failures standing and a success raised drops the oldest failure.
- The same words twice are two notices.

**Heard.** Two visually hidden live regions are always on the page, polite (`role="status"`) and
assertive (`role="alert"`). They carry each new notice's words once, in the order raised, and are
emptied once those notices have gone; a failure is assertive. The visible stack is a labelled
`section` and is not itself live, so nothing is read twice. Every key is reachable with Tab.

**Motion.** A notice slides in along the bar from the keys' side and settles, a little past and
back, without leaving the bar. Reduced motion: no slide and no fan motion, a fade at most.

## Consequences

- A test that read `ISnackbar.ShownSnackbars` reads `DeskToasts.Shown` (newest first) or `Last`.
- `DeskTestContext` registers `DeskToasts`, because the header and the desk use it.
- An application that set snackbar options through `AddDesk(mud: …)` still can; nothing reads them.
- A page that does not render the desk's header shows no visible notices. Its `DeskToasts` still
  keep the list and the clocks.
- The header's live regions are the stack's two hidden logs; a test that asserted the header had
  no `role="status"` asserts instead that nothing but those logs carries one.
