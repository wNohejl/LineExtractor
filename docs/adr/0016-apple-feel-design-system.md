# ADR 0016 — Weight replaces hue, materials replace moulding

**Status:** Accepted

**Supersedes in part:** [ADR 0008](0008-gloss-as-affordance-and-the-mudblazor-seam.md), whose
gloss and hue rules are retired here. The MudBlazor seam that ADR established is unchanged.

**Amends:** [ADR 0013](0013-the-board-and-the-floating-layer.md), which reserved the floating
layer for comparison and left the desk with no modal tier at all.

## Context

The desk was matte graphite with exactly two semantic channels, and ADR 0008 had named both:
gloss meant affordance, hue meant state. A surface catching the light could be pressed; a
colour said what had happened.

Hue-as-state was the one that did not survive contact with real panels. Because every button
carried a tone, and every tone was a colour, a panel with five buttons had five coloured
buttons — which is to say five primary actions, which is to say none. `OpsPanel`'s toolbar
read as a row of equally urgent choices when four of them were incidental and one was the
thing you came to do. The channel was doing its job perfectly and the job was wrong: hue
spent on affordance is hue no longer available for state, so a genuinely bad number sat in
a screen already full of colour and had nothing left to say it with.

Gloss failed more quietly. A moulded cap with a crown highlight and a cast shadow is three
box-shadows and a gradient per state, per tone, on every control that is not a field. It
reads as a physical object, which was the point, but it also reads as *2013* — and it put a
lighting model on a console whose actual content is a table of numbers. The desk was
spending its most expensive visual device on the least informative element.

The system also had two holes rather than one. There was no modal tier: ADR 0013 deliberately
made `DeskDialog` non-modal because comparison is the workflow, and then nothing was ever
built for the cases comparison serves badly — a form that must complete or cancel, a
multi-step configuration. And there was **no way to ask the operator a question at all**. A
destructive action either happened or was preceded by a `window.confirm` that looked like it
came from a different decade.

## Decision

The desk adopts Apple's Human Interface Guidelines as its design system: true-neutral dark
surfaces, one accent, translucent materials on anything that floats, an SF-derived type
ramp, and motion that says where a thing came from.

### Weight replaces hue

Buttons state **how loud**, not **what colour**. `DeskEmphasis` is `Plain` / `Tinted` /
`Filled`, and the rule at a call site is: **exactly one Filled per context.** If a panel
seems to need two, one of them is not the primary action.

Danger is a separate question, because a destructive action can be any weight — a `Filled`
"Delete" in a confirmation, a `Plain` "Remove" in a row's overflow menu. So `DeskRole` is its
own axis, and `Role.Destructive` is **the only place a button borrows a state colour.**

State colours move to where states actually live: numbers, tags, the pulse strip. A red
number is read at the moment the number is; a red button is read every time the panel is.
`DeskState` — `Neutral` / `Info` / `Positive` / `Warning` / `Negative` — is applied by
judgment where a state genuinely needs a colour, rather than as an always-on channel tinting
every control on the desk.

Accent means interactivity, focus, and selection, and nothing else. A second accent is how an
interface stops meaning anything.

### Materials and depth replace moulding

**Anything that floats takes a material. Anything holding data stays opaque.** Four tiers,
thinnest to thickest, mirroring HIG materials: the rail's popover, the pull menus, the
floating dialog, the modal sheet. Panel bodies, grids, tiles and skeletons are flat, because
data legibility beats depth and a translucent table is a table you read twice.

Elevation is a surface ramp — `--surface-0` through `--surface-3` — plus a hairline. Hairlines
separate; they never outline. A border drawn around a panel is the Material habit this system
is replacing, and it was doing the job the elevation ramp now does.

### The seam from ADR 0008 survives intact

Three files, the same three jobs, with Apple contents:

- **`Theming/DeskTheme.cs`** — the palette in C#, because MudBlazor derives values CSS cannot.
- **`wwwroot/css/mud-bridge.css`** — everything that is not a colour: radii, type, elevation,
  z-index, and the hard-coded Material tells.
- **`Components/Desk/DeskButton.razor`** — the call site. `Emphasis`, `Role` and `Size`.

