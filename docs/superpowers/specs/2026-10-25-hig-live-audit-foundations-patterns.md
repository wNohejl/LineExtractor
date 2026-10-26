# HIG live audit: Foundations and Patterns, 25 October 2026

Audit of the desk (`src/LineOps.Desk`, panels in `src/LineOps.Web/Components/Panels`) against
the live Human Interface Guidelines, read today as Markdown (`<page>.md`). It covers Foundations
and Patterns only. Components are a sibling audit. It does not re-open anything already
decided in `2026-10-25-hig-desk-audit.md`, `2026-10-24-panel-anatomy-design.md` or the
`apple-hig-components` skill: no outlined or nested boxes, macOS density (13px body, 28px
controls), one accent and only for interaction, sentence case, and a 1px hairline edge on a group
or card. The wager window's two cards are out of scope.

Line numbers are taken at HEAD `195e41e`. That commit landed during the audit and moved
`lineops.css` from 4865 to 4909 lines, so lines after about 3300 were re-checked.

Contrast figures are WCAG 2 ratios computed from the token blocks, with each alpha token
composited over `--surface-1`. The HIG's own thresholds (Accessibility, March 2025) are 4.5:1 for
text up to 17pt, 3:1 at 18pt or bold, and 7:1 to strive for with custom colours in small text
(Dark Mode).

| Theme | secondary | tertiary | accent as text | positive | negative | warning | white on accent |
|---|---|---|---|---|---|---|---|
| apple-dark | 5.72 | **2.70** | **4.30** | 7.75 | 4.60 | 7.62 | **3.65** |
| apple-light | 5.74 | **2.11** | **4.02** | **2.22** | **3.55** | **2.20** | **4.02** |
| nord-dark | 5.70 | **2.37** | 6.24 | 6.13 | **3.05** | 8.00 | 6.24 |
| nord-light | 5.61 | **1.92** | **3.72** | **2.93** | **3.78** | **2.63** | **4.03** |
| solarized-dark | 5.54 | **2.34** | **4.08** | 4.69 | **3.25** | 4.68 | **3.68** |
| solarized-light | 5.63 | **1.88** | **3.41** | **2.97** | **4.29** | **2.98** | **3.68** |
| rose-pine-dark | 5.62 | **2.49** | 7.88 | 9.68 | 5.67 | 10.06 | 8.43 |
| rose-pine-light | 5.46 | **1.89** | **3.65** | **3.30** | **4.04** | **2.16** | **3.79** |
| contrast-dark | 12.58 | **3.77** | 9.07 | 10.99 | 6.54 | 10.98 | 9.62 |
| contrast-light | 12.63 | **3.35** | 7.04 | 5.52 | 6.04 | 5.22 | 7.04 |

Apple's own increased-contrast light values, measured on white, all clear 4.5:1. Green
`#008932` is 4.54, orange `#C55300` 4.55, red `#E9152D` 4.56 and blue `#1E6EF4` 4.57. The
increased-contrast dark blue `#5CB8FF` is 7.28 on `#232326`.

---

## Color

Change log: **December 16, 2025** (Liquid Glass guidance updated); June 9, 2025 (system colour
values updated, Liquid Glass added).

- Don't use one colour to mean different things. If the brand or accent colour marks
  interactivity, don't also use it on non-interactive text.
- Every colour needs light, dark **and increased-contrast** variants that are significantly more
  differentiated.
- Don't rely on colour alone to show state or interactivity. Pair it with a label or a shape.
- Don't hard-code system colour values, and don't redefine the semantic colours (no separator
  colour as text, no secondary label colour as a background).
- Spend colour sparingly on floating controls: tint the background of the primary action, not
  several controls.
- macOS defines semantic roles (label tiers, separator, selected content, unemphasized
  selection, keyboard focus indicator) and an app accent that the user's system accent setting
  overrides.
- The June 2025 system values changed. Dark blue is now 0,145,255, dark orange 255,146,48 and
  dark red 255,66,69; light blue 0,136,255, light orange 255,141,40 and light red 255,56,60.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| One colour, one meaning | Partly | Accent on non-interactive text: `.hdr__brand em` (`lineops.css:475`), `.desk-empty__mark em` (1302), `.wstep--current .wstep__title` (4513). Accent wash as a hover: `.pullmenu__item:hover` (2419), `.book-picker__list .book-row:hover` (4652). Accent as status: `.pulse--active` (1557), `.desk-toast--action` (`mud-bridge.css:517`). | Use `--text-primary` for the wordmark `em`s and the current-step title (weight already distinguishes it). Use `--surface-3` for the two hover fills. Use `--text-secondary` for the Info toast edge. Keep the accent for focus, selection and Filled/Tinted. |
| Increased-contrast variant of every colour | Not met | No `prefers-contrast` rule in any CSS file. `theme.js` watches only `prefers-color-scheme` (`theme.js:51-91`). The High contrast family exists but is opt-in only. | In `theme.js`, also watch `(prefers-contrast: more)`. Have `ThemeService` resolve the `contrast-*` block while it matches and the operator is on Standard, the same way System resolves dark or light. |
| Not colour alone | Partly | The pulse strip says healthy, warn or critical by hue only. It is labelled for screen readers (`AppWindow.razor:96-97`), but sighted readers get no shape. `.up`/`.down` carry a sign. Tags carry words. Met there. | When `Window.Pulse` is Warn or Critical, put a 12px warning or alert glyph before `win__status` (`AppWindow.razor:56-59`), so the bar says it in shape too. |
| Don't redefine semantic roles | Met | `--separator` is never used as text. Text tiers stay text. `--chart-neutral` is a separate, opaque token (`lineops.css:70-77`). | n/a |
| Current system values | Partly | `--accent: #0A84FF` (48), `--state-warning: #FF9F0A` (64), light `--accent: #007AFF` (265) and `#FF9500` (287) are the pre-June-2025 values. | Update the `--state-*` fill tokens to the 2025 values in both apple blocks and in `DeskTheme.cs` (ADR 0016 mirror). **Keep the accent fill**: white on `#0091FF` is only 3.23:1. |
| Light, dark and contrast palettes per theme | Partly | Every family has dark and light. Only one family has contrast. Light-theme state colours fail as text (table above). | See the state-ink finding under Accessibility. |

