# Apple-Feel Design System — Design

**Date:** 2026-08-29
**Status:** Approved for planning
**Supersedes (visually):** ADR 0008's gloss-as-affordance and hue-as-state rules
**Amends:** ADR 0013 (adds a modal tier beside the floating layer)

## Goal

Two deliverables, built in sequence:

1. **Evolve LineOps in place** to an Apple-style dark visual language — materials, motion,
   typography, and component conventions drawn from Apple's Human Interface Guidelines —
   replacing the current graphite/gloss system. Clean slate: the old semantic rules
   (gloss = pressable, hue = state) are dropped and every control and call site is
   re-judged against HIG guidance.
2. **Extract a standalone Claude skill package** ("apple-mudblazor") from the files that
   shipped, so lower models can apply the same system to any MudBlazor project. A future
   NuGet class library is designed for but not built (see final section).

**Approach:** token-first, in place. ADR 0008's three-file seam (`DeskTheme.cs` /
`mud-bridge.css` / thin `Desk*` wrappers) is the right structure; only its contents
change. Component names and call sites survive except the deliberate `Tone` →
`Emphasis` + `Role` API change. The desk never shows two button languages at once.

## Section 1 — Visual language & token architecture

**Palette.** True-neutral macOS-dark surfaces replace the blue graphite:

- Elevation ramp: `#1C1C1E` window void → `#232326` panel body → `#2C2C2E` raised
  surfaces; hairline separators at `rgba(255,255,255,0.10)`.
- Text is white at opacity tiers, not fixed grays: primary ~92%, secondary ~55%,
  tertiary ~30% — so text sits correctly on any material.
- **One accent: systemBlue `#0A84FF`** for interactivity, focus, and selection.
- Semantic colors are Apple's dark-mode set — systemGreen `#30D158`, systemRed
  `#FF453A`, systemOrange `#FF9F0A` — applied per-HIG judgment at each call site,
  not as an always-on channel.

**Materials.** Four translucency tiers modeled on HIG materials (ultra-thin → thick):
`rgba` surface + `backdrop-filter: blur()` recipes exposed as tokens
(`--material-thin`, etc.). Floating dialogs, menus, and the rail get materials; flat
panel bodies stay opaque for data legibility.

**Typography.** SF Pro is not licensable off Apple platforms. Stack:
`-apple-system, BlinkMacSystemFont` (real SF on Macs) with **bundled Inter variable
font** as the everywhere-else face, shipped locally so Windows never falls back to
Segoe. Full HIG text-style ramp as tokens: Large Title 26 / Title 1–3 / Headline
13-semibold / Body 13 / Subheadline / Footnote / Caption. **Tabular numerals
(`font-variant-numeric: tabular-nums`) on all odds, price, and metric displays.**

**Shape & depth.** Continuous-feel corner radii: panels 12px, controls 6–7px, sheets
14px. Depth comes from soft layered shadows plus materials, not borders — hairlines
are for separators and inset input controls only, never as outlines around panels
or buttons.

**Motion.** Duration tokens (150/250/350ms) and spring-approximating `cubic-bezier`
curves. `prefers-reduced-motion` honored globally. Motion communicates where things
come from — sheets rise, popovers scale from their anchor, panels settle — never
decoration.

**Future themes.** All tokens are semantic (`--surface-2`, `--accent`,
`--text-secondary`), never color-named. One `:root` token block keyed by a
`data-theme` attribute on `<html>`. Ships with one theme (`apple-dark`); a light
theme later is a second token block plus a `DeskTheme.cs` variant with zero
component edits. `DeskTheme.cs` remains the C# mirror feeding MudBlazor per ADR
0008's seam (including the `:root:root` specificity trick in the bridge).

## Section 2 — Component restyle (the clean slate)

**Affordance language.** Gloss dies; Apple's button hierarchy replaces it:

- **Filled** — accent-tinted, white text; the one primary action in a context.
- **Tinted** — accent at ~15% wash with accent text; secondary actions.
- **Plain** — text-only; hover reveals a subtle wash; toolbar and inline actions.

`DeskButton`'s API becomes **`Emphasis` (Filled/Tinted/Plain) × `Role`
(Normal/Destructive)** — Apple's own model. The `Tone` enum retires. The call-site
sweep maps all ~45 usages by judgment: most `Go`/`Action` become one Filled per
panel plus Tinted for the rest; `Stop` becomes `Role.Destructive`. Disabled is
reduced opacity (standard convention, safe again now that gloss carries no meaning).
Busy keeps the no-spinner rule, expressed as a subtle indeterminate shimmer within
the control.

**Inputs.** Fields become macOS-style: subtly inset, hairline border, 2px accent
outer-glow focus ring. `DeskSwitch` becomes the iOS/macOS pill toggle.
Selects/menus become material-backed popovers with checkmark selection.

