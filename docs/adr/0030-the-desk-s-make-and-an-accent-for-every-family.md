# ADR 0030 — The desk's make, and an accent for every family

**Status:** Accepted, 2026-11-21. Extends [ADR 0016](0016-apple-feel-design-system.md) (one token
block per theme) and the Window manager of [ADR 0007](0007-window-manager-ui.md).

## Context

The owner's word for the themes was "very basic". Nine families each changed the palette and
nothing else: every desk had the same corners, the same air, the same ground and the same face,
and only Standard took a chosen accent, so a Nord desk was one fixed thing. About thirteen more
families are coming, which the Window manager's flat row of tiles could not hold, and every list
of families — the picker's, the boot script's, half the tests' `InlineData` — was written out by
hand in a different file.

## Decision

**Four makes, beside the palette.** A theme stays a family and a mode. What it is made of becomes
four further choices that belong to every family, each one attribute on `<html>`, absent at its
default, and one token block per position in `desk.css` keyed on that attribute alone — never on a
`[data-theme]`, because a make is not a palette:

- **Shape** (`DeskShape`: Standard, Sharp, Round; `data-shape`) rescales the five radii together,
  `--radius-xs` (new: the plate inside a control, which was a 4px literal) to `--radius-sheet`, so
  the ladder keeps its order and an inner corner written from an outer one stays concentric.
  Switches, radios, dots and swatches stay round in every shape: they are circles and capsules,
  not corners, and a square switch reads as a checkbox.
- **Density** (`DeskDensity`: Standard, Compact, Roomy; `data-density`) rescales the spacing ramp,
  the title bar and footer, and the control, row and cell heights. Those heights were 28px, 24px
  and 34px literals in forty-odd rules across `desk.css` and `controls.css`; they are now
  `--control-h`, `-sm` and `-lg`, `--row-h`, and `--cell-pad-y`/`--cell-inset` for MudDataGrid
  through the bridge. Compact takes the air out and never shortens a target: every control stays
  at least Standard's height, and 28px stays the floor of anything pressed. Roomy grows both.
- **Backdrop** (`DeskBackdrop`: Dots, Plain, Gradient, Grid; `data-backdrop`) is drawn on `.desk`,
  the ground behind the windows, and nowhere else. Dots is the faint grid the desk already drew,
  so it is the default and stamps nothing. Every pattern is a token: the grid's lines are
  `--surface-2`, the dots `--surface-3`, and the glow is the accent mixed into the ground at
  `--backdrop-tint` (9%). Windows are opaque and chrome is material, so the only text on a pattern
  is the empty desk's start screen; ThemeContrastTests holds its primary and secondary text to
  4.5:1 over the glow on every desk in every accent. A contrast theme gets the plain ground.
- **Typeface** (`DeskTypeface`: System, Humanist, Serif, Mono; `data-font`) sets `--face-ui` and a
  new `--face-display`, which the titles now spend. The stacks are local — nothing is fetched — and
  each lands on something visibly its own on Windows 11: Calibri, Sitka (Cambria), Cascadia Mono
  (Consolas). There is no Rounded: Windows has no rounded face of its own, and a choice that fell
  through to Segoe there would change nothing for the person who asked for it.

The geometry tokens left the Standard dark block for a plain `:root` block after the theme blocks,
for the reason the scales sit outside them: a swatch carries its own `data-theme`, and a block that
declared the radii or the faces would put the defaults back inside every swatch.

**An accent for every family.** A family's row now carries its accent list, its own accent first
(`DeskFamilyAccent`: key, name, dark and light palettes). The first stamps nothing and has no
block; each later one is two compound blocks, `[data-theme="<slug>-dark"][data-accent="<key>"]`
and `-light`, declaring exactly the six tokens Standard's always have, mirrored in the row, and
listed in the boot script's `ACCENTS` table. `ThemeService` keeps the choice per family under
`desk.accent.<slug>`, holds a stored key to the family it is stored under, and spends the showing
family's; Standard reads the old `desk.accent` while its own key is empty, so a choice made before
this is honoured and one made since is never overruled by it. The accents for the other families
are designed for their grounds and supplied with them; none is borrowed from Standard.

**The catalogue is the one list.** `DeskThemeFamilies.Catalog` holds a row per family — member,
name, slug, picker heading (`DeskThemeGroups`), both palettes, accents — and the picker, `DeskTheme`,
the boot test and every per-family test iterate it. Adding a family is a row, two palettes, two
blocks and a boot entry; the tests that fail when one is missing are named in the Desk's README.

