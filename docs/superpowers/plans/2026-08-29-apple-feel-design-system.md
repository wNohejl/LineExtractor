# Apple-Feel Design System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace LineOps' graphite/gloss visual system with an Apple HIG-derived dark design system (materials, SF-style typography, spring motion, Filled/Tinted/Plain buttons, sheets and alerts), then extract it as a reusable `apple-mudblazor` Claude skill package.

**Architecture:** Token-first, in place. ADR 0008 established a three-file seam — `Theming/DeskTheme.cs` (palette in C#, because MudBlazor derives `-rgb`/`-darken`/`-lighten`/`-hover` variants CSS cannot), `wwwroot/css/mud-bridge.css` (everything non-colour), and thin `Desk*` wrapper components. That structure is kept; only its contents change. Component names and call-site signatures survive except the deliberate `Tone` → `Emphasis`+`Role` API change, which the compiler forces us to visit everywhere.

**Tech Stack:** .NET 10, Blazor Server (global InteractiveServer per ADR 0004), MudBlazor 9.7.0, bUnit + xUnit for component tests, plain CSS with custom properties (no preprocessor).

## Global Constraints

- **MudBlazor version is 9.7.0**, pinned centrally in `Directory.Packages.props`. Do not add package references with inline versions.
- **Never use `!important` in `mud-bridge.css`.** Every override raises specificity honestly or wins on source order. If a rule stops working after a MudBlazor upgrade, re-read their stylesheet.
- **`:root:root` in `mud-bridge.css` is load-bearing.** `MudThemeProvider` injects a runtime `<style>` containing `:root { … }`, which beats an external sheet's plain `:root` on source order. The doubled pseudo-class settles it without `!important`.
- **Stylesheet load order is fixed in `App.razor`** and must not change: `MudBlazor.min.css` → `lineops.css` (defines tokens) → `mud-bridge.css` (spends them).
- **Colours are never written literally in `mud-bridge.css`.** They resolve through `lineops.css` tokens. The C# mirror lives in `DeskTheme.cs` and the two must agree exactly.
- **All tokens are semantic, never colour-named** (`--surface-1`, `--accent`, `--text-secondary` — not `--blue`, `--gray-800`).
- **`color-mix(in oklab, …)` is permitted** (ADR 0008 already accepted the Chrome 111 / Safari 16.2 floor for this single-operator console).
- **Every file created in this plan is marked `TEMPLATE-ABLE` or `LINEOPS-SPECIFIC`** in a comment at the top. Template-able files must reference only token names and MudBlazor APIs — no LineOps types, no app-specific CSS hooks. Task 18 extracts exactly the template-able ones.
- **`prefers-reduced-motion: reduce` must be honoured** by every animation or transition introduced.
- Run tests with `dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj`.
- Build with `dotnet build LineOps.slnx`.

---

## File Structure

**Phase 1 — Foundation**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — `:root` token block (lines 10–108) replaced; `.desk-key`/`.btn` recipe (lines ~1676–1875) replaced; `.field` block (lines ~1878–1972) replaced.
- Create: `src/LineOps.Web/wwwroot/fonts/` — Inter variable font files.
- Modify: `src/LineOps.Web/Components/App.razor` — drop Google Fonts CDN links, self-host.
- Modify: `src/LineOps.Web/Theming/DeskTheme.cs` — Apple palette + type/shape mirror.
- Create: `src/LineOps.Web/Components/Desk/DeskEmphasis.cs` — `DeskEmphasis`, `DeskRole` enums.
- Delete: `src/LineOps.Web/Components/Desk/DeskTone.cs` — `DeskKeySize` moves to `DeskEmphasis.cs`.
- Modify: `src/LineOps.Web/Components/Desk/DeskButton.razor` — new API.
- Modify: `src/LineOps.Web/Components/Desk/RowAction.cs` — `Tone` property → `Emphasis`/`Role`.
- Modify: 25 call-site files (Task 5 lists them).
- Modify: `src/LineOps.Web/wwwroot/css/mud-bridge.css` — remapped to new tokens.
- Modify/Create tests: `tests/LineOps.Web.Tests/DeskButtonTests.cs`, `DeskThemeTests.cs`.

**Phase 2 — Chrome & surfaces**
- Modify: `lineops.css` (window chrome, rail, grid/table, tag, skeleton, progress, empty-state, pulse blocks).
- Modify: `Components/Windowing/AppWindow.razor`, `WindowBar.razor`, `RailMenu.razor`, `DeskHeader.razor`, `DeskFooter.razor` — only where markup must change for the new chrome.

**Phase 3 — Modality**
- Modify: `Components/Desk/DeskDialog.razor` + its CSS — material restyle.
- Create: `Components/Desk/DeskSheet.razor` — modal sheet (TEMPLATE-ABLE).
- Create: `Components/Desk/DeskAlert.razor` — alert body rendered inside a Mud dialog (TEMPLATE-ABLE).
- Create: `Components/Desk/DeskAlerts.cs` — `IDeskAlerts` service + implementation (TEMPLATE-ABLE).
- Modify: `Program.cs` — register the alerts service.
- Modify: `Components/Desk/PullMenu.razor` — anchored popover.
- Create tests: `DeskSheetTests.cs`, `DeskAlertTests.cs`.

**Phase 4 — Docs & extraction**
- Create: `docs/adr/0016-apple-feel-design-system.md`.
- Modify: `docs/adr/0013-the-board-and-the-floating-layer.md` — amendment note.
- Modify: `Components/Panels/PartsPanel.razor` — showcase the new system.
- Create: `.claude/skills/apple-mudblazor/**` — the skill package.

---

## Phase 1 — Foundation

### Task 1: Apple-dark token block and self-hosted type

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css:1-127` (header comment, `:root`, `html, body`, `.num/.mono`)
- Create: `src/LineOps.Web/wwwroot/fonts/InterVariable.woff2`
- Create: `src/LineOps.Web/wwwroot/fonts/InterVariable-Italic.woff2`
- Modify: `src/LineOps.Web/Components/App.razor:16-19`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: the full token vocabulary every later task spends — `--surface-0..3`, `--separator`, `--separator-strong`, `--text-primary/secondary/tertiary`, `--accent`, `--accent-hover`, `--accent-wash`, `--on-accent`, `--state-positive`, `--state-negative`, `--state-warning` and their `-wash` variants, `--chart-neutral`, `--chart-neutral-dim`, `--material-ultrathin/thin/regular/thick`, `--material-blur`, `--face-ui`, `--face-data`, `--text-largetitle/title1/title2/title3/headline/body/subheadline/footnote/caption`, `--weight-regular/medium/semibold`, `--space-1..16`, `--radius-sm/radius/radius-panel/radius-sheet`, `--dur-fast/base/slow`, `--ease-standard/spring/exit`, `--shadow-1/2/3`, `--z-*`, `--rail-size`, `--titlebar-h`.

- [ ] **Step 1: Download the Inter variable font**

Inter is SIL Open Font License 1.1, so redistribution inside the repo is fine. Fetch the two variable files:

```bash
mkdir -p src/LineOps.Web/wwwroot/fonts
curl -L -o src/LineOps.Web/wwwroot/fonts/InterVariable.woff2 https://github.com/rsms/inter/raw/master/docs/font-files/InterVariable.woff2
curl -L -o src/LineOps.Web/wwwroot/fonts/InterVariable-Italic.woff2 https://github.com/rsms/inter/raw/master/docs/font-files/InterVariable-Italic.woff2
```

Verify both files are non-trivial (each should be >300KB):

```bash
ls -l src/LineOps.Web/wwwroot/fonts/
```

If the URLs 404, the fallback is the official release zip at `https://github.com/rsms/inter/releases/latest` — extract `InterVariable.woff2` and `InterVariable-Italic.woff2` from `web/`. Do not substitute a different family; the stack below depends on Inter's metrics matching SF closely.

- [ ] **Step 2: Replace the header comment and `:root` block**

In `src/LineOps.Web/wwwroot/css/lineops.css`, replace everything from line 1 through the closing brace of `:root` (line 108) with this. Nothing else in the file changes yet — later tasks handle the component blocks that reference the old names.

```css
/* ============================================================================
   LineOps — "The Desk"
   A single-operator sports-data operations console.

   The visual language follows Apple's Human Interface Guidelines: true-neutral
   dark surfaces, one accent, translucent materials on anything that floats,
   an SF-derived type ramp, and motion that says where a thing came from.

   Every token below is semantic — it names a role, never a colour. That is what
   lets a second theme be a second token block rather than a component rewrite.
   The C# mirror of the colour tokens lives in Theming/DeskTheme.cs and must
   agree with this block exactly; see ADR 0016.
   ============================================================================ */

/* The default theme is apple-dark. A future theme adds a second block keyed on
   its own [data-theme] value and changes nothing else in this file. */
:root,
[data-theme="apple-dark"] {
    /* --- Surfaces -----------------------------------------------------------
       A true-neutral elevation ramp. Warmth or a blue undertone would fight the
       accent; macOS keeps its chrome neutral for exactly that reason. */
    --surface-0: #1C1C1E; /* desk void — the ground everything sits on */
    --surface-1: #232326; /* panel and window body */
    --surface-2: #2C2C2E; /* title bars, raised rows, resting controls */
    --surface-3: #38383A; /* hover fills, pressed states */

    /* Hairlines separate; they never outline. A border around a panel is the
       Material habit this system is replacing. */
    --separator: rgba(255, 255, 255, .10);
    --separator-strong: rgba(255, 255, 255, .16);

    /* --- Text ---------------------------------------------------------------
       White at opacity tiers rather than three fixed greys, so text keeps its
       relationship to whatever surface or material is underneath it. */
    --text-primary: rgba(255, 255, 255, .92);
    --text-secondary: rgba(255, 255, 255, .55);
    --text-tertiary: rgba(255, 255, 255, .30);

    /* --- Accent -------------------------------------------------------------
       One accent, systemBlue. Interactivity, focus, and selection — nothing
       else. A second accent is how an interface stops meaning anything. */
    --accent: #0A84FF;
    --accent-hover: #3D9BFF;
    --accent-press: #0871DB;
    --accent-wash: rgba(10, 132, 255, .15);
    --accent-wash-strong: rgba(10, 132, 255, .24);
    --on-accent: #FFFFFF;

    /* --- Semantic state -----------------------------------------------------
       Apple's dark-mode system colours. Unlike the old dial these are applied by
       judgment where a state genuinely needs a colour — not as an always-on
       channel that tinted every control on the desk. */
    --state-positive: #30D158;
    --state-negative: #FF453A;
    --state-warning: #FF9F0A;
    --state-positive-wash: rgba(48, 209, 88, .15);
    --state-negative-wash: rgba(255, 69, 58, .15);
    --state-warning-wash: rgba(255, 159, 10, .15);

    /* Chart ink. Opaque on purpose: a translucent series colour composites
       against whatever is drawn beneath it — gridlines, another series — and
       stops being one colour. These are Apple's systemGray and systemGray2,
       the same ramp the surfaces come from (systemGray5 and systemGray6 are
       --surface-2 and --surface-0). */
    --chart-neutral: #8E8E93;
    --chart-neutral-dim: #636366;

    /* --- Materials ----------------------------------------------------------
       Four tiers, thinnest to thickest, mirroring HIG materials. A material is
       for things that float — menus, dialogs, sheets, the rail. Flat panel
       bodies stay opaque, because data legibility beats depth.
       See the @supports rule under the shell for the no-backdrop-filter path. */
    --material-ultrathin: rgba(44, 44, 46, .55);
    --material-thin: rgba(40, 40, 42, .70);
    --material-regular: rgba(36, 36, 38, .82);
    --material-thick: rgba(30, 30, 32, .92);
    --material-blur: saturate(180%) blur(30px);

    /* --- Type ---------------------------------------------------------------
       SF Pro cannot be licensed off Apple platforms, so: real SF via -apple-system
       on a Mac, bundled Inter everywhere else. Inter is the closest legal match on
       metrics, and shipping it locally stops Windows falling through to Segoe.
       One family for everything — Apple sets numbers in SF too. */
    --face-ui: -apple-system, BlinkMacSystemFont, 'Inter var', 'Inter', 'Segoe UI', system-ui, sans-serif;
    --face-data: var(--face-ui);

    /* The HIG text-style ramp. A call site names the style, never the pixels. */
    --text-largetitle: 26px;
    --text-title1: 22px;
    --text-title2: 17px;
    --text-title3: 15px;
    --text-headline: 13px; /* same size as body, distinguished by weight */
    --text-body: 13px;
    --text-subheadline: 12px;
    --text-footnote: 11px;
    --text-caption: 10px;

    --leading-title: 1.25;
    --leading-body: 1.45;
    --leading-tight: 1.2;

    --weight-regular: 400;
    --weight-medium: 500;
    --weight-semibold: 600;

    /* --- Spacing ------------------------------------------------------------
       One 4px ramp for the whole desk. Steps skip where the eye cannot tell the
       difference, so every gap is a named rung. Unchanged from the old system —
       it was already right, and DeskSpace in C# maps onto it. */
    --space-1: 4px;
    --space-2: 8px;
    --space-3: 12px;
    --space-4: 16px;
    --space-6: 24px;
    --space-8: 32px;
    --space-12: 48px;
    --space-16: 64px;

    --rail-size: 46px;
    --titlebar-h: 34px;

    /* --- Shape --------------------------------------------------------------
       Apple's radii are larger than Material's and grow with the surface. */
    --radius-sm: 5px; /* chips, tags, inline marks */
    --radius: 7px; /* controls — buttons, fields, selects */
    --radius-panel: 12px; /* windows, panels, popovers */
    --radius-sheet: 14px; /* sheets and alerts */

    /* --- Motion -------------------------------------------------------------
       Motion says where a thing came from. A sheet rises, a popover scales out
       of its anchor, a control settles. Nothing moves for decoration.
       --ease-spring overshoots a hair and settles, approximating the platform
       spring; --ease-exit does not overshoot, because things leaving should not
       draw a second look. */
    --dur-fast: .15s;
    --dur-base: .25s;
    --dur-slow: .35s;
    --ease-standard: cubic-bezier(.25, .1, .25, 1);
    --ease-spring: cubic-bezier(.32, 1.28, .42, 1);
    --ease-exit: cubic-bezier(.4, 0, 1, 1);

    /* --- Depth --------------------------------------------------------------
       Soft, layered, and wide. Depth comes from shadow and material, not border. */
    --shadow-1: 0 1px 2px rgba(0, 0, 0, .30);
    --shadow-2: 0 4px 12px -2px rgba(0, 0, 0, .45), 0 1px 3px rgba(0, 0, 0, .30);
    --shadow-3: 0 16px 40px -8px rgba(0, 0, 0, .60), 0 4px 12px rgba(0, 0, 0, .35);
    --shadow-sheet: 0 32px 80px -12px rgba(0, 0, 0, .75), 0 8px 24px rgba(0, 0, 0, .45);

    /* Focus. One ring, everywhere, accent-coloured — Apple's own convention.
       ADR 0008 used a chalk ring because an iris ring on an iris key vanished;
       that problem is gone now that filled buttons are the only accent surface
       and the ring sits outside them on an offset. */
    --focus-ring: 0 0 0 3px var(--accent-wash-strong), 0 0 0 1px var(--accent);

    /* --- Layers -------------------------------------------------------------
       Deliberately low at the desk level so anything floating has room. */
    --z-window: 10;
    --z-divider: 40;
    --z-chrome: 8000; /* header, footer, side rail */
    --z-popover: 9000; /* menus anchored to chrome */
    --z-modal: 9500; /* scrim, sheets, alerts */
    --z-toast: 9800; /* transient notices, always on top */
}

/* Inter, self-hosted. The variable file covers every weight the ramp asks for,
   so there is one request rather than four. */
@font-face {
    font-family: 'Inter var';
    font-style: normal;
    font-weight: 100 900;
    font-display: swap;
    src: url('../fonts/InterVariable.woff2') format('woff2');
}

@font-face {
    font-family: 'Inter var';
    font-style: italic;
    font-weight: 100 900;
    font-display: swap;
    src: url('../fonts/InterVariable-Italic.woff2') format('woff2');
}

html, body {
    margin: 0;
    padding: 0;
    height: 100%;
    overflow: hidden; /* the desk manages its own scrolling, per window */
    background: var(--surface-0);
    color: var(--text-primary);
    font-family: var(--face-ui);
    font-size: var(--text-body);
    line-height: var(--leading-body);
    -webkit-font-smoothing: antialiased;
    -moz-osx-font-smoothing: grayscale;
}

/* Numbers are read in columns, so they get tabular figures everywhere. There is
   no separate mono family any more — SF and Inter both carry proper tabular
   figures, and one family across the desk is the HIG reading. */
.num, .mono {
    font-variant-numeric: tabular-nums;
    font-feature-settings: 'tnum' 1, 'cv05' 1;
    letter-spacing: 0;
}

/* Motion is a signal, not a flourish, so it goes away entirely when asked. */
@media (prefers-reduced-motion: reduce) {
    *, *::before, *::after {
        animation-duration: .01ms !important;
        animation-iteration-count: 1 !important;
        transition-duration: .01ms !important;
        scroll-behavior: auto !important;
    }
}
```

The `!important` in the reduced-motion block is the one sanctioned use in the codebase — it is the standard accessibility escape hatch and lives in `lineops.css`, not in the bridge, so the "no `!important` in `mud-bridge.css`" constraint is intact.

- [ ] **Step 3: Stop loading Google Fonts**

In `src/LineOps.Web/Components/App.razor`, delete the three CDN lines (the two `preconnect` links and the `fonts.googleapis.com` stylesheet link at lines 16–19). Leave the four stylesheet links below them exactly as they are — their order is load-bearing.

- [ ] **Step 4: Build and eyeball the result**

```bash
dotnet build LineOps.slnx
```

Expected: build succeeds. The desk will look half-migrated at this point — surfaces and type are Apple, but buttons still carry the old gloss recipe referencing now-deleted tokens like `--ink-600` and `--chalk`, so they will render with invalid colours. That is expected and Tasks 3–7 close it. Do not "fix" it by re-adding old tokens.

- [ ] **Step 5: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css src/LineOps.Web/wwwroot/fonts src/LineOps.Web/Components/App.razor
git commit -m "feat(theme): Apple-dark token vocabulary and self-hosted Inter"
```

---

### Task 2: The C# palette mirror

**Files:**
- Modify: `src/LineOps.Web/Theming/DeskTheme.cs`
- Test: `tests/LineOps.Web.Tests/DeskThemeTests.cs` (create)

**Interfaces:**
- Consumes: the token values from Task 1 (the hex literals must match exactly).
- Produces: `DeskTheme.Surface0/1/2/3`, `DeskTheme.Accent`, `DeskTheme.StatePositive`, `DeskTheme.StateNegative`, `DeskTheme.StateWarning`, `DeskTheme.TextPrimary/Secondary/Tertiary`, `DeskTheme.OnAccent`, `DeskTheme.Build()` returning `MudTheme`, and the existing `DeskTheme.SpaceVar(DeskSpace)` unchanged.

- [ ] **Step 1: Read the current file end to end**

```bash
cat src/LineOps.Web/Theming/DeskTheme.cs
```

Note how `Build()` (or the existing theme-construction member) is shaped and what `DeskSpace` values `SpaceVar` maps. `SpaceVar` and `DeskSpace` are unchanged by this task — the spacing ramp survived Task 1 intact. Keep them exactly as they are.

- [ ] **Step 2: Write the failing test**

Create `tests/LineOps.Web.Tests/DeskThemeTests.cs`:

```csharp
using System.Text.RegularExpressions;
using LineOps.Web.Theming;

namespace LineOps.Web.Tests;

/// <summary>
/// The pairing seam from ADR 0008, still load-bearing under ADR 0016.
///
/// MudBlazor derives values CSS cannot — every palette entry becomes
/// --mud-palette-x plus -rgb, -darken, -lighten and -hover, and its stylesheet
/// reaches for those on hover and focus. A colour handed to only one of the two
/// sides produces a hover shade that exists nowhere else in the product. These
/// tests are what stops the two drifting.
/// </summary>
public class DeskThemeTests
{
    private static readonly string Css =
        File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Web/wwwroot/css/lineops.css"));

    [Theory]
    [InlineData("--surface-0", "#1C1C1E")]
    [InlineData("--surface-1", "#232326")]
    [InlineData("--surface-2", "#2C2C2E")]
    [InlineData("--surface-3", "#38383A")]
    [InlineData("--accent", "#0A84FF")]
    [InlineData("--state-positive", "#30D158")]
    [InlineData("--state-negative", "#FF453A")]
    [InlineData("--state-warning", "#FF9F0A")]
    public void Css_declares_the_colour_token(string token, string expected)
    {
        Assert.Equal(expected, TokenValue(token));
    }

    /// <summary>Each C# constant must be the same string the stylesheet declares.</summary>
    [Theory]
    [InlineData("--surface-0", nameof(DeskTheme.Surface0))]
    [InlineData("--surface-1", nameof(DeskTheme.Surface1))]
    [InlineData("--surface-2", nameof(DeskTheme.Surface2))]
    [InlineData("--surface-3", nameof(DeskTheme.Surface3))]
    [InlineData("--accent", nameof(DeskTheme.Accent))]
    [InlineData("--state-positive", nameof(DeskTheme.StatePositive))]
    [InlineData("--state-negative", nameof(DeskTheme.StateNegative))]
    [InlineData("--state-warning", nameof(DeskTheme.StateWarning))]
    public void Csharp_mirrors_the_stylesheet(string token, string constantName)
    {
        var constant = (string)typeof(DeskTheme)
            .GetField(constantName)!
            .GetValue(null)!;

        Assert.Equal(TokenValue(token), constant, ignoreCase: true);
    }

    [Fact]
    public void The_theme_hands_mudblazor_the_desk_palette()
    {
        var theme = DeskTheme.Build();
        var dark = theme.PaletteDark;

        Assert.Equal(DeskTheme.Accent, dark.Primary.Value, ignoreCase: true);
        Assert.Equal(DeskTheme.Surface0, dark.Background.Value, ignoreCase: true);
        Assert.Equal(DeskTheme.Surface1, dark.Surface.Value, ignoreCase: true);
        Assert.Equal(DeskTheme.StatePositive, dark.Success.Value, ignoreCase: true);
        Assert.Equal(DeskTheme.StateNegative, dark.Error.Value, ignoreCase: true);
        Assert.Equal(DeskTheme.StateWarning, dark.Warning.Value, ignoreCase: true);
    }

    /// <summary>
    /// ADR 0008's hard-won consequence: `.mud-button-root:disabled` sets its colour with
    /// !important, so it cannot be out-specified from the bridge. The palette therefore has
    /// to agree with the desk rather than fight it.
    /// </summary>
    [Fact]
    public void Disabled_action_colour_agrees_with_the_desks_dim_text()
    {
        var dark = DeskTheme.Build().PaletteDark;

        Assert.Equal(DeskTheme.TextTertiary, dark.ActionDisabled.Value, ignoreCase: true);
    }

    private static string TokenValue(string token)
    {
        var match = Regex.Match(Css, Regex.Escape(token) + @"\s*:\s*([^;]+);");

        Assert.True(match.Success, $"{token} is not declared in lineops.css");

        return match.Groups[1].Value.Trim();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LineOps.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);

        return dir!.FullName;
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskThemeTests"
```

Expected: compile error — `DeskTheme.Surface0`, `Accent`, `Green`, `Red`, `Orange`, `TextTertiary` and possibly `Build` do not exist yet.

- [ ] **Step 4: Rewrite the palette half of `DeskTheme.cs`**

Replace the colour constants (the `Ink900`…`OnFlag` block, roughly lines 26–56) with the block below, and update the `MudTheme` construction to match. Keep the class summary's explanation of *why* the C# mirror exists — only the values change — and keep `SpaceVar`/`DeskSpace` untouched.

```csharp
    // Surfaces — a true-neutral elevation ramp. Mirrors --surface-0 … --surface-3.
    public const string Surface0 = "#1C1C1E"; // desk void
    public const string Surface1 = "#232326"; // panel and window body
    public const string Surface2 = "#2C2C2E"; // title bars, raised rows, resting controls
    public const string Surface3 = "#38383A"; // hover fills, pressed states

    // Text. White at opacity tiers, so it keeps its relationship to any material.
    public const string TextPrimary = "rgba(255, 255, 255, .92)";
    public const string TextSecondary = "rgba(255, 255, 255, .55)";
    public const string TextTertiary = "rgba(255, 255, 255, .30)";

    // One accent. Interactivity, focus, selection — nothing else.
    public const string Accent = "#0A84FF";
    public const string AccentHover = "#3D9BFF";
    public const string OnAccent = "#FFFFFF";

    // Semantic state. Apple's dark-mode system colours, applied where a state
    // genuinely needs a colour rather than as an always-on channel.
    public const string StatePositive = "#30D158";
    public const string StateNegative = "#FF453A";
    public const string StateWarning = "#FF9F0A";

    public const string Separator = "rgba(255, 255, 255, .10)";

    // Chart ink. Opaque on purpose — a translucent series colour composites against
    // whatever is drawn beneath it and stops being one colour. Apple's systemGray and
    // systemGray2, from the same ramp the surfaces come from.
    public const string ChartNeutral = "#8E8E93";
    public const string ChartNeutralDim = "#636366";
```

Then build the theme:

```csharp
    /// <summary>
    /// The palette handed to <c>MudThemeProvider</c>. Only colour lives here — radii,
    /// type, elevation and z-index are remapped in wwwroot/css/mud-bridge.css, because
    /// CSS can express them and one place beats two.
    /// </summary>
    public static MudTheme Build() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Accent,
            PrimaryContrastText = OnAccent,
            Secondary = Accent,
            Tertiary = Accent,

            Background = Surface0,
            BackgroundGray = Surface0,
            Surface = Surface1,
            DrawerBackground = Surface1,
            DrawerText = TextPrimary,
            AppbarBackground = Surface2,
            AppbarText = TextPrimary,

            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            TextDisabled = TextTertiary,

            Success = StatePositive,
            Error = StateNegative,
            Warning = StateWarning,
            Info = Accent,

            Divider = Separator,
            DividerLight = Separator,
            TableLines = Separator,
            LinesDefault = Separator,
            LinesInputs = Separator,

            // Cannot be out-specified: .mud-button-root:disabled sets colour with
            // !important. Agreeing with the desk beats fighting the stylesheet.
            ActionDisabled = TextTertiary,
            ActionDisabledBackground = Surface2,
            ActionDefault = TextSecondary
        }
    };
