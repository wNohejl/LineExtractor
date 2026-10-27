# Desk finalisation, Phase B: the package shape — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `src/LineOps.Desk` has the shape of a package: its stylesheet holds no window of this app, its namespace holds no type of this app, its tests need no app, a consumer wires it with one service call and two head components, `dotnet pack` works, and a README says how.

**Architecture:** Nothing moves out of the repository and nothing is renamed; what changes is which side of the Desk/app line each file sits on. CSS is split by section into `desk.css` (system) and an app `lineops.css`; the Window manager and the Parts bin move into the Desk with the seams they need (`IDeskAccess` links, `IDeskSheets`); the two app components in the Desk namespace leave it; a `tests/LineOps.Desk.Tests` project takes the primitive tests with a fake catalogue; `AddDesk`, `<DeskHead />` and `<DeskScripts />` become the whole consumer wiring.

**Tech Stack:** .NET 10 Blazor Server RCL (`Microsoft.NET.Sdk.Razor`), MudBlazor 9.7, bUnit + xUnit.

**Spec:** `docs/superpowers/specs/2026-10-26-desk-finalisation-design.md` (Phase B).

## Global Constraints

- CLAUDE.md: developer identity, no `Co-Authored-By`, `type(scope): sentence` + why-body, push after every commit, two branches, no worktrees.
- Assembly, namespace and `_content/LineOps.Desk` path keep their names (spec non-goal); `DeskAssets.Root` only gathers the path.
- Phase A's rules still hold: no literal colour outside token blocks (`DeskCssConventionTests`), every field named (`AccessibleNameTests`), `GridConventionTests` walks `src/**`.
- Another session may share the checkout: agents work in their own copy of the tree, never run git; the controller copies files back and commits with `git commit -- <paths>`.
- Before "done": `dotnet build` clean on `src/LineOps.Web`, `src/LineOps.Worker`, all three test projects; all tests green; `dotnet format --verify-no-changes` clean in the Linux sdk container; `dotnet pack src/LineOps.Desk` succeeds.

## Review Focus

1. The stylesheet order is load-bearing (MudBlazor → desk → controls → themes → mud-bridge → app). `DeskHead` must emit it in that order and the app sheet must come after — Task 4's test reads the rendered head.
2. A selector moved to the app sheet that a Desk component still emits (e.g. `.teammark` is app, but `.tag--live` is system) — Task 1's ownership test lists what each side may hold; the reviewer spot-checks three moved selectors against their emitters.
3. The theme survives the key rename: a browser with `lineops.theme` set must come up in that theme once and then be on `desk.theme` — Task 3's test pins the fallback read in both JS files.
4. The Window manager in the Desk must still sign in and out in the gated app — Task 2 keeps `WindowManagerControlsTests` green and adds the gated case.
5. The Desk test project must not quietly depend on the app through a transitive reference — its csproj references only `src/LineOps.Desk`, and Task 5 asserts that by reading the csproj.

---

## Wave 1 (independent file sets)

### Task 1: Two stylesheets

**Files:**
- Rename: `src/LineOps.Desk/wwwroot/css/lineops.css` → `src/LineOps.Desk/wwwroot/css/desk.css` (system)
- Create: `src/LineOps.Web/wwwroot/css/lineops.css` (app)
- Modify: `src/LineOps.Desk/wwwroot/css/mud-bridge.css:462–468` (the `.board__row--open` rule moves to the app sheet)
- Modify: `src/LineOps.Web/Components/App.razor` (`lineops.css` link → `desk.css`, plus the app sheet last, after `mud-bridge.css`)
- Modify: every test that reads `lineops.css` by path: `DeskCssConventionTests`, `DeskThemeTests`, `ThemeContrastTests`, `DeskSparklineTests`, `PartsPanelTests`, `PerformancePanelTests`, `ScoreboardTests`, `WagerRecipeTests`, `DeskChartTests` (reads mud-bridge) — point each at the file that now holds what it reads.
- Create: `tests/LineOps.Web.Tests/StylesheetOwnershipTests.cs`

**The split**, by the banners in `lineops.css` at commit 73b67ec (line numbers from `grep -n "^/\* [-=]"`):