## Dark Mode

Change log: August 6, 2024 (art only). The rules predate Liquid Glass.

- Don't offer an app-specific appearance setting. The app should follow the system, and should
  keep following it while it runs ("Auto").
- Contrast must be at least 4.5:1. For custom foreground and background colours, strive for
  **7:1, especially in small text**.
- Test with Increase Contrast and Reduce Transparency, both separately and together.
- Dark mode colours are re-derived, not inverted. Base surfaces recede and elevated surfaces
  advance (popovers, sheets, a window in front).
- Use the label tiers for labels and system views for text fields.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| No app-specific appearance setting | Partly | Default is System (`ThemeService.cs:48`) and the System option keeps following live (`theme.js:72`). Dark, Light and System are still offered as a gate (`WindowManagerPanel.razor:315-320`). | Keep it, because families are a legitimate personalization. Order it the macOS way: Light, Dark, Automatic. |
| 4.5:1 minimum, 7:1 for small custom text | Not met | `--text-tertiary` is 1.88 to 3.77 in all ten blocks. It is used for information people must read, not only for placeholders. See Accessibility. | Move the call sites to `--text-secondary` (the house rule is "fix the call site, never brighten the dim token"). |
| Base vs elevated | Met | `.deskdialog` and `.palette__panel` sit on `--material-thick` (2263, 1742). Windows are opaque `--surface-1`/`--surface-2`. The void is `--surface-0`. | n/a |
| Tested under Increase Contrast and Reduce Transparency | Not met | Neither preference is read (see Color and Materials). | See those rows. |

## Typography

Change log: **December 16, 2025** (emphasized weights added to every platform's text-style
table); March 7, 2025 (Dynamic Type expanded).

- macOS sizes: default 13pt, **minimum 10pt**. Avoid Ultralight, Thin and Light weights.
- macOS styles (size/leading, weight, emphasized weight): Large Title 26/32 Regular, Bold.
  Title 1 22/26, Bold. Title 2 17/22, Bold. Title 3 15/20, Semibold. **Headline 13/16 Bold,
  Heavy.** Body 13/16 Regular, Semibold. **Callout 12/15. Subheadline 11/14. Footnote 10/13.**
  Caption 1 and 2 10/13.
- Use few typefaces. Build hierarchy from weight, size and colour, and keep it intact when text
  grows.
- Use the built-in styles. Use tight leading only up to two lines. Don't use tight leading for
  three or more lines.
- Tracking is size-specific: +0.12pt at 10, 0 at 12, **-0.08pt at 13**, -0.23pt at 15. Small UI
  text never gets positive em-tracking.