**Chrome.** `AppWindow`/`WindowBar` restyle to macOS proportions: tighter title
bars, materials on the bar, a traffic-light-inspired close control (not a literal
clone of Apple's). The rail becomes a sidebar-material surface. The pulse strip
survives as the one permitted loud element, recolored to the semantic set.

**Data surfaces.** `DeskGrid`/tables go macOS-list style: no zebra striping,
hairline row separators, accent selection wash, tabular numerals, compact 28–32px
rows — density stays, decoration goes. Tags/badges become capsules; skeletons,
progress, and empty states restyle to match.

## Section 3 — The modality layer

Two layers, by purpose.

**Floating layer (non-modal), restyled not replaced.** `DeskDialog` keeps
drag/cascade/raise and becomes an Apple-style utility window: thick material, 14px
radius, soft deep shadow, macOS title bar. Menus and `PullMenu` become anchored
popovers on thin material with directional scale-in from the anchor.

**Modal layer (new), two components:**

- **`DeskSheet`** — centered modal sheet over a dimming scrim: thick material,
  spring-settle entrance, focus trap, `Esc` cancels. For focused self-contained
  tasks. Built on `MudDialog` (its service provides scrim, focus trap, stacking)
  with our chrome and motion over it.
- **`DeskAlert`** — small centered alert: icon, title, message, ≤3 buttons;
  destructive action styled `Role.Destructive` and never the default. One-line
  async API: `await Alerts.ConfirmAsync(...)`.

**The decision rule (ships verbatim in the skill):**

> Modal is for *interruption with intent*. Use `DeskAlert` when the user must decide
> before anything else happens. Use `DeskSheet` when a task is self-contained and
> completing it is the only goal. Use `DeskDialog` (floating) when the user needs to
> see or compare other things while working — comparison is never modal. If you're
> unsure, don't be modal.

ADR bookkeeping: a new ADR records the clean-slate restyle superseding 0008's
visual rules; ADR 0013 is amended to add the modal tier.

## Section 4 — The skill package

Extracted at the end from shipped files, into `.claude/skills/apple-mudblazor/`:

```
apple-mudblazor/
├── SKILL.md                  — trigger description, design rules, workflow
├── references/
│   ├── tokens.md             — palette, materials, type ramp, radii, motion — and why
│   ├── components.md         — Filled/Tinted/Plain × Role, inputs, lists, chrome,
│   │                           with do/don't pairs
│   ├── modality.md           — the sheet/alert/floating decision rule + recipes
│   └── mudblazor-seam.md     — the three-file seam, :root:root trick, Mud gotchas
└── templates/
    ├── AppleTheme.cs         — generalized DeskTheme.cs (no LineOps names)
    ├── apple-tokens.css      — the :root token block
    ├── apple-bridge.css      — generalized mud-bridge.css
    ├── AppleButton.razor     — plus Sheet, Alert, Field, Switch templates
    └── fonts/                — Inter variable + @font-face block
```

**Usage model for lower models:** SKILL.md's workflow is copy templates → rename
namespace → wire `MudThemeProvider` → apply the rules to the app's own components.
Judgment calls (which action is Filled, when to be modal) are stated as rules, not
left to taste. Templates are plain copyable source.

**Constraint honored during LineOps work so extraction stays cheap:** every new file
is marked template-able or LineOps-specific from day one, and template-able
components reference only token names and MudBlazor APIs — no LineOps types, no
app-specific CSS hooks.

## Section 5 — Phasing, verification, risks

**Phases** (each independently shippable; never two button languages at once):

1. **Foundation** — token block in `lineops.css`, Inter bundled, rewritten
   `DeskTheme.cs` + `mud-bridge.css`, core control restyle (buttons, fields,
   switches, selects). The `Tone` → `Emphasis`+`Role` change and full call-site
   sweep happen here — the compiler forces every site to be visited anyway.
2. **Chrome & surfaces** — windows, rail, title bars, grids/tables, tags,
   skeletons, progress, empty states, pulse strip recolor.
3. **Modality** — `DeskSheet`, `DeskAlert`, popover-ized menus, `DeskDialog`
   material restyle; migrate existing confirm flows onto `DeskAlert`.
4. **Docs & extraction** — new ADR, `PartsPanel` updated to showcase the system,
   skill package extracted.

**Verification.** Browser pane is the test rig: after each phase, drive the live
app (`PartsPanel` for controls, real panels for density) and capture screenshot
proof. Carry forward ADR 0008's lesson: hidden tabs don't composite, so measure
transitioned properties with transitions disabled or measure geometry. Explicit
checks for reduced-motion and `:focus-visible` parity.

**Risks:**

- **`backdrop-filter` performance** over layered surfaces on weak GPUs — materials
  are limited to the floating/overlay tier by design, and the `--material-*` tokens
  have an opaque fallback if it bites.
- **Contrast** — white-at-55% secondary text on `#232326` must pass WCAG checks in
  the data-dense grids; adjust token values, not components.
- **MudBlazor 9.7 dialog internals** own the scrim/focus trap; if its markup fights
  the sheet chrome, fall back to rendering our own scrim via the existing
  `FollowUpLayer` pattern.

## Future: AppleMud class library (designed for, not built)

When the skill proves out, point an LLM at this section:

- Extract `templates/` into a Razor Class Library, `AppleMud.csproj`, with
  `apple-tokens.css`, `apple-bridge.css`, and the Inter fonts as static web assets
  (`_content/AppleMud/...`).
- Components move as-is — they already reference only token names and MudBlazor
  APIs by the constraint above.
- The skill package remains the guidance layer on top of the library (rules and
  judgment don't compile); version the two together.
- LineOps then consumes the package and deletes its local copies, becoming
  consumer #1 of the library exactly as it was consumer #1 of the skill.
