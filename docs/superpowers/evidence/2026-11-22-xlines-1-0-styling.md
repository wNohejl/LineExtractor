# XLines 1.0 styling — what was measured in a browser

**Date:** 2026-11-22
**For:** [the 1.0 styling triage](../specs/2026-11-22-xlines-1-0-styling.md), item 6, and
[ADR 0031](../../adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md) Phase A.
**How:** the desk run from this tree on `http://localhost:5264` with its scheduler and evaluator
off, against the local database; values are computed styles read from the page, not from the
sheets. No screenshot is filed: every window that was open shows the owner's journal, which does
not belong in this repository.

Suites on the same tree: Desk 2664, Desk.Hosting 26, Web 1069, LineOps.Tests 1182, none failing;
`dotnet build LineOps.slnx` 0 errors.

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

## Not looked at

- The Activities stack fanned.
- Charts in any family but Standard: the 174 test cases hold their contrast, a browser did not.
- Compact and Roomy across whole windows; only the table header and the body's padding were
  measured at those densities.
- The other twenty families.