- macOS has no Dynamic Type. Larger text must still keep truncation down and keep the hierarchy.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| 10pt floor at every setting | Not met | `--text-caption: calc(10px * var(--text-scale))` (108) and `html { zoom: var(--ui-scale) }` (361). The settings window offers Interface 80% and Text 90% (`WindowManagerPanel.razor:327,336`), so caption renders at 8px and footnote at 8.8px. | Clamp the ramp: `--text-caption: max(10px, calc(10px * var(--text-scale)))`. Drop the 80% interface scale (or stop the zoom shrinking type below 10px by dividing the clamp by `--ui-scale`). |
| One family | Met | `--face-ui` is one family. Numbers use `tabular-nums` (`.num`, 398-402). | n/a |
| Style names match the platform | Partly | The desk's `--text-subheadline` is 12px (Apple's Callout) and `--text-footnote` is 11px (Apple's Subheadline) (`lineops.css:106-107`). Designers reading the HIG table pick the wrong token. | Rename to `--text-callout: 12px` and `--text-subheadline: 11px`, and keep the sizes. Add the Apple `--text-footnote` as 10px only if a call site needs it. |
| Headline is Bold, emphasized weights exist | Partly | `.win__title` uses `--text-headline` with `--weight-semibold` (1421-1422), and so do `.panel-h` and `.mud-dialog-title`. There is no token for an emphasized style. | Add `--weight-headline: 700`, use it on `.win__title`, `.desk-sheet__title` and `.mud-dialog-title`, and document the emphasized weight beside each style in the token block. |
| Leading | Partly | `--leading-body: 1.45` (111) is 18.9px at 13px. macOS Body is 16pt (1.23). It is inherited by every grid cell and row. | Make `--leading-body: 1.25` for UI. Add `--leading-prose: 1.45` for Notes, empty-state paragraphs and `.desk-empty__hint`. |
| No positive tracking on small UI text | Not met | `.mud-dialog-title` .05em (`mud-bridge.css:395`), `.mud-tooltip` .02em (425), toast .01em (505), chart headline .01em (579), legend .04em (599). | Set all five to `letter-spacing: 0` (or -0.006em at 13px). Tracking is reserved for the wordmark (`.hdr__brand`, 469). |
| Text styles, not literals | Partly | Off-ramp sizes are still in the bridge: `calc(12.5px …)` (`mud-bridge.css:503,577`) and `calc(10.5px …)` (598). | Snap them to `--text-subheadline` (12) and `--text-footnote` (11) respectively. |
| Text grows without breaking hierarchy | Met | `--text-scale` multiplies the ramp only (`lineops.css:337-345`). Window titles and keys use the same ramp, so they grow together. | n/a |

## Layout

Change log: **September 9, 2026** ("updated guidance to reflect current best practices"); June 9,
2025 (Liquid Glass).

- Order content by importance, leading edge and top first. Align things to make them scannable,
  and use indentation for hierarchy.
- Group with negative space, container shapes or separators. Use progressive disclosure:
  disclosure triangles, menus, nested views.
- Set controls apart from content with a distinct layer, not a solid background behind them.
- Adapt to window size and text size. Keep functionality the same when space changes; only show
  less.
- **macOS: avoid putting controls or critical information at the bottom of a window**, because
  people push windows below the screen edge.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Nothing critical at the bottom | Partly | The only way back from an eviction (the refusal and its Undo) lives in the 38px footer (`DeskFooter.razor:16-35`, `.ftr__refusal` 1209). The browser chrome and the OS taskbar sit right under it. | Show the eviction notice and Undo in the header, after `.hdr__count` (`DeskHeader.razor:126`), where the click that caused it was aimed. Keep the footer for passive stats. |
| Grouping, alignment, disclosure | Met | Group drawers (`.bar__menu-panel`, 818), snippets, PanelSection and the grouped forms follow the house rulebook. | n/a |
| Controls vs content | Met | Chrome (header, footer, drawers, dialogs) is material. Window bodies are opaque. | n/a |
| Adapts without losing function | Met | `@container bar (max-width: 700px)` drops names but keeps keys (874-880). `.hdr__sub` hides at 900px (520). | n/a |

## Materials

Change log: **September 9, 2025** (Liquid Glass updated); June 9, 2025 (Liquid Glass added).

- Liquid Glass is the functional layer: controls and navigation float above content. **Don't use
  it in the content layer.** Use it sparingly.
- Use the regular variant (blurred, luminosity adjusted) for text-heavy floating things such as
  alerts, sidebars and popovers. Use clear only over media, with a 35% dim if the background is
  bright.
- Standard materials give structure inside content. Choose them by purpose, not by the colour
  they impart.
- Put vibrant (alpha-tier) labels on materials. Use thicker materials for fine text.
- Appearance changes with Reduce Transparency and Increase Contrast.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| No glass in the content layer | Met | Window bodies stay opaque by design (`lineops.css:78-81`). | n/a |
| Sparingly, by purpose | Met | Thin for the header and footer, regular for drawers, thick for dialogs and the palette (`--material-*`, 82-86). | n/a |
| Respect Reduce Transparency | Not met | Only a no-support fallback exists: `@supports not (backdrop-filter …)` (4144, 4656). There is no `prefers-reduced-transparency` rule. | Add `@media (prefers-reduced-transparency: reduce)` with the same body as the `@supports not` block at 4144 (every material becomes its `--surface-*` tier). |
| Vibrant labels on materials | Met | Text tiers are alpha white or black (40-44), so they composite over any material. | n/a |

## Motion

Change log: September 9, 2025 (Liquid Glass); June 10, 2024.

- Add motion on purpose. Make it optional and never the only carrier of information.
- Keep feedback animations brief and precise. **Avoid motion on UI interactions that happen
  often.**
- Let people cancel motion, and don't make them wait for an animation.
- Liquid Glass motion is "more subdued" with a trackpad or mouse than with touch.
- Accessibility (Reduce Motion): cut automatic and repetitive animation, tighten springs to
  reduce bounce.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Optional, honours Reduce Motion | Met | A global `prefers-reduced-motion` rule (407-414) plus per-block rules. The pulse breathe and sweep stop. | n/a |
| Subdued for pointer, no bounce on frequent interactions | Partly | `--ease-spring: cubic-bezier(.32, 1.28, .42, 1)` overshoots on every gate selection (`.marble.gate__cap`, 983), every tab marble (925), every pull-menu and book-picker open (2389, 4639), and every dialog and sheet entrance (2272, 3915). | Use `--ease-standard` for `.marble`, `.gate__cap` and both `pullmenu-in` uses. Keep `--ease-spring` only for `deskdialog-in` and `desk-sheet-in`, which happen rarely. |
| No motion on frequent hover | Partly | `.metric--action:hover { transform: translateY(-1px) }` (1920). The glide plate also chases the pointer across every toolbar cluster (`.glide-plate` transform/width/height, 1114-1128). | Drop the translate (the fill and the chevron already say "pressable"). Keep the glide, but cut its `transform` transition to `--dur-fast` × 0.6 (about 90ms) so it tracks the pointer rather than trailing it. |
| Brief, cancellable | Met | `--dur-fast` .15s and `--dur-base` .25s. Everything is CSS transitions, which can be interrupted. | n/a |

## Accessibility

Change log: June 9, 2025 (Assistive Access, Switch Control, Nutrition Labels); March 7, 2025 (all
guidance refined).

- Contrast must be ≥4.5:1 up to 17pt and ≥3:1 at 18pt or bold, in **both** appearances. If the
  default can't reach it, provide a higher-contrast scheme under Increase Contrast.
- Let people enlarge text to **at least 200%**.
- macOS control size: default 28×28, **minimum 20×20**. Leave about 12pt around bezelled
  controls.
- Make everything reachable from the keyboard (Full Keyboard Access). Offer alternatives to
  gestures.
- **Minimize time-boxed elements.** Things that auto-dismiss fail people who need more time.
- Don't rely on one channel. Describe the UI for VoiceOver.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| 4.5:1 for readable text | Not met | `--text-tertiary` carries information in `.launcher__desc` (1831), `.metric__note` (1903), `.grid .dim` (1970), `.price--closing .price__sel` (2161), `.snippet__note` at caption size (3579), `.deskchart__takeaway` (`mud-bridge.css:583`), `.hdr__sub` (513), and every closed window key `.bar__key` (649). | Repoint each to `--text-secondary`. Keep tertiary for `::placeholder` (2794), disabled states and decorative marks only. |
| State colours as text, light themes | Not met | `.up`/`.down` (2547-2548), `.metric--good/warn/bad` (1892-1894), `.tag--good/warn/bad` (2593-2595), `.price__edge--wide` (2103), `.field-err` and the 26 other `color: var(--state-*)` rules. In apple-light they measure 2.22 (positive), 3.55 (negative) and 2.20 (warning). | Add `--state-positive-ink`, `--state-negative-ink` and `--state-warning-ink` to every block. For apple-light use Apple's increased-contrast values: `#008932`, `#E9152D` and `#C55300` (4.54/4.56/4.55). The dark ink can equal the fill. Change the text rules to `-ink`, and leave fills, dots and pulses on the fill token. |
| Accent as text | Partly | `--accent` as text is 4.30 (dark) and 4.02 (light), and 3.41 to 3.72 in the Nord, Solarized and Rosé Pine light blocks: `.bar__key--current` (703), `.desklink:hover` (2534), `.book-row--chosen .book-row__price` (4376). | Add `--accent-ink`: dark `#409CFF` (5.54), light `#0062CC`, which is `--accent-press` (5.80). Use it wherever the accent colours text or glyphs. Leave fills on `--accent`. |
| Enlarge text to 200% | Partly | Text scale tops out at 140% (`WindowManagerPanel.razor:340`). With interface 150% the total reaches 210%, but only by also scaling the layout. | Add 175% and 200% to `_textScales`. The gate already handles five or more positions with `--n`. |
| 28/20 control sizes | Met | Keys, gates, tabs and window controls are 28px (`.win__ctl` 1483-1484, `.gate__opt` 24 + 4 track). The smallest is `.ftr__dismiss`/`.ftr__undo` at 24px. | n/a |
| Keyboard alternatives | Partly | Divider resize has arrow keys (`Desk.razor:230-247`). Drawers walk with arrows (`WindowBar.razor:279-280`). **Window reorder is pointer-only** (`windowing.js:171-240`). | Add Ctrl+Alt+← and Ctrl+Alt+→ on a focused window (and "Move left" and "Move right" in the window's palette entry) calling `Manager.Reorder`. |
| Minimize time-boxed UI | Partly | Toasts auto-dismiss after 4000ms (`Program.cs:41`). Some carry the only report of a finished pull or a leg added. | Raise `VisibleStateDuration` to 6000. Keep the close icon. Check that Mud pauses the timer on hover; if it doesn't, set `RequireInteraction` for `Fail`. |
| VoiceOver labels | Met | Icon-only keys have `aria-label`s (`AppWindow.razor:72-89`). Glyphs are `aria-hidden` (`Glyph.razor:4`). Field errors are `role="alert"` (`DeskField.razor:41`). | n/a |

## Writing

Change log: **December 16, 2025** (clarified language patterns; added guidance on possessive
pronouns); February 27, 2023.

- Be clear, use fewer words, and be action-oriented: button labels are verbs.
- Build language patterns and apply one capitalization style consistently.
- **Use possessive pronouns sparingly**, and **never mix "my" and "your"** for the same thing.
- Empty states: give a clear next step and a button.
- Errors: close to the problem, no blame, say how to fix it. No "oops" and no robotic "Invalid".
- Settings labels: say what the setting does when on.
- Use hints and placeholders in fields. Errors sit next to the field.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Possessives, my/your mixed | Not met | The wager's step 1 is "Your bet" (`BoardWager.razor:157`). The key next to it is "Enter my own price" (280-281, again at 986). | Rename the step "Bet" and the key "Enter a price" (and its tooltip "Use the price you got"). Sweep the 12 "your" uses in `BoardWager.razor` with the same rule. |
| Empty states lead somewhere | Met | `EmptyStateKind` New, Filtered and Error, with Action (`EmptyState.razor`). The empty desk has two buttons (`Desk.razor:26-39`). | n/a |
| Clear errors | Partly | "The board did not load." with Try again (`PanelBase.cs:219`, `LoadFailure.razor`) is clean and blame-free, but never says why (database down, provider out of credits). | Have `PanelBase` map known failures (Npgsql connection, provider 401/429) to a second sentence such as "The database is not answering." |
| Consistent multi-step language | Met | Steps are "Bet / Stake / Review and log", and the commit key is "Log" or "Add as leg". | n/a |
| Sentence case | Met | Keys and headers ("Save changes", "Try again", "Morning triage"). | n/a |

## Icons

Change log: June 9, 2025 (a table of SF Symbols for common actions).

- Use recognizable, simplified, consistent icons: one size, detail level, stroke weight and
  perspective.
- **Match the icon's weight to the adjacent text.**
- Use standard glyphs for standard actions: `xmark` for close and cancel, `arrow.uturn.backward`
  for undo, `magnifyingglass` for search, `line.3.horizontal.decrease` for filter, `ellipsis` for
  more.
- Use vectors, and give custom icons alternative text.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| One consistent set | Met | 143 references, all `Icons.Material.Filled.*`. `Glyph.razor` renders them as SVG with `currentColor`. | n/a |
| Weight matches text | Partly | Filled Material glyphs (solid shapes) at 15-17px (`.bar__key svg` 17px, 716) next to 12px regular labels read heavier than the words. | Move `Glyph` to the Material Symbols Outlined path set at weight 400 (MudBlazor ships `Icons.Material.Outlined`) desk-wide, in one change in the catalogue and the windowing, so the set stays uniform. |
| Standard glyphs | Met | Close is `Close`, search is `Search`, refresh is `Refresh`, back and forward are chevrons. | n/a |

## SF Symbols

Change log: July 28, 2025 (Draw animations, gradients in SF Symbols 7).

- Use rendering modes (monochrome or hierarchical) and variable colour to show change, not
  depth.
- Use symbol animations judiciously and only with a clear purpose: Pulse and Breathe for ongoing
  status, Rotate for in progress, Bounce for "an action occurred".
- Custom symbols need alt text.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| SF Symbols themselves | N/A | They can't be licensed off Apple platforms. The desk's analogue is the Material set (above). | n/a |
| Animation semantics | Met | Critical status breathes (`.pulse--critical`, 1574), work in progress sweeps (`.pulse--active::after`, `.prog--indeterminate`, 3440), busy keys sheen (2744). Each animation matches its HIG meaning. | n/a |

## Patterns index

The live index (`patterns.md`) lists 25 patterns: Charting data, Collaboration and sharing, Drag
and drop, Entering data, Feedback, File management, Going full screen, Launching, Live-viewing
apps, Loading, Managing accounts, Managing notifications, Modality, Multitasking, Offering help,
Onboarding, Playing audio, Playing haptics, Playing video, Printing, Ratings and reviews,
Searching, Settings, Undo and redo, Workouts. This audit reads the ten the brief named. Of the
rest, Launching, Live-viewing apps and Multitasking are worth a later pass.

## Modality

Change log: December 5, 2023.

- Be modal only when there is a clear benefit. Keep the task short, and don't build an app
  inside it.
- Always give an obvious way to dismiss. On macOS that is a button in the content.
- **Confirm before closing if user content would be lost.**
- Title the modal with its task.
- Let people dismiss one modal before another appears; don't stack them.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Modal only with benefit | Met | DeskDialog is deliberately non-modal so games can be compared (`DeskDialog.razor:6-16`). DeskSheet is reserved for self-contained tasks. Alerts are reserved for decisions. | n/a |
| Obvious dismissal, titled | Met | Each has a Close key and a title (`DeskDialog.razor:28-41`, `DeskSheet.razor:35-49`). DeskAlert has Cancel, and Cancel is the default when the action is destructive (`DeskAlert.razor:57-66`). | n/a |
| Guard data loss on close | Partly | The only production modal with a form is the gallery sheet, and it closes on a backdrop click (`PartsPanel.razor:1009`). No component has a dirty-state guard (no `IsDirty` anywhere). | Give `DeskSheet` an `IsDirty` parameter. When it is true, set `BackdropClick = false` and route Escape and Close through `Alerts.ConfirmAsync("Discard changes?", …)`. |
| Don't stack modals | Met | Alerts stack only when a confirm is raised from a sheet. The focus hand-back is handled (`dialogs.js:85-109`). | n/a |

## Loading

Change log: June 9, 2025; June 10, 2024.

- **Show something as soon as possible**: placeholders, replaced as content arrives.
- Let people do other things while content loads.
- Be clear that content is loading and how long it will take: determinate progress when the
  length is known, indeterminate when not.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Show something at once | Partly | Ten panels use `DeskSkeleton` or `DeskGrid Loading`. Six still show a sentence instead: "Loading the game…" (`GamePanel.razor:30`), `PlayerPanel.razor:30`, `TeamPanel.razor:32`, `HeadToHeadPanel.razor:25`, `BoardForm.razor:15`, `BoardAllBets.razor:21`. | Replace those `<EmptyState>Loading …</EmptyState>` lines with a `DeskSkeleton` in the shape of the first section (title row plus three grid rows). That matches the other ten panels and stops the layout jumping. |
| Keep working while it loads | Met | Each window loads independently. Pulses sweep per window. | n/a |
| Determinate vs indeterminate | Met | `DeskProgress` has both (`.prog--determinate`/`--indeterminate`, 3434-3443). The backfill reports walked, skipped and failed (`HistoryPanel.razor:182-190`). | n/a |

## Feedback

No change log on the live page.

- Make feedback accessible, through more than one channel.
- **Put status near the item it describes**, where it doesn't interrupt.
- Use alerts only for critical, actionable information.
- **Warn before unexpected, irreversible data loss.** Don't warn when loss is the expected result.
- Confirm significant completions. Say when a command can't run, and why.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Status near the item | Met | Per-window pulse and `win__status` (`AppWindow.razor:56-59,96`). Inline Notes. Checks in the wager window. | n/a |
| Alerts only for decisions | Met | `Alerts.ConfirmAsync` is used for deletes and for starting a slip over (`JournalPanel.razor:568,585`, `SlipPanel.razor:373`). | n/a |
| Warn on unexpected loss | Not met | A half-filled Journal entry (`JournalPanel.razor:78-124`) or wager is dropped without a word when its window closes (`AppWindow.razor:152`, `Manager.Close`) or when LRU eviction closes it. Eviction says so after the fact, but the typed values are gone. | Give `WindowInstance` a `HasUnsavedInput` flag that panels set from their draft. When it is set, `WindowManager.Close` and eviction raise `ConfirmAsync("Close Journal and discard the entry?")`, and eviction skips the window as a victim. |
| Say why a command can't run | Partly | Journal's disabled Save explains itself only in a `title` tooltip ("Needs a selection, a stake above zero, a price and a book.", `JournalPanel.razor:121`). Keyboard and screen-reader users never hear it. The wager window shows its wait text visibly (`.wstep__wait`, 4484). | Render the same sentence as a visible `.wstep__wait`-style footnote beside the disabled key in `PanelForm`'s footer, and link it with `aria-describedby`. |

## Entering data

Change log: June 21, 2023.

- Get data from the system where possible, and prefill sensible defaults.
- Be clear about what is needed (label or hint text).
- **Offer choices instead of text entry when possible.**
- Support paste and drag.
- **Validate as people type**, and give feedback as soon as a problem is found. Make it clear
  what is required before they can proceed.
- macOS: show clipped field text in an expansion tooltip.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Choices over text | Not met | Once a Game is picked, Selection is still a free-text field with the placeholder "Team" or "over / under" (`JournalPanel.razor:681-685`), so a typo makes an entry that grading cannot match. | When `d.Game` is set, render Selection as a `DeskSwitch` of the two team names (moneyline or spread) or Over/Under (total). Keep the text field for "Other (describe)". |
| Live validation | Not met | No production form sets `Error=` on a field. The only one is the gallery (`PartsPanel.razor:209`). Journal validates only through the disabled Save. | Bind `Error` on Price ("Enter an American price such as -110 or +150"), Stake and Line in `LegFields` (`JournalPanel.razor:654-701`), shown once the field has lost focus. |
| Hints and defaults | Met | Placeholders give examples ("-110", "-3", "e.g. Player X over 1.5 receiving TDs"). Book defaults to the current book. | n/a |
| Expansion tooltip for clipped values | Partly | `.bar__key-name` and `.win__title` ellipsize (`text-overflow: ellipsis`, 655, 1426), and only some have `title`. | Set `title="@Window.Title"` on `.win__title` (`AppWindow.razor:56`) and on every ellipsizing `.bar__key-name`. |

## Searching

Change log: **June 8, 2026** (terminology updated, best practices refined); June 9, 2025.

- If search matters, give it a primary position.
- **Search everything from one place.** Local search can filter a section.
- Show the current scope clearly (placeholder, scope bar or title).
- **Offer suggestions**: recents before typing, predictions while typing.
- Respect privacy around history, and let people clear it.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| One place, primary position | Met | The palette is reachable with Ctrl+K or ⌘K anywhere (`windowing.js:26`) and from the header key (`DeskHeader.razor:108`). | n/a |
| Scope shown | Met | The placeholder says "Go to a team, player, game or window" and the results are grouped (`CommandPalette.razor:20,38`). | n/a |
| Suggestions and recents | Partly | Before typing it lists windows only (`Local`, `CommandPalette.razor:192-203`). When nothing matches, it says "Nothing to show." (31). There are no recents. | Keep the last eight chosen results (per browser, beside the layout) as a "Recent" group shown first when the field is empty. Add a "Clear recents" row at its foot. |
| Shortcut legend | Partly | `title="Go to anything (Ctrl+K)"` (`DeskHeader.razor:108`) says Ctrl on a Mac too. | Choose ⌘ or Ctrl from `navigator.platform` once in `windowing.js` and pass it to the header and to `palette__kbd`. |

## Settings

Change log: June 10, 2024.

- Choose defaults that suit most people, and keep the number of settings small.
- **Open settings in the expected way: ⌘, (Command-Comma).**
- **Respect system-wide settings, and don't duplicate them.**
- Keep task-specific options in the screens they affect.
- macOS: put a settings item in the app menu, not in the toolbar. Use a non-customizable pane
  toolbar, **put the current pane in the window title, and restore the last-viewed pane.**

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| ⌘, opens settings | Not met | The only global shortcut is Ctrl/⌘+K (`windowing.js:22-30`). Settings are reachable only through a key in a group drawer. | In the same keydown handler, open the Window manager on `(ctrlKey||metaKey) && key === ','`. |
| Settings item in the app menu | Not met | The brand menu (the desk's app menu) holds workspaces only (`DeskHeader.razor:36-40`). | Add a "Settings…" item, with the shortcut shown, at the foot of the brand `RailMenu`. |
| Panes, titled, last pane restored | Partly | `WindowManagerPanel.razor` is one long scroll of six sections: Appearance, Scale, Concurrent windows, Primary window, Resolution, Workspaces (81-300). | Split it with `DeskTabs` into three panes: Appearance (appearance and scale), Windows (concurrent, primary, resolution) and Workspaces. Store the last tab with the layout. Set the window's StatusText to the pane name. |
| Don't duplicate system settings | Partly | Interface scale (80-150%) duplicates browser zoom. The Dark/Light/System gate restates the OS appearance (see Dark Mode). | Keep text scale (macOS has no Dynamic Type, so it earns its place). Fold Interface scale into a "Match browser zoom" note, or keep it but cap it at 100-150% (see Typography for the 10px floor). |
| Few settings | Met | Six groups, and every one acts on the desk itself. | n/a |

## Undo and redo

No change log on the live page.

- Help people predict what undo does. Label it with the action ("Undo close Board").
- **Show the result** of an undo, even when it happens off-screen.
- Allow many levels of undo, and consider batch undo.
- Provide dedicated buttons only when necessary, using the standard symbol.
- **macOS: Edit-menu placement and the standard shortcuts, ⌘Z and ⇧⌘Z.**

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Standard shortcuts | Not met | Undo is `history.back` on Alt+← (`DeskFooter.razor:25,75`). ⌘Z and Ctrl+Z do nothing outside text fields. | In `windowing.js`, map ⌘Z/Ctrl+Z (when focus is not in a field) to the same `history.back`, and ⇧⌘Z/Ctrl+Y to `history.forward`. |
| Predictable label | Partly | The footer key just says "Undo", and its tooltip is "Bring it back (Alt+Left)". | Label it with what it restores, for example "Reopen Board", from `Manager.LastEviction`'s victim. |
| Many levels | Met | The browser history stack walks every open and close (`windowing.js:300+`). | n/a |
| Undo instead of confirming destructive edits | Partly | "Leg added" toasts (`BoardPanel.razor:950`, `BoardAllBets.razor:216`) offer no way back, and Journal deletes are confirmed rather than undoable. | Add a snackbar action "Undo" to the two Leg added toasts that removes the leg (`options.Action`/`OnClick` in a new `DeskToasts.Success(message, undo)` overload). |

## Charting data

Change log: September 23, 2022.

- Use a chart to highlight something specific, and keep it simple, with details on demand.
- **Make every chart accessible**: labels that describe the values, plus elements people can
  navigate.
- Add descriptive text: a headline for the takeaway, annotations.
- Stay consistent across charts, and keep continuity for the same data.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Takeaway text | Partly | The Headline and Takeaway slots exist and are a `<figcaption>` (`DeskChart.razor:38-50`), but the takeaway is set in `--text-tertiary` (2.70:1, `mud-bridge.css:583`), so the most important sentence is the faintest. | Make `.deskchart__takeaway` `--text-secondary`. |
| Accessible values | Partly | One `role="img"` and `aria-label` describes the whole plot (`DeskChart.razor:54`). Individual values can't be reached. | Render a visually hidden `<table>` of the series and labels after the plot, inside the figure, so a screen reader can step through points. |
| Consistency | Met | Chart colours come from `DeskChartFamily`/`DeskChartPalette`, so a family keeps its colours across windows. | n/a |

## Offering help

Change log: December 5, 2023 (visionOS tooltips); September 12, 2023 (tips).

- Fit the help to the task. Don't explain standard controls.
- Tips: one or two sentences, actionable, no more than three steps.
- **Tooltips (macOS)**: describe only the control, start with a verb, **don't repeat the
  control's name**, keep to 60-75 characters, use sentence case, and change with state.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Verb-first, no repetition | Partly | Good: "Back to {title}", "Take the whole row", "Bring it back". Repetitive: `title="Close"` on a key whose `aria-label` is "Close {title}" (`AppWindow.razor:86-87`), `title="Minimise"`. | Make window-control tooltips state-aware and verb-first: "Close this window", "Collapse to a tab", and "Restore the row" (already done). |
| 60-75 characters | Partly | "Open this bet again in a new window, to log it at another book or a second time" (`BoardWager.razor:459`, 79 chars). The desk-full tooltip (`DeskHeader.razor:153`) runs past 90 with long titles. | Cut them: "Log this bet again at another book" and "Full: opening another closes {victim}". |
| Context-sensitive | Met | Maximise and restore, Back to {x}, and the disabled-Save reason all change with state. | n/a |

## Drag and drop

Change log: October 24, 2023.

- Support drag and drop widely, and **always offer a non-drag alternative.**
- Show a drag image after about **3pt** of movement, translucent.
- Show whether a destination will accept the drop. Give feedback when a drop fails, and support
  undo.
- After a drop, keep the content selected. On macOS, let people drag from an inactive window
  with one motion.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Non-drag alternative | Not met for reorder, Met for resize | `windowing.js:171-240` is the only way to reorder. The divider has arrow keys (`Desk.razor:230`). | See Accessibility: Ctrl+Alt+←/→ plus palette commands. |
| Drag starts near 3pt | Partly | `DRAG_THRESHOLD = 6` (`windowing.js:171`). | Lower it to 4 (in layout pixels, after the zoom division the file already does). |
| Lift and land feedback | Met | The lifted tab gets `--shadow-sheet` and z-lift (`.win--dragging-tab`, 1339-1343). Neighbours slide (`transition: left .16s`, 1333). | n/a |
| Undo a drop | Met | A reorder is a history entry, so Back reverts it (`windowing.js:300+`). | n/a |

## Designing for macOS

No change log on the live page.

- Use the large display: more content, fewer nesting levels, less modality, at a comfortable
  density.
- Let people resize, hide, show and move windows, and offer full screen.
- **Give access to every command through the menu bar.**
- **Handle keyboard shortcuts** for keyboard-only work.
- Support personalization: toolbars, window configurations, colours and fonts.

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Density, low nesting, little modality | Met | 13px and 28px, tiling columns, non-modal dialogs. | n/a |
| Resize, move, hide windows | Met | Divider resize, tab reorder, collapse, maximise, saved workspaces. | n/a |
| Every command reachable | Partly | The palette (the desk's menu bar) reaches windows, workspaces and subjects only (`CommandPalette.razor:192-203`). Commands such as "Pull today's games", "Evaluate", "Settle", "Settings" and "Close window" aren't in it. | Register an `IDeskSearch` provider of commands (Group "Commands") that wraps the PullMenu jobs, Ops Evaluate, Journal Settle and the window controls of the focused window. |
| Keyboard shortcuts | Partly | Ctrl/⌘+K, Alt+←/→, Escape and arrows only. | Add ⌘, (settings), ⌘W or Ctrl+W (close the focused window; the browser reserves Ctrl+W, so use Ctrl+Shift+W there), ⌘Z, and Ctrl+1…9 (focus window n). List them in the palette's empty state. |
| Personalization | Met | Five theme families × two appearances, text and interface scale, workspaces. | n/a |

---

## Top findings

Ranked by how much a user would notice the fix.

1. **State colours fail as text in every light theme** (apple-light: positive 2.22, warning
   2.20, negative 3.55). The rules affected are `.up`/`.down` (`lineops.css:2547-2548`),
   `.metric--good/warn/bad` (1892-1894), `.tag--*` (2593-2595) and `.price__edge--wide` (2103).
   Fix: add `--state-*-ink` tokens using Apple's increased-contrast values (`#008932`, `#E9152D`,
   `#C55300`) and repoint the text rules.
2. **`--text-tertiary` carries information people have to read** (1.88 to 3.77:1 in all ten
   blocks). Call sites: `.launcher__desc` (1831), `.metric__note` (1903), `.grid .dim` (1970),
   `.price--closing .price__sel` (2161), `.snippet__note` (3579), `.hdr__sub` (513), resting
   `.bar__key` (649) and `.deskchart__takeaway` (`mud-bridge.css:583`). Fix: move them to
   `--text-secondary`.
3. **Unsaved input dies silently when a window closes or is evicted.** `JournalPanel.razor:78-124`
   and the wager draft, via `AppWindow.razor:152` and LRU eviction. Fix: a `HasUnsavedInput` flag
   that triggers a confirm and exempts the window from eviction.
4. **The OS Increase Contrast and Reduce Transparency settings are ignored.** There is no
   `prefers-contrast` or `prefers-reduced-transparency` handling anywhere (`theme.js:51-91`,
   `lineops.css:4144`). Fix: resolve the `contrast-*` family under `prefers-contrast: more`, and
   reuse the `@supports not` body under `prefers-reduced-transparency`.
5. **Accent text is under 4.5:1** (4.30 dark, 4.02 light, 3.41 to 3.72 in three light families).
   `.bar__key--current` (703), `.desklink:hover` (2534). Fix: `--accent-ink` (`#409CFF` dark,
   `#0062CC` light).
6. **Type can render below the 10pt floor.** At Interface 80% and Text 90%
   (`WindowManagerPanel.razor:327,336` × `lineops.css:108,361`), caption is 8px. Fix: clamp the
   ramp at 10px and drop the 80% option.
7. **Journal's Selection is free text after a game is chosen** (`JournalPanel.razor:681-685`).
   Fix: a `DeskSwitch` of the two teams, or Over/Under.
8. **There is no live field validation in production forms.** Only `PartsPanel.razor:209` sets
   `Error`, and Journal's reason is only in a `title` (121). Fix: bind `Error` on Price, Stake and
   Line, and show the disabled-Save reason visibly with `aria-describedby`.
9. **Window reorder has no keyboard or menu alternative** (`windowing.js:171-240`). Fix:
   Ctrl+Alt+←/→ and palette commands calling `Manager.Reorder`.
10. **No ⌘, for settings and no settings item in the app menu** (`windowing.js:22-30`,
    `DeskHeader.razor:36-40`). Fix: handle ⌘/Ctrl+, and add "Settings…" to the brand menu.
11. **No ⌘Z or Ctrl+Z, and Undo is unlabelled** (`DeskFooter.razor:25-27,75`). Fix: map ⌘Z and
    ⇧⌘Z to history back and forward, and label the key "Reopen {window}".
12. **The eviction notice and its Undo sit at the bottom edge** (`DeskFooter.razor:16-35`). Fix:
    move them into the header beside `.hdr__count`.
13. **Six panels still load with a sentence instead of a skeleton** (`GamePanel.razor:30`,
    `PlayerPanel.razor:30`, `TeamPanel.razor:32`, `HeadToHeadPanel.razor:25`,
    `BoardForm.razor:15`, `BoardAllBets.razor:21`). Fix: a `DeskSkeleton` in the shape of the
    first section.
14. **The springy overshoot runs on frequent pointer interactions** (`--ease-spring` on
    `.gate__cap` 983, `.marble` 925, `pullmenu-in` 2389/4639) and metric tiles lift on hover
    (1920). Fix: `--ease-standard` for those, keep the spring for dialog and sheet entrances, and
    drop the translate.
15. **"Your bet" next to "Enter my own price"** (`BoardWager.razor:157,280-281,986`). Fix: "Bet"
    and "Enter a price".

## Things the HIG says that the desk should NOT adopt

- **Touch sizes and spacing.** The 44×44pt iOS minimum and the "12pt around bezelled controls,
  24pt around unbezelled" spacing advice are tap-error guidance. The desk is pointer-first: keep
  28px controls, 24px keys inside 29px rows, and the 4px ramp.
- **iOS type sizes and Dynamic Type tables** (17pt body, the xSmall–AX5 ladders). macOS has no
  Dynamic Type. The desk's `--text-scale` is the right analogue, and 13px stays the body.
- **iOS grouped-list conventions**: uppercase or footnote section headers, inset grouped rounded
  tables, full-width rows with disclosure chevrons. The house grouped form (label leading, control
  trailing, one inset rule) is already the macOS reading.
- **Full Liquid Glass**: lensing, clear glass, scroll-edge effects, background extension under
  sidebars, a 35% dim over media. The desk has no media background, and nothing scrolls under
  its chrome. Standard materials on floating chrome are the correct scope, and backdrop-filter
  everywhere would cost frame time on dense grids.
- **Desktop tinting and dark-mode transparency on custom component backgrounds.** This is a macOS
  graphite-accent behaviour a browser can't observe. Don't add alpha to resting controls to fake
  it.
- **The literal macOS settings window**: dimmed minimize and zoom buttons, a toolbar of pane
  icons, a title of "LineOps Settings". The desk's settings is a desk window, so use `DeskTabs`
  panes (Settings, above) rather than rebuilding NSToolbar chrome.
- **The June 2025 system blue as the accent fill.** White on `#0091FF` is 3.23:1, worse than the
  current 3.65. Update the state fills if you like, but keep the accent fill as it is.
- **Removing the appearance and theme choice** because "avoid an app-specific appearance
  setting". The families are personalization, which Designing for macOS explicitly encourages,
  and the default already follows the system.
- **Menu-bar placement for undo and settings.** A browser app has no NSMenu. The brand menu and
  the command palette are the desk's menu bar, so put the commands there and keep the standard
  shortcuts. Don't build a fake menu bar.
- **Haptics and audio as feedback channels.** Not available in a desktop browser. Shape plus
  text plus colour is the multi-channel answer here.
