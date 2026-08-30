# ADR 0008 — Gloss is an affordance, and MudBlazor gets a seam

**Status:** Accepted

**Superseded in part by:** [ADR 0016](0016-apple-feel-design-system.md). The gloss and
hue rules below are no longer in force. The MudBlazor seam this ADR established — three
files, and the `:root:root` reasoning — is unchanged and still current.

**Amends:** [ADR 0007](0007-window-manager-ui.md), which recorded "MudBlazor remains for charts only."

## Context

Two things were true at once.

The desk was deliberately matte. ADR 0007 gave the pulse strip permission to be the one
loud element and asked everything else to stay quiet, which worked — but it left every
control reading at the same volume. A button, a table row and a static tag were all a
graphite rectangle with a hairline border. Nothing on the surface said *this one responds
to a click*, and on a console whose whole job is acting on what you are looking at, that
is the wrong thing to leave unsaid.

Meanwhile MudBlazor was going to be used far more than "charts only" — panels want its
buttons and, later, its tables, selects and dialogs. It was configured for that about as
far as pasting five hex values into a layout file gets you. Its Material defaults were
still live underneath: 4px radii, three-layer drop shadows per elevation step, Roboto on
any role the theme did not name, and ripples. Every Mud component dropped into a panel
would have announced that it came from somewhere else, and there was no place to fix that
once for all of them.

## Decision

### Gloss means "you can press this"

Buttons are **keys**: moulded caps seated in the panel, lit from above, with a crown
highlight on the top edge and a shadow where the cap meets its seat. Nothing else on the
desk is glossy.

That makes gloss a *channel* rather than a texture, and it sits alongside the one the desk
already had. Hue means state. Gloss means affordance. A surface that catches the light can
be clicked; one that does not, cannot.

The rule pays for itself in three places that would otherwise each need a decision:

- **Highlighting lifts the cap 1px** toward the light, with the sheen and the cast shadow
  growing to match. That is the answer to "what will this click hit?", and it is a physical
  answer rather than a colour-change convention. Pressing sinks it 1px past the light line,
  so a key has 2px of real throw.
- **`:focus-visible` lifts identically to `:hover`.** The keyboard gets the same answer the
  pointer does, which is the accessibility argument for choosing lift over a hover-only
  glow. The focus ring is chalk, not iris — an iris ring on an iris key is invisible.
- **A disabled key loses its sheen outright** rather than fading to 45% opacity. Under the
  rule, unlit *is* the statement. Opacity would have said "this is a button, dimmed"; no
  gloss says "this is not a thing you press."

Fields are the inverse and exist to keep the rule honest: cut *into* the panel, unlit,
recessed. They previously wore `.btn` with `text-transform: none` patched on inline at
every call site, which under this change would have made every text box look pressable.

**Busy is not disabled.** A key that has started work keeps its cap and its light, refuses
further presses, and runs the pulse strip's own sweep across itself. Borrowing the sweep
rather than introducing a spinner means the desk keeps one word for "in flight".

The whole thing is one CSS rule driven by two custom properties, `--key-tint` and
`--key-ink`. A new tone is two lines. The recipe is shared by `.btn` and `.desk-key`, so
the ~45 existing hand-written buttons got the treatment without being touched and there is
never a period where two button looks coexist.

### MudBlazor gets a seam rather than a coat of paint

Three files, each with one job:

- **`Theming/DeskTheme.cs`** — the palette, in C#, because MudBlazor derives values CSS
  cannot: every entry becomes `--mud-palette-x` plus `-rgb`, `-darken`, `-lighten` and
  `-hover`, and its stylesheet reaches for those on hover and focus. A colour given to
  only one side produces a hover shade that exists nowhere else in the product.
- **`wwwroot/css/mud-bridge.css`** — everything that is *not* a colour: radii, type,
  elevation, z-index, and the Material tells that are hard-coded rather than variable-driven.
- **`Components/Desk/DeskButton.razor`** — the call site. `Tone` and `Size`, nothing else.

`DeskButton` renders MudButton as `Variant.Text` with `Color.Inherit` and ripple off — the
variant that contributes the fewest visual rules — so the cap comes entirely from
`.desk-key`. Ripple goes because the desk answers a press with travel, and two answers to
one question is one too many.

The **`Tone` enum names consequences, not colours**: `Action`, `Go`, `Stop`, `Caution`,
`Neutral`. "Should this be `Stop`?" is a reviewable question; "should this be red?" is not.

### A parts bin

The reusable controls live in a window (`PartsPanel`) rather than a document. It renders at
the same column width, on the same surface, under the same theme as the panel being built
beside it, so "does this fit?" is answered by looking. It is a catalog entry like any other
window — which is ADR 0007's "any sub-page can be a window" claim being cashed in.

## Consequences

- **`:root:root` in mud-bridge.css is deliberate and load-bearing.** MudThemeProvider does
  not ship its variables in a stylesheet; it writes a `<style>` element into the document at
  runtime containing `:root { ... }`. A DOM style element comes after every external sheet,
  so a plain `:root` would tie on specificity and lose on source order — silently, for every
  value in the block. Doubling the pseudo-class settles it without `!important`.
- **One MudBlazor rule cannot be out-specified**: `.mud-button-root:disabled` sets its colour
  with `!important`. `DeskTheme` therefore sets `ActionDisabled` to the desk's dim text so
  the two agree rather than fight. This is the reason the palette must exist in C# at all.
- Nothing in the bridge uses `!important`. If a rule there stops working after a MudBlazor
  upgrade, that is the signal to re-read their stylesheet, not to escalate.
- `color-mix(in oklab, …)` derives each cap's border and shading from its tint, which is what
  keeps a tone to two lines. It rules out browsers older than Chrome 111 / Safari 16.2 —
  acceptable for a single-operator console, and worth naming.
- Inline `text-transform: none; letter-spacing: 0` is gone from ~15 call sites, because the
  control it was correcting is now `.field` and no longer dressed as a button.
- **Transitions do not advance in a browser tab that is not compositing.** This cost real
  time during verification: `getComputedStyle` on a transitioned property returned its start
  value indefinitely in a hidden pane, which reads exactly like a broken rule. Measure
  transitioned properties with `transition: none` applied, or measure geometry.