```

If the existing file exposes the theme as a property (e.g. `public static MudTheme Theme { get; }`) rather than a `Build()` method, keep whichever shape the call site in `MainLayout.razor` already uses and adjust the test's `DeskTheme.Build()` calls to match. Check with:

```bash
grep -rn "DeskTheme" src/LineOps.Web/Components/Layout/MainLayout.razor
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskThemeTests"
```

Expected: PASS, all cases.

- [ ] **Step 6: Commit**

```bash
git add src/LineOps.Web/Theming/DeskTheme.cs tests/LineOps.Web.Tests/DeskThemeTests.cs
git commit -m "feat(theme): mirror the Apple palette into MudBlazor's C# theme"
```

---

### Task 3: The Emphasis × Role button API

**Files:**
- Create: `src/LineOps.Web/Components/Desk/DeskEmphasis.cs`
- Delete: `src/LineOps.Web/Components/Desk/DeskTone.cs`
- Modify: `src/LineOps.Web/Components/Desk/DeskButton.razor`
- Test: `tests/LineOps.Web.Tests/DeskButtonTests.cs` (rewrite)

**Interfaces:**
- Consumes: nothing from earlier tasks (pure API change).
- Produces: `enum DeskEmphasis { Plain, Tinted, Filled }`, `enum DeskRole { Normal, Destructive }`, `enum DeskKeySize { Small, Medium, Large }` (moved verbatim from `DeskTone.cs`), and `DeskButton` parameters `Emphasis`, `Role`, `Size`, `Busy`, `Disabled`, `Icon`, `Title`, `ButtonType`, `OnClick`, `Class`, `Extra`. The rendered class list is `desk-btn desk-btn--{plain|tinted|filled}` plus optional `desk-btn--destructive`, `desk-btn--sm`/`--lg`, `desk-btn--icon`, `desk-btn--busy`, then `Class` last.
- **Removed:** `DeskTone` and the `Quiet` parameter. `Quiet` is gone because `DeskEmphasis.Plain` *is* the quiet key — keeping both would give two ways to say one thing.

- [ ] **Step 1: Write the new enums**

Create `src/LineOps.Web/Components/Desk/DeskEmphasis.cs`:

```csharp
namespace LineOps.Web.Components.Desk;

/// <summary>
/// How much visual weight a button claims, following Apple's button hierarchy.
///
/// <para>
/// This replaces ADR 0008's <c>DeskTone</c>, which named a consequence and painted it as
/// a hue. Under ADR 0016 hue is no longer a channel: an interface with five coloured
/// button types has no primary action, only five competing ones. Emphasis says how loud;
/// <see cref="DeskRole"/> says whether the action is dangerous. Those are the two
/// questions a caller can actually answer.
/// </para>
///
/// <para>
/// The rule at a call site: <b>exactly one Filled per context.</b> If a panel seems to
/// need two, one of them is not the primary action.
/// </para>
/// </summary>
public enum DeskEmphasis
{
    /// <summary>
    /// Transparent until pointed at. Toolbar and inline actions, and the escape from a
    /// thing (Cancel, Close, Back) — anything that must be available without competing
    /// with the action beside it that commits.
    /// </summary>
    Plain,

    /// <summary>
    /// An accent wash behind accent text. Secondary actions that still need to be found
    /// at a glance in a dense panel.
    /// </summary>
    Tinted,

    /// <summary>
    /// Solid accent. The one action a context exists to commit — save, apply, run.
    /// </summary>
    Filled
}

/// <summary>
/// Whether an action destroys something. Kept separate from <see cref="DeskEmphasis"/>
/// because a destructive action can be any weight: a Filled "Delete" in a confirmation
/// alert, a Plain "Remove" in a row's overflow menu.
/// </summary>
public enum DeskRole
{
    Normal,

    /// <summary>
    /// Destroys, or is hard to undo. Renders in red, and never becomes the default
    /// button in an alert — see DeskAlert.
    /// </summary>
    Destructive
}

/// <summary>How much of the panel a button claims. Chrome is small, a page's intent is large.</summary>
public enum DeskKeySize
{
    Small,
    Medium,
    Large
}
```

- [ ] **Step 2: Delete the old enum file**

```bash
git rm src/LineOps.Web/Components/Desk/DeskTone.cs
```

- [ ] **Step 3: Write the failing test**

Replace the entire contents of `tests/LineOps.Web.Tests/DeskButtonTests.cs`:

```csharp
using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LineOps.Web.Tests;

/// <summary>
/// What a button promises, checked at the seam. Per ADR 0016 the caller states two
/// things — how loud (Emphasis) and whether it destroys (Role) — and the class list is
/// the contract that carries both into CSS.
/// </summary>
public class DeskButtonTests : DeskTestContext
{
    [Fact]
    public void Renders_its_label()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Ingest"));