**Twenty-three families under four headings.** The fourteen that followed — Paper, Midnight,
Obsidian, Synthwave, Terra, Everforest, Kanagawa, One, Monokai, Ayu, Primer, Night Owl, Palenight
and Zenburn — landed as rows, and the seven families before them gained accents of their own (every
family but High contrast now offers three or four). The picker files them under plain words that
help a person choose, not where a palette came from: **Built in** (Standard, High contrast),
**Character** (Paper, Midnight, Obsidian, Synthwave, Terra — desks drawn from a material rather than an
editor), **Soft** (Nord, Rosé Pine, Catppuccin, Everforest, Kanagawa, Zenburn, Palenight,
Solarized, Gruvbox — muted, low-contrast grounds) and **Vivid** (Dracula, Tokyo Night, Monokai,
One, Night Owl, Ayu, Primer — saturated hues). "Classics" is retired. The lists live in
`DeskFamilyAccents`; a family's grey is called Graphite, as Standard's is, unless its palette
names it. `--on-accent` is the family's and is not always white (Ayu's dark label in both
appearances, Midnight and Synthwave dark, Paper's and Terra's parchment, Monokai and Zenburn
light), so every accent's fill is chosen to carry its family's label.

**A picker that scales.** `DeskThemePicker` draws every family as a tile painted by its own blocks
(and its remembered accent) under plain headings, in a grid that wraps. It is one radiogroup
however many headings: one Tab stop, arrows travel every tile in reading order and stop at the
ends. The choice is a ring, a tick and a heavier name, not hue alone. The Window manager's
Appearance group holds Mode, Theme, Accent, Glass and the four makes as gates, and "Reset
appearance" puts every one back — family unchosen, so Increase Contrast may choose again — in one
cross-fade, offered only while something is off its default.

*Amended 2026-11-22 ([ADR 0032](0032-a-window-s-sections-are-cards-in-a-flow.md)).* The headings
are a filter now, not shelves: under floating headings the shelves were ragged (two tiles, then
five, nine, seven). The picker is one grid; `Group` narrows it to one heading's families (null or
`""` for all) and `DeskThemePicker.ThemeFilters` is the segmented choice a caller draws over it.
It stays one radiogroup, and when the filter hides the chosen tile the first tile showing holds
the Tab stop. The themes left the Appearance group for a card of their own, `Rest` of the
Window manager's first row, with the filter in its header bar opening on All; Appearance leads with Mode beside Accent in one split row, then Glass and the makes.

Every setter cross-fades as the others do, and skips the fade when no stamped attribute moved.

**One row per look, and no tables in the boot script** (amended 2026-11-21). The glass and the four
makes each cost about nine edits in `ThemeService`, three in `theme.js`, a row in `theme-boot.js`,
an option list and a row in the Window manager and rows in three test tables — a ninth setting was
eight files and some seventy lines. Each is now a `DeskLook` row in `DeskLooks.All`: its label,
storage key, attribute, positions (stored name, stamped value or null for the default, gate label)
and the machine setting it yields to (the glass yields to Reduce Transparency). `ThemeService`
holds every look in one table and has one setter, one stamped-attribute comparison, one reset and
one `applyLooks` call carrying an attribute-to-value map; `Glass`, `SetShapeAsync` and the rest stay
as typed views of it. `theme.js` reads, writes and stamps whatever keys and attributes it is handed.
The Window manager draws a gate per row. Adding a setting is an enum, a row and a CSS block; the
storage keys, stored values and attributes are the ones they were, so every browser restores as
before.

The boot script's `SLUGS`, `ACCENTS` and `LOOK` tables were copies of the catalogue held to it by
regex tests. `DeskHead` now writes them from C# (`DeskBoot.Json`, serialised once, with the
default encoder escaping `<`, `>` and `&`) as a `<script type="application/json" id="desk-boot">`
block ahead of `theme-boot.js`. `DeskHead` renders statically in the head, so the tables are in the
first response; a data block is never executed, so the edge's script policy does not reach it. A
host whose head lacks the block, or a block that does not parse, boots the plain desk by the stored
mode, and ThemeService paints the rest when the circuit opens.

The arrow-key switch the three self-drawn radiogroups (`DeskThemePicker`, `DeskAccentPicker`,
`DeskRadioGroup`) each wrote out is `Roving.Next`, and `theme.js` holds the keys from scrolling the
page on any radiogroup that carries `data-roving` instead of naming the pickers' classes.

## Consequences

- `ThemeService.Accent` is a `DeskFamilyAccent`, not a `DeskAccent`; `SetAccentAsync(string)` sets
  the showing family's, `SetAccentAsync(DeskThemeFamily, string)` any family's, and the
  `DeskAccent` overload still sets Standard's. `DeskAccentPicker` takes a `Family` and a key.
- The picker's order is the catalogue's: High contrast now sits beside Standard under "Built in".
- An application's own sheet keeps its literal heights until it spends `--control-h`; the Desk's
  controls follow Density, a host's hand-drawn ones do not.
- Mono is wider than any other face at the same size: a column sized in `ch` is fine, one sized in
  pixels for Inter may clip.
- `DeskRadioGroup` now carries `data-roving`, so its arrows, Home and End no longer scroll the page
  under it, as the two pickers' never did.
- `theme.js`'s exports for the looks changed (`readValues`, `storeValue`, `applyLooks`,
  `forgetAppearance(slugs, looks)` in place of `readGlass`, `readChoice`, `applyGlass`, `applyLook`
  and the rest); it ships with `ThemeService` and nothing else calls it.
