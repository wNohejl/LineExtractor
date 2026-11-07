---
name: apple-mudblazor
description: Use when building, restyling or reviewing anything on the Desk (the LineOps.Desk package, or its source in src/LineOps.Desk): where the tokens, Desk* primitives and MudBlazor seam live, the rules that hold them together, and when to be modal. Pairs with apple-hig-components (what to draw) and anthropic-skills:apple-hig-web (patterns).
---

# Apple-feel MudBlazor, as the Desk builds it

Apple's Human Interface Guidelines applied to MudBlazor. The system is **already built** and
ships as the `LineOps.Desk` package (`dotnet new deskapp` starts an application on it). This
skill exists so that every new window, panel and primitive lands *inside* the system rather
than beside it. It was once a set of copyable templates; those are gone — the Desk is the
template now, and its README is its manual.

## Where the system lives

The Desk is read in one of two places. **In an application on the package** (one `dotnet new
deskapp` made), the Desk is a NuGet package: its stylesheets, scripts, README and design ADRs are
in the package folder, `~/.nuget/packages/lineops.desk/<version>/` (on Windows
`%USERPROFILE%\.nuget\packages\lineops.desk\<version>\`), its API is the XML documentation beside
the assembly, and every primitive is live in the **Parts bin** window. **In the Desk's own
repository** it is source, under `src/LineOps.Desk/`. Read the column for where you are; the
`_content/LineOps.Desk/…` path the page loads, the namespaces and the assembly are the same in both.

| The system's part | In an application on the package | In the Desk's repository |
|---|---|---|
| The manual: wiring, every primitive, the tokens, the rules | `README.md` in the package folder | `src/LineOps.Desk/README.md` |
| Tokens — surfaces, materials, type ramp, spacing, motion, depth, z | `staticwebassets/css/desk.css` (first token block) | `src/LineOps.Desk/wwwroot/css/desk.css` |
| Theme families (dark + light blocks per family) and the five accents | `staticwebassets/css/themes.css` | `src/LineOps.Desk/wwwroot/css/themes.css` |
| Control recipes — buttons, fields, the gate, tags, sheet, alert, steps | `staticwebassets/css/controls.css` and `desk.css` | `src/LineOps.Desk/wwwroot/css/controls.css` and `desk.css` |
| The MudBlazor seam (re-points Mud's variables at the tokens; never `!important`) | `staticwebassets/css/mud-bridge.css` — loads **last** | `src/LineOps.Desk/wwwroot/css/mud-bridge.css` |
| Why the system is shaped this way | `docs/adr/` in the package folder | `docs/adr/` (0007, 0008, 0013, 0016, 0020) |
| Primitives, their parameters, and the enums (`DeskEmphasis`, `DeskRole`, `DeskKeySize`, `DeskState`) | `lib/net10.0/LineOps.Desk.xml`, the README's Primitives table, and the Parts bin | `src/LineOps.Desk/Primitives/` |
| Window chrome — Desk, AppWindow, WindowBar, header, footer, rail, palette, Parts bin | the README's Wiring section | `src/LineOps.Desk/Windowing/` |
| The C# palette MudBlazor and the charts read | `DeskPalettes`, `DeskTheme`, `ThemeService` in the XML docs | `src/LineOps.Desk/Theming/` |
| The application's own sheet (spends tokens; defines none) | `wwwroot/css/app.css` | `src/LineOps.Web/wwwroot/css/lineops.css` |
| Render tests on the `DeskTestContext` bench, and the Desk's conventions as calls | a test project on `LineOps.Desk.Testing` (`DeskConventions.All`) | `tests/LineOps.Desk.Tests/`, `tests/LineOps.Web.Tests/` |
| Fonts — SF via `-apple-system`, self-hosted Inter elsewhere | `staticwebassets/fonts/` | `src/LineOps.Desk/wwwroot/fonts/` |

**An application never edits the package's files.** A change the Desk needs — a new primitive,
a token, a fix to a recipe — is made in the Desk's repository and released; the application's
sheet only spends tokens and lays out its own windows.

## Workflow

1. **Read `references/tokens.md`** once, then the token block of `desk.css`. Every colour,
   radius, shadow and duration in new work is a token from there. If the token you need does
   not exist, add it to **every** theme block in `themes.css` and to its C# mirror, and run
   `DeskThemeTests`; never write a literal. `DeskCssConventionTests` fails a literal colour.
2. **Build on the primitives.** A new panel is a `DeskPanelBase` window with `PanelTitle`,
   `PanelHeader`, `PanelSection` and `PanelToolbar`; its controls are `DeskButton`,
   `DeskField`, `DeskSelect`, `DeskSwitch`, `DeskToggle`, `DeskTabs`, `DeskSteps`; its data is
   `DeskGrid`, `Metric`/`MetricRow` or `DeskChart`; its empty and failed states are
   `EmptyState` and `PanelLoadFailure`; its actions end in a `KeyRow`. Open the Parts bin
   (catalogue key `parts`) to see each one live on the current theme. Read
   `references/components.md` for the conventions each carries.
3. **A field is a plain element carrying `.field`**, so `@bind`, validation and native
   attributes keep working. `DeskField` wraps the labelled case; it does not replace the rule.
4. **Read `references/modality.md` before adding any dialog, sheet or alert.** The desk is
   never modal for comparison (`DeskDialog` floats); `DeskSheet` is for one self-contained
   task; `DeskAlert`, through `IDeskAlerts`, for one decision that names its consequence.
5. **Read `references/mudblazor-seam.md` before touching an unstyled Mud component.** The
   failure mode is a correct rule that loses the cascade; the eight instances are catalogued.
6. **Write the render test on `DeskTestContext`** in the same commit, and run the host's
   `DeskConventionTests` (every field has a name; the app sheet spends tokens).
7. **Decide what to draw with apple-hig-components** (which control, how grouped, the audit),
   and reach for `anthropic-skills:apple-hig-web` for a *pattern* — navigation, loading,
   feedback, search, settings, undo — or to read a live HIG page as Markdown. Its
   `hig-tokens.css`, `hig-components.css` and `hig.js` assets are **not** used here; the
   Desk's tokens are the vocabulary.

## The rules that are not negotiable

These are the decisions that make the system cohere. Breaking one does not produce a
variation; it produces an interface that looks like MudBlazor wearing a costume.

- **One accent.** Interactivity, focus and selection, and nothing else. *Which* accent is
  the operator's (`DeskAccent`: Blue, Teal, Indigo, Purple, Graphite); a component never
  names the hue.
- **Exactly one Filled key per context,** last in its row. If a panel seems to need two, one
  of them is not the primary action. `KeyRow` holds the order.
- **Hue is not an affordance channel.** State colours (green, red, orange) belong on values,
  tags and status — not on the controls that act on them. `DeskRole.Destructive` is the one
  exception, and it is a role, not a tone.
- **One edge.** A group or card carries one 1px hairline where it is defined (the October
  2026 house rule, after a borderless pass was rejected); rows inside are divided by inset
  hairlines and padding, never framed again; a bordered thing inside a bordered thing is
  wrong; buttons and fields carry no outline at rest. `DeskCssConventionTests` holds it.
- **Materials are for things that float.** Menus, popovers, dialogs, sheets and chrome.
  Anything holding data stays opaque, because legibility beats depth.
- **Motion says where a thing came from.** Sheets rise, popovers scale from their anchor,
  controls settle. Nothing moves for decoration, and everything stops under
  `prefers-reduced-motion`.
- **Never `!important` in `mud-bridge.css`.** If a rule needs it, the override is in the
  wrong place. The one legitimate `!important` in the system is the `prefers-reduced-motion`
  block, because a preference a component can out-specify is not a preference.
- **No domain in the Desk.** Its code and documentation name no application's records
  (`DeskVocabularyTests`). Anything that knows about the application's own records lives in
  the application (in LineOps, `LineOps.Web`).

## The second theme, and the others

A theme family is three things that must agree: a dark and a light block in `themes.css`
(each redeclaring every colour token of the Standard dark block and nothing else), a member
of `DeskThemeFamily`, and its two palettes in `DeskPalettes`. No component, selector or C#
call site knows how many themes there are; if adding one requires touching anything below
the token blocks, the token that made it necessary was named for a colour.

Three things are not obvious. The light surface ramp **re-derives rather than inverts** —
grey ground, white content on it, interactive tiers darkening — because a ground lighter
than white does not exist. Hover and press **deepen** instead of lightening, because a
control that lightens under the pointer on a white page reads as fading. And a forgotten
token **fails nowhere**: it silently keeps its dark value, so `DeskThemeTests` and
`ThemeContrastTests` hold the mirror mechanically in both directions.
`references/tokens.md` has the full second-theme section.

## The one warning worth reading twice

When you change a design system **in place**, the dominant failure mode is not a missing
rule. It is **a correct rule that silently loses the cascade** — right property, right
value, out-specified by a MudBlazor compound selector or shadowed by source order, so nothing
appears wrong in the source and everything is wrong on screen. Eight separate instances
occurred in one migration; they are catalogued in `references/mudblazor-seam.md`. Grep finds
your tokens. Only the rendered page finds these — screenshot both themes before "done".
