# The MudBlazor seam

Verified against **MudBlazor 9.7.0**. Everything here was found by rendering a
page, not by reading a stylesheet.

---

## The three files, and what each one is for

| File | Job | Loads |
|---|---|---|
| `apple-tokens.css` | Defines every custom property. Names roles, never colours. | after MudBlazor's sheet |
| `apple-components.css` | The recipes that spend those tokens — buttons, fields, gate, tags, sheet, alert. Ends with the `@supports` material fallbacks. | after tokens |
| `apple-bridge.css` | Re-points MudBlazor's own variables at your tokens, then removes the Material tells that survive that. | **last** |

Plus a fourth file that is not CSS: **`AppleTheme.cs`**, the C# palette.

The load order is not a preference. Several rules in this system win only because
of it, and each of those rules carries a comment saying so. If you change the
order, re-read every comment that mentions source order.

---

## Why the palette must exist in C# as well as CSS

It looks like duplication. It is not.

`MudThemeProvider` takes your `MudTheme` and, for **every** palette entry, emits a
family of derived custom properties:

```
--mud-palette-primary
--mud-palette-primary-rgb
--mud-palette-primary-darken
--mud-palette-primary-lighten
--mud-palette-primary-hover
```

MudBlazor's own stylesheet reaches for those derivations — `-hover` on hover,
`-rgb` inside `rgba()` for overlays, `-lighten`/`-darken` on states. CSS cannot
compute them for you. So if you define your accent only in CSS, a Mud component
hovers to a shade derived from whatever colour MudBlazor's default palette had,
and that shade exists nowhere else in your product.

The stronger reason, and the one that makes the C# palette non-optional:

> `.mud-button-root:disabled` sets its colour with `!important`.

That is the one MudBlazor rule you cannot out-specify from a stylesheet at any
specificity. The answer is not to escalate — it is to make the two sides **agree**.
`AppleTheme.cs` sets `ActionDisabled` to the system's dim text token, so when
MudBlazor forces the disabled colour it forces *your* colour.

**The constants in `AppleTheme.cs` must equal the custom properties in
`apple-tokens.css` character for character.** That duplication is the price of the
C# palette. Keep them adjacent in review.

Everything that is *not* a colour — radii, type, elevation, z-index — is remapped
in `apple-bridge.css` instead, because CSS can express it and one place beats two.

---

## `:root:root` is not a typo

It is the single most important line in the bridge file.

`MudThemeProvider` does not ship its variables in a stylesheet. It writes a
`<style>` element **into the document at runtime**, containing `:root { … }` with
every variable it knows — including ones the C# `MudTheme` API cannot express.

A DOM `<style>` element comes after every external stylesheet. So a plain `:root`
in your bridge file ties on specificity `(0,1,0)` and **loses on source order** —
silently, for every value in the block. Nothing errors. Your radii, your type, your
elevations simply never arrive.

Doubling the pseudo-class raises specificity by one class `(0,2,0)` and settles it
permanently, without `!important`.

```css
:root:root {
    --mud-default-borderradius: var(--radius);
    /* … */
}
```

---

## The cascade-defeat catalogue

**Eight of these occurred in a single migration.** Each is a rule that is correct
in every respect except that it does not reach the screen. Read this list before
you debug anything, because none of them looks wrong in the source.

### 1. The compound-selector defeat (the companion-rule pattern)

MudBlazor states `.mud-button-text.mud-button-text-inherit { color: inherit }` at
**two classes, `(0,2,0)`**. Your wrapper class `.app-btn` is **one class,
`(0,1,0)`**. Mud wins, and the button's ink comes from the panel behind it instead
of from the variant.

The fix is not `!important` and not a deeper nesting hack. It is a **companion
rule** at matching specificity that hands control back to your custom property:

```css
.app-btn.mud-button-text { color: var(--btn-ink); }
```

Two classes, tied on specificity, won on source order because the bridge loads
after MudBlazor. Generalize it: **for every compound MudBlazor selector that
touches a property you own, write one bridge rule at matching specificity.**

### 2. The same defeat on interaction states

`.mud-button:hover`, `:focus-visible` and `:active` each set a literal
`background-color` (`--mud-palette-action-default-hover`) at the same two-class
depth. Left alone, every hover, focus and press shows Mud's flat grey instead of
the variant's fill — Plain never shows its surface tint, Filled never darkens
toward the pressed accent.

```css
.app-btn.mud-button:hover,
.app-btn.mud-button:focus-visible,
.app-btn.mud-button:active { background-color: var(--btn-fill); }
```

This rule adds **no behaviour of its own**. It exists purely so `--btn-fill`,
already updated by the variant's own `:hover`, keeps winning.

### 3. `.mud-button { min-width: 64px }`

Which makes an icon-only button oblong. One line, easy to miss because it looks
like a padding bug.

### 4. Dialog styling must be two-class

MudBlazor declares `.mud-dialog` at one class. Your bridge also declares
`.mud-dialog` at one class, for ordinary dialogs. Now a sheet-specific rule
written as `.app-sheet` **ties with the bridge and loses on source order**,
because the bridge loads after the component sheet.

So every sheet surface rule is written as **`.mud-dialog.app-sheet`** — two
classes, which settles it on specificity in both directions and leaves the
bridge's plain-dialog styling intact for the dialogs that are not sheets.

