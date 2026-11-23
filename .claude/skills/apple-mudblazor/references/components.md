# Component conventions

---

## The affordance hierarchy

Two questions, two enums. **`DeskEmphasis` says how loud. `DeskRole` says
whether it destroys.** Those are the two things a call site can actually answer.

| Emphasis | Looks like | For |
|---|---|---|
| `Plain` | transparent until pointed at | toolbar and inline actions; the escape from a thing (Cancel, Close, Back) |
| `Tinted` | accent wash behind accent text | secondary actions that still need finding at a glance in a dense panel |
| `Filled` | solid accent | the one action a context exists to commit — save, apply, run |

### The two-jobs lesson

The enum this replaces was a single "tone"/"severity" vocabulary that both painted
buttons and named states. It looked economical and it was two ideas wearing one
name.

Splitting it touched 25 files, and it was a **judgment sweep, not a rename**: every
call site had to be asked *"is this the primary action, or is it reporting a
state?"* — and the old enum had let both answers be spelled the same way. Budget
for that if you are migrating an existing app. An inventory taken before the sweep
still missed one file, which surfaced only in a later vocabulary pass.

The lasting rule: **hue is not an affordance channel.** An interface with five
coloured button types has no primary action, only five competing ones. State
colours belong on values, tags and status — not on the controls that act on them.
`Role.Destructive` is the single exception, and it is a role, not a tone.

### Do / don't

**Emphasis**
- ✅ One `Filled` per panel — the action the panel exists to commit.
- ❌ `Filled` on every button in a toolbar. Five primaries is zero primaries.
- ✅ Cancel is `Plain` beside a `Filled` confirm.
- ❌ Cancel as `Tinted` "so it's findable". It is findable; it is beside the thing
  you are cancelling.

> **Exactly one Filled per context.** If a panel seems to need two, one of them is
> not the primary action. Work out which and demote it.

**Role**
- ✅ `Destructive` on delete, discard, revoke, force-stop.
- ✅ Destructive at any weight: a `Filled` "Delete" in a confirmation alert, a
  `Plain` "Remove" in a row's overflow menu. Weight and danger are independent.
- ❌ `Destructive` to mean "important" or "urgent". Red means *this destroys
  something*, and diluting it costs you the one warning that works.

### Busy is occupied, not inert

`Busy` refuses further presses and runs a sheen across the button's own fill. It
does **not** go grey.

Grey says *"this is not for you"*. The truth is *"this is already doing what you
asked"*. Those are different sentences and the operator needs the second one.
`Disabled` and `Busy` are separate parameters for exactly that reason.

### Why the CSS says `.desk-btn, .btn`

`DeskButton` renders `.desk-btn` with one modifier per decision: `--filled`, `--tinted` or
`--plain` for the emphasis, `--destructive` for the role, `--sm` or `--lg` for the size,
`--icon` for a glyph with no label, `--busy` while it works. `desk.css` writes the recipe
against `.desk-btn, .btn`: `.btn` is a dead-cheap co-selector so a stray hand-written button
picks up the same recipe instead of a second button language. Nothing shipped uses it.

### Sizes

`Small` / `Medium` / `Large` change height and padding; the recipe does not change.
Chrome is small; a page's statement of intent is large. An icon with no label
renders **square**, so it reads as a control rather than a clipped button — and it
then requires `Title`, which is both the tooltip and the accessible name.

---

## `DeskState` — for the things that report

`DeskState` (`Neutral` / `Positive` / `Negative` / `Warning` / `Info`) is the half
of the retired tone enum that was never about buttons. It belongs on a figure, a
tag, a toast, a status strip — anything that *reports* rather than *acts*.

**Buttons do not take an `DeskState`.** If you find yourself wanting one on a
button, the question you are actually answering is emphasis or role.

Hue is always the second channel. A tag carries its word, a metric carries its
label, a toast carries its sentence — remove the colour and the meaning survives.

---

## Fields — inset wells

**There is no field wrapper component, on purpose.** A field is a plain `<input>`,
`<select>` or `<textarea>` carrying `.field`. That keeps `@bind`, validation
attributes, `aria-*` and every native attribute working without a parameter to
forward each one. The CSS is in `controls.css`.

**A field is cut into the surface, not raised off it**: a darker well
(`--surface-0`), a hairline, and no fill of its own. A field is a *place to put
something*; a button is a *thing to press*. Drawing them the same way — which the
Material default does, and which a gloss-based system does worse — makes an
interface where you cannot tell input from action at a glance.