| Stays in `desk.css` | Moves to the app `lineops.css` |
|---|---|
| 1–2021 (tokens, scale, shell, strip, drawers, marble, tab bar, glide, footer, desk, window, pulse, divider, rail, palette, launcher, panel typography) | 2022–2376 "SIGNATURE III: the price cell and its spread rail" (`.price*`, `.rail*`, `.value*`, `.board__*`, `.scoreline`, `.allbets`, `.deskgrid--board`) |
| 2377–3028 (floating dialog, linked cell, buttons, fields, checkbox) | 3337–3398 runbook |
| 3029–3336 panel anatomy **except** `.teammark` (~3065–3087), `.scoreboard` (~3088–3153), `.ptoolbar > .game-search`, `.board__table + .note` | those four fragments |
| 3399–3608 (loading vocabulary, grid rows, progress, a11y) | 3802–3817 splits strip |
| 3609–3801 two-stage disclosure **except** the odds snippet (`.snippet__markets/sides/sel/price/book`) and `.formrun` | those two fragments |
| 3818–3974 log rows **except** `.logrow__opp`, `.logrow__score`, `.price__sel` | those three |
| 4124–4553 (sheet, alert, swatches, Window manager, Parts, material fallbacks) | 3975–4035 line movement; 4036–4123 board rhythm; 4424–4465 Performance |
| | 4554–5402 wager, checks, slip (all 17 subsections); 5403–5469 operations; 5470–end game lines |
| | from mud-bridge.css: the `.deskgrid--clickable .mud-table-row.board__row--open` rule |