`:root:root` in the bridge is still load-bearing, for the same reason it always was:
MudThemeProvider writes its variables into a runtime `<style>` element, which comes after
every external sheet, so a plain `:root` ties on specificity and loses on source order —
silently, for every value in the block.

That the seam needed no structural change when the system underneath it was replaced is the
strongest evidence the seam was drawn in the right place.

### A modal tier beside the floating layer

`DeskSheet` and `DeskAlert` are new; `DeskDialog` is unchanged. The rule, quoted verbatim
from `DeskSheet.razor` and from the skill:

> Use a sheet when the task is self-contained and completing it is the only goal; use
> DeskDialog when the operator needs to see or compare other things while working.
> Comparison is never modal. If you are unsure, do not be modal.

`DeskAlert` is the narrowest use of modality the system allows and the only one permitted to
interrupt: confirmations and destructive gates, nothing else. Two of its conventions are
load-bearing rather than stylistic. **A destructive confirm renders red and is never the
default**, because making the dangerous button answer Enter is how people delete things they
meant to keep. And **labels name the consequence** — "Delete run", not "OK" — because a button
that says OK asks the operator to remember what they clicked.

`IDeskAlerts` is the seam, scoped for the same reason `DeskToasts` is: the dialog stack it
talks to is per-circuit.

### The type stack is `-apple-system` with bundled Inter

SF Pro cannot be licensed off Apple platforms. So: real SF via `-apple-system` on a Mac,
self-hosted Inter everywhere else — the closest legal match on metrics, shipped locally so
Windows does not fall through to Segoe.

**One family for everything.** Apple sets numbers in SF too, so there is no second "data" face
to name: a column of figures is this face plus `font-variant-numeric: tabular-nums`, which is
what `.num` and `.mono` set. The retired `--face-data` was solving a problem SF does not have.

## Consequences

- **Every token is semantic and `[data-theme]`-keyed**, so a second theme is a second token
  block rather than a component rewrite. `DeskTheme.cs` mirrors the colour tokens and must
  agree with the stylesheet exactly — that duplication is the price of the C# palette, and
  ADR 0008 explains why the palette cannot be CSS-only.

- **`backdrop-filter` is limited to the floating tier** for GPU cost, and every material has an
  opaque `@supports` fallback. **That fallback block must stay last in the file.** `@supports`
  adds no specificity, so its single-class rules win on source order alone; when it was written
  earlier in the sheet it was shadowed for three of its four consumers and every one of them
  fell back to the wrong tier. The block carries its own comment saying so.

- **`--text-secondary` at 55% white was contrast-checked** against `--surface-1` during Task 9:
  **5.71:1**, which passes AA. `--text-tertiary` at 30% measures **2.70:1** — below AA, and
  correct for exactly the job it has (a hint, a placeholder, a unit nobody must read). A cell
  an operator has to read is not that job, which is why `RunsPanel`'s timestamps, durations,
  job keys and credit counts moved off `.dim` and onto `.quiet`.

- **`DeskTone` was two ideas wearing one enum.** Splitting it into `DeskEmphasis` × `DeskRole`
  for buttons and `DeskState` for things that report touched 25 files, and it was a judgment
  sweep rather than a rename: every call site had to be asked "is this the primary action, or
  is it reporting a state?", and the old enum had let both answers be spelled the same way.
  The inventory taken before that sweep still missed `TagTones.cs`, which held `DeskState`
  values under the retired word and was only found in Task 16's vocabulary pass.

- **Hover is neutral; accent means selection — except in menus.** The launcher's rows and the
  glide plates hover to `--surface-3`. `.pullmenu__item` hovers to the accent wash, because
  the hovered row in a real menu *is* a provisional selection: releasing the pointer commits
  it, which is what macOS's own menus do and what makes the thing read as a menu rather than a
  hover-follow strip. The distinction is commented at the rule, because it looks like an
  inconsistency to anyone sweeping for one.