- `.field-set` is the frame: label above, error below, `--space-1` gap.
- It appears **only when the field has something to say about itself**. A field
  with no label and no error renders bare, so the frame is an addition rather
  than a new baseline.
- `.field-err` has its margin zeroed, because it usually renders as a `<p>` whose
  default margin would reopen the rhythm `.field-set`'s gap owns.
- `aria-invalid="true"` drives the error border and swaps the focus ring to the
  negative colour. Drive it from validation state; do not hand-apply a class.
- `select.field` draws its chevron with two gradients rather than importing an
  image, so the chevron takes the text colour and re-themes for free.
- `.field.num` for numeric input — tabular figures.

---

## The segmented gate

"One of a few, all visible" — the platform's segmented control. A recessed track
holding a raised cap that **slides** to the selected position.

**The mechanism is two custom properties**, set inline by whatever renders the
control:

- `--n` — how many positions there are.
- `--i` — which one is current.

```html
<div class="marble-row gate" style="--n:3; --i:1">
  <div class="marble gate__cap"></div>
  <button class="gate__opt">Day</button>
  <button class="gate__opt gate__opt--on">Week</button>
  <button class="gate__opt">Month</button>
</div>
```

The cap is positioned off `--i` and sized off `--n`, so the CSS never needs to
know how many options exist and no per-option rule is generated.

Three things that are load-bearing and easy to get wrong:

1. **A value matching no position sets `--i` to `-1` and renders no cap at all.**
   Parking the cap on position 0 is worse than showing nothing — it asserts an
   answer that is not the answer.
2. **The travel step is one column *plus the gap*.** Travelling by column width
   alone looks right at two positions and drifts a pixel per position after that,
   which shows up as a cap not quite sitting on the last option. `--gap` defaults
   to `0px`, so ungapped hosts are unaffected by the arithmetic.
3. **The track fill is `--surface-0`, not `--surface-1`.** A recessed channel
   inside a panel that already sits at `--surface-1` is a channel nobody can see.
   The same applies to any recessed channel over a translucent material: pick the
   tier below what the material composites to.

The slide uses `--ease-spring`, because a real slide switch lands with a settle,
not a stop. It is neutralised under `prefers-reduced-motion`.

---

## Tags — capsules, not outlined chips

`.tag` plus `.tag--good` / `--warn` / `--bad` / `--info`.

**Why a capsule and not an outlined chip.** An outline is a second edge inside a
table that is already ruled by hairlines, and a column of outlined chips reads as
a column of little boxes rather than as a column of states. A capsule has one edge
— its own fill — so a table of them settles: the shape says *label*, the wash says
*which state*, and the ink is that state's own colour so the word and its fill
agree.

Neutral is `--surface-3` under `--text-secondary`: still a capsule, no state
claimed. The word is always present, so the state survives the colour being
removed.

Do not put a capsule on a control. A capsule means *this is a state*; a control
that looks like a state is a control nobody presses.

---

## Section cards and the flow

What to draw is `apple-hig-components` (`forms.md`, rule 5 and "Sections in a window"); this is
how the Desk draws it (ADR 0032). **Build it from `FormGroup`; the class names are its output.**

`FormGroup` renders `section.wsection > div.wgroup`, the card: `--surface-2` fill, one 1px
`--separator-strong` edge, `--radius-panel`. Its parameters:

