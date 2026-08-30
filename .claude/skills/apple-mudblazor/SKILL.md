---
name: apple-mudblazor
description: Use when building or restyling a Blazor UI with MudBlazor that should feel like an Apple product — dark neutral surfaces, translucent materials, SF-style type, one accent, Filled/Tinted/Plain buttons, and sheets/alerts for modality. Covers the design tokens, the MudBlazor theming seam, component conventions, and when to be modal.
---

# Apple-feel MudBlazor

A design system for MudBlazor applications that follows Apple's Human Interface
Guidelines. It ships as tokens plus a small set of wrapper components, and it is
proven in production in a data-dense operations console — every template here is
a generalization of a file that actually runs, not fresh authoring.

## What this gives you

- A semantic token vocabulary — surfaces, materials, type ramp, motion, depth —
  keyed by `[data-theme]` so a second theme is a second token block.
- A three-file MudBlazor seam that makes unstyled Mud components land looking
  correct, instead of styling them one at a time.
- Buttons with a real hierarchy: Filled, Tinted, Plain, crossed with a
  Normal/Destructive role.
- A sheet, an alert, and a rule for when each is allowed.

## Workflow

1. **Read `references/tokens.md`** and copy `templates/apple-tokens.css` into the
   project's `wwwroot/css/`. This is the foundation; nothing else works without it.
2. **Bundle a type face.** See `templates/fonts/README.md` — SF via `-apple-system`
   on Apple platforms, self-hosted Inter everywhere else. Do not skip this: without
   it, Windows renders the whole product in Segoe UI.
3. **Copy `templates/AppleTheme.cs`**, rename the namespace, and register it with
   `<MudThemeProvider Theme="AppleTheme.Instance" IsDarkMode="true" />`. The colour
   values must match the CSS exactly — read `references/mudblazor-seam.md` for why
   both sides are required. It carries both palettes; if you ship the light theme,
   bind `IsDarkMode` to the same boolean that sets `data-theme` rather than a
   second one.
4. **Copy `templates/apple-components.css`** (buttons, fields, the segmented gate,
   tags, sheet, alert) and then **`templates/apple-bridge.css`**. Load order is
   load-bearing and is fixed in `App.razor`:

   ```
   MudBlazor.min.css → apple-tokens.css → apple-components.css → apple-bridge.css
   ```

5. **Copy the component templates you need** — `AppleEmphasis.cs`, `AppleState.cs`,
   `AppleButton.razor`, `AppleSheet.razor`, `AppleAlert.razor`, `AppleAlerts.cs` —
   renaming the namespace in each. They assume `@using MudBlazor` and
   `@using Microsoft.AspNetCore.Components.Web` are in `_Imports.razor`, that
   `AddMudServices()` has run, and that `<MudDialogProvider />` is in the layout.
   Read `references/components.md` before wiring them into call sites.
6. **Read `references/modality.md` before adding any dialog, sheet, or alert.**

There is no `AppleField` component, on purpose. A field is a plain `<input>`,
`<select>` or `<textarea>` carrying `.field`, so `@bind`, validation and every
native attribute keep working without a parameter to forward each one. The CSS
lives in `apple-components.css`; the conventions are in `references/components.md`.

## The rules that are not negotiable

These are the decisions that make the system cohere. Breaking one does not produce
a variation; it produces an interface that looks like MudBlazor wearing a costume.

- **One accent.** Interactivity, focus, and selection, and nothing else. A second
  accent is how an interface stops meaning anything.
- **Exactly one Filled button per context.** If a panel seems to need two, one of
  them is not the primary action.
- **Hue is not an affordance channel.** State colours (green, red, orange) belong
  on values, tags, and status — not on the controls that act on them. The one
  exception is `Role.Destructive`.
- **Materials are for things that float.** Menus, popovers, dialogs, sheets, and
  chrome. Anything holding data stays opaque, because legibility beats depth.
- **Hairlines separate; they never outline.** A border around a panel or a button
  is the Material habit this system replaces. Depth comes from fill and shadow.
- **Motion says where a thing came from.** Sheets rise, popovers scale from their
  anchor, controls settle. Nothing moves for decoration, and everything stops
  under `prefers-reduced-motion`.
- **Never `!important` in the bridge file.** If a rule needs it, the override is
  in the wrong place. The one legitimate `!important` in the whole system is the
  `prefers-reduced-motion` block, because a preference a component can
  out-specify is not a preference.

## The light theme

`templates/apple-tokens.css` ships both blocks. Light mode is
`[data-theme="light"]` redefining the same names — no component changes, and if
adding one requires touching anything below the token blocks, the token that made
it necessary was named for a colour.

Three things are not obvious and are worth reading before you tune the values.
The surface ramp **re-derives rather than inverts** — grey ground, white content
on it, interactive tiers darkening — because a ground lighter than white does not
exist. Hover and press **deepen** instead of lightening, because against a white
page a control that lightens under the pointer reads as fading. And a forgotten
token **fails nowhere**: it silently keeps its dark value, so the mirror has to be
tested mechanically in both directions.

`references/tokens.md` has the full second-theme section — the ramp table, the
contrast arithmetic redone for light, what to test, and how to wire the toggle.

## The one warning worth reading twice

When you replace a design system **in place**, the dominant failure mode is not a
missing rule. It is **a correct rule that silently loses the cascade** — right
property, right value, out-specified by a MudBlazor compound selector or shadowed
by source order, so nothing appears wrong in the source and everything is wrong on
screen. Eight separate instances of this occurred in one migration; they are
catalogued in `references/mudblazor-seam.md`. Grep finds your tokens. Only the
rendered page finds these.
