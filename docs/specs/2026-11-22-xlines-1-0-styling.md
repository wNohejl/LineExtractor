# XLines 1.0 — the styling proposals, triaged against the desk

**Date:** 2026-11-22
**Status:** Triage done; items 1 to 5 of "What closes 1.0" built the same day, and item 6 done as [measurements in a browser](../evidence/2026-11-22-xlines-1-0-styling.md), which lists what was looked at and what was not.
**Input:** `docs/evidence/styling_improvements.pdf`, an export of a chat with another assistant (Gemini)
that proposes redesigns of the desk from screenshots. It is read as third-party suggestions.
**Closes:** the first bullet of Phase A in
[ADR 0031](../adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md) ("the
remaining styling work").

## What the PDF is

Four parts: an audit table of four rows (colour, numbers, density, window management); three
design concepts (A "Obsidian Quant", B "Tactical Slate & Amber", C "Bento Command"); a
"component and token contract" for training a model to write MudBlazor (a `MudTheme` in hex, a
`lo-*` utility sheet, five mapping rules, sample `@page` workspaces built on `MudGrid`,
`MudDataGrid`, `MudTabs` and `MudDrawer`); and a closing set of "modular layout shells" and
mockups, among them an execution drawer and "one-click hedge execution".

It was written from screenshots, not from the source. Most of what it asks for is already on the
desk, often with the PDF's own hex values; much of the rest is something an Accepted ADR decided
against, with its reasons on record. Every verdict below was checked in the tree on this date.

## Verdicts

Already done: the desk has it. Adopt for 1.0: small, not built, worth closing 1.0 with. Later:
reasonable, not 1.0. Rejected: contradicts an Accepted ADR or DESIGN §1, named.

| # | Proposal | What the desk already has | Verdict |
|---|---|---|---|
| 1 | Audit: "true obsidian / carbon" surfaces (#0B0D10, #13161B, #1B1F26) with crisp hairlines | The **Obsidian** family (`DeskPalettes.ObsidianDark`: s0 #090A0D, s1 #111318, s2 #1B1F26, slate hairline at .15, accent #38BDF8, positive #00E676) under Character in `DeskThemeFamilies.Catalog`; Midnight for true black. Contrast held per block by `ThemeContrastTests` (audit 2026-11-20) | **Already done.** The family is the PDF's palette, token for token |
| 2 | Audit: tabular numerals and a monospace stack so odds align | `.num`, `.mono`, `.grid td`, `.price__odds`, `.price__ev`, `.value__ev`, `.metric__note`, `.field.num` all set `tabular-nums` (`desk.css`, `xlines.css`); ADR 0016 sets one face for figures and prose; ADR 0030's Typeface make offers Mono (Cascadia Mono, Consolas) to whoever wants it | **Already done.** A forced second "data" face is what ADR 0016 retired (`--face-data`) |
| 3 | Audit: `Dense="true"` grids, rows of 36–40px, tighter tiles | `DeskGrid` renders `MudDataGrid` with `Dense="true"` always; a grid row is 20px + 2×`--cell-pad-y` + 1px = 29px at Standard; the Density make (ADR 0030) has Compact | **Already done.** The proposed 36–40px rows are looser than the desk's |
| 4 | Audit: collapse helper prose into `MudTooltip` icons | The desk keeps reasons in visible text on purpose: `BoardPanel` notes that a title on text that takes no focus reaches only a pointer (HIG 12); `Note` is passive by design. One standing note does cost the lines the PDF counts: `LineWatch.BoardHint`, two sentences above the Board every time Line watch, the featured workspace, is on the desk | **Adopt for 1.0, narrowed:** fold that one note to a sentence and a disclosure; no tooltips (§ What closes 1.0, item 3) |
| 5 | Audit: map windows to `MudTabs`, `MudSplitter`, `MudDrawer` | ADR 0007 (one row of columns, dividers in the gutter), ADR 0027 (a column past the limit becomes a tab group; tabs stay mounted), ADR 0013 (follow-ups float, non-modal). The "manual column counters" are the footer's "2 columns, 3 windows" (ADR 0027 amendment) | **Rejected** by ADR 0007 and 0027: the desk is the window manager, and it already tabs |
| 6 | Concept A "Obsidian Quant": pitch dark, no shadows, 4px radius, 38px bar, Ctrl+K, status chips | Obsidian family; Shape Sharp puts `--radius-panel` at 4px; Compact puts the title bar at 30px; `CommandPalette` on Ctrl+K; the pulse strip and the header's lit keys report status (ADR 0007). Data surfaces are already flat (ADR 0016); only floating things carry depth | **Already done** as Obsidian + Sharp + Compact. Zero shadows on floating layers is not taken: depth is how a material reads (ADR 0016) |
| 7 | Concept B "Tactical Slate & Amber": amber for focus and selection, cyan for fair benchmarks, a 56px mini-drawer rail | ADR 0016: one accent, spent only on interactivity, focus and selection. The PDF's own palette makes Warning #F59E0B the same amber as its focus colour; Obsidian's warning is #F5A524. DESIGN §6: "there is no longer a rail docking to an edge"; the header's `WindowBar` keys switch windows | **Rejected** by ADR 0016 (amber selection reads as a warning; cyan is a second accent) and DESIGN §6 (no rail). Charcoal grounds exist already (Standard, One, Primer) |
| 8 | Concept C "Bento Command": launcher cards, violet primary, pill filters | The empty desk (`Desk.razor`) features one workspace with the page's one Filled key, lists the rest and the saved ones (`WorkspaceList`), offers a single window and Ctrl+K, and folds the mechanics into "How the desk works". Violet and Purple are accents in Standard and most families; `Tag` is the pill | **Already done.** A 2×2 card grid would put a hairline box round each choice, which the one-edge rule forbids (apple-hig-components) |
| 9 | A `XLinesTheme.ObsidianQuant` `MudTheme` in hex | `DeskTheme.For` builds one `MudTheme` per family from its `DeskPalette`, which `DeskThemeTests` holds to its `themes.css` block (ADR 0008 seam, ADR 0016 amendment) | **Already done.** A hand-written second `MudTheme` would drift from the token blocks; the seam exists to prevent that |
| 10 | `xlines-terminal.css`: `.lo-num`, `.lo-pane`, `.lo-odds-cell`, `.lo-workspace-card`, `.lo-kbd` | `.num` is `.lo-num`; windows are `.win` with the elevation ramp; `.price` is the odds cell; `.desk-empty__row` the workspace row; `.desk-empty__single kbd` and `.palette__kbd` the keycaps. The `lo-*` rules write literal hex and a 1px border round every pane | **Rejected** by ADR 0016 ("hairlines separate; they never outline"), the apple-mudblazor rule that every colour is a token (`DeskCssConventionTests` fails a literal), and the one-edge rule |
| 11 | `MudDataGrid` for every table, `FixedHeader`, `Truncate` | `DeskGrid` is `MudDataGrid` (sorting, resizing, child rows, skeletons, `FixedHeader` as a parameter), restyled through `mud-bridge.css` | **Already done** |
| 12 | `MudAppBar`, `MudMenu` dropdowns, `MudDrawer`, routed `@page "/workspace/…"` screens | ADR 0007: one route, fixed header and footer, windows between; workspaces are named layouts, not pages; DESIGN §6 | **Rejected** by ADR 0007: deep links were removed on purpose, and two UIs for one data drift |
| 13 | Workspace launcher: Line watch featured, Morning triage, Journal & review, Bet a game, saved desk, Ctrl+K | `WindowCatalog.Workspaces` holds exactly these four, Line watch first and featured; "Saved on this desk" lists the operator's own; the layout survives a reload (`DeskLayout`), so "Restore desk" has nothing to restore | **Already done** |
| 14 | Live counts on the launcher ("18 games · 4 +EV", "3 open alerts") | Not built. The Desk names no domain (`DeskVocabularyTests`), so it needs a host seam on `Workspace`, and a read on the first screen before any window has loaded | **Later.** A feature with a new seam, not a finish |
| 15 | A docked right-hand inspector that a price press populates | Line watch (ADR 0029): a Board press re-aims the Line movement window in the next column to that game, market and side; the Journal is a tab behind it | **Already done** |
| 16 | A slide-over `MudDrawer` (`Temporary`, 480px) or bottom sheet for tickets and box scores | ADR 0013: follow-ups add a window, and float as non-modal `DeskDialog`s once every column is in use, because comparison is never modal (ADR 0016's modal tier is for one self-contained task) | **Rejected** by ADR 0013 and 0016 |
| 17 | Journal ledger beside a performance pane with an equity curve and breakdowns | The Review workspace is Journal beside Performance; Performance has ROI, net, win rate, beat the close, value at close, the bankroll curve on `DeskChart`, and breakdowns by market, sport and book (DESIGN §6) | **Already done** |
| 18 | Morning ops triage: feed health, incidents, runs, credits | The Morning triage workspace is Ops, Incidents, Runs; Ops states source health and the month's credits (ADR 0026) | **Already done** |
| 19 | Heat-mapped odds cells: green or red fill by EV | Each `PriceCell` is a pressable key carrying the best price, the book's monogram, the spread rail and its EV in `--state-positive-ink` when positive (`.price__ev--plus`); the Value column sorts by best EV | **Rejected** by ADR 0016 (hue never on a control; state lives on the value, as it already does) and ADR 0013 (the rail is the one loud element on the board) |
| 20 | Inline sparklines in board rows | `DeskSparkline` exists and is spent in the Line movement window and `MarketMovement`, not on the Board | **Later.** Six per row would compete with the rail; movement is one press away in Line watch |
| 21 | "One-click hedge execution", an "execution drawer", Discover → Validate → **Execute**, an "Execution" chip | DESIGN §1: no integration with any wagering flow, no "place bet" anywhere; the journal records bets placed elsewhere; ADR 0031 carries the rule to XLines | **Rejected** by DESIGN §1 |
| 22 | `<LoWidgetShell>` and three layout shells (master-detail 70/30, triage drawer, three-stage flow) | Every panel is a `DeskPanelBase` window with one anatomy (ADR 0020: title, toolbar, note, metrics, sections); workspaces are the layout shells, and the primary window's share is the 70/30 (ADR 0007) | **Already done**, except the drawer (row 16) |
| 23 | Secondary hues for the obsidian look (the PDF's violet #A78BFA, its slate) | Obsidian offers one accent, Sky (`DeskThemeFamilies.Catalog`, `[new("sky", "Sky")]`), where ADR 0030 says every family but High contrast offers three or four. ADR 0030's family list also leaves Obsidian out (it counts twenty-two) | **Adopt for 1.0** (item 2) |
| 24 | A "component and token contract" so a model writes compliant markup | The apple-mudblazor and apple-hig-components skills, the Parts bin, and `DeskConventions.All` in `XLines.Desk.Testing` are that contract, enforced by tests | **Already done.** A fine-tuning dataset is out of scope |

**Count:** 13 already done, 2 adopt for 1.0, 2 later, 7 rejected (24 rows).

## Found while checking, not in the PDF

- **The sticky table header misses at two of three densities.** `.grid th` pins with
  `top: -14px`, commented "cancels .win__body padding". Since ADR 0030, `.win__body` pads by
  `calc(var(--space-4) - var(--space-1) / 2)`: 14px at Standard, 10.5px at Compact, 17.5px at
  Roomy. At Compact the header pins 3.5px above the scroll edge and its top is cut; at Roomy rows
  show through a 3.5px slit above it. The audit's item 8 suspected a slit at Standard; the
  arithmetic says Standard is exact and the other two are not. Not yet seen in a browser.
- **Audit items 5 and 7 are still open:** no `--radius-inset` in the tree, and `DeskPalette`
  carries no inks, so `DeskChartPalette.For` still passes state fills that measure under 3:1 on
  six light desks (audit §3.5).

## What closes 1.0

Little remains. In order, each a few hours:

1. **The sticky header follows Density.** `desk.css`: one token for the window body's padding in
   the geometry `:root` block, spent by `.win__body`'s padding and by `.grid th`'s `top` (as its
   negative). Done: a hand-written `.grid` table scrolled in a window shows no slit and no cut
   heading at Compact, Standard and Roomy, checked in a browser with computed styles (ADR 0016).
2. **Obsidian gets its accents.** `DeskFamilyAccents.Obsidian` with Sky, Violet (the PDF's
   #A78BFA by night) and Slate; two compound blocks each in `themes.css`; the README's families
   row; ADR 0030's Character list names Obsidian and the count reads twenty-three. No amber and no
   green: they are this family's warning and positive. Done: `DeskThemeTests` and
   `ThemeContrastTests` green with the new rows, and the picker shows three swatches.
3. **The Line watch note is one line.** `LineWatch.BoardHint` keeps its first clause ("pressing
   a game shows it in Line movement"); the rest moves into a `DeskDisclosure` inside the same
   `Note`, as `SlipPanel`'s report does. Done: the Board's first row sits one line higher in Line
   watch, the full text is one press away from the keyboard, and `LineWatchTests` assert both.
4. **Concentric radii** (audit §4.1, item 5). `--radius-inset` in the geometry block;
   `.bar__menu-panel .bar__key`, `.popover__panel .menu__item`, the window strip's keys and
   `.launcher__item` spend it. Done: a `DeskCssConventionTests` case holds child = container −
   inset for each pair, under all three shapes.
5. **Chart series on inks** (audit §4.1, item 7). The four state inks join `DeskPalette` and its
   mirror test; `DeskChartPalette.For` takes the ink for any series whose fill is under 3:1 on
   `--surface-1`; slot order unchanged. Done: every non-dim slot clears 3:1 on its panel in every
   block, by test.
6. **One browser pass before the tag.** Standard, Obsidian and High contrast, dark and light, at
   Compact and Standard density: the Board in Line watch, the empty desk, a tab group, the
   Activities stack fanned. Done: screenshots in `docs/evidence/`, and anything that
   lost the cascade fixed or listed.

## The owner's calls, not on the list

- Audit §4.2 items 9, 11 and 12 (Increase Contrast per family, stronger light hairlines, field
  edges) remain judgment calls with their numbers already measured; none blocks 1.0.
- **"Place wager" and "Bet a game."** The PDF's "Execution" chip prompted a look at the desk's own
  words. DESIGN §1 forbids a "place bet" button and names the UI verbs "Log entry" and "Record
  result"; the desk's key and window are labelled "Place wager" (`WagerKey.PlaceLabel`,
  `WindowCatalog`) and a workspace is "Bet a game", while DESIGN §6 and the README name both as
  built. The key opens a journal form and records nothing elsewhere, so this is wording, not
  behaviour. Renaming them before XLines goes public touches about a dozen components and as many
  test files; keeping them is defensible. Either way DESIGN §1 and §6 should agree.