| Parameter | What it draws |
|---|---|
| `Title` | the card's name, `h3.wgroup__title` in the header bar (`.wgroup__bar`), headline style, sentence case; it names the section (`aria-labelledby`) |
| `Keys` | what trails the title in the bar (`.wgroup__keys`): ONE small Plain/Tinted key, a count, or a `DeskSwitch` filter at `DeskKeySize.Small`; never the Filled key |
| `ChildContent` | the rows: `FormRow`s, a split row, or any `.wgroup__row` |
| `Notice` | a `Note` under the card, before the footer — never inside |
| `Footer` | `p.wgroup__foot`, the sentence under the card |
| `Span` | a `DeskCardSpan`: `Track` (default), `Row` (`.wsection--span`, the whole row), `Rest` (`.wsection--rest`, the rest of the first row from 856px of flow; must be the flow's second card) |
| `LabelWidth` | a CSS length for the group's `--form-label` column |
| `Class`, `GroupClass`, `Style` | on the section, on the `.wgroup`, on the section |

With neither `Title` nor `Keys` there is no bar. Every `FormGroup` with a `Title` draws it in
the bar: there is no option for the old spelling.

**The bar is a divider inside the one card, not a second box.** `.wgroup__bar` is the card's
first child, filled `color-mix(in srgb, var(--surface-2) 50%, var(--surface-1))` — half a step
back down the ramp, so it is right in every family and both appearances with no token of its own
— with `border-bottom: 1px solid var(--separator)` across the full width. That is the only
`border` on anything inside `.wgroup`; the bar, a cell, a key and a row never take a border or a
`box-shadow`. It wraps: in a narrow card the keys drop beneath the title.

**The flow is `.wflow`** (not `.wcards`: the application owns `.wcard`, and the host's
convention tests match class names by substring). `display: grid; grid-template-columns:
repeat(auto-fill, minmax(min(100%, 420px), 1fr)); align-items: start; gap: var(--space-5)
var(--space-4)`, and it is a container named `wflow`, so `Rest` asks the flow's width, not the
window's. Any window with two or more sections uses it; never give the window a `max-width`
column instead. `align-items: start` is load-bearing: without it a short card stretches to its
row's tallest. Why 420px: a card's form rows stack their labels above their controls under 400px
of card, so a 360px track stacked every row of a settings card and stood it twice as tall; at 420px
a 1600px window is three tracks of about 520px with labels beside their controls, and a window of
about 680px is one column.

**The split row** is spelled by hand, inside a `FormGroup`:

```razor
<div class="wgroup__row wgroup__split">
    <div class="wgroup__cell"><span class="wgroup__label">Mode</span> <DeskSwitch … /></div>
    <div class="wgroup__cell"><span class="wgroup__label">Accent</span> <DeskAccentPicker … /></div>
</div>
```

The cells share the row equally, divided by a vertical `--separator` hairline inset by
`--space-2` top and bottom; the first cell's label takes `--form-label`. Under 720px of card (two
cells that each fit a label column and a three-segment gate) the
cells stack and the divider becomes the inset row hairline. A cell never wraps: its label stays
beside its control, side by side or stacked, so both cells always lay out alike; only under 400px
do both cells put their labels
above their controls at once, as a `FormRow` does. A split row with one cell is that cell, the whole row —
so a cell whose control cannot act is not drawn and its neighbour takes the row.

**A filter in the bar over a tile grid** — the Window manager's Themes card:

```razor
<FormGroup Title="Themes" Span="DeskCardSpan.Rest">
    <Keys>
        <DeskSwitch Options="DeskThemePicker.ThemeFilters" Label="Show themes" Size="DeskKeySize.Small"
                    Value="_group" ValueChanged="@((string g) => _group = g)" />
    </Keys>
    <ChildContent>
        <div class="wgroup__row wgroup__row--pad"><DeskThemePicker Group="@_group" … /></div>
    </ChildContent>
    <Footer>Showing …</Footer>
</FormGroup>
```

`DeskThemePicker.Group` is a heading of the catalogue (`""` or null for all);
`ThemeFilters` is All then each heading; `GroupOf(family)` is the heading a family is filed under.
The Window manager opens the filter on All: one heading's two tiles in a card spanning the rest of
a wide row were a large empty box. The footer names the family showing and its heading, because a
narrower filter can hide the tick.

**What a card holds that is not a row** is styled by the card, once, in `desk.css`; a window puts
it straight in the card and writes none of these rules in its own sheet:

- **A `DeskGrid`** (`.wgroup > .deskgrid`): the card is its frame. It insets by
  `calc(var(--space-4) - var(--deskgrid-inset))`, so its text starts where the bar's title does,
  and its last row's hairline stands down (`border-bottom: 0`), because the card's edge is under
  it and two lines a pixel apart read as a box inside the box. The rows above keep their dividers.
  That rule beats `mud-bridge.css`'s `.deskgrid .mud-table-body .mud-table-cell` by weight, since the
  bridge loads later (`mudblazor-seam.md`).
- **A lone `EmptyState`** (`.wgroup .empty`, not inside a row): `margin: 0` and the rows' inset,
  `--space-3` by `--space-4`. One inside a row keeps that row's inset.
- **An `InlineConfirm` row** (`.wgroup > .confirm`): `--space-2` by `--space-4`, starting at the
  labels' edge.