        Assert.Contains("Ingest", cut.Find("button").TextContent);
    }

    [Theory]
    [InlineData(DeskEmphasis.Plain, "desk-btn--plain")]
    [InlineData(DeskEmphasis.Tinted, "desk-btn--tinted")]
    [InlineData(DeskEmphasis.Filled, "desk-btn--filled")]
    public void Emphasis_maps_to_its_class(DeskEmphasis emphasis, string expected)
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, emphasis)
            .AddChildContent("Go"));

        var classes = ClassList(cut);

        Assert.Contains("desk-btn", classes);
        Assert.Contains(expected, classes);
    }

    /// <summary>
    /// Plain is the default because most buttons on a dense console are chrome. A
    /// default of Filled would make every panel shout.
    /// </summary>
    [Fact]
    public void Default_emphasis_is_plain()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Go"));

        Assert.Contains("desk-btn--plain", ClassList(cut));
    }

    [Fact]
    public void Destructive_is_marked_independently_of_emphasis()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Role, DeskRole.Destructive)
            .AddChildContent("Delete"));

        var classes = ClassList(cut);

        Assert.Contains("desk-btn--filled", classes);
        Assert.Contains("desk-btn--destructive", classes);
    }

    [Fact]
    public void A_normal_button_says_nothing_about_role()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Save"));

        Assert.DoesNotContain("desk-btn--destructive", ClassList(cut));
    }

    [Theory]
    [InlineData(DeskKeySize.Small, "desk-btn--sm")]
    [InlineData(DeskKeySize.Large, "desk-btn--lg")]
    public void Size_maps_to_its_class(DeskKeySize size, string expected)
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Size, size)
            .AddChildContent("Go"));

        Assert.Contains(expected, ClassList(cut));
    }

    [Fact]
    public void Medium_size_adds_no_class()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Size, DeskKeySize.Medium)
            .AddChildContent("Go"));

        var classes = ClassList(cut);

        Assert.DoesNotContain("desk-btn--sm", classes);
        Assert.DoesNotContain("desk-btn--lg", classes);
    }

    /// <summary>
    /// Busy is not disabled. A button that has started work keeps its emphasis and
    /// refuses further presses — it must not fall back to the grey of a dead control.
    /// </summary>
    [Fact]
    public void Busy_keeps_its_emphasis_and_is_announced_as_busy()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Busy, true)
            .AddChildContent("Ingesting"));

        var button = cut.Find("button");

        Assert.Contains("desk-btn--busy", button.GetAttribute("class"));
        Assert.Contains("desk-btn--filled", button.GetAttribute("class"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
    }

    [Fact]
    public void A_button_that_is_not_busy_says_nothing_about_it()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Ingest"));

        var button = cut.Find("button");

        Assert.Null(button.GetAttribute("aria-busy"));
        Assert.DoesNotContain("desk-btn--busy", button.GetAttribute("class"));
    }

    [Fact]
    public void Busy_refuses_further_presses()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Busy, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingesting"));

        cut.Find("button").Click();

        Assert.Equal(0, presses);
    }

    [Fact]
    public void An_idle_button_reports_its_press()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingest"));

        cut.Find("button").Click();

        Assert.Equal(1, presses);
    }

    [Fact]
    public void Disabled_stops_the_press()
    {
        var presses = 0;

        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Disabled, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => presses++))
            .AddChildContent("Ingest"));

        cut.Find("button").Click();

        Assert.Equal(0, presses);
    }

    [Fact]
    public void An_icon_without_a_label_becomes_a_square()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .Add(x => x.Title, "Refresh"));

        var button = cut.Find("button");

        Assert.Contains("desk-btn--icon", button.GetAttribute("class"));
        Assert.Equal("Refresh", button.GetAttribute("title"));
    }

    [Fact]
    public void An_icon_beside_a_label_is_not_a_square()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Icon, MudBlazor.Icons.Material.Filled.Refresh)
            .AddChildContent("Refresh"));

        Assert.DoesNotContain("desk-btn--icon", ClassList(cut));
    }

    [Fact]
    public void A_one_off_class_is_appended_last_so_it_can_win()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.Emphasis, DeskEmphasis.Filled)
            .Add(x => x.Class, "panel__commit")
            .AddChildContent("Save"));

        var classes = ClassList(cut);

        Assert.EndsWith("panel__commit", classes.Trim());
        Assert.Contains("desk-btn--filled", classes);
    }

    [Fact]
    public void Unmatched_attributes_reach_the_button()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .AddUnmatched("data-testid", "commit")
            .AddChildContent("Save"));

        Assert.Equal("commit", cut.Find("button").GetAttribute("data-testid"));
    }

    [Fact]
    public void Button_type_defaults_to_button_so_a_press_never_submits_by_accident()
    {
        var cut = RenderComponent<DeskButton>(p => p.AddChildContent("Save"));

        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
    }

    [Fact]
    public void Button_type_can_be_asked_to_submit()
    {
        var cut = RenderComponent<DeskButton>(p => p
            .Add(x => x.ButtonType, MudBlazor.ButtonType.Submit)
            .AddChildContent("Save"));

        Assert.Equal("submit", cut.Find("button").GetAttribute("type"));
    }

    private static string ClassList(IRenderedComponent<DeskButton> cut)
        => cut.Find("button").GetAttribute("class") ?? string.Empty;
}
```

- [ ] **Step 4: Run the test to verify it fails**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskButtonTests"
```

Expected: compile errors — `DeskEmphasis`, `DeskRole`, and the `Emphasis`/`Role` parameters do not exist.

- [ ] **Step 5: Rewrite `DeskButton.razor`**

Replace the whole file:

```razor
@namespace LineOps.Web.Components.Desk
@using LineOps.Web.Components.Windowing

@*
    TEMPLATE-ABLE — see docs ADR 0016 and .claude/skills/apple-mudblazor.

    The desk's button, and the reason it wraps MudBlazor rather than sitting beside it.
    Panels hold a lot of these, and a call site that has to remember Variant, Color,
    Ripple, DropShadow and a class string is one that will drift from the call site next
    to it. Here the caller states how loud (Emphasis) and whether it destroys (Role), and
    the styling is not their problem.

    The MudButton underneath is deliberately its plainest variant — Text + Inherit, which
    contributes almost no visual rules of its own — plus Ripple off, because a ripple is
    Material's answer to a press and this system answers with a fast state change instead.
    Everything the button looks like comes from .desk-btn in lineops.css. See
    css/mud-bridge.css for why that stack resolves the way it does.
*@

<MudButton Class="@Classes"
           Variant="Variant.Text"
           Color="Color.Inherit"
           Ripple="false"
           DropShadow="false"
           ButtonType="@ButtonType"
           Disabled="@(Disabled || Busy)"
           OnClick="OnClick"
           aria-busy="@(Busy ? "true" : null)"
           title="@Title"
           @attributes="Extra">
    @if (Icon is not null)
    {
        <Glyph Icon="@Icon" />
    }
    @ChildContent
</MudButton>

@code {
    /// <summary>The label. Omit it for an icon-only button — then <see cref="Title"/> is required.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>A MudBlazor icon string, drawn bare through <c>Glyph</c> so buttons use the same icon set as the chrome.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>
    /// How much weight this button claims. See <see cref="DeskEmphasis"/> — exactly one
    /// Filled per context.
    /// </summary>
    [Parameter] public DeskEmphasis Emphasis { get; set; } = DeskEmphasis.Plain;

    /// <summary>Whether pressing this destroys something. See <see cref="DeskRole"/>.</summary>
    [Parameter] public DeskRole Role { get; set; } = DeskRole.Normal;

    [Parameter] public DeskKeySize Size { get; set; } = DeskKeySize.Medium;

    /// <summary>
    /// Work this button started is still running. Runs the in-flight shimmer and refuses
    /// further presses, without going grey — an occupied control is not an inert one.
    /// </summary>
    [Parameter] public bool Busy { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Tooltip, and the accessible name when there is no label to read.</summary>
    [Parameter] public string? Title { get; set; }

    [Parameter] public ButtonType ButtonType { get; set; } = ButtonType.Button;

    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>Extra classes, appended last so a one-off can still win.</summary>
    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? Extra { get; set; }

    private string Classes => string.Join(' ', Parts().Where(p => p is { Length: > 0 }));

    private IEnumerable<string?> Parts()
    {
        yield return "desk-btn";

        yield return Emphasis switch
        {
            DeskEmphasis.Filled => "desk-btn--filled",
            DeskEmphasis.Tinted => "desk-btn--tinted",
            _ => "desk-btn--plain"
        };

        yield return Role is DeskRole.Destructive ? "desk-btn--destructive" : null;

        yield return Size switch
        {
            DeskKeySize.Small => "desk-btn--sm",
            DeskKeySize.Large => "desk-btn--lg",
            _ => null
        };

        // An icon with no label is a square, so it does not read as a clipped button.
        yield return Icon is not null && ChildContent is null ? "desk-btn--icon" : null;
        yield return Busy ? "desk-btn--busy" : null;
        yield return Class;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

The app itself will not compile yet — 25 call-site files still pass `Tone`. Run only this test class, which compiles the component in isolation… it will not, because the test project references the whole web project. So expect this step to still fail on call sites, and confirm the failures are *only* about `Tone`/`Quiet` at call sites:

```bash
dotnet build src/LineOps.Web/LineOps.Web.csproj 2>&1 | grep -E "error" | head -40
```

Expected: a list of `CS0117`/`CS1061` errors naming `DeskTone` or `Quiet`, all in the 25 files Task 5 handles. If any error names something else, stop and resolve it before continuing.

- [ ] **Step 7: Commit**

```bash
git add src/LineOps.Web/Components/Desk/DeskEmphasis.cs src/LineOps.Web/Components/Desk/DeskTone.cs src/LineOps.Web/Components/Desk/DeskButton.razor tests/LineOps.Web.Tests/DeskButtonTests.cs
git commit -m "feat(desk): replace Tone with Apple's Emphasis x Role button model"
```

The tree does not build at this commit. That is deliberate and is closed by Task 5 — the alternative is one enormous commit mixing an API change with 25 files of judgment calls.

---

### Task 4: The button recipe in CSS

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — replace the `.desk-key`/`.btn` block (from the "gloss" section comment at ~line 1676 through `.desk-key--on` at ~line 2299; verify exact bounds before editing).

**Interfaces:**
- Consumes: tokens from Task 1; the class contract from Task 3 (`desk-btn`, `--plain|--tinted|--filled`, `--destructive`, `--sm|--lg`, `--icon`, `--busy`).
- Produces: `.desk-btn` and `.btn` sharing one recipe, so hand-written buttons migrate without being touched.

- [ ] **Step 1: Find the exact block bounds**

```bash
grep -n "desk-key\|^\.btn\|\.btn[,: ]" src/LineOps.Web/wwwroot/css/lineops.css
```

Record the first line of the section comment introducing the gloss recipe and the last line of the final `.desk-key--*` rule. Also check whether `.btn` shares the recipe:

```bash
grep -n "^\.btn" -A 3 src/LineOps.Web/wwwroot/css/lineops.css | head -30
```

- [ ] **Step 2: Replace the block**

Delete everything between those bounds and insert:

```css
/* ------------------------------------------------------------------ buttons ---
   Apple's three-level hierarchy, and nothing else. ADR 0008 made gloss mean
   "pressable" and hue mean "consequence"; ADR 0016 retires both. Under the old
   rule every button was a moulded, tinted cap, which meant a panel with five
   buttons had five primary actions and therefore none.

   Now weight carries the message. Filled is the one action a context commits to.
   Tinted is a secondary action that still has to be findable. Plain is chrome —
   transparent until pointed at. Red is reserved for Role.Destructive and is the
   only place a button borrows a state colour.

   .desk-btn and .btn share the recipe, so the hand-written buttons scattered
   through the panels migrate without being touched and there is never a period
   where two button languages coexist.
   --------------------------------------------------------------------------- */

.desk-btn, .btn {
    /* The two dials every variant below spends. */
    --btn-fill: transparent;
    --btn-ink: var(--text-secondary);

    position: relative;
    overflow: hidden; /* clips the in-flight shimmer */
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: var(--space-1);
    height: 28px;
    min-width: 0;
    padding: 0 var(--space-3);
    box-sizing: border-box;

    border: none; /* depth comes from fill and shadow, never an outline */
    border-radius: var(--radius);

    background-color: var(--btn-fill);
    color: var(--btn-ink);

    font-family: var(--face-ui);
    font-size: var(--text-body);
    font-weight: var(--weight-medium);
    letter-spacing: 0; /* the uppercase tracking went with the gloss */
    text-transform: none;
    text-decoration: none;
    white-space: nowrap;
    cursor: pointer;

    transition: background-color var(--dur-fast) var(--ease-standard),
                color var(--dur-fast) var(--ease-standard),
                transform var(--dur-fast) var(--ease-standard);
}

.desk-btn svg, .btn svg { width: 15px; height: 15px; flex: 0 0 auto; }

/* One focus ring for the whole desk, sitting outside the control on an offset. */
.desk-btn:focus-visible, .btn:focus-visible {
    outline: none;
    box-shadow: var(--focus-ring);
}

/* Apple answers a press with a fast, small scale rather than travel. */
.desk-btn:active:not(:disabled), .btn:active:not(:disabled) {
    transform: scale(.97);
    transition-duration: .05s;
}

.desk-btn:disabled, .desk-btn[disabled],
.btn:disabled, .btn[disabled] {
    opacity: .40;
    cursor: not-allowed;
    transform: none;
}

/* --- Plain: chrome. Transparent until pointed at. ------------------------- */
.desk-btn--plain { --btn-fill: transparent; --btn-ink: var(--text-secondary); }

.desk-btn--plain:hover:not(:disabled) {
    --btn-fill: var(--surface-3);
    --btn-ink: var(--text-primary);
}

/* --- Tinted: an accent wash behind accent text. --------------------------- */
.desk-btn--tinted { --btn-fill: var(--accent-wash); --btn-ink: var(--accent); }

.desk-btn--tinted:hover:not(:disabled) { --btn-fill: var(--accent-wash-strong); }

/* --- Filled: the one action the context commits to. ----------------------- */
.desk-btn--filled {
    --btn-fill: var(--accent);
    --btn-ink: var(--on-accent);
    font-weight: var(--weight-semibold);
    box-shadow: var(--shadow-1);
}

.desk-btn--filled:hover:not(:disabled) { --btn-fill: var(--accent-hover); }
.desk-btn--filled:active:not(:disabled) { --btn-fill: var(--accent-press); }

/* --- Destructive: the only place a button borrows a state colour. ---------- */
.desk-btn--destructive.desk-btn--plain { --btn-ink: var(--state-negative); }

.desk-btn--destructive.desk-btn--plain:hover:not(:disabled) {
    --btn-fill: var(--state-negative-wash);
    --btn-ink: var(--state-negative);
}

.desk-btn--destructive.desk-btn--tinted { --btn-fill: var(--state-negative-wash); --btn-ink: var(--state-negative); }

.desk-btn--destructive.desk-btn--tinted:hover:not(:disabled) {
    --btn-fill: color-mix(in oklab, var(--state-negative) 24%, transparent);
}