Each moved block keeps its banner and comment. The app sheet opens with a header that says what it is (this app's windows, loaded after the Desk's sheets, spending the Desk's tokens) and which sections it holds. The `.wsection`/`.wgroup`/`.wgroup__*` family (~4570–4700, "the house rule on groups") is **system** and stays in `desk.css`, moved out of the wager block into the panel-anatomy section; so do `.wfoot`, `.wsections`, `.jform`-free rules that Journal, Ops, Incidents and the Window manager rely on. Rule of thumb from the spec: named after a window or a domain → app.

- [ ] **Step 1: Write the failing test**

```csharp
public class StylesheetOwnershipTests
{
    private static string Desk(string n) => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "css", n));
    private static string App() => File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "wwwroot", "css", "lineops.css"));

    [Theory]
    [InlineData(".price"), InlineData(".board__"), InlineData(".scoreboard"), InlineData(".teammark"), InlineData(".wager"), InlineData(".slip"), InlineData(".journal-"), InlineData(".ops"), InlineData(".gamelines"), InlineData(".perf-"), InlineData(".movement"), InlineData(".runbook"), InlineData(".formrun"), InlineData(".game-search"), InlineData(".logrow__opp")]
    public void A_window_s_selector_lives_in_the_app_sheet(string selector)
    {
        Assert.DoesNotContain(selector, Desk("desk.css"));
        Assert.DoesNotContain(selector, Desk("mud-bridge.css"));
        Assert.Contains(selector, App());
    }

    [Theory]
    [InlineData(".wgroup"), InlineData(".wsection"), InlineData(".pform"), InlineData(".desk-btn"), InlineData(".deskgrid"), InlineData(".wm-"), InlineData(".parts"), InlineData(".tag--live")]
    public void A_system_selector_lives_in_desk_css(string selector) => Assert.Contains(selector, Desk("desk.css"));

    [Fact]
    public void The_app_sheet_loads_last()
    {
        var app = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "App.razor"));
        Assert.True(app.IndexOf("mud-bridge.css", StringComparison.Ordinal) < app.IndexOf("css/lineops.css", StringComparison.Ordinal));
        Assert.DoesNotContain("_content/LineOps.Desk/css/lineops.css", app);
    }
}
```
(`.wager` and `.journal-` etc. must be spelled as the sheet spells them — check with grep and adjust the InlineData to real class prefixes.)

- [ ] **Step 2: Run it; it fails.**
- [ ] **Step 3: Split the file** as the table says, with the comments intact; rename the system file; write the app file's header. Keep `@container`/`@media` blocks with the section they belong to.
- [ ] **Step 4: Fix the links and the test paths; run the whole suite** (the Phase A convention tests must still find the token blocks in `desk.css`).
- [ ] **Step 5: Commit** — `refactor(desk): the Desk's stylesheet holds the system and the app's holds its windows`.

### Task 2: The Window manager and the Parts bin are the Desk's; sheets open without MudBlazor in the caller

**Files:**
- Move: `src/LineOps.Web/Components/Panels/WindowManagerPanel.razor` → `src/LineOps.Desk/Windowing/WindowManagerPanel.razor` (namespace `LineOps.Desk.Windowing`)
- Move: `src/LineOps.Web/Components/Panels/PartsPanel.razor` → `src/LineOps.Desk/Windowing/PartsPanel.razor`
- Modify: `src/LineOps.Desk/DeskAccess.cs` (`IDeskAccess` gains `bool IsGated => false; string? SignInPath => null; string? SignOutPath => null;` as default interface members), the app's `IDeskAccess` implementation in `src/LineOps.Web/Access/*` (supplies them from `AccessOptions` / `AccessEndpoints`)
- Create: `src/LineOps.Desk/Primitives/DeskSheets.cs` — `public interface IDeskSheets { Task<IDeskSheetHandle> ShowAsync<TSheet>(DialogParameters<TSheet> parameters, string width = "480px") where TSheet : IComponent; }` with `IDeskSheetHandle { Task<DialogResult> Result { get; } void Close(); }`, implemented over `IDialogService` beside `DeskAlerts`; registered in `AddDesk` (`services.AddScoped<IDeskSheets, DeskSheets>();`)
- Modify: `src/LineOps.Web/Windowing/WindowCatalog.cs:292, 304` (types now in `LineOps.Desk.Windowing`)
- Modify: `src/LineOps.Desk/_Imports.razor` (needs `LineOps.Desk.Theming`, `LineOps.Desk.Primitives`, `MudBlazor` if not already)
- Test: `tests/LineOps.Web.Tests/WindowManagerControlsTests.cs` (add the gated case), `tests/LineOps.Web.Tests/PartsPanelTests.cs` (drop price-cell assertions; add a sheet-opens assertion through a `FakeSheets`), `tests/LineOps.Web.Tests/Support/FakeSheets.cs` (create)

**Interfaces produced:** `IDeskAccess.IsGated/SignInPath/SignOutPath`; `IDeskSheets`; `DeskSheets`; both panels under `LineOps.Desk.Windowing`.

- [ ] **Step 1: Failing tests** — `WindowManagerControlsTests.A_gated_desk_offers_sign_out`: register an `IDeskAccess` whose `IsGated` is true and `SignOutPath` is "/out"; assert a key "Sign out" renders. `PartsPanelTests.The_sheet_demo_opens_through_the_desk`: register `FakeSheets`; click the demo key; assert `FakeSheets.Shown` is `typeof(DeskSheet)`. Also assert `PartsPanel` source has no `LineOps.Data` using and no `IDialogService`.
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Window manager** — move the file; replace the three `LineOps.Web.Access` uses with `Access.IsGated`, `Access.SignOutPath`, `Access.SignInPath` (inject `IDeskAccess Access`; `NavigationManager` stays); keep every string and comment.
- [ ] **Step 4: Parts bin** — move the file; delete the three `PriceCell` demos and their `BestOffer` fixtures (`:394–396`, `:939–998`) and the `@using LineOps.Data.CrossReference`; the price-cell section's heading becomes "Linked cell" showing `DeskLink` and `Tag` instead; the "New wager" sheet becomes a `DeskSheet` titled "Add a note" with one grouped form (a `DeskField` and a `DeskTextArea`) opened through `IDeskSheets`.
- [ ] **Step 5: `IDeskSheets`** — implement over `IDialogService` as `DeskAlerts` does (`DialogOptions { MaxWidth = MaxWidth.Small, NoHeader = true, ... }` copied from `PartsPanel.razor:1047–1062`), register in `AddDesk`. No file in `src/LineOps.Web` injects `IDialogService` afterwards (grep).
- [ ] **Step 6: Suite green; commit** — `refactor(desk): the Window manager and the Parts bin are the Desk's own windows, and a sheet opens through the Desk`.

### Task 3: Namespaces, words and keys

**Files:**
- Modify: `src/LineOps.Web/Components/Desk/PriceCell.razor:1`, `PullMenu.razor:1` — `@namespace LineOps.Web.Components.Desk` (already imported by `_Imports.razor`)
- Move: `src/LineOps.Web/Components/Desk/SnippetFailure.razor` → `src/LineOps.Desk/Primitives/SnippetFailure.razor`; `CheckList.razor` → `src/LineOps.Desk/Primitives/CheckList.razor` with the `Check` record (from `Checks.cs:10`) → `src/LineOps.Desk/Primitives/Check.cs` (namespace `LineOps.Desk.Primitives`; `Checks.cs` keeps `Checks` and `CheckActions` and gains `using LineOps.Desk.Primitives`)
- Modify: `src/LineOps.Desk/Primitives/Tag.razor:19, 39` (Live's docs: "something is happening now")
- Modify: `src/LineOps.Desk/wwwroot/js/theme.js:10, 34` and `theme-boot.js:24–25` — keys `desk.theme` / `desk.theme.family`, each read falling back to the old key (`localStorage.getItem('desk.theme') ?? localStorage.getItem('lineops.theme')`); `store` writes the new key only
- Modify: `windowing.js:115, 150–157, 237` and `desk-glide.js:226` — `desk:rescaled`, `__deskSplit`, `__deskTabWired`
- Create: `src/LineOps.Desk/DeskAssets.cs` — `public static class DeskAssets { public const string Root = "_content/LineOps.Desk"; public static string Css(string n) => $"{Root}/css/{n}"; public static string Js(string n) => $"{Root}/js/{n}"; }`; use it at `DeskDialog.razor:76`, `DeskMenu.razor:92`, `DeskPopover.razor:91`, `PanelToolbar.razor:226, 237`, `ThemeService.cs:32`, `Desk.razor:144`, `ReconnectModal.razor:1`
- Create: `tests/LineOps.Web.Tests/DeskVocabularyTests.cs`
- Test: `ThemeBootTests` (extend: both keys appear in both files)

- [ ] **Step 1: Failing tests**

```csharp
public class DeskVocabularyTests
{
    private static readonly string[] Words = ["game", "book", "odds", "wager", "journal", "team", "player", "fight", "bout", "stake", "parlay", "slip", "league", "bet"];

    [Fact]
    public void The_desk_s_code_knows_no_sport()
    {
        var root = Path.Combine(Repo.Root(), "src", "LineOps.Desk");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".js"))
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var code = StripComments(File.ReadAllText(file));   // removes // … , /* … */, @* … *@ and /// lines
            foreach (var w in Words)
                if (Regex.IsMatch(code, $@"\b{w}s?\b", RegexOptions.IgnoreCase))
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {w}");
        }
        Assert.Empty(offenders);
    }
}
```
`StripComments` is a private static helper in the test; it must not strip string literals.
Expected offenders before the task: `Windowing/CommandPalette.razor` (placeholder — Task 4 fixes it; this test may stay red on that one line until Task 4 lands, so the Task 3 agent lists it in the report rather than touching `CommandPalette.razor`), `Windowing/PartsPanel.razor` and `WindowManagerPanel.razor` if Task 2 has landed with any domain word in a demo string (Task 2 keeps its demos generic). Also `ThemeBootTests.The_boot_script_reads_the_same_keys_as_theme_js` is extended so both `desk.theme` and the fallback `lineops.theme` appear in both files.

- [ ] **Step 2: Run; see the failures.**
- [ ] **Step 3: Make the edits** listed under Files. Keep every comment's voice; where a comment says "lineops:rescaled", say "desk:rescaled".
- [ ] **Step 4: Suite green; commit** — `refactor(desk): two app components leave the Desk namespace, two generic ones join it, and the Desk's keys and events carry its own name`.

## Wave 2 (after Wave 1 lands)

### Task 4: Wiring a consumer, packing, and the README

**Files:**
- Modify: `src/LineOps.Desk/DeskHost.cs` — `DeskBrand` gains `string SearchPlaceholder = "Search"` (positional, last, with default); `AddDesk(this IServiceCollection, DeskBrand, DeskClock? = null, Action<MudServicesConfiguration>? mud = null)` calls `services.AddMudServices(o => { <the snackbar block from Program.cs:25–48, with its comments>; mud?.Invoke(o); })`
- Modify: `src/LineOps.Desk/Windowing/CommandPalette.razor:20` — `placeholder="@Brand.SearchPlaceholder"` (inject `DeskBrand Brand`)
- Create: `src/LineOps.Desk/Layout/DeskHead.razor` — emits, in order, `<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]">`, then `desk.css`, `controls.css`, `themes.css`, `mud-bridge.css` via `@Assets[DeskAssets.Css(...)]`, preceded by `<script src="@Assets[DeskAssets.Js("theme-boot.js")]"></script>`; a header comment carrying the load-order explanation now in `App.razor:15–20`
- Create: `src/LineOps.Desk/Layout/DeskScripts.razor` — `<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>` and `desk-glide.js`
- Modify: `src/LineOps.Web/Components/App.razor` — the head becomes `<DeskHead />` + the app's own `css/lineops.css` + `LineOps.Web.styles.css` + icon + `<HeadOutlet>`; the body's scripts become `blazor.web.js` + `<DeskScripts />`
- Modify: `src/LineOps.Web/Program.cs` — delete the `AddMudServices` block (and `using MudBlazor; using MudBlazor.Services;` if unused); pass the placeholder: `new DeskBrand("LINE", "OPS", "sports-data ingestion & analytics operations", "Go to a team, player, game or window")`
- Modify: `src/LineOps.Desk/LineOps.Desk.csproj` — `<IsPackable>true</IsPackable>`, `<PackageId>LineOps.Desk</PackageId>`, `<Version>0.1.0</Version>`, `<Description>An Apple-HIG desk for Blazor Server on MudBlazor: windows, primitives, themes.</Description>`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, `<NoWarn>$(NoWarn);CS1591</NoWarn>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` with `<None Include="README.md" Pack="true" PackagePath="\" />`
- Create: `src/LineOps.Desk/README.md`
- Test: `tests/LineOps.Web.Tests/DeskWiringTests.cs` (create)

- [ ] **Step 1: Failing tests** — `DeskHead_emits_the_sheets_in_load_order` (render `DeskHead` in `DeskTestContext`; assert the `href`s appear in the order MudBlazor, desk, controls, themes, mud-bridge, with `theme-boot.js` before the first); `App_wires_the_desk_with_two_components` (read `App.razor`: contains `<DeskHead />` and `<DeskScripts />`, not `_content/LineOps.Desk/css/`); `Program_does_not_call_AddMudServices` (read `Program.cs`); `The_palette_placeholder_is_the_brand_s` (render `CommandPalette` with a brand whose placeholder is "Find a thing"; assert the input's placeholder).
- [ ] **Step 2: Run; they fail.**
- [ ] **Step 3: Make the edits.** `DeskHead` and `DeskScripts` use `ComponentBase.Assets` (the `ResourceAssetCollection` .NET 10 injects); if a test context has no `ResourceAssetCollection`, register `ResourceAssetCollection.Empty` in `DeskTestContext`.
- [ ] **Step 4: README** — sections, in this order: what the Desk is (three sentences); wiring (the csproj reference, `AddDesk`, `<DeskHead />`/`<DeskScripts />`, the layout, `ReconnectModal`, the catalogue — a complete `Program.cs` + `App.razor` snippet of under 30 lines); the token families (one line each, pointing at `desk.css`) and how a family is added (`themes.css` block + `DeskThemeFamily` + `DeskPalettes`, as `DeskThemeTests` pins); the primitives, one table row each (name, what it is for, the MudBlazor it wraps if any); the MudBlazor seam (what `mud-bridge.css` does; `PropertyColumn`/`TemplateColumn` in `DeskGrid`; `ChartSeries` in `DeskChart`; the four providers in `MainLayout`); the Parts bin as the gallery (`WindowCatalog` key `parts`); the rules (the house rule on edges, one Filled key, sentence case, names on controls, no literal colours) with the tests that hold each.
- [ ] **Step 5: `dotnet pack src/LineOps.Desk -o <scratch>/pack` succeeds; suite green; commit** — `feat(desk): a consumer wires the Desk with AddDesk, DeskHead and DeskScripts, and the package has a README`.

### Task 5: A test project of the Desk's own

**Files:**
- Create: `tests/LineOps.Desk.Tests/LineOps.Desk.Tests.csproj` (same packages as `LineOps.Web.Tests`; `ProjectReference` only `..\..\src\LineOps.Desk\LineOps.Desk.csproj`; the same `NuGetAuditSuppress`)
- Create: `tests/LineOps.Desk.Tests/DeskTestContext.cs` — as `LineOps.Web.Tests.DeskTestContext` but `DeskClock.Utc`, a `FakeWindowCatalog`, no app types
- Create: `tests/LineOps.Desk.Tests/Support/FakeWindowCatalog.cs` — three definitions (`one`, `two`, `three`; one singleton, two subject) and one workspace; `DefaultPrimary = "one"`
- Move (git mv, then fix namespaces to `LineOps.Desk.Tests`): `DeskAlertTests`, `DeskButtonTests`, `DeskChartTests`, `DeskCssConventionTests`, `DeskDialogTests`, `DeskGridTests`, `DeskLinkTests`, `DeskMenuTests`, `DeskSheetTests`, `DeskSparklineTests`, `DeskStepperTests`, `DeskSwitchTests`, `DeskTabsTests`, `DeskThemeTests`, `EmptyStateTests`, `GridResizeTests`, `PanelFormTests`, `PanelSectionTests`, `PanelTitleTests`, `PanelToolbarTests`, `RowActionsTests`, `ThemeContrastTests`, `ThemeServiceTests`, `WindowErrorBoundaryTests`, `StylesheetOwnershipTests`, `DeskVocabularyTests`, `DeskWiringTests`, `WindowManagerControlsTests`, `PartsPanelTests`, and `Support/FakeAlerts.cs`, `Support/FakeSheets.cs`; copy `Support/Repo.cs`
- Modify: `LineOps.slnx` (add the project under `/tests/`); `.github/workflows/ci.yml` needs nothing (solution-level `dotnet test`)
- Test: `tests/LineOps.Desk.Tests/ProjectShapeTests.cs`

- [ ] **Step 1: Failing test** — `ProjectShapeTests.The_desk_tests_reference_only_the_desk`: read the csproj, assert exactly one `ProjectReference` and that it ends with `LineOps.Desk.csproj`; and `No_moved_test_names_the_app`: grep the project's `.cs` for `LineOps.Web`, `LineOps.Core`, `LineOps.Data` → none.
- [ ] **Step 2: Create the project; move the files; fix namespaces and `using`s** (`DeskTestContext` lives in the new namespace; tests that used `AppWindowCatalog` keys by name use `FakeWindowCatalog`'s).
- [ ] **Step 3: `dotnet test tests/LineOps.Desk.Tests` green; `dotnet test tests/LineOps.Web.Tests` green (fewer tests now); `dotnet build` on the solution clean.**
- [ ] **Step 4: Commit** — `test(desk): the Desk's tests are their own project with a fake catalogue`.

## Wave 3

### Task 6: The Desk's documentation reads as the package's

**Files:** the ~40 Desk files the boundary survey listed with domain words in comments (`WindowModels.cs`, `WindowManager.cs`, `FollowUpLauncher.cs`, `DeskSearch.cs`, `DeskSignals.cs`, `DeskHeader.razor`, `WindowBar.razor`, `CommandPalette.razor`, `RowActions.razor`, `DeskGrid.razor`, `DeskSparkline.razor`, `DeskProgress.razor`, `DeskChartFamily.cs`, `DeskToasts.cs`, `DeskSteps.razor`, `DeskStep.razor`, `PanelForm.razor`, `PanelTitle.razor`, `PanelToolbar.razor`, `DeskDialog.razor`, `DeskSheet.razor`, `FollowUpLayer.razor`, `DeskDisclosure.razor`, `DeskSearch.razor`, `DeskSelect.razor`, `DeskMenu.razor`, `DeskMenuItem.razor`, `DeskField.razor`, `DeskLink.razor`, `DeskPopover.razor`, `PanelHeader.razor`, `PanelHeaderGap.cs`, `PanelSection.razor`, `Metric.razor`, `RowAction.cs`, `DeskFollowUps.cs`, `dialogs.js`, `LineOps.Desk.csproj`, the CSS banners).

- [ ] **Step 1:** Extend `DeskVocabularyTests` with `The_desk_s_docs_know_no_sport` that scans the *comments* (the inverse of `StripComments`) for the same words, with an allow-list of the sample markup in `DeskGrid.razor:22–23` and `DeskSparkline.razor:26` replaced by generic samples ("Name", "Score" → "Item", "Count"). It fails on ~120 lines.
- [ ] **Step 2:** Rewrite each comment in the file's own voice with a generic example in the domain word's place ("a wager to the slip" → "an item to a list"), never deleting the why.
- [ ] **Step 3:** Both test projects green; commit — `docs(desk): the Desk's comments describe a desk, not a sportsbook`.

### Task 7: Verification

- [ ] Builds (Web, Worker, three test projects), all tests, `dotnet pack`, `dotnet format --verify-no-changes` in the Linux container from the pushed branch, the Worker starts, the preview on port 5271 shows every window as the Phase A walk left it (Board, Journal edit, Ops, Window manager, Parts bin in dark and light), and `git worktree list` shows one entry.