The material `@supports` fallback for the sheet keeps **both** classes for the
same reason; source order alone would not clear the bridge's single-class rule.

### 5. Mud's dialog wrapper padding double-applies

`.mud-dialog-title`, `.mud-dialog-content` and `.mud-dialog-actions` each carry
their own padding (16px/24px), stated at two classes. A sheet whose head, body and
foot already carry their own spacing gets it **twice** and reads as loose. Reset
them at three classes, which clears Mud's two:

```css
.mud-dialog.app-sheet .mud-dialog-title,
.mud-dialog.app-sheet .mud-dialog-content,
.mud-dialog.app-sheet .mud-dialog-actions { padding: 0; letter-spacing: normal; }
```

This is also why `AppleAlert` renders **bare content** rather than a `MudDialog`:
an alert has no title bar, no scroll body and no action tray to fill, so wrapping
it in one buys nothing but padding to undo.

### 6. The `@supports` fallback block must be LAST in the stylesheet

`@supports` adds **no specificity**. Its rules beat their unconditional
counterparts on source order alone. Written earlier in a sheet, the block is
silently shadowed — and in the migration this happened, three of four material
surfaces fell back to the wrong opaque tier and nobody could see why.

Keep it at the end of the last of your own stylesheets, and put a comment there
saying so. Every new material surface needs a line added to that block and a
re-test with `backdrop-filter` disabled.

### 7. `.mud-button-root:disabled` uses `!important`

The one rule that cannot be out-specified at any specificity. **Do not fight it —
agree with it** from the C# palette (`ActionDisabled`, `ActionDisabledBackground`,
`ActionDefault`). See the palette section above. There is deliberately no CSS rule
for this in the bridge; a comment stands in its place so the next person does not
go looking for the missing rule.

### 8. Mud's ambient button `letter-spacing`

Material tracks button labels and uppercases them. That tracking is inherited by
anything button-shaped, including things you did not think of as buttons. It is
killed in the token block, not per-component:

```css
--mud-typography-button-letterspacing: 0;
--mud-typography-button-text-transform: none;
```

The same trap sits on `.mud-table-head`, whose `subtitle2` default is tracked.
Reset `letter-spacing: normal` alongside `text-transform: none` wherever you
restyle a Mud text role.

Do not confuse an inherited trap with a deliberate treatment. `.mud-chip` in the
bridge *sets* `.07em` uppercase on purpose, because a chip is a capsule and
matches `.tag` — that tracking is the typographic treatment of a small state word,
not Mud's ambient tracking leaking in. Reset the tracking that arrives by
inheritance; keep the tracking you chose.

### Bonus: `Align="Align.Right"` emits no class

In 9.7.0, `MudDataGrid`'s `Align` enum does **not** reliably emit
`.mud-align-right` / `.mud-align-end` for a `PropertyColumn` or `TemplateColumn`.
A rule hung off those classes is a rule that sometimes never fires, and the
symptom is numbers that do not line up down a column — subtle enough to ship.

Mitigate by not depending on the hook: put `font-variant-numeric: tabular-nums` on
**every** cell. Tabular figures cost nothing on text cells, and the column aligns
whether or not the class arrives. Keep the `.mud-align-right` rule too, as the
belt to that braces.

### Bonus: MudBlazor's class names are not guessable

Two that cost real time:

- `MudDataGrid` does **not** use `MudTable`'s `.mud-table-sort-label`. It emits
  `.column-header` wrapping `.sortable-column-header` and a `.column-options`
  button. Styling the `.mud-table-*` names looks right and does nothing — visible
  only if you actually click a header.
- The snackbar's dismiss button is `.mud-snackbar-close-button`, **not**
  `MudAlert`'s `.mud-alert-close-button`. The two components share a look but not
  a class name, and the wrong one is the one that reads as obvious.

Inspect the rendered DOM. Do not infer a class name from a shared prefix.

---

## The meta-lesson

> When you replace a design system in place, the dominant failure mode is **a
> correct rule that silently loses the cascade**. Grep finds your tokens. Only the
> rendered page finds these.

Practical consequences:

- Budget verification time for *looking at the page*, not for auditing the source.
- When a rule "does nothing", check specificity and source order **before** you
  check the rule.
- Never reach for `!important` in the bridge. If a rule seems to need it, either
  the companion-rule pattern applies, or the override is in the wrong file.
- If a rule stops working after a MudBlazor upgrade, that is the signal to re-read
  their stylesheet — not to escalate.

---

## The measurement trap

This one is not CSS, and it will waste an afternoon.

> **Transitions do not advance in a browser tab that is not compositing.**

`getComputedStyle` on a transitioned property returns its **start value
indefinitely** in a hidden pane, a background tab, or a headless context that is
not painting. It reads exactly like a broken rule: you assert the hover colour,
you get the resting colour, you conclude the rule lost the cascade, and you go
hunting for a specificity problem that does not exist.

Two safe ways to measure:

1. Apply `transition: none` to the element first, then read the computed value.
2. Measure **geometry** (`getBoundingClientRect`) rather than a transitioned
   paint property.

---

## Browser floor

`color-mix(in oklab, …)` is used to derive washes from a base colour, which keeps
a variant to two lines. It rules out engines older than **Chrome 111 / Safari
16.2**. That was acceptable for a single-operator console; if it is not for you,
substitute literal `rgba()` values at those sites.