Under 480px of card all three take `--space-3` in place of `--space-4`, as the rows do.

**Hand-spelled**, only where `FormGroup` cannot be used: `section.wsection` (with
`aria-labelledby`) `> div.wgroup > div.wgroup__bar > h3.wgroup__title` (and `div.wgroup__keys`),
then the rows, then `p.wgroup__foot` after the `.wgroup`. `h3.wgroup__head` — a quiet grey line
*above* the group — is the old spelling, kept in `desk.css` only for the sections an application
has not yet moved; nothing new uses it.

### The drawer under a row

`SnippetShell` is on the same contract: by default it *is* the card (name in the bar, the
escape as the bar's one trailing key, the answer as rows). When the answer is itself several
cards (More bets' per-market `FormGroup`s in a `.wflow`), the caller sets `Cards`
(`RowAction.SnippetCards`) and the shell becomes an unframed pane header — the same headline
title and key, no fill, no edge — over them. Never a card around cards.

---

## Lists and grids

- **No zebra striping.** Rows separate with a hairline and nothing else.
- **Dense by default** — a 28px row is `--space-1` above and below a 20px line box
  at `--text-body`.
- **Tabular numerals on every cell**, not only right-aligned ones. See the
  `Align="Align.Right"` note in `mudblazor-seam.md`: the class you would hang that
  rule on does not reliably arrive.
- **Selection is an accent wash on the row** — never a border, never a left-edge
  bar. Specificity must clear the hover rule, or a selected row goes dark the
  moment the pointer leaves it.
- **Headers are quiet**: `--text-secondary`, `--text-footnote`, semibold, and
  explicitly *untracked* — MudBlazor's `subtitle2` default is tracked.

---

## Tooltips — a sentence, or a small table

`DeskTooltip Text` is a sentence; where one short phrase will do, a `title` stays. When the
explanation is a set of figures (one reading per source, per region, per side), use the
structured shape instead of writing them into `Text`: `Title` (semibold, what the trigger is),
`Caption` (secondary, wraps at the measure, a `\n` kept), `Rows` of `DeskTooltipRow(Label,
Value, Qualifier, Emphasis)` — a name, never a code; the value right-aligned in tabular
numerals; the row the trigger shows marked by `Emphasis`, which is weight, not hue — and a
`Footnote`. Cap the rows at about five, best first, and say in the footnote where the rest are:
a tooltip cannot be scrolled or clicked into. The drawn shape is `aria-hidden`; `Text` (or the
parts composed as plain lines) is what a screen reader hears, and what a test asserts. The
measure lives on `.desk-tip` inside the popover, because Mud writes `max-width: none` inline on
`.mud-tooltip` (mudblazor-seam.md) — never fight that with `!important`. Ten figures in one
`Text` string ran as one ~1100px line across a window; that is the failure this shape exists for.

---

## Hover, selection, and the one exception

> **Hover is neutral. Accent means selection.**

Hover moves a surface one step up the ramp (`--surface-3`). Selection is the
accent wash. Keeping the two on different channels is what lets a hovered row and
a selected row be told apart at a glance — and neutral-for-selection would lose to
its own hover plate, which is a step lighter.

**The exception is menus.** A menu row hovers to the *accent* wash, because the
hovered row in a real menu **is a provisional selection**: releasing the pointer
commits it. That is what the platform's own menus do, and it is what makes the
thing read as a menu rather than a hover-follow strip.

Comment this at the rule. It looks like an inconsistency to anyone sweeping for
one, and someone will "fix" it otherwise.

---

## The focus ring

**One ring, everywhere:**

```css
--focus-ring: 0 0 0 3px var(--accent-wash-strong), 0 0 0 1px var(--accent);
```

Applied as `box-shadow` on `:focus-visible` with `outline: none`.

- `:focus-visible`, **not** `:focus` — a pointer click should not leave a ring.
- The ring sits **outside** the control on an offset, which is what lets an accent
  ring survive on an accent-filled button. A ring drawn inside an accent surface
  vanishes into it.
- The error variant swaps the accent for `--state-negative` and keeps the geometry.

> **Never paint a ring on something that does not have focus.** A `box-shadow`
> byte-identical to `--focus-ring`, drawn on a button as a "default" marker while
> focus sits elsewhere, is indistinguishable from a real ring to a keyboard
> operator — and Enter activates nothing. That is worse than no marking at all.
> If a button should be the default, **give it focus**. See `modality.md`.
