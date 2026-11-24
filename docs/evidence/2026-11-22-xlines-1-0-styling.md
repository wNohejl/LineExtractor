# XLines 1.0 styling — what was measured in a browser

**Date:** 2026-11-22
**For:** [the 1.0 styling triage](../specs/2026-11-22-xlines-1-0-styling.md), item 6, and
[ADR 0031](../adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md) Phase A.
**How:** the desk run from this tree on `http://localhost:5264` with its scheduler and evaluator
off, against the local database; values are computed styles read from the page, not from the
sheets. No screenshot is filed: every window that was open shows the owner's journal, which does
not belong in this repository.

Suites on the same tree: Desk 2664, Desk.Hosting 26, Web 1069, XLines.Tests 1182, none failing;
`dotnet build XLines.slnx` 0 errors.

## The brand

| Check | Result |
|---|---|
| Page title | `XLines` |
| Header and empty-desk wordmark | `XLINES` |
| Browser storage key for the layout | `lineops.desk`, unchanged |
| A layout saved before a restart of the host | Restored after it, windows and tabs as left |

## The pinned table header (item 1)

A `.grid` table scrolled inside a window body, at each density:

| Density | Body padding | `th` top | Header's offset from the body's top edge |
|---|---|---|---|
| Compact | 10.5px | −10.5px | 0 |
| Standard | 14px | −14px | 0 |
| Roomy | 17.5px | −17.5px | 0 |

Before the change the top was −14px at all three, so the heading was cut by 3.5px at Compact and
showed a 3.5px slit at Roomy.

## Concentric corners (item 4)

Standard density; container, the padding between, and the row inside it.

| Pair | Shape | Container | Padding | Row |
|---|---|---|---|---|
| Window strip and its keys | Standard | 7px | 4px | 3px |
| Header drawer and its keys | Standard | 12px | 4px | 8px |
| Launcher popover and its rows | Standard | 12px | 4px + 4px | 4px |
| Popover and a menu row | Standard | 12px | 4px | 8px |
| Popover and a menu row | Sharp | 4px | 4px | 0 |
| Popover and a menu row | Round | 18px | 4px | 14px |

The popover-and-menu-row figures were read from a probe element with those two classes, since no
menu was open when they were taken; the others from the desk's own elements.

## Obsidian's accents (item 2)

The picker shows three swatches for Obsidian: Sky, Violet and Slate.

| Block | `--accent` | `--accent-ink` |
|---|---|---|
| Obsidian dark, Violet | #A78BFA | #BAA4FB |
| Obsidian light, Violet | #6D28D9 | #5B21B6 |
| Obsidian dark, Slate | #94A3B8 | #94A3B8 |
| Obsidian light, Slate | #475569 | #334155 |

Violet was chosen in the picker and worn by the desk in both appearances; Slate's values were
read with the attribute set by hand.

## The Line watch note (item 3)

One line on the Board in Line watch: "Line watch: pressing a game shows it in Line movement.",
18.8px tall, with the rest behind its disclosure. Its label is 13px where an ordinary note's text
is 12px; left as it is.

## Charts on their inks (item 5)

Standard, light: the Performance window's profit curve is drawn in `rgb(0, 118, 43)`, the
positive ink, at 5.79:1 on the white panel. Two charts were on screen; both drew.

## Appearances looked at

| Theme | Read |
|---|---|
| Standard dark (`apple-dark`) | Line watch, the Game window, Journal and Performance, Settings |
| Standard light (`apple-light`) | Journal, Performance and its charts, a tab group of three |
| Obsidian dark and light, Violet | Line watch; Journal and Settings |
| High contrast light (`contrast-light`) | Journal and Settings; text on panel 21:1, accent ink on panel 7.04:1 |
| High contrast dark (`contrast-dark`) | Text on panel 19.8:1, accent ink on panel 9.07:1 |

The console showed no error while the host was up.

## Every family, and the other two densities

All 45 `[data-theme]` blocks the sheets declare were put on the page in turn and six text and
ink tokens measured against the panel (`--text-primary`, `--text-secondary`, `--accent-ink` and
the three state inks on `--surface-1`). None is under 4.5:1. The five lowest minima: Ayu dark
4.84, Ayu light 5.28, Solarized dark 5.32, Kanagawa light 5.33, Midnight light 5.36.

Line watch (the Board beside Line movement) was drawn at Compact and at Roomy as well as
Standard. The header is 42, 50 and 60px; the Line watch note stays one line (18.8px) at all
three; nothing is cut.

A second family's charts: Catppuccin light, where the positive fill is 2.96:1 on the panel. The
profit curve is drawn in the positive ink, `rgb(45, 112, 30)`, at 5.39:1.

## Found and fixed during the pass

In a narrow Line movement column, a long matchup in "Biggest moves today" ran under its own
prices: the move's column could shrink below its unwrapped text. `.slatewatch__move` no longer
gives up width and the matchup wraps instead (`xlines.css`). After the change, none of the
eight rows on screen overlapped.

## The Activity stack, fanned

Closing a window raises the header's "Activity · 1" capsule with its "Closed … / Reopen …"
notice. A notice outlives a tool's round trip by too little to be opened by hand, so the fanned
state was drawn by putting the stack's own `toasts--fanned` class on it with a second copy of
the same notice: the list drops beneath the capsule as a 400px panel, 12px corners, 4px padding,
two 76px rows with one hairline between them, wholly inside the viewport. A row's corner there
is 7px where concentric would be 8px; the stack is not one of the triage's pairs and was left.

## The landing page

`site/index.html`, opened from disk at 375px: no sideways scroll, six sections, the clone
command filled in from the one `<meta>` that names the repository. The preview draws a local
file in light only, so the dark palette, which is the page's default, was computed from its own
values against its canvas (#1C1C1E): primary text 14.51:1, secondary 7.22:1, accent ink 6.14:1,
warning ink 9.54:1, negative ink 6.03:1. Not seen drawn in dark; its Copy buttons need a secure
origin and were not seen either.

## Not looked at

- The stack opened by a press on a live notice (the paragraph above is the same sheet, not the
  same gesture).
- Each of the twenty other families as a whole desk; they were measured, not read.