- **The reduced-motion block is the only `!important` that overrides component styling.**
  Motion is a signal, not a flourish, so it goes away entirely when asked — and a preference
  that can be out-specified by a component is not a preference. It is not, however, the only
  `!important` in the codebase, and the honest inventory is worth stating so nobody has to
  re-derive it: `lineops.css` carries **14 declarations across 6 sites** and `mud-bridge.css`
  carries none (its three matches are prose, so ADR 0008's claim about the bridge still
  holds). Besides reduced motion, they divide three ways rather than into one tidy category:

  - **Three sites beat an *inline* style, which no selector can out-specify at any
    specificity.** `.deskdialog`'s `top`/`left`/`width` and `.desk__surface`'s `width`/`height`
    and `.win`'s `left`/`width`, all under `max-width: 720px`, against geometry written by
    `dialogs.js`, `windowing.js` and `Desk.razor`. This is the only genuinely unavoidable
    group.
  - **One is specificity, not inline.** `body.splitting *` sets one cursor across the desk for
    the length of a drag; at `(0,1,1)` it cannot beat compound rules like
    `.deskdialog--dragging .deskdialog__bar` `(0,2,0)` without it.
  - **Two declarations are belt-and-braces and could be dropped.** `.shell__body`'s
    `flex-direction` and `.win`'s `position` sit in a media query at the same specificity as
    the base rules they override, so source order already decides them. They are uniform with
    the siblings around them that do need it.

  None of the five non-motion sites decides how a component *looks*; they are geometry, cursor
  and layout mode.

- **The pulse strip keeps its bespoke timings.** The interaction-motion tokens (`--dur-fast`,
  `--ease-standard`) describe how an interface answers a press. A continuous ambient signal is
  not answering anything, and tokenising its sweep would have made "make buttons snappier"
  silently change what "this window is busy" looks like.

- **MudBlazor 9.7.0's `Align="Align.Right"` does not emit an alignment class** on
  `PropertyColumn` or `TemplateColumn` — the parameter is accepted and dropped, which the
  MUD0002 analyser warns about without saying that the alignment is simply lost. Mitigated by
  giving every numeric cell tabular figures so columns line up on digit width regardless; the
  real fix is tracked separately.

## What building it found

**The dominant failure mode when replacing a design system in place is a correct rule that
silently loses the cascade.** Eight new rules never reached the screen. Seven lost outright
and one was pre-empted:

- MudBlazor's compound `.mud-button-text.mud-button-text-inherit` out-specifies any
  single-class desk rule on the same element.
- `.bar__group .rail__btn` — a descendant selector still in the sheet, beating the flat rule
  that replaced it.
- The glide-plate suppressors — **two of them** — which had to out-specify the `:hover` rules
  they stand down.
- The `@supports` material fallback block, shadowed on source order for **three of its four
  consumers**, so three rules at once — see the Consequences note above.
- `DeskSheet`'s `.mud-dialog` styling was written as `.mud-dialog.desk-sheet` from the start,
  which is why it is the one that never broke: `mud-bridge.css` loads after `lineops.css`, so
  a single-class sheet rule could not have won on source order either.

That is one, one, two and three — seven that lost, plus the sheet's, which is the eighth
because it is the one that would have lost had it been written the obvious way.

None of these are findable by grep. A token sweep proves the old names are gone; it says
nothing about whether the new rule is the one the browser is applying. Seven of the eight were
found by looking at the rendered page; the eighth never shipped, because the sheet's rules were
written compound from the start. Verify a design-system migration in a browser, on the real
screen, with computed styles — not in the stylesheet.

**Retired tokens hide outside stylesheets.** Task 10 drove `lineops.css` to zero retired
tokens and the gate was believed clean for five more tasks. Eight sites kept the old names in
inline `style=` attributes and in a style string emitted from C#, where no CSS grep reached
them. Each resolved to nothing, so the declaration was dropped: every team heading on the desk
had lost its colour, and `JournalPanel`'s entry form rendered with no background and no border
at all. A token gate has to cover `*.razor` and `*.cs`, not just `*.css`.

**A type rename is not a vocabulary rename.** Task 5's grep gate caught every reference to the
`DeskTone` *type* and none of the retired *language* — helpers named `SeverityTone` returning
a `DeskState`, doc comments describing "the desk's dial" and "steam for money you kept", a
summary reading "Critical reads as Stop" over a method returning `Negative`. The compiler is
indifferent to prose, and prose is what the next reader learns the system from.