.desk-btn--destructive.desk-btn--filled { --btn-fill: var(--state-negative); --btn-ink: #FFFFFF; }

.desk-btn--destructive.desk-btn--filled:hover:not(:disabled) {
    --btn-fill: color-mix(in oklab, var(--state-negative) 85%, #fff);
}

/* --- Sizes. The control changes height and padding; the recipe does not. --- */
.desk-btn--sm { height: 22px; padding: 0 var(--space-2); font-size: var(--text-footnote); }
.desk-btn--sm svg { width: 13px; height: 13px; }
.desk-btn--lg { height: 34px; padding: 0 var(--space-4); font-size: var(--text-title3); }
.desk-btn--lg svg { width: 17px; height: 17px; }

/* --- Icon-only: square, so it reads as a control rather than a clipped button. */
.desk-btn--icon { padding: 0; width: 28px; }
.desk-btn--icon.desk-btn--sm { width: 22px; }
.desk-btn--icon.desk-btn--lg { width: 34px; }

/* --- Busy: occupied, not inert. Keeps its fill and runs a sheen across it. -- */
.desk-btn--busy,
.desk-btn--busy:disabled {
    opacity: 1;
    cursor: progress;
}

.desk-btn--busy::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(90deg,
        transparent 0%,
        rgba(255, 255, 255, .18) 50%,
        transparent 100%);
    animation: desk-btn-sheen 1.1s var(--ease-standard) infinite;
}

@keyframes desk-btn-sheen {
    from { transform: translateX(-100%); }
    to { transform: translateX(100%); }
}

@media (prefers-reduced-motion: reduce) {
    .desk-btn--busy::after { animation: none; opacity: .35; }
    .desk-btn:active:not(:disabled), .btn:active:not(:disabled) { transform: none; }
}
```

- [ ] **Step 3: Find and fix orphaned references to deleted classes**

```bash
grep -rn "desk-key" src/LineOps.Web/ | grep -v "\.min\."
```

Every hit is either CSS that Step 2 should have removed, or a call site to update. Notable one from the old file: `[data-glide-live] .desk-key--quiet:hover` — rewrite that selector to `[data-glide-live] .desk-btn--plain:hover` keeping its declaration. Another: `.desk-key--on` — check what uses it:

```bash
grep -rn "desk-key--on" src/LineOps.Web/
```

If it marks a toggled state, rename it to `.desk-btn--on` and restyle it as `--btn-fill: var(--accent-wash); --btn-ink: var(--accent);` so a toggled-on control reads as selected rather than as a separate colour.

- [ ] **Step 4: Verify no stale token references remain in the block**

```bash
grep -n "ink-900\|ink-800\|ink-700\|ink-600\|ink-500\|ink-400\|--chalk\|--haze\|--steam\|--drift\|--flag\|--iris\|key-tint\|key-ink\|key-lift\|key-sheen\|key-crown" src/LineOps.Web/wwwroot/css/lineops.css | head -40
```

Hits outside the button block are expected at this stage (Task 7 and Phase 2 handle them). Hits *inside* the block you just wrote are a mistake — fix them.

- [ ] **Step 5: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css
git commit -m "feat(desk): Filled/Tinted/Plain button recipe replaces the gloss cap"
```

---

### Task 5: The call-site sweep

**Files (25, with current `Tone=` counts):**

| File | Sites |
|---|---|
| `Components/Panels/PartsPanel.razor` | 28 |
| `Components/Panels/HistoryPanel.razor` | 13 |
| `Components/Panels/OpsPanel.razor` | 12 |
| `Components/Panels/IncidentsPanel.razor` | 11 |
| `Components/Panels/BoardWager.razor` | 5 |
| `Components/Panels/WindowManagerPanel.razor` | 3 |
| `Components/Panels/PerformancePanel.razor` | 3 |
| `Components/Panels/JournalPanel.razor` | 3 |
| `Components/Panels/DashboardPanel.razor` | 3 |
| `Components/Snippets/PlayerRecentSnippet.razor` | 2 |
| `Components/Snippets/OddsSnippet.razor` | 2 |
| `Components/Snippets/GameResultSnippet.razor` | 2 |
| `Components/Panels/TeamPanel.razor` | 2 |
| `Components/Panels/GamePanel.razor` | 2 |
| `Components/Desk/PullMenu.razor` | 2 |
| `Components/Windowing/Desk.razor` | 1 |
| `Components/Snippets/HeadToHeadSnippet.razor` | 1 |
| `Components/Snippets/FormSnippet.razor` | 1 |
| `Components/Panels/RunsPanel.razor` | 1 |
| `Components/Panels/HeadToHeadPanel.razor` | 1 |
| `Components/Panels/BoardPanel.razor` | 1 |
| `Components/Panels/BoardAllBets.razor` | 1 |
| `Components/FormRun.razor` | 1 |
| `Components/Desk/RunbookSteps.razor` | 1 |
| `Components/Desk/RowActions.razor` | 1 |

- Also modify: `src/LineOps.Web/Components/Desk/RowAction.cs` — the `Tone` property.
- Also create: `src/LineOps.Web/Components/Desk/DeskState.cs` — the `DeskState` enum (see Step 0).
- Also modify: `Components/Desk/Tag.razor`, `Components/Desk/Note.razor`, `Components/Desk/Metric.razor`, `Components/Desk/DeskToasts.cs` — migrate from `DeskTone` to `DeskState`.

**Interfaces:**
- Consumes: `DeskEmphasis`, `DeskRole` from Task 3.
- Produces: `enum DeskState { Neutral, Positive, Negative, Warning, Info }`, and a codebase with zero `DeskTone` references and a green build.

**This is a judgment task, not a find-and-replace.** ADR 0016's whole point is that the old tones over-coloured the desk. A mechanical map would carry that problem across intact.

**Two traps this task must not fall into, both found during Task 3's review:**

1. **`Quiet="true"` does not produce a compile error.** `DeskButton` declares `[Parameter(CaptureUnmatchedValues = true)] Extra`, so a removed parameter silently lands there and renders as a stray HTML attribute. A green build is therefore *not* evidence that `Quiet` is gone. Remove it by grep, and verify by grep.
2. **`DeskTone` was doing two different jobs**, and only one of them is about buttons. See Step 0.

- [ ] **Step 0: Split the enum, because `DeskTone` was conflating two things**

Task 3 retired `DeskTone` on the assumption it only described buttons. It did not. Four components use it to name a **state**, not an action's weight:

- `Tag.razor` — `DeskTone.Go` → `tag--good`, `Stop` → `tag--bad`, `Caution` → `tag--warn`, `Action` → `tag--info`
- `Metric.razor` — the same four, as `metric--good` / `--bad` / `--warn` / `--info`
- `Note.razor` — checks `Tone == DeskTone.Caution` to wear the flag rule
- `DeskToasts.cs` — maps tones onto MudBlazor `Severity` and `desk-toast--*` classes

Mapping these onto `DeskEmphasis`/`DeskRole` would be wrong. A tag has no emphasis; it reports what something *is*. And this is precisely where the design says state colour belongs: hue moves off the controls and onto "numbers, tags, the pulse strip".

So create `src/LineOps.Web/Components/Desk/DeskState.cs`:

```csharp
namespace LineOps.Web.Components.Desk;

/// <summary>
/// TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.
///
/// What something *is*, for the things that report rather than act.
///
/// <para>
/// This is the half of the retired <c>DeskTone</c> that was never about buttons. Under
/// ADR 0016 hue stops being an affordance channel and moves to where states actually
/// live — a figure, a tag, a toast, the pulse strip. Those still need to say "this is
/// healthy" or "this breached", and they say it here.
/// </para>
///
/// <para>
/// Buttons do not take a <c>DeskState</c>. They take <see cref="DeskEmphasis"/> and
/// <see cref="DeskRole"/>, because the question a button answers is how much weight it
/// claims and whether it destroys something — not what colour it is.
/// </para>
/// </summary>
public enum DeskState
{
    /// <summary>No state worth colouring. The default.</summary>
    Neutral,

    /// <summary>Healthy, settled, moved your way, won.</summary>
    Positive,

    /// <summary>Breached, failed, moved against you, lost.</summary>
    Negative,

    /// <summary>Proceeding, but the operator should know the cost. Budget pressure, pending.</summary>
    Warning,

    /// <summary>Called out for attention without implying good or bad.</summary>
    Info
}
```

Then migrate the four consumers, renaming their `Tone` parameter to `State` and their type to `DeskState`:

| Old | New |
|---|---|
| `DeskTone.Go` | `DeskState.Positive` |
| `DeskTone.Stop` | `DeskState.Negative` |
| `DeskTone.Caution` | `DeskState.Warning` |
| `DeskTone.Action` | `DeskState.Info` |
| `DeskTone.Neutral` | `DeskState.Neutral` |

Their existing CSS class names (`tag--good`, `metric--bad`, `desk-toast--go`, …) do not change in this task — Task 10 restyles those blocks and can rename them then if it wants. Keep the mapping switch expressions pointing at the same class strings, so this step is an enum swap and nothing else.

`DeskToasts.cs`'s `Severity` mapping is unchanged in meaning: `Positive` → `Severity.Success`, `Info` → `Severity.Info`, `Warning` → `Severity.Warning`, `Negative` → `Severity.Error`.

Update the four components' doc comments: they currently say "name the consequence, not the colour", which was the button rule. For these, the rule is "name the state, not the colour."

Then update every call site that passes a tone to one of these four components — `Tag`, `Metric`, and `Note` are used across the panels, so expect `Tone="DeskTone.Go"` on a `<Tag>` to become `State="DeskState.Positive"`. These are mechanical: a tag saying "won" is still saying "won".

- [ ] **Step 1: Change the `RowAction` record, since panels depend on it**

In `src/LineOps.Web/Components/Desk/RowAction.cs`, replace the `Tone` property:

```csharp
    /// <summary>How much weight this action claims in the row's action bar.</summary>
    public DeskEmphasis Emphasis { get; init; } = DeskEmphasis.Plain;

    /// <summary>Whether this action destroys something. Renders in red.</summary>
    public DeskRole Role { get; init; } = DeskRole.Normal;
```

- [ ] **Step 2: Apply the translation rules, file by file**

Work one file at a time, smallest first, so the error count falls visibly. For each `Tone=` site, decide with these rules rather than a lookup table:

1. **`Tone="DeskTone.Stop"` → `Role="DeskRole.Destructive"`.** Keep the emphasis it deserves on its own: a Delete in a row menu is `Plain` + `Destructive`; a Delete that is the point of a confirmation is `Filled` + `Destructive`. **Do not** carry `Stop` over as an emphasis — Stop was a colour, Destructive is a role.
2. **`Tone="DeskTone.Action"` → `Emphasis="DeskEmphasis.Filled"`, but at most one per panel.** Scan the whole file first. If it has three `Action` sites, exactly one is the action the panel exists to commit; the other two become `Tinted`.
3. **`Tone="DeskTone.Go"` → usually `Emphasis="DeskEmphasis.Filled"`** when it starts the panel's main work (Ingest, Sync, Run), otherwise `Tinted`. Same one-Filled-per-panel ceiling shared with rule 2 — Go and Action were both "primary" under the old system, which is exactly the over-colouring being removed.
4. **`Tone="DeskTone.Caution"` → `Emphasis="DeskEmphasis.Tinted"`.** The warning belongs in the confirmation the button opens (Phase 3's `DeskAlert`), not in the button's colour. Where the caution is genuinely about cost, note it in the label ("Backfill 30 days") rather than the hue.
5. **`Tone="DeskTone.Neutral"` → delete the attribute.** `Plain` is the default.
6. **`Quiet="true"` → delete the attribute.** Plain already is the quiet button. If the site also set a tone, resolve to `Plain`. Remember trap 1: the compiler will not find these for you. `grep -rn 'Quiet' src/LineOps.Web --include=*.razor` is how you find them, and it currently reports 12 files.

For `PartsPanel.razor` (28 sites) do not apply these rules — it is the parts catalog and Task 17 rewrites it wholesale. For now, get it compiling with the minimum edit: `Tone="DeskTone.X"` → `Emphasis="DeskEmphasis.Plain"` and drop `Quiet`. Leave a `@* TODO(Task 17): showcase rewrite *@` comment at the top of its buttons section.

- [ ] **Step 3: Confirm the old enum is gone from the codebase**

```bash
grep -rn "DeskTone\|Quiet" src/ --include=*.razor --include=*.cs
```

Expected: no output. Note this greps bare `Quiet`, not `Quiet=`, because prose in a comment mentioning the retired parameter is also stale — `PartsPanel.razor` has one such line describing what Quiet meant under the gloss rule.

The file table above counts `Tone=` attributes only. The true `DeskTone` footprint is wider — 29 files, including `RowAction.cs`, `DeskToasts.cs`, `Tag.razor`, `Note.razor` and `Metric.razor`, which the table does not list because they name the type without a `Tone=` attribute. Trust this grep, not the table.

- [ ] **Step 4: Build**

```bash
dotnet build LineOps.slnx
```

Expected: build succeeds with no errors.

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj
```

Expected: all pass. `RowActionsTests.cs` likely asserts on the old `Tone` property — update its assertions to `Emphasis`/`Role` to match the record change, keeping each test's intent.

- [ ] **Step 6: Audit the Filled count**

```bash
grep -rc "DeskEmphasis.Filled" src/LineOps.Web/Components/Panels/*.razor | grep -v ':0'
```

Any panel with more than one Filled needs a second look — that is rule 2 being violated. Fix before committing.

- [ ] **Step 7: Commit**

```bash
git add src/LineOps.Web/Components src/LineOps.Web/Components/Desk/RowAction.cs tests/LineOps.Web.Tests
git commit -m "refactor(desk): re-judge every button call site against the new hierarchy"
```

---

### Task 6: Rewrite the MudBlazor bridge

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/mud-bridge.css` (481 lines — the token-remapping section is rewritten; the "Material tells" section is audited)

**Interfaces:**
- Consumes: every token from Task 1.
- Produces: MudBlazor components that land looking like the desk without per-component rules.

- [ ] **Step 1: Rewrite the `:root:root` remapping block**

Keep the file's header comment (it explains the seam and the load order, both still true) but update its second paragraph to reference ADR 0016 alongside 0008. Then replace the `:root:root { … }` block's contents with:

```css
:root:root {
    /* Shape. Apple's radii are larger than Material's and grow with the surface. */
    --mud-default-borderradius: var(--radius);

    /* Type. One family across every role — an unanswered role silently falls back
       to Roboto, which is why each is named rather than inherited. */
    --mud-typography-default-family: var(--face-ui);
    --mud-typography-body1-family: var(--face-ui);
    --mud-typography-body2-family: var(--face-ui);
    --mud-typography-button-family: var(--face-ui);
    --mud-typography-caption-family: var(--face-ui);
    --mud-typography-overline-family: var(--face-ui);
    --mud-typography-subtitle1-family: var(--face-ui);
    --mud-typography-subtitle2-family: var(--face-ui);
    --mud-typography-h1-family: var(--face-ui);
    --mud-typography-h2-family: var(--face-ui);
    --mud-typography-h3-family: var(--face-ui);
    --mud-typography-h4-family: var(--face-ui);
    --mud-typography-h5-family: var(--face-ui);
    --mud-typography-h6-family: var(--face-ui);

    /* Buttons are set like body text now. The uppercase tracking left with the
       gloss — Apple sets button labels in the same face and case as everything
       else, and leans on weight instead. */
    --mud-typography-button-size: var(--text-body);
    --mud-typography-button-weight: var(--weight-medium);
    --mud-typography-button-letterspacing: 0;
    --mud-typography-button-text-transform: none;

    /* Elevation. Material stacks three blurs per step; this system has three
       shadows total, because a surface is either resting, floating, or modal.
       0-3 is panel furniture, 4-7 floats, 8+ is a dialog, 16+ a sheet. */
    --mud-elevation-0: none;
    --mud-elevation-1: var(--shadow-1);
    --mud-elevation-2: var(--shadow-1);
    --mud-elevation-3: var(--shadow-1);
    --mud-elevation-4: var(--shadow-2);
    --mud-elevation-5: var(--shadow-2);
    --mud-elevation-6: var(--shadow-2);
    --mud-elevation-7: var(--shadow-2);
    --mud-elevation-8: var(--shadow-3);
    --mud-elevation-9: var(--shadow-3);
    --mud-elevation-10: var(--shadow-3);
    --mud-elevation-11: var(--shadow-3);
    --mud-elevation-12: var(--shadow-3);
    --mud-elevation-13: var(--shadow-3);
    --mud-elevation-14: var(--shadow-3);
    --mud-elevation-15: var(--shadow-3);
    --mud-elevation-16: var(--shadow-sheet);
    --mud-elevation-17: var(--shadow-sheet);
    --mud-elevation-18: var(--shadow-sheet);
    --mud-elevation-19: var(--shadow-sheet);
    --mud-elevation-20: var(--shadow-sheet);
    --mud-elevation-21: var(--shadow-sheet);
    --mud-elevation-22: var(--shadow-sheet);
    --mud-elevation-23: var(--shadow-sheet);
    --mud-elevation-24: var(--shadow-sheet);

    /* Layers. Mud's defaults sit in the 1000s and would land under the desk's
       own chrome; re-point them at the desk's scale. */
    --mud-zindex-popover: var(--z-popover);
    --mud-zindex-dialog: var(--z-modal);
    --mud-zindex-snackbar: var(--z-toast);
    --mud-zindex-tooltip: var(--z-toast);
}
```

- [ ] **Step 2: Audit the remaining sections against the new system**

The file's second half removes hard-coded Material tells. Read it in full and update each rule to the new tokens:

```bash
sed -n '100,481p' src/LineOps.Web/wwwroot/css/mud-bridge.css
```

For each rule: if it references a deleted token (`--ink-*`, `--chalk`, `--haze`, `--steam`, `--drift`, `--flag`, `--iris`, `--radius-lg`, `--shadow-win`, `--shadow-float`, `--face-data` as a distinct family, `--ease-marble`, `--ease-glide`, `--dur-marble`, `--dur-glide`), re-point it:

| Deleted | Replacement |
|---|---|
| `--ink-900` | `--surface-0` |
| `--ink-800` / `--ink-700` | `--surface-1` |
| `--ink-600` | `--surface-2` |
| `--ink-500` | `--separator` (as a line) or `--surface-3` (as a fill) |
| `--ink-400` | `--surface-3` |
| `--chalk` | `--text-primary` |
| `--haze` | `--text-secondary` |
| `--haze-dim` | `--text-tertiary` |
| `--iris` | `--accent` |
| `--iris-dim` | `--accent-wash` |
| `--steam` / `--drift` / `--flag` | `--state-positive` / `--state-negative` / `--state-warning` |
| `--radius-lg` | `--radius-panel` |
| `--shadow-win` | `--shadow-3` |
| `--shadow-float` | `--shadow-sheet` |
| `--ease-marble` | `--ease-spring` |
| `--ease-glide` | `--ease-standard` |
| `--dur-marble` | `--dur-base` |
| `--dur-glide` | `--dur-fast` |
| `--key-ink` | `--btn-ink` (set by the recipe in `lineops.css`) |
| `--key-tint` | `--btn-fill` (set by the recipe in `lineops.css`) |
| `.desk-key` | `.desk-btn` |

Delete outright any rule whose only job was flattening a Material shadow into the old hard, short desk shadow — the new elevation ramp above already produces soft Apple shadows, so those rules now fight the system rather than serve it.

> **Later addition, from Task 10:** `.mud-chip` in this file still mirrors the retired tag shape — bordered, square-ish — and has diverged from the capsule the desk's own `.tag` became. Task 10 could not fix it because this file was closed to it. If you are reading this during Task 6 the divergence does not exist yet; if you are reading it later, whoever reopens this file should bring `.mud-chip` into line with the capsule treatment: `border-radius: 999px`, no border, a wash fill with matching text colour.

- [ ] **Step 2b: Port the button companion rules to `.desk-btn` — this one is load-bearing**

The file currently carries three rules whose only job is stopping MudBlazor from overriding the desk's own button styling. They still name `.desk-key` and the retired `--key-ink` / `--key-tint`:

```bash
grep -n "desk-key" src/LineOps.Web/wwwroot/css/mud-bridge.css
```

**Why these cannot simply be deleted:** MudBlazor renders `Variant.Text` + `Color.Inherit` with `class="mud-button-text mud-button-text-inherit"`, and its stylesheet has `.mud-button-text.mud-button-text-inherit { color: inherit; }` — a compound selector with specificity 0,2,0. The new recipe in `lineops.css` sets `color: var(--btn-ink)` on the bare `.desk-btn, .btn` selector, specificity 0,1,0. **Specificity is compared before source order, so MudBlazor wins and every button loses its ink colour.** This is exactly the problem the `.desk-key` versions of these rules were written to solve; the rename does not make it go away.

Rewrite all three for the new class and custom properties:

```css
.desk-btn.mud-button-text { color: var(--btn-ink); }

.desk-btn.mud-button:hover,
.desk-btn.mud-button:focus-visible,
.desk-btn.mud-button:active { background-color: var(--btn-fill); }

/* Icon-only buttons would otherwise inherit MudBlazor's 64px minimum. */
.desk-btn.mud-button { min-width: 0; }
```

Keep the explanatory comment that sits above them — update its class names, but its specificity explanation is still the reason the rules exist and is worth more than the rules themselves.

On the hover rule specifically: the new recipe already changes `--btn-fill` on hover via custom property, so check whether MudBlazor's own hover background still needs suppressing before keeping that rule as-is. If it does not, say so in your report rather than carrying a rule that does nothing.

- [ ] **Step 3: Verify the constraint holds**

```bash
grep -n "important" src/LineOps.Web/wwwroot/css/mud-bridge.css
```

Expected: no output. If a rule needs `!important`, the bridge is the wrong place — raise specificity or move it.

```bash
grep -nE "#[0-9A-Fa-f]{3,8}\b|rgba?\(" src/LineOps.Web/wwwroot/css/mud-bridge.css
```

Expected: no output. Colours resolve through tokens; literals belong in `lineops.css`.

- [ ] **Step 4: Build and commit**

```bash
dotnet build LineOps.slnx
git add src/LineOps.Web/wwwroot/css/mud-bridge.css
git commit -m "feat(theme): re-point the MudBlazor bridge at the Apple tokens"
```

---

### Task 7: Fields, switches, and selects

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — the `.field` block (~lines 1878–1972) and the switch block.
- Modify: `src/LineOps.Web/Components/Desk/DeskSwitch.razor` if its markup cannot carry the pill (check first).
- Test: `tests/LineOps.Web.Tests/DeskSwitchTests.cs` — keep green.

**Interfaces:**
- Consumes: tokens from Task 1, focus ring from Task 4's convention.
- Produces: `.field`, `.field-set`, `.field-lbl`, `.field-err`, `.desk-switch` restyled. Class names are unchanged, so no call site moves.

- [ ] **Step 1: Replace the `.field` block**

```css
/* ------------------------------------------------------------------- fields ---
   A field is a place to put something, so it is cut into the surface rather than
   raised off it: a darker well, a hairline, and no fill of its own. ADR 0008 had
   fields inheriting a button recipe with text-transform patched off at every call
   site; that inversion is gone with the gloss it was correcting.
   --------------------------------------------------------------------------- */

.field {
    display: block;
    width: 100%;
    height: 28px;
    padding: 0 var(--space-2);
    box-sizing: border-box;

    background: var(--surface-0);
    border: 1px solid var(--separator);
    border-radius: var(--radius);

    color: var(--text-primary);
    font-family: var(--face-ui);
    font-size: var(--text-body);
    font-weight: var(--weight-regular);
    letter-spacing: 0;
    text-transform: none;

    transition: border-color var(--dur-fast) var(--ease-standard),
                box-shadow var(--dur-fast) var(--ease-standard);
}

.field::placeholder { color: var(--text-tertiary); }

.field:hover:not(:disabled) { border-color: var(--separator-strong); }

.field:focus, .field:focus-visible {
    outline: none;
    border-color: var(--accent);
    box-shadow: var(--focus-ring);
}

.field:disabled {
    opacity: .40;
    cursor: not-allowed;
}

.field.num { font-variant-numeric: tabular-nums; }

textarea.field {
    height: auto;
    padding: var(--space-1) var(--space-2);
    line-height: var(--leading-body);
    resize: vertical;
}

select.field {
    appearance: none;
    padding-right: var(--space-6);
    cursor: pointer;
    /* The chevron is drawn rather than imported, so it takes the text colour. */
    background-image: linear-gradient(45deg, transparent 50%, var(--text-secondary) 50%),
                      linear-gradient(135deg, var(--text-secondary) 50%, transparent 50%);
    background-position: calc(100% - 14px) 12px, calc(100% - 9px) 12px;
    background-size: 5px 5px, 5px 5px;
    background-repeat: no-repeat;
}

select.field option { background: var(--surface-2); color: var(--text-primary); }

.field-set { display: flex; flex-direction: column; gap: var(--space-1); min-width: 0; }
.field-set > .field { width: 100%; }

.field-lbl {
    font-size: var(--text-footnote);
    font-weight: var(--weight-medium);
    color: var(--text-secondary);
    letter-spacing: 0;
    text-transform: none;
}

.field-req { color: var(--state-negative); margin-left: 3px; }

.field-err {
    font-size: var(--text-footnote);
    color: var(--state-negative);
}

.field[aria-invalid="true"] { border-color: var(--state-negative); }

.field[aria-invalid="true"]:focus,
.field[aria-invalid="true"]:focus-visible {
    border-color: var(--state-negative);
    box-shadow: 0 0 0 3px var(--state-negative-wash), 0 0 0 1px var(--state-negative);
}
```

Note the old block set `.field-lbl` uppercase with tracking — that goes, per the type ramp.

- [ ] **Step 2: Restyle the gate as a segmented control**

**Read the component before writing any CSS:**

```bash
cat src/LineOps.Web/Components/Desk/DeskSwitch.razor
grep -n "gate\|marble" src/LineOps.Web/wwwroot/css/lineops.css | head -30
```

`DeskSwitch` is **not** a binary on/off switch. It is a `role="radiogroup"` holding 2–5 fixed positions — a market, a lookback window, a layout mode — with a "marble" cap that slides along a channel to the selected one. Its Apple counterpart is therefore the **segmented control**, not the iOS pill switch. Do not restyle it as a pill; that would be the wrong control entirely.

The markup it emits, which your selectors must match exactly:

- `.marble-row.gate` on the container, plus `.gate--sm` / `.gate--lg` for size and `.gate--num` when positions are numeric. It carries inline `--n` (position count) and `--i` (selected index) custom properties, which is how the cap knows where to sit. **Those two properties are load-bearing — your CSS must keep using them to place the cap.**
- `.marble.gate__cap` — the sliding cap. Rendered only when something is selected; a custom value typed beside the gate leaves `--i` at `-1` and the cap simply absent rather than lying about a position.
- `.gate__opt` per position, with `.gate__opt--on` on the selected one.

The segmented-control treatment:

```css
/* A segmented control: a recessed track holding a raised cap that slides to the
   selected position. The platform's answer to "one of a few, all visible", and the
   right shape for a control that was already a cap travelling in a channel — only
   the moulding changes, not the mechanism.

   The cap is placed off --i and sized off --n, both set inline by the component. */
.marble-row.gate {
    position: relative;
    display: inline-grid;
    grid-template-columns: repeat(var(--n), 1fr);
    align-items: center;
    padding: 2px;
    background: var(--surface-0);
    border-radius: var(--radius);
    box-shadow: inset 0 0 0 1px var(--separator);
}

.marble.gate__cap {
    position: absolute;
    top: 2px;
    bottom: 2px;
    left: 2px;
    width: calc((100% - 4px) / var(--n));
    border-radius: calc(var(--radius) - 2px);
    background: var(--surface-3);
    box-shadow: var(--shadow-1);
    transform: translateX(calc(var(--i) * 100%));
    transition: transform var(--dur-base) var(--ease-spring);
    pointer-events: none;
}

.gate__opt {
    position: relative; /* above the cap */
    z-index: 1;
    padding: 0 var(--space-3);
    height: 24px;
    border: none;
    background: transparent;
    color: var(--text-secondary);
    font-family: var(--face-ui);
    font-size: var(--text-subheadline);
    font-weight: var(--weight-medium);
    white-space: nowrap;
    cursor: pointer;
    transition: color var(--dur-fast) var(--ease-standard);
}

.gate__opt:hover { color: var(--text-primary); }

.gate__opt--on { color: var(--text-primary); font-weight: var(--weight-semibold); }

.gate__opt:focus-visible { outline: none; box-shadow: var(--focus-ring); border-radius: calc(var(--radius) - 2px); }

.gate--num .gate__opt { font-variant-numeric: tabular-nums; }

.gate--sm .gate__opt { height: 20px; padding: 0 var(--space-2); font-size: var(--text-footnote); }
.gate--lg .gate__opt { height: 28px; padding: 0 var(--space-4); font-size: var(--text-body); }

@media (prefers-reduced-motion: reduce) {
    .marble.gate__cap { transition: none; }
}
```

Treat the exact values as a starting point rather than gospel: the old block may express sizing or the cap's travel differently, and matching the component's actual geometry matters more than matching these numbers. What must not change is the mechanism — cap placed from `--i`, width from `--n` — and the reading: recessed track, raised selected cap, labels that gain weight and contrast when selected.

If the old block styles anything these rules do not cover (a divider between positions, a disabled state), carry it across rather than dropping it.

- [ ] **Step 3: Run the tests**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj
```

Expected: all pass, including `DeskSwitchTests`. These assert markup semantics, not CSS, so a pure restyle should not move them. If one fails, the component's markup changed when it should not have — revert that part.

- [ ] **Step 4: Verify in the browser**

Start the preview and check the parts catalog renders with the new controls:

- `preview_start` with the dev server config (create `.claude/launch.json` if absent, with `dotnet` / `["run","--project","src/LineOps.Web"]` and the port from `launchSettings.json`).
- Navigate to the desk, open the **Parts** window.
- `read_console_messages` — expect no errors.
- Take a screenshot for the record.

Known measurement trap from ADR 0008: a browser tab that is not compositing does not advance transitions, so `getComputedStyle` on a transitioned property returns its start value indefinitely. Measure geometry, or apply `transition: none` first.

- [ ] **Step 5: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css src/LineOps.Web/Components/Desk/DeskSwitch.razor
git commit -m "feat(desk): inset fields and a platform switch"
```

---

## Phase 2 — Chrome & surfaces

### Task 8: Window chrome, rail, header, footer

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — shell, header, footer, rail, `.win`/`.win__bar`/`.win__ctl` blocks.
- Modify: `src/LineOps.Web/Components/Windowing/WindowBar.razor` only if the close/minimise controls need different markup.

**Interfaces:**
- Consumes: material tokens, `--radius-panel`, `--shadow-3`, `--titlebar-h`, `--rail-size`.
- Produces: restyled chrome; no component API changes.

- [ ] **Step 1: Locate the blocks**

```bash
grep -n "^\.hdr\|^\.ftr\|^\.rail\|^\.win\b\|^\.win__\|^\.shell" src/LineOps.Web/wwwroot/css/lineops.css
```

- [ ] **Step 2: Apply the chrome rules**

Rewrite those blocks against these decisions, re-pointing every deleted token per Task 6's table:

- **Header and footer** sit on `--material-thin` with `backdrop-filter: var(--material-blur)`, separated from the body by a `1px solid var(--separator)` bottom/top edge and no shadow. A window scrolling under them reads as depth.
- **Rail** uses `--material-regular` and the same blur; its selected item is `--accent-wash` with `--accent` text and no border.
- **Window body** is opaque `--surface-1` with `border-radius: var(--radius-panel)`, `box-shadow: var(--shadow-3)`, and no border. Materials are for things that float; a window full of numbers stays opaque.
- **Title bar** is `--surface-2`, height `var(--titlebar-h)`, with the title in `--text-headline` weight `var(--weight-semibold)` and `--text-primary`; subtitle in `--text-subheadline` `--text-secondary`.
- **Window controls** (`.win__ctl`) become 12px circles with `--space-2` gaps, taking `--text-tertiary` as a resting fill and revealing their glyph only on bar hover. Close fills `--state-negative` on its own hover. This is traffic-light *behaviour* — do not reproduce Apple's exact three-colour set.
- **Focus** anywhere in the chrome uses `box-shadow: var(--focus-ring)` and never an `outline` colour of its own.

Keep the existing `@supports not (backdrop-filter: blur(1px))` fallback block, updating its solid colours to `--surface-1` / `--surface-2`. If no such block exists, add one — every material needs an opaque path.

- [ ] **Step 3: Verify in the browser**

Reload the preview, screenshot the desk with two windows open and the rail expanded. Check `read_console_messages` for errors, and confirm with `javascript_tool` that the header actually composites a material:

```javascript
getComputedStyle(document.querySelector('.hdr')).backdropFilter
```

Expected: a non-`none` value containing `blur`.

- [ ] **Step 4: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css src/LineOps.Web/Components/Windowing
git commit -m "feat(desk): materials and macOS proportions on the window chrome"
```

---

### Task 9: Grids and tables

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — grid/table/row blocks.
- Modify: `src/LineOps.Web/wwwroot/css/mud-bridge.css` — `MudDataGrid`/`MudTable` rules, if present.

**Interfaces:**
- Consumes: tokens from Task 1.
- Produces: restyled data surfaces; `DeskGrid.razor` and `DeskRow.razor` APIs unchanged.

- [ ] **Step 1: Locate the blocks**

```bash
grep -n "^\.grid\|^\.row\|^\.dg\|mud-table\|mud-datagrid" src/LineOps.Web/wwwroot/css/lineops.css src/LineOps.Web/wwwroot/css/mud-bridge.css
```

- [ ] **Step 2: Apply the list rules**

- **No zebra striping.** Delete any `:nth-child(even)` background rule. Rows separate with `border-bottom: 1px solid var(--separator)` and nothing else.
- **Row height 28px** (`--space-1` vertical padding at `--text-body`); density is the point of this console and survives the restyle intact.
- **Header row** is `--surface-2`, `--text-footnote`, `var(--weight-semibold)`, `--text-secondary`, no uppercase and no letter-spacing.
- **Hover** is `background: var(--surface-2)`; **selected** is `background: var(--accent-wash)` with `--text-primary`. Selection never uses a border or a left-edge bar.
- **Every numeric cell** carries `font-variant-numeric: tabular-nums` — either via the existing `.num` class or a rule on the cell selector.
- Values that carry a genuine state (a moved line, a breached budget, a win or loss) keep `--state-positive`/`--state-negative`/`--state-warning` **on the text only**, never as a row fill. A whole row painted with a state colour was the old dial's habit.

- [ ] **Step 3: Verify in the browser**

Open a data-dense window (Board or History), screenshot, and confirm row rhythm and number alignment. Then check contrast on secondary text, which the spec flags as a risk:

```javascript
getComputedStyle(document.querySelector('.grid td, .row')).color
```

Compute the contrast ratio of `--text-secondary` (white at 55%) composited over `--surface-1` (`#232326`). If it lands below 4.5:1 for body-size text, raise `--text-secondary` in `lineops.css` — adjust the token, never the component — and note the new value so Task 2's `DeskThemeTests` stays honest.

- [ ] **Step 4: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css src/LineOps.Web/wwwroot/css/mud-bridge.css
git commit -m "feat(desk): macOS-list density for grids and tables"
```

---

### Task 10: Display primitives, and the residual token sweep

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — the blocks behind `Tag.razor`, `DeskProgress.razor`, `DeskSkeleton.razor`, `EmptyState.razor`, `Note.razor`, `Metric.razor`, `MetricRow.razor`, `PriceCell.razor`, **plus every remaining block still using retired tokens** — the runbook, the launcher, the `.glide-plate` hover system, and whatever residue Tasks 8 and 9 leave behind.

**Interfaces:**
- Consumes: tokens from Task 1.
- Produces: restyled display primitives; no API changes.

**This task is also the catch-all**, and that is a bigger job than its title suggests. Tasks 4, 6, 7, 8 and 9 each own a named region; every remaining block in `lineops.css` that still speaks the retired vocabulary is yours. As of Task 8 that is roughly 200 lines across these sections, none of which any other task claims:

| Section | Retired-token lines | Notes |
|---|---|---|
| panel typography (~1230–1783) | ~81 | tags, metrics, notes, snippets — minus whatever Task 9 took for grids |
| shell (~219–717) | ~30 | the residue Task 8 left: the `.glide-plate` hover system, toolbar rules |
| runbook (~2102–2313) | ~24 | owned by no earlier task |
| launcher (~1170–1229) | ~9 | owned by no earlier task |
| window / desk (~780–1056) | ~12 | residue Task 8 left behind |

Take an inventory before you start, so you are working from the real number rather than this estimate:

```bash
grep -cE "\-\-(ink-[0-9]+|chalk|haze|steam|drift|flag|iris|radius-lg|shadow-win|shadow-float|ease-marble|ease-glide|dur-marble|dur-glide|face-data)\b" src/LineOps.Web/wwwroot/css/lineops.css
```

For every block outside the five named in Step 1, the work is a faithful re-point using Task 6's mapping table, not a redesign — you are finishing a migration, not restyling a subsystem nobody asked you to touch. Two judgment calls recur:

- **`--face-data`** has no replacement token: the system collapsed to one family with `font-variant-numeric: tabular-nums`. Drop the family declaration and make sure tabular numerals survive wherever numbers are read in a column.
- **`.glide-plate` and the toolbar** are a JS-driven hover system whose tone attributes (`data-tone="stop"` and friends) still resolve through retired state tokens. Re-point them to the role-named ones. Task 8 already fixed the two instances that were visibly broken — the close button's hover and the marble plate — and the rest are the same shape. Note `.bar__key--current`, the tab bar's selected-state styling, is among them: it still references `--ink-600`/`--chalk` and currently paints nothing, so the selected tab is invisible as a selection. That one is a visible defect, not just residue.

**Two items Task 9 handed you explicitly:**

- **The `.rowacts` / `.snippet` / `.logrow` disclosure section** was judged genuinely ambiguous between Tasks 9 and 10 and deliberately left alone rather than silently claimed. It is yours. It is row-disclosure furniture — inline snippets that open under a row — so treat it as display, not as grid structure.
- **`RunsPanel.razor` applies `.dim` to real operational data**, not merely quiet annotation. `.dim` resolves to `--text-tertiary`, which measures 2.70:1 against `--surface-1` — well under WCAG AA, and fine for a genuinely de-emphasised tier but not for data an operator must read. Task 9 could not fix it without touching markup. **You may change that `.razor`**: move those cells to `--text-secondary` (measured 5.71:1, passes AA) by using the appropriate class, or introduce one if none fits. Do not lower the bar by brightening `--text-tertiary` — that token is doing its job correctly elsewhere; the call site is what is wrong.

- [ ] **Step 1: Restyle each, following these rules**

- **Tags/badges** become capsules: `border-radius: 999px`, `padding: 2px var(--space-2)`, `font-size: var(--text-caption)`, `font-weight: var(--weight-medium)`, no border, a wash fill (`--accent-wash`, `--state-positive-wash`, `--state-negative-wash`, `--state-warning-wash`) with matching text colour, or `--surface-3` with `--text-secondary` when neutral.
- **Skeletons** are `--surface-2` with a shimmer using the same `desk-btn-sheen` keyframes and `--ease-standard`; radius matches whatever they stand in for.
- **Progress** is a 4px `--surface-3` track with an `--accent` fill and `border-radius: 999px` on both. Indeterminate slides rather than pulses.
- **Empty states** centre a `--text-secondary` line at `--text-body`; no border, no box — an empty state is an absence, not a card. (An earlier draft called for a `--text-tertiary` glyph above the line. `EmptyState.razor` has no glyph element, and adding markup for decoration argues against the rule the component is expressing. Dropped deliberately.)
- **Metric/PriceCell** keep tabular numerals and take their state colour on the number only.

- [ ] **Step 2: Confirm no deleted tokens survive anywhere**

```bash
grep -nE "\-\-(ink-[0-9]+|chalk|haze|haze-dim|steam|drift|flag|iris)(-dim)?\b" src/LineOps.Web/wwwroot/css/*.css
```

Expected: no output. Any hit is a block missed by Tasks 4, 6, 7, 8, 9, or this one — fix it now.

- [ ] **Step 3: Run tests, verify in browser, commit**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj
```

Expected: all pass (`PriceCellTests` asserts markup, not colour).

Screenshot the Parts window and one panel showing tags and metrics.

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css
git commit -m "feat(desk): capsules, shimmer skeletons, and quiet empty states"
```

---

### Task 11: The pulse strip

**Files:**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — pulse block.

**Interfaces:**
- Consumes: `--state-positive`, `--state-negative`, `--state-warning`, `--accent`, motion tokens.
- Produces: the one loud element, recoloured.

- [ ] **Step 1: Locate and recolour**

```bash
grep -n "pulse" src/LineOps.Web/wwwroot/css/lineops.css | head -20
```

ADR 0007 gave the pulse strip permission to be the desk's one loud element and that permission survives. Map its hues onto the new semantic set (`--steam` → `--state-positive`, `--drift` → `--state-negative`, `--flag` → `--state-warning`, `--iris` → `--accent`) and re-point its easing to `--ease-standard` at `--dur-base`. Do not tone it down — it is the exception the system is built around, and it is the only place the desk animates continuously.

- [ ] **Step 2: Confirm reduced-motion**

The global block from Task 1 already neutralises it, but verify the strip does not use an inline `style` animation that bypasses CSS:

```bash
grep -rn "animation" src/LineOps.Web/Components/Windowing/DeskFooter.razor src/LineOps.Web/Components/Windowing/DeskHeader.razor
```

- [ ] **Step 3: Commit**

```bash
git add src/LineOps.Web/wwwroot/css/lineops.css
git commit -m "feat(desk): recolour the pulse strip to the semantic set"
```

---

## Phase 3 — Modality

### Task 12: DeskDialog as an Apple utility window

**Files:**
- Modify: `src/LineOps.Web/Components/Desk/DeskDialog.razor` — class names and chrome only; drag/cascade/raise logic is untouched.
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — `.deskdialog*` block.

**Interfaces:**
- Consumes: `--material-thick`, `--material-blur`, `--radius-sheet`, `--shadow-sheet`, motion tokens.
- Produces: `DeskDialog` unchanged as an API (`Title`, `Subtitle`, `Icon`, `Width`, `ChildContent`, `OnClose`, `OnRaise`, `ZIndex`, `Index`, `Class`).

- [ ] **Step 1: Restyle the CSS**

```bash
grep -n "deskdialog" src/LineOps.Web/wwwroot/css/lineops.css
```

Give `.deskdialog` `background: var(--material-thick)`, `backdrop-filter: var(--material-blur)`, `border-radius: var(--radius-sheet)`, `box-shadow: var(--shadow-sheet)`, and a `1px solid var(--separator)` hairline (a floating surface is the one place a hairline edge earns its place — it separates the material from whatever it floats over). `.deskdialog__bar` becomes transparent over the material with the title in `--text-headline`/`--weight-semibold`. Add an entrance:

```css
.deskdialog {
    animation: deskdialog-in var(--dur-base) var(--ease-spring);
}

@keyframes deskdialog-in {
    from { opacity: 0; transform: scale(.96); }
    to { opacity: 1; transform: scale(1); }
}
```

Add the `@supports not (backdrop-filter: blur(1px))` fallback to `--surface-2`.

- [ ] **Step 2: Verify the drag still works**

Reload the preview, open a row drill-down that spawns a floating dialog, drag it by its bar, and confirm it moves and raises. The JS module (`js/dialogs.js`) is untouched, so a failure here means a class name the module selects on was renamed — check:

```bash
grep -n "querySelector\|classList" src/LineOps.Web/wwwroot/js/dialogs.js
```

- [ ] **Step 3: Commit**

```bash
git add src/LineOps.Web/Components/Desk/DeskDialog.razor src/LineOps.Web/wwwroot/css/lineops.css
git commit -m "feat(desk): float the dialog on a thick material"
```

---

### Task 13: DeskSheet

**Files:**
- Create: `src/LineOps.Web/Components/Desk/DeskSheet.razor`
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — `.desk-sheet` block.
- Test: `tests/LineOps.Web.Tests/DeskSheetTests.cs` (create)

**Interfaces:**
- Consumes: `DeskButton` (Task 3), tokens.
- Produces: `DeskSheet` with parameters `Title` (string, required), `Subtitle` (string?), `ChildContent` (RenderFragment?), `Footer` (RenderFragment?), `Width` (string, default `"520px"`), `OnCancel` (EventCallback). Rendered inside a `MudDialog` instance, so it is shown via `IDialogService.ShowAsync<DeskSheet>(title, parameters, options)`.

- [ ] **Step 1: Write the failing test**

Create `tests/LineOps.Web.Tests/DeskSheetTests.cs`:

```csharp
using Bunit;
using LineOps.Web.Components.Desk;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LineOps.Web.Tests;

/// <summary>
/// A sheet is for a task that is self-contained and whose only goal is completing it.
/// These tests check the frame it puts around such a task — title, body, footer, and a
/// way out — not the task itself.
/// </summary>
public class DeskSheetTests : DeskTestContext
{
    [Fact]
    public void Renders_its_title_and_body()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p>body</p>"))));

        Assert.Contains("New wager", cut.Markup);
        Assert.Contains("body", cut.Markup);
    }

    [Fact]
    public void Carries_the_sheet_class_so_css_can_find_it()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        Assert.Contains("desk-sheet", cut.Markup);
    }

    [Fact]
    public void A_subtitle_is_optional()
    {
        var cut = RenderInDialog(p => p.Add(x => x.Title, "New wager"));

        Assert.DoesNotContain("desk-sheet__sub", cut.Markup);
    }

    [Fact]
    public void A_subtitle_renders_when_given()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.Subtitle, "Board 12"));

        Assert.Contains("Board 12", cut.Markup);
    }

    [Fact]
    public void The_footer_holds_the_tasks_own_buttons()
    {
        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Place</button>"))));

        Assert.Contains("Place", cut.Markup);
    }

    /// <summary>
    /// The way out is not optional. A sheet that cannot be cancelled is a trap, and
    /// "help people recover from mistakes" is the whole reason modality is allowed here.
    /// </summary>
    [Fact]
    public void Cancelling_reports_it()
    {
        var cancelled = false;

        var cut = RenderInDialog(p => p
            .Add(x => x.Title, "New wager")
            .Add(x => x.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        cut.Find(".desk-sheet__close").Click();

        Assert.True(cancelled);
    }

    private IRenderedFragment RenderInDialog(Action<ComponentParameterCollectionBuilder<DeskSheet>> parameters)
    {
        var provider = RenderComponent<MudDialogProvider>();
        var service = Services.GetRequiredService<IDialogService>();

        var builder = new ComponentParameterCollectionBuilder<DeskSheet>();
        parameters(builder);

        var dialogParameters = new DialogParameters();

        foreach (var p in builder.Build())
            dialogParameters.Add(p.Name, p.Value);

        InvokeAsync(() => service.ShowAsync<DeskSheet>(string.Empty, dialogParameters));

        provider.Render();

        return provider;
    }
}
```

Add the DI using at the top if the analyzer asks: `using Microsoft.Extensions.DependencyInjection;`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskSheetTests"
```

Expected: compile error — `DeskSheet` does not exist.

- [ ] **Step 3: Write the component**

Create `src/LineOps.Web/Components/Desk/DeskSheet.razor`:

```razor
@namespace LineOps.Web.Components.Desk
@using LineOps.Web.Components.Windowing

@*
    TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.

    A modal sheet: one self-contained task, and completing it is the only goal.

    ADR 0013 chose a floating, non-modal layer because comparing games is the desk's
    core workflow and a modal would force the operator to finish with one before
    looking at the next. That still holds, and this does not replace it — ADR 0016
    adds a modal tier beside it for the cases the floating layer serves badly:
    a form that must complete or cancel, a multi-step configuration.

    The rule, which is also the one written into the skill: use a sheet when the task
    is self-contained; use DeskDialog when the operator needs to see anything else
    while working. Comparison is never modal. If you are unsure, do not be modal.

    MudDialog underneath owns the scrim, the focus trap, and the stacking — plumbing
    that is genuinely hard to get right and which MudBlazor already has. Everything
    visible comes from .desk-sheet.
*@

<MudDialog>
    <TitleContent>
        <div class="desk-sheet__head">
            <div class="desk-sheet__titles">
                <span class="desk-sheet__title">@Title</span>
                @if (Subtitle is not null)
                {
                    <span class="desk-sheet__sub">@Subtitle</span>
                }
            </div>
            <button type="button"
                    class="desk-sheet__close"
                    title="Close"
                    @onclick="Cancel">
                <Glyph Icon="@Icons.Material.Filled.Close" />
            </button>
        </div>
    </TitleContent>

    <DialogContent>
        <div class="desk-sheet desk-sheet__body" style="min-width:@Width">
            @ChildContent
        </div>
    </DialogContent>

    <DialogActions>
        @if (Footer is not null)
        {
            <div class="desk-sheet__foot">
                @Footer
            </div>
        }
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance? Instance { get; set; }

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter] public string? Subtitle { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// The task's own buttons. Exactly one of them should be Filled — the one that
    /// completes the task — and Cancel should be Plain beside it.
    /// </summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    [Parameter] public string Width { get; set; } = "520px";

    /// <summary>
    /// Raised when the operator leaves without completing. A sheet that cannot be
    /// cancelled is a trap.
    /// </summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    private async Task Cancel()
    {
        await OnCancel.InvokeAsync();

        Instance?.Cancel();
    }
}
```

**A note on where the width comes from, which will otherwise be re-litigated:** `MudDialog` owns its outer element, so the sheet cannot wrap itself in a sized container. The dialog's actual width is set by the caller through `DialogOptions.MaxWidth`; the `Width` parameter here applies `min-width` to the inner body so a sheet with little content still reads as a sheet rather than a tooltip. Say exactly that in the component's summary comment so the next reader does not try to hoist it.

The surface itself — material, radius, shadow, entrance — is styled in Step 4 by targeting `.mud-dialog`, the class MudBlazor puts on that outer element.

- [ ] **Step 4: Add the CSS**

Append to `lineops.css`:

```css
/* -------------------------------------------------------------------- sheet ---
   A modal sheet. Rises from slightly below and settles — the motion says it came
   from the task, not from the edge of the screen. */

.mud-dialog { /* Mud owns the surface; the desk owns how it looks. */
    background: var(--material-thick);
    backdrop-filter: var(--material-blur);
    border: 1px solid var(--separator);
    border-radius: var(--radius-sheet);
    box-shadow: var(--shadow-sheet);
    animation: desk-sheet-in var(--dur-base) var(--ease-spring);
}

@keyframes desk-sheet-in {
    from { opacity: 0; transform: translateY(12px) scale(.97); }
    to { opacity: 1; transform: translateY(0) scale(1); }
}

.mud-overlay-dialog { background: rgba(0, 0, 0, .45); }

.desk-sheet__head {
    display: flex;
    align-items: flex-start;
    gap: var(--space-3);
    padding: var(--space-4) var(--space-4) var(--space-2);
}

.desk-sheet__titles { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1; }

.desk-sheet__title {
    font-size: var(--text-title3);
    font-weight: var(--weight-semibold);
    color: var(--text-primary);
}

.desk-sheet__sub { font-size: var(--text-subheadline); color: var(--text-secondary); }

.desk-sheet__close {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 24px;
    height: 24px;
    padding: 0;
    border: none;
    border-radius: 50%;
    background: var(--surface-3);
    color: var(--text-secondary);
    cursor: pointer;
    transition: background-color var(--dur-fast) var(--ease-standard);
}

.desk-sheet__close:hover { background: var(--state-negative); color: #FFFFFF; }
.desk-sheet__close:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.desk-sheet__close svg { width: 14px; height: 14px; }

.desk-sheet__body { padding: 0 var(--space-4) var(--space-4); }

.desk-sheet__foot {
    display: flex;
    justify-content: flex-end;
    gap: var(--space-2);
    padding: var(--space-3) var(--space-4);
    border-top: 1px solid var(--separator);
}

@supports not (backdrop-filter: blur(1px)) {
    .mud-dialog { background: var(--surface-2); }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskSheetTests"
```

Expected: PASS. If `RenderInDialog` fights bUnit's rendering, simplify: render `DeskSheet` directly with a `CascadingValue<IMudDialogInstance>` of a test double instead of going through `IDialogService`, and keep every assertion identical.

- [ ] **Step 6: Commit**

```bash
git add src/LineOps.Web/Components/Desk/DeskSheet.razor src/LineOps.Web/wwwroot/css/lineops.css tests/LineOps.Web.Tests/DeskSheetTests.cs
git commit -m "feat(desk): add DeskSheet for self-contained modal tasks"
```

---

### Task 14: DeskAlert and the alerts service

**Files:**
- Create: `src/LineOps.Web/Components/Desk/DeskAlert.razor`
- Create: `src/LineOps.Web/Components/Desk/DeskAlerts.cs`
- Modify: `src/LineOps.Web/Program.cs` — register the service.
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — `.desk-alert` block.
- Test: `tests/LineOps.Web.Tests/DeskAlertTests.cs` (create)

**Interfaces:**
- Consumes: `DeskButton`, `DeskEmphasis`, `DeskRole`, `IDialogService`.
- Produces:
  - `DeskAlert` parameters: `Heading` (string), `Message` (string?), `ConfirmLabel` (string, default `"OK"`), `CancelLabel` (string?, default `"Cancel"`), `Destructive` (bool, default `false`).
  - `interface IDeskAlerts { Task<bool> ConfirmAsync(string heading, string? message = null, string confirmLabel = "OK", string? cancelLabel = "Cancel", bool destructive = false); }`
  - `sealed class DeskAlerts : IDeskAlerts` taking `IDialogService` by constructor.

- [ ] **Step 1: Write the failing test**

Create `tests/LineOps.Web.Tests/DeskAlertTests.cs`:

```csharp
using Bunit;
using LineOps.Web.Components.Desk;

namespace LineOps.Web.Tests;

/// <summary>
/// An alert is the narrowest modal there is: the operator must decide before anything
/// else happens. Its whole job is stating the consequence and offering two ways out.
/// </summary>
public class DeskAlertTests : DeskTestContext
{
    [Fact]
    public void States_its_heading_and_message()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Message, "The ingested odds stay; only the run record goes."));

        Assert.Contains("Delete this run?", cut.Markup);
        Assert.Contains("only the run record goes", cut.Markup);
    }

    [Fact]
    public void Offers_a_way_out_beside_the_confirm()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?"));

        var buttons = cut.FindAll("button");

        Assert.Equal(2, buttons.Count);
    }

    [Fact]
    public void A_single_button_alert_drops_the_cancel()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Ingest finished")
            .Add(x => x.CancelLabel, (string?)null));

        Assert.Single(cut.FindAll("button"));
    }

    /// <summary>
    /// A destructive confirm is red and is never the default. Making the dangerous
    /// button the one that answers Enter is how people delete things they meant to keep.
    /// </summary>
    [Fact]
    public void A_destructive_confirm_is_red_and_not_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.Destructive, true));

        var markup = cut.Markup;

        Assert.Contains("desk-btn--destructive", markup);
        Assert.DoesNotContain("desk-alert__confirm--default", markup);
    }

    [Fact]
    public void A_normal_confirm_is_the_default()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Apply these changes?"));

        Assert.Contains("desk-alert__confirm--default", cut.Markup);
    }

    [Fact]
    public void Labels_can_name_the_actual_consequence()
    {
        var cut = RenderComponent<DeskAlert>(p => p
            .Add(x => x.Heading, "Delete this run?")
            .Add(x => x.ConfirmLabel, "Delete run")
            .Add(x => x.CancelLabel, "Keep it"));

        Assert.Contains("Delete run", cut.Markup);
        Assert.Contains("Keep it", cut.Markup);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskAlertTests"
```

Expected: compile error — `DeskAlert` does not exist.

- [ ] **Step 3: Write the component**

Create `src/LineOps.Web/Components/Desk/DeskAlert.razor`:

```razor
@namespace LineOps.Web.Components.Desk

@*
    TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.

    An alert: the operator must decide before anything else happens.

    This is the narrowest use of modality the system allows, and the only one that is
    allowed to interrupt. Confirmations and destructive gates, nothing else — an alert
    is not a place to show information the operator did not ask about.

    Two conventions here are load-bearing rather than stylistic. A destructive confirm
    renders red and is never the default, because making the dangerous button answer
    Enter is how people delete things they meant to keep. And labels name the actual
    consequence — "Delete run", not "OK" — because a button that says OK is asking the
    operator to remember what they clicked.
*@

<div class="desk-alert">
    <div class="desk-alert__heading">@Heading</div>

    @if (Message is not null)
    {
        <div class="desk-alert__message">@Message</div>
    }

    <div class="desk-alert__actions">
        @if (CancelLabel is not null)
        {
            <DeskButton Emphasis="DeskEmphasis.Plain" OnClick="Cancel">@CancelLabel</DeskButton>
        }

        <DeskButton Emphasis="DeskEmphasis.Filled"
                    Role="@(Destructive ? DeskRole.Destructive : DeskRole.Normal)"
                    Class="@ConfirmClass"
                    OnClick="Confirm">
            @ConfirmLabel
        </DeskButton>
    </div>
</div>

@code {
    [CascadingParameter] private IMudDialogInstance? Instance { get; set; }

    [Parameter, EditorRequired] public string Heading { get; set; } = string.Empty;

    /// <summary>The consequence, in a sentence. Omit it when the heading already says everything.</summary>
    [Parameter] public string? Message { get; set; }

    /// <summary>Name the consequence, not the acknowledgement. "Delete run" beats "OK".</summary>
    [Parameter] public string ConfirmLabel { get; set; } = "OK";

    /// <summary>Null for a one-button alert that only needs acknowledging.</summary>
    [Parameter] public string? CancelLabel { get; set; } = "Cancel";

    /// <summary>Renders the confirm red and refuses to make it the default.</summary>
    [Parameter] public bool Destructive { get; set; }

    private string? ConfirmClass => Destructive ? null : "desk-alert__confirm--default";

    private void Confirm() => Instance?.Close(DialogResult.Ok(true));

    private void Cancel() => Instance?.Close(DialogResult.Ok(false));
}
```

- [ ] **Step 4: Write the service**

Create `src/LineOps.Web/Components/Desk/DeskAlerts.cs`:

```csharp
using MudBlazor;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// TEMPLATE-ABLE — see .claude/skills/apple-mudblazor.
///
/// Asking the operator to decide, in one line at the call site.
///
/// <para>
/// Without this, every confirmation is six lines of DialogParameters and a cast, which
/// is enough friction that call sites quietly skip the confirmation instead. The point
/// of the service is that guarding a destructive action costs one <c>await</c>.
/// </para>
/// </summary>
public interface IDeskAlerts
{
    /// <summary>
    /// Puts a question to the operator and waits. Returns true if they confirmed.
    /// </summary>
    /// <param name="heading">The question, as a question.</param>
    /// <param name="message">The consequence, in a sentence. Omit when the heading says it all.</param>
    /// <param name="confirmLabel">Name the consequence — "Delete run", not "OK".</param>
    /// <param name="cancelLabel">Null for a one-button alert that only needs acknowledging.</param>
    /// <param name="destructive">Renders the confirm red, and never as the default.</param>
    Task<bool> ConfirmAsync(
        string heading,
        string? message = null,
        string confirmLabel = "OK",
        string? cancelLabel = "Cancel",
        bool destructive = false);
}

/// <inheritdoc />
public sealed class DeskAlerts(IDialogService dialogs) : IDeskAlerts
{
    public async Task<bool> ConfirmAsync(
        string heading,
        string? message = null,
        string confirmLabel = "OK",
        string? cancelLabel = "Cancel",
        bool destructive = false)
    {
        var parameters = new DialogParameters<DeskAlert>
        {
            { x => x.Heading, heading },
            { x => x.Message, message },
            { x => x.ConfirmLabel, confirmLabel },
            { x => x.CancelLabel, cancelLabel },
            { x => x.Destructive, destructive }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.ExtraSmall,
            CloseOnEscapeKey = true,
            BackdropClick = false // a decision is not dismissed by missing the alert
        };

        var dialog = await dialogs.ShowAsync<DeskAlert>(string.Empty, parameters, options);
        var result = await dialog.Result;

        return result is { Canceled: false, Data: true };
    }
}
```

- [ ] **Step 5: Register the service**

In `src/LineOps.Web/Program.cs`, beside the existing MudBlazor registration:

```csharp
builder.Services.AddScoped<IDeskAlerts, DeskAlerts>();
```

Add `using LineOps.Web.Components.Desk;` if it is not already there. Scoped is correct — a Blazor Server circuit is a scope, and the service holds per-circuit dialog state.

- [ ] **Step 6: Add the CSS**

```css
/* -------------------------------------------------------------------- alert ---
   Narrow, centred, and quiet. Everything here is subordinate to the question. */

.desk-alert {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
    padding: var(--space-4);
    text-align: center;
    max-width: 320px;
}

.desk-alert__heading {
    font-size: var(--text-title3);
    font-weight: var(--weight-semibold);
    color: var(--text-primary);
}

.desk-alert__message {
    font-size: var(--text-subheadline);
    line-height: var(--leading-body);
    color: var(--text-secondary);
}

.desk-alert__actions {
    display: flex;
    justify-content: center;
    gap: var(--space-2);
    margin-top: var(--space-2);
}

/* The default confirm carries the focus ring at rest, so Enter has a visible target. */
.desk-alert__confirm--default { box-shadow: var(--focus-ring); }
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj --filter "FullyQualifiedName~DeskAlertTests"
```

Expected: PASS.

- [ ] **Step 8: Migrate existing confirm flows**

Find anything currently confirming inline:

```bash
grep -rn "confirm\|Confirm" src/LineOps.Web/Components --include=*.razor --include=*.cs | grep -vi "confirmLabel\|DeskAlert\|IDeskAlerts"
```

For each hit that guards a destructive or irreversible action, replace it with an injected `IDeskAlerts` call:

```razor
@inject IDeskAlerts Alerts
```

```csharp
if (!await Alerts.ConfirmAsync(
        "Delete this run?",
        "The ingested odds stay; only the run record goes.",
        confirmLabel: "Delete run",
        destructive: true))
    return;
```

If no such flows exist yet, skip this step and note it — the service is still correct to have, because Phase 4's parts catalog demonstrates it.

- [ ] **Step 9: Build, test, commit**

```bash
dotnet build LineOps.slnx && dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj
git add src/LineOps.Web/Components/Desk/DeskAlert.razor src/LineOps.Web/Components/Desk/DeskAlerts.cs src/LineOps.Web/Program.cs src/LineOps.Web/wwwroot/css/lineops.css tests/LineOps.Web.Tests/DeskAlertTests.cs
git commit -m "feat(desk): add DeskAlert and a one-line confirm service"
```

---

### Task 15: PullMenu as an anchored popover

**Files:**
- Modify: `src/LineOps.Web/Components/Desk/PullMenu.razor`
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css` — `.pullmenu` block.

**Interfaces:**
- Consumes: `--material-regular`, `--radius-panel`, `--shadow-3`, motion tokens.
- Produces: `PullMenu`'s API unchanged.

- [ ] **Step 1: Restyle**

```bash
grep -n "pullmenu\|pull-menu" src/LineOps.Web/wwwroot/css/lineops.css
```

The surface becomes `--material-regular` + `var(--material-blur)`, `border-radius: var(--radius-panel)`, `box-shadow: var(--shadow-3)`, `1px solid var(--separator)`. Items are `--text-body`, `--text-primary`, 26px tall, with `--space-2` horizontal padding; hover is `--accent-wash` with `--accent` text — the platform's own menu highlight. A selected item shows a leading checkmark glyph rather than a changed background. A destructive item takes `--state-negative` text and a `--state-negative-wash` hover.

Add the anchored entrance, which is what makes a popover read as coming *out of* its trigger:

```css
.pullmenu {
    transform-origin: var(--pullmenu-origin, top right);
    animation: pullmenu-in var(--dur-fast) var(--ease-spring);
}

@keyframes pullmenu-in {
    from { opacity: 0; transform: scale(.92); }
    to { opacity: 1; transform: scale(1); }
}
```

Commit 1243c55 already opens the Workspaces menu inward from the right edge, so the component knows which side it is on — set `--pullmenu-origin` inline from that same decision rather than hard-coding `top right`. Read the component to find where it makes the choice:

```bash
grep -n "right\|left\|edge\|inward" src/LineOps.Web/Components/Desk/PullMenu.razor
```

- [ ] **Step 2: Verify in the browser**

Open the desk, click the Workspaces menu, screenshot it open, and confirm it scales from the corner nearest its trigger. Confirm no console errors.

- [ ] **Step 3: Commit**

```bash
git add src/LineOps.Web/Components/Desk/PullMenu.razor src/LineOps.Web/wwwroot/css/lineops.css
git commit -m "feat(desk): menus become anchored popovers on a material"
```

---

## Phase 4 — Docs & extraction

### Task 16: ADR 0016

**Files:**
- Create: `docs/adr/0016-apple-feel-design-system.md`
- Modify: `docs/adr/0013-the-board-and-the-floating-layer.md`
- Modify: `docs/adr/0008-gloss-as-affordance-and-the-mudblazor-seam.md`

- [ ] **Step 0a: Retired tokens hiding in markup — these are live defects**

Task 10 drove `lineops.css` to zero retired tokens, but the sweep only ever looked at stylesheets. **Retired tokens also live in inline `style=` attributes and in style strings emitted from C#**, where no CSS grep would find them. Each one resolves to nothing, so the declaration is dropped and the element renders unstyled.

```bash
grep -rnE "\-\-(ink-[0-9]+|chalk|haze|steam|drift|flag|iris|radius-lg|shadow-win|shadow-float|ease-marble|ease-glide|dur-marble|dur-glide|face-data)\b" src/LineOps.Web --include=*.razor --include=*.cs
```

Eight live sites, all needing the mapping table:

| File | What breaks today |
|---|---|
| `Components/Desk/PanelHeader.razor:107` | emits `color:var(--haze)` from C#; every team heading loses its colour |
| `Components/Panels/JournalPanel.razor:29` | `--ink-600` background and `--ink-500` border — the block renders with **no background and no border at all** |
| `Components/Panels/PartsPanel.razor:243` | `--iris` bar is invisible |
| `Components/Panels/WindowManagerPanel.razor:77` | `accent-color: var(--iris)` on a range input, so the native control falls back to the browser default |
| `Components/Panels/BoardForm.razor:84` | team heading colour |
| `Components/Panels/GamePanel.razor:343` and `:396` | team heading colour |
| `Components/Panels/OddsPanel.razor:178` | team heading colour |

The five team-heading sites all want `--text-secondary`. `JournalPanel` wants `--surface-2` and `--separator`. Both `--iris` sites want `--accent`.

While you are there: those inline styles duplicate what `PanelHeader` and the panel stylesheet already express. Where an inline style is doing nothing a class could not, prefer the class — but do not turn this into a refactor. Re-point first; consolidate only where it is obvious.

- [ ] **Step 0b: The vocabulary pass**

Task 5's grep gate caught every reference to the retired `DeskTone` *type*, but not the retired *vocabulary* that survives in prose and in identifiers. Clear it now, before the ADR describes a system whose own code still speaks the old language.

Rename the identifiers that still carry the retired word while holding a `DeskState`:

- `Components/TagTones.cs` — the class name, and the file name.
- The private helpers `AlertTone` / `StateTone` / `StatusTone` / `SeverityTone` / `ResultTone` across `DashboardPanel`, `GamePanel`, `IncidentsPanel`, `OpsPanel`, `RunsPanel`, `JournalPanel`. Each returns a `DeskState`, so each should say `State`.
- `PartsPanel.razor`'s `TagDemoTones` tuple field.

These were deliberately kept out of Task 5, because renaming them there would have buried that task's judgment under mechanical diff noise. They are a rename and nothing else — the compiler finds every call site, so verify with a green build rather than a grep.

Then fix the stale prose the type-grep could not see:

- `IncidentsPanel.razor` — a summary reading "Critical reads as Stop; anything below it is Caution" above a method now returning `Negative` / `Warning`.
- `DeskChartFamily.cs` — enum doc comments still describing "the desk's dial", "iris for something you interact with", "steam for money you kept".

Search for the retired vocabulary generally:

```bash
grep -rni "iris\|steam\|drift\|\bflag\b\|chalk\|haze\|gloss\|moulded\|the dial" src/LineOps.Web --include=*.razor --include=*.cs
```

Judge each hit. Some are legitimate domain words — a betting console may well have a `Flag` that means a flag. You are looking for the ones describing the retired *visual* system.

- [ ] **Step 1: Write the ADR**

Follow the house style exactly — read two existing ADRs first (`0008` and `0013`) and match their voice: a Context that states what was true, a Decision with named subsections, and a Consequences list that includes the things that cost time.

Cover, at minimum:

- **Context:** the desk was matte graphite with two semantic channels — gloss for affordance, hue for state. Hue-as-state meant a panel with five buttons had five primary actions and therefore none. The system also had no modal tier at all, and no way to ask the operator a question.
- **Decision — weight replaces hue.** Filled / Tinted / Plain, at most one Filled per context; `Role.Destructive` is the only place a button borrows a state colour. State colours move to where states actually live: numbers, tags, the pulse strip.
- **Decision — materials and depth replace moulding.** Anything that floats takes a material; anything holding data stays opaque.
- **Decision — the seam from ADR 0008 survives intact.** Three files, same jobs, Apple contents. `:root:root` is still load-bearing for the same reason.
- **Decision — a modal tier beside the floating layer,** with the sheet/alert/dialog rule quoted verbatim.
- **Decision — the type stack is `-apple-system` with bundled Inter,** because SF cannot be licensed off-platform.
- **Consequences:** every token is semantic and `[data-theme]`-keyed, so a second theme is a token block; `backdrop-filter` is limited to the floating tier for GPU cost and every material has an opaque `@supports` fallback; `--text-secondary` at 55% was contrast-checked against `--surface-1` (record the measured ratio from Task 9); the `DeskTone` → `Emphasis`/`Role` change touched 25 files and was a judgment sweep, not a rename; the reduced-motion block is the codebase's one sanctioned `!important`.

- [ ] **Step 2: Amend ADR 0013**

Add near the top, after its Status line:

```markdown
**Amended by:** [ADR 0016](0016-apple-feel-design-system.md), which adds a modal tier
(sheets and alerts) beside the floating layer. The floating layer's reasoning is
unchanged — comparison is still never modal.
```

- [ ] **Step 3: Amend ADR 0008**

Add near the top, after its Status line:

```markdown
**Superseded in part by:** [ADR 0016](0016-apple-feel-design-system.md). The gloss and
hue rules below are no longer in force. The MudBlazor seam this ADR established — three
files, and the `:root:root` reasoning — is unchanged and still current.
```

Do not delete 0008's content. An ADR records what was decided and why, including decisions later reversed.

- [ ] **Step 4: Commit**

```bash
git add docs/adr
git commit -m "docs: ADR 0016 records the Apple-feel design system"
```

---

### Task 17: PartsPanel showcase

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/PartsPanel.razor`

**Interfaces:**
- Consumes: every component from Phases 1–3.
- Produces: the living catalog, which is also the reference the skill's templates are read against.

- [ ] **Step 1: Rewrite the catalog**

Remove the `TODO(Task 17)` marker left in Task 5 and rebuild the sections so each shows the system rather than every permutation:

- **Buttons** — a row per emphasis (Plain, Tinted, Filled), each shown Normal and Destructive, at all three sizes, plus disabled and busy. Beside them, a one-line statement of the rule: *exactly one Filled per context*.
- **Fields** — text, numeric with tabular figures, select, textarea, one in its error state.
- **Switch** — on and off.
- **Tags** — neutral and each semantic wash.
- **Display** — metric, price cell, progress, skeleton, empty state.
- **Modality** — three live buttons: one opening a `DeskDialog`, one a `DeskSheet`, one calling `Alerts.ConfirmAsync` with `destructive: true`. Under them, the decision rule in full, because this window is where someone building a panel will actually read it. **The sheet demo must read the `DialogResult` rather than rely on `OnCancel`** — Escape and backdrop dismissal go through Mud's own cancel path and never raise `OnCancel`, a gap found in Task 13; the demo is where that correct usage pattern gets demonstrated for every future call site.

ADR 0008's argument for the catalog being a window rather than a document still holds: it renders at the same column width, on the same surface, under the same theme as the panel being built beside it, so "does this fit?" is answered by looking.

- [ ] **Step 2: Verify every part renders**

Reload the preview, open the Parts window, screenshot each section. Click all three modality buttons and confirm the dialog floats, the sheet rises with a scrim, and the alert's destructive confirm is red and not focus-defaulted.

- [ ] **Step 3: Commit**

```bash
git add src/LineOps.Web/Components/Panels/PartsPanel.razor
git commit -m "docs(desk): rebuild the parts catalog around the new system"
```

---

### Task 18: Extract the skill package

**Files:**
- Create: `.claude/skills/apple-mudblazor/SKILL.md`
- Create: `.claude/skills/apple-mudblazor/references/tokens.md`
- Create: `.claude/skills/apple-mudblazor/references/components.md`
- Create: `.claude/skills/apple-mudblazor/references/modality.md`
- Create: `.claude/skills/apple-mudblazor/references/mudblazor-seam.md`
- Create: `.claude/skills/apple-mudblazor/templates/AppleTheme.cs`
- Create: `.claude/skills/apple-mudblazor/templates/apple-tokens.css`
- Create: `.claude/skills/apple-mudblazor/templates/apple-bridge.css`
- Create: `.claude/skills/apple-mudblazor/templates/AppleButton.razor`
- Create: `.claude/skills/apple-mudblazor/templates/AppleSheet.razor`
- Create: `.claude/skills/apple-mudblazor/templates/AppleAlert.razor`
- Create: `.claude/skills/apple-mudblazor/templates/AppleAlerts.cs`
- Create: `.claude/skills/apple-mudblazor/templates/AppleField.razor` (if a `DeskField` wrapper exists to generalize; otherwise ship the field CSS only and say so)
- Create: `.claude/skills/apple-mudblazor/templates/fonts/README.md`

**Interfaces:**
- Consumes: the shipped LineOps files, which are the source of truth. Templates are generalizations of files that actually work — not fresh authoring.
- Produces: a skill a lower model can invoke and apply to any MudBlazor project.

- [ ] **Step 1: Confirm what is template-able, and fix the markers that are missing**

```bash
grep -rln "TEMPLATE-ABLE" src/LineOps.Web/
```

Every file marked in Phases 1–3 gets a template. Anything not marked stays in LineOps.

**Two known gaps to close before you extract anything**, both found during Task 5's review:

- `Components/Desk/DeskEmphasis.cs` carries no marker, but `DeskState.cs` (which is marked) cross-references `DeskEmphasis` and `DeskRole` by `<see cref="…"/>`. Extracting only the marked files would leave dangling crefs in the template. Mark it.
- `Components/Desk/DeskButton.razor`'s marker line cites "docs ADR 0016" by number. A template's reader has no `docs/adr/`. Replace the citation with the reasoning itself.

Then check every file you are about to extract for the same two problems: a `cref` pointing at something that stays behind, and a citation pointing at a document that does not travel.

- [ ] **Step 2: Write SKILL.md**

```markdown
---
name: apple-mudblazor
description: Use when building or restyling a Blazor UI with MudBlazor that should feel like an Apple product — dark neutral surfaces, translucent materials, SF-style type, one accent, Filled/Tinted/Plain buttons, and sheets/alerts for modality. Covers the design tokens, the MudBlazor theming seam, component conventions, and when to be modal.
---

# Apple-feel MudBlazor

A design system for MudBlazor applications that follows Apple's Human Interface
Guidelines. It ships as tokens plus a small set of wrapper components, and it is
proven in production in a data-dense operations console.

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
   project's stylesheet. This is the foundation; nothing else works without it.
2. **Bundle a type face.** See `templates/fonts/README.md` — SF via `-apple-system`
   on Apple platforms, self-hosted Inter everywhere else.
3. **Copy `templates/AppleTheme.cs`**, rename the namespace, and register it with
   `MudThemeProvider`. The colour values must match the CSS exactly — read
   `references/mudblazor-seam.md` for why both sides are required.
4. **Copy `templates/apple-bridge.css`** and load it after MudBlazor's stylesheet
   and after your tokens. Order is load-bearing.
5. **Copy the component templates you need**, renaming the namespace. Read
   `references/components.md` before wiring them into call sites.
6. **Read `references/modality.md` before adding any dialog, sheet, or alert.**

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
  in the wrong place.

## Adapting to a light theme

Every token is semantic and defined in one `:root` block. A light theme is a
second block keyed on its own `[data-theme]` value that redefines the same names —
no component changes. Invert the surface ramp, swap white-opacity text for
black-opacity text, and keep the accent.
```

- [ ] **Step 3: Write the reference documents**

Each generalizes what shipped. Do not invent new guidance here — every rule must be traceable to a decision made in Phases 1–3 or to ADR 0016.

- **`tokens.md`** — the full token list with each value and *why* it holds that value: why the surface ramp is true-neutral, why text is opacity tiers rather than fixed greys, why there are four material tiers, the HIG type ramp with its style names, radii growing with surface size, and the three motion curves with what each is for.
- **`components.md`** — the affordance hierarchy with a do/don't pair for each level (do: one Filled per panel; don't: Filled on every action in a toolbar). Then fields as inset wells, the switch, list/grid density rules (no zebra, hairline separators, accent-wash selection, tabular numerals), capsules, and the focus-ring convention.
- **`modality.md`** — the decision rule verbatim, then a recipe for each of the three. Include the two alert conventions that are load-bearing: destructive confirms are red and never the default, and labels name the consequence rather than saying OK.
- **`mudblazor-seam.md`** — the three-file structure and each file's job; why the palette must exist in C# (Mud derives `-rgb`/`-darken`/`-lighten`/`-hover` and its stylesheet reaches for them); the `:root:root` specificity explanation in full; the `.mud-button-root:disabled` `!important` trap and why the palette agrees rather than fights; the fixed load order; and ADR 0008's measurement trap — a non-compositing tab does not advance transitions, so `getComputedStyle` returns start values indefinitely.

- [ ] **Step 4: Generalize the templates**

Copy each `TEMPLATE-ABLE` file and mechanically strip LineOps from it:

- Namespace `LineOps.Web.Components.Desk` → `YourApp.Components.Ui`, with a comment at the top of each file saying to rename it.
- `Desk*` type names → `Apple*` (`DeskButton` → `AppleButton`, `DeskSheet` → `AppleSheet`, `DeskAlert` → `AppleAlert`, `DeskAlerts` → `AppleAlerts`, `DeskEmphasis`/`DeskRole` → `AppleEmphasis`/`AppleRole`).
- CSS classes `desk-btn` → `app-btn`, `desk-sheet` → `app-sheet`, `desk-alert` → `app-alert`.
- Replace the `Glyph` component with a bare `<MudIcon Icon="@Icon" />`, since `Glyph` is LineOps-specific chrome.
- Remove every ADR cross-reference and replace it with the reasoning itself — a template's reader has no `docs/adr/`.

Verify nothing leaked:

```bash
grep -rni "lineops\|desk\|adr 00" .claude/skills/apple-mudblazor/templates/
```

Expected: no output.

- [ ] **Step 5: Write the fonts README**

`templates/fonts/README.md` explains that SF Pro cannot be licensed off Apple platforms, so the stack is `-apple-system, BlinkMacSystemFont` first with self-hosted Inter (SIL OFL 1.1) behind it; gives the two `curl` commands from Task 1; and gives the `@font-face` block. Do not copy the font binaries into the skill — a skill carrying a megabyte of woff2 is a skill nobody wants to sync.

- [ ] **Step 6: Verify the skill loads**

```bash
ls -R .claude/skills/apple-mudblazor/
```

Confirm `SKILL.md` has valid YAML frontmatter with `name` and `description`, and that the description states *when to use it* — that string is the only thing a model sees when deciding whether to invoke the skill.

- [ ] **Step 7: Commit**

```bash
git add .claude/skills/apple-mudblazor
git commit -m "feat(skill): extract the apple-mudblazor design system package"
```

---

### Task 19: Final verification

- [ ] **Step 1: Full build and test**

```bash
dotnet build LineOps.slnx && dotnet test tests/LineOps.Web.Tests/LineOps.Web.Tests.csproj
```

Expected: build clean, all tests pass. Also run the other suite:

```bash
dotnet test tests/LineOps.Tests/LineOps.Tests.csproj
```

- [ ] **Step 2: Confirm no trace of the old system**

```bash
grep -rniE "desk-key|DeskTone|--ink-[0-9]|--chalk|--haze|--steam|--drift|--flag|--iris|key-tint|key-sheen|ease-marble|ease-glide" src/ docs/adr/0016*.md
```

Expected: no output. Hits inside `docs/adr/0007*.md` or `docs/adr/0008*.md` are correct and expected — those records describe what was true then.

- [ ] **Step 3: Browser verification sweep**

With the preview running, walk the desk and capture evidence:

1. Screenshot the desk with two windows open.
2. Open Parts; screenshot each section.
3. Open a data-dense panel; confirm row density and number alignment.
4. Trigger a sheet and an alert; screenshot both.
5. `read_console_messages` — expect zero errors.
6. `resize_window` to a narrow width; confirm no horizontal body scroll.
7. Emulate reduced motion and confirm the pulse strip and busy shimmer stop:

```javascript
matchMedia('(prefers-reduced-motion: reduce)').matches
```

8. Tab through a panel and confirm every control shows the accent focus ring.

- [ ] **Step 4: Report**

Summarize for the user: what shipped per phase, the contrast ratio measured in Task 9, any token values adjusted from the plan, and the skill package path. Include the screenshots.

---

## Self-Review

**Spec coverage:** Every section of the design maps to tasks. Section 1 (tokens/palette/materials/typography/shape/motion/future themes) → Tasks 1–2. Section 2 (affordance hierarchy, inputs, chrome, data surfaces) → Tasks 3–11. Section 3 (floating layer restyle, DeskSheet, DeskAlert, decision rule) → Tasks 12–15. Section 4 (skill package, layout, usage model, template-able constraint) → Task 18, with the constraint enforced from Task 1 via the Global Constraints marker rule. Section 5 (phasing, verification, three named risks) → task-level browser verification plus Task 19; the `backdrop-filter` risk is handled by limiting materials to the floating tier with `@supports` fallbacks (Tasks 8, 12, 13), the contrast risk by the explicit check in Task 9, and the MudBlazor-dialog risk by the fallback noted in Task 13 Step 5. The "Future: AppleMud class library" section is documentation the spec already carries and needs no task.

**Placeholder scan:** No TBDs. The two places the plan defers to inspection — the exact CSS block bounds in Tasks 4, 8, 9, 10, 11, 15, and whether `DeskTheme` exposes `Build()` or a property in Task 2 — each ship with the exact `grep` that resolves them and instructions for both outcomes, because those line numbers will have shifted by the time the task runs.

**Type consistency:** `DeskEmphasis`/`DeskRole`/`DeskKeySize` are defined in Task 3 and used identically in Tasks 5, 13, 14, 17. The class contract `desk-btn--{plain,tinted,filled,destructive,sm,lg,icon,busy}` is produced in Task 3 and consumed unchanged in Task 4's CSS and Task 14's test assertion. `IDeskAlerts.ConfirmAsync`'s signature in Task 14 Step 4 matches its call in Step 8 and in Task 17. `DeskTheme.Build()` is defined in Task 2 and asserted against in the same task's tests.

**One deviation from the spec worth flagging to the user:** the spec estimated "~45 existing buttons" for the call-site sweep; the actual count is 103 `Tone=` attributes across 25 files plus 156 `DeskTone` references overall. Task 5 is sized accordingly and `PartsPanel` (28 sites) is deliberately deferred to Task 17 rather than swept twice.

---

## Execution

Plan complete and saved to `docs/superpowers/plans/2026-08-29-apple-feel-design-system.md`.
