# Desk finalisation, Phase E: the shared desk — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Everything generic that `LineOps.Web` still holds moves into the packages — the Desk's own step, tab and grid-child styles; a `DeskPanelBase`; `PanelLoadFailure`; `DeskTime`; `DeskJobMenu`; `DeskErrorPage`/`DeskNotFound`; and a new `LineOps.Desk.Hosting` package with the access gate and the edge headers — and two new primitives, `KeyRow` and `InlineConfirm`, close HIG findings 1 and 3 at every cited site, with the review's CSS and razor quick fixes alongside.

**Architecture:** Moves keep behaviour and names wherever the Desk already emits the name (`.wstep*`, `.tabs__panel`, `.deskgrid__child*`); the app keeps thin adapters where the zone, the jobs or the cookie are LineOps's (`DisplayTime` → `DeskTime`, `IngestionDeskJobs : IDeskJobs`, `lineops.access`). The Hosting package is a plain class library (`Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`) referencing only `LineOps.Desk`; its request-pipeline pieces expose pure functions (`DeskEdge.Headers`, `DeskSignInPage.Render`) so they test without a test server. New primitives follow the shapes in `src/LineOps.Desk/Primitives` (`Class`/`Extra` with `DeskLayout.ExtraClass`, sentence-case copy, CSS spending tokens in `controls.css`).

**Tech Stack:** .NET 10 Blazor Server, MudBlazor 9.7.0, bUnit 1.40 + xUnit; `tests/LineOps.Desk.Tests` (bench `DeskTestContext`, `FakeWindowCatalog`), `tests/LineOps.Web.Tests`, new `tests/LineOps.Desk.Hosting.Tests` (xUnit only).

**Spec:** `docs/superpowers/specs/2026-10-30-desk-shared-phase-e-design.md`. Line numbers below were re-read at `4e50a0d`; re-grep before editing, they move as earlier tasks land.

## Global Constraints

- **Git (CLAUDE.md).** Two branches only — `main` and `LineX_Development`; no worktrees (`git worktree list` shows one entry), no extra branches, stashes or tags. Commits are the developer's own identity; **never** a `Co-Authored-By: Claude` trailer or a Claude committer. Subjects are `type(scope): what changed, as a sentence` with a why-body. Push `LineX_Development` after every commit.
- **The controller commits; agents never run git.** Agents edit files and run builds/tests only. The controller commits each task by explicit pathspec (`git commit -- <paths>`), because another session may share the checkout.
- **No sports in the packages.** `DeskVocabularyTests` scans `src/LineOps.Desk` (and, from Task 3, `src/LineOps.Desk.Hosting`) code *and comments* for game, book, odds, wager, journal, team, player, fight, bout, stake, parlay, slip, league, bet (Task 3 adds espn, mlb, nfl, ufc). Also avoid board, ESPN, MLB, NFL, UFC in anything new in either package. Examples are generic ("an item", "a run", "the record").
- **Tests that must stay green throughout:** `AccessibleNameTests` (every field named), `DeskCssConventionTests` (no literal colour outside token blocks; one-edge rules), `StylesheetOwnershipTests` (system selectors in Desk sheets, window selectors in `lineops.css`), `ProjectShapeTests` (Desk tests reference only the Desk), `DeskVocabularyTests`, `PartsPanelTests` (every `Desk*` razor in Primitives, and the `NamedForWhatTheyAre` list, has a Parts bin section and an alphabetical README row — so a new `Desk*` primitive is not green until both exist).
- **One owner per file per wave.** Ownership is listed per task. `lineops.css` and `controls.css` each have exactly one owner per wave; `PartsPanel.razor` and `src/LineOps.Desk/README.md` likewise. A task that needs a change in a file it does not own writes it to `<tree>/scratch/task<N>-<file>` with a banner naming the destination section, and the controller merges it at the end of the wave before running the wave's tests.
- **Builds.** When another agent may be building, build and test to a scratch output outside the repo: `dotnet build <proj> -p:BaseOutputPath=$env:TEMP\lineops-e-<task>\` (or `dotnet test ... -p:BaseOutputPath=...`). Never `Stop-Process` LineOps.Web or any process another session started; preview on port 5271 with `Ingestion:HostScheduler=false` and `Reliability:HostEvaluator=false`.
- **Targets ≥ 28px tall** on the desk (segments inside a gate ≥ 24px); focus is `box-shadow: var(--focus-ring)`, never a bare `outline` of its own.
- **TDD.** Where a test can exist, write it first, run it, see it fail for the right reason, then implement.
- **Before "done":** `dotnet build` clean on `src/LineOps.Web`, `src/LineOps.Worker`, `tests/LineOps.Tests`, `tests/LineOps.Web.Tests`, `tests/LineOps.Desk.Tests`, `tests/LineOps.Desk.Hosting.Tests`; `dotnet test` green; `dotnet run --project src/LineOps.Worker` starts; `dotnet pack src/LineOps.Desk` and `dotnet pack src/LineOps.Desk.Hosting` succeed; `dotnet format --verify-no-changes` clean in the `mcr.microsoft.com/dotnet/sdk:10.0` container.

## Review Focus

1. **The step styles really left the app sheet** (Task 1): `DeskStepTests` would pass with no CSS at all, so the proof is `StylesheetOwnershipTests` plus a browser look at a wager window — rail, markers and capsules drawn exactly as before (finding 10 aside).
2. **`DeskPanelBase` is a move, not a rewrite** (Task 2): the generation guard, the 1 s settle, the `_markedHost` release and the "refresh failed" pulse must survive verbatim; `PanelLoadStateTests` and `UnsavedInputTests` in Web.Tests pass unchanged.
3. **Nobody is signed out** (Task 3): the cookie is still `lineops.access`, the section is still `Access`, the hash format is unchanged (`A_hash_made_by_the_setup_script_is_one_the_desk_accepts` moves intact), and the Dockerfile restores the new csproj.
4. **`aria-describedby` points at text that exists** (Tasks 5, 9, 10): a `KeyRow` with no reason emits no id and the primary gets `null`; two rows on one desk never share an id.
5. **Key order** (Tasks 9, 10): every adopted row reads secondary keys, reason, Filled key — `Cancel` first in every confirm.
6. **A takeable price is a real button** (Task 8): Space activates it without scrolling the window, a visitor's price is not a button at all, and the cell looks identical.

---

## Wave 1 (four tasks, disjoint files)

### Task 1: The Desk's own styles live in the Desk's sheets; the CSS-only HIG fixes

**Files (owner of all CSS in wave 1):**
- Modify: `src/LineOps.Web/wwwroot/css/lineops.css`, `src/LineOps.Desk/wwwroot/css/controls.css`, `src/LineOps.Desk/wwwroot/css/desk.css`
- Modify: `tests/LineOps.Desk.Tests/StylesheetOwnershipTests.cs`, `tests/LineOps.Desk.Tests/DeskCssConventionTests.cs`

**Moves:**
- `lineops.css:1041–1133` (the banner "The stepper inside the card" through `.wstep__body > .note`) → a new `/* ---- Steps ---- */` section in `controls.css` after "Radio group" (before "Accent picker"), class names unchanged. Rewrite the banner comment generically (a sequence of steps on a rail; waiting/current/done markers; a folded step is one line) — no "card", "wager" or "wizards.md". The `--wsep` fallback stays (`var(--wsep, var(--separator))`). Leave in `lineops.css` every rule that only *contextualises* steps for the wager window: `.wstep__summary .wager-offer__*` (989–990), `.wstep--review*`, `.wsteps .wgroup`, `.wsteps .stake-row*`, `.wsteps .waction*`, `.wsteps .wager-why`, `.wsteps .check .desk-btn`, `.wsteps .wstake*`, `.wsteps .selection-gate*`.
- From the `@container (max-width: 480px)` block (`lineops.css:1466`), move `.wstep { padding-right: var(--space-3); }` (1468) and `.wstep__hint { font-size: var(--text-footnote); }` (1470) into a `@container (max-width: 480px)` block at the end of the new Steps section.
- `lineops.css:1457` `.wtabs + .tabs__panel { container-type: inline-size; }` → delete; add `.tabs__panel { container-type: inline-size; }` to `controls.css` after the `.tabs__badge` block (~722) with a one-line comment (a tab's body is a container so what it holds can answer to the window's width).
- `lineops.css:720–721` (`.deskgrid--board .deskgrid__child-cell`, `.deskgrid--board .deskgrid__child > td`) → delete; add to `desk.css` after `.deskgrid--clickable .mud-table-row` (3503): `.deskgrid__child > td { background: var(--accent-wash); }` and `.deskgrid__child-cell { width: auto; white-space: normal; }` with a comment (an open row's child spans the row and reads as part of it).
- `.whead`, `.wcard`, `.wreceipt`, `.waction` **stay** in `lineops.css` (spec: no Desk component emits them).
- Before moving, grep `mud-bridge.css` and `themes.css` for any selector the moved rules interact with (`.note`, `.desklink`, `.mud-`); if a moved rule used to win only by loading after one, raise its specificity by the minimum (e.g. `.wsteps .wstep__body > .note`) and say so in the task report.

**HIG CSS fixes (all in `lineops.css` unless moved above):**
- 10: in the moved `.wstep__title` — remove `border`; base `background: var(--surface-3)`; `.wstep--current .wstep__title { background: var(--accent-wash); color: var(--accent-ink); }`; `.wstep--todo .wstep__title { background: none; color: var(--text-secondary); }` (no `border-color` anywhere).
- 11: merge the two `.book-picker__trigger` blocks (:1199 at 32px, :1425 at 22px) into one block at the first location with `height: 28px`, keeping the later block's look (radius, fill, padding, gap) since it wins today; fold the two `:hover`/`[aria-expanded]`/`:focus-visible`/`svg` rule pairs likewise, keeping the later one's values. `.selection-gate .gate__opt` (:1367–1368) `min-height: 24px`.
- 13: `.gamelines__cell--take:focus-visible` (:1605) → `outline: none; box-shadow: var(--focus-ring);`.
- 18 (CSS): delete `.whead .ptitle__row .tag` (:973; `.tag` is already a capsule); `.runbook` `border-radius: var(--radius-sm)` (:482); `.slip-badge` `border-radius: 999px` (:1262).
- 19: `.price__edge` (:119) and `.price__ev` (:136) `transition: opacity var(--dur-fast) var(--ease-standard);`.
- 20: delete `.book-chips` and `.book-chip__price` (:904–905); `grep -rn "book-chip" src` is empty after.

**Tests:**
- `StylesheetOwnershipTests`: remove `[InlineData(".wstep")]` from `A_window_s_selector_lives_in_the_app_sheet` (keep `.wcard`). Add `A_desk_component_s_selector_lives_in_a_desk_sheet` (`[Theory]` over `.wsteps {`, `.wstep__dot`, `.wstep__title`, `.wstep__change`, `.wstep__hint`, `.tabs__panel`, `.deskgrid__child`): the selector is in `Desk("controls.css") + Desk("desk.css")` and **not** in `App()`. Rewrite the class summary's first sentence to name controls.css as holding the components' own rules.
- `DeskCssConventionTests.No_literal_colour_outside_the_token_blocks`: also `Assert.DoesNotMatch(LiteralColour, WithoutComments(Css("controls.css")))`. Add `Targets_are_at_least_28px_in_the_app_sheet`: no `height:` or `min-height:` below 24px on a selector containing `__trigger`, `__opt` or `--take` in `AppCss()` (regex over rule blocks; a deliberately small decorative rule may be listed in an allow-list with a reason).

- [ ] **Step 1: Failing tests** — the new ownership theory (fails: rules are in the app sheet), the controls.css colour assertion (passes — it guards), the targets test (fails on 22px and 20px).
- [ ] **Step 2: Move the rules and make the six fixes.** Comments in Desk sheets pass `DeskVocabularyTests` (it reads `.css` comments).
- [ ] **Step 3: Desk.Tests green; Web.Tests green.** Browser at 1280×800, dark and light: a wager window (steps rail, capsules fill-only, receipt), Journal tabs, Board open row wash, book picker at 28px.
- [ ] **Step 4: Commit** — `fix(desk): the step, tab and child-row styles ship with the components that draw them`.

### Task 2: `DeskPanelBase` and `PanelLoadFailure`

**Files:**
- Create: `src/LineOps.Desk/Windowing/DeskPanelBase.cs`, `src/LineOps.Desk/Primitives/PanelLoadFailure.razor`
- Modify: `src/LineOps.Web/Components/Panels/PanelBase.cs`
- Delete: `src/LineOps.Web/Components/Panels/LoadFailure.razor`
- Modify (tag rename only, `<LoadFailure ` → `<PanelLoadFailure `): `src/LineOps.Web/Components/Panels/{BoardAllBets,BoardForm,BoardPanel,GamePanel,HeadToHeadPanel,HistoryPanel,IncidentsPanel,JournalPanel,OddsPanel,OpsPanel,PerformancePanel,PlayerPanel,PlayersPanel,RunsPanel,TeamPanel}.razor` (16 sites; `grep -rn "<LoadFailure" src` empty after)
- Modify: `tests/LineOps.Web.Tests/SubjectLoadStateTests.cs` (:34 and :40 look for `<PanelLoadFailure`)
- Create: `tests/LineOps.Desk.Tests/DeskPanelBaseTests.cs`, `tests/LineOps.Desk.Tests/PanelLoadFailureTests.cs`

**Interfaces produced:**
- `namespace LineOps.Desk.Windowing; public abstract class DeskPanelBase : ComponentBase, IDisposable` — `PanelBase.cs:26–289` moved verbatim, with: `Settle` becomes `protected virtual TimeSpan RefreshSettle => TimeSpan.FromSeconds(1);` (so a test bench can pass `TimeSpan.Zero`); `Watches` doc says "the topics this panel draws from, as published to `DeskSignals`"; `LoadSubject` default `"This window"`, doc example `"The list", "The runs"`. Members kept with their access: `WindowId`, `HostWindowId` (cascading, by name), `Manager`, `Log`, `LoadSubject`, `LoadError`, `Failed`, `Loaded`, `Watches`, `RefreshAsync`, `OnDispose`, `SetParametersAsync`, `RequestRefresh`, `Dispose`, `Report`, `MarkUnsaved`, `WithActivityAsync`, `RetryAsync`. Doc comments are rewritten generically ("this journal entry" → "this draft").
- `public abstract class PanelBase : DeskPanelBase` keeps `PanelBase.cs:291–386` unchanged and its own summary (sports helpers for this app's windows).
- `PanelLoadFailure`: `[Parameter, EditorRequired] string? Message; [Parameter, EditorRequired] EventCallback OnRetry; string RetryLabel = "Try again"; string? Class; Extra`. Markup as `LoadFailure.razor:12–19` today (`EmptyState Kind=Error`, `DeskButton Size=Small Icon=Refresh`).

- [ ] **Step 1: Failing tests** — `DeskPanelBaseTests` with a bench `sealed class Bench : DeskPanelBase` (C# `BuildRenderTree`, `RefreshSettle => TimeSpan.Zero`, exposes `Load(Func<Task>)`): `A_failed_load_names_what_did_not_load` (`LoadError == "This window did not load."`, pulse Critical on the cascaded window), `Retry_repeats_the_last_load`, `An_older_load_landing_late_does_not_overwrite_the_newer`, `A_watched_topic_refreshes` (`DeskSignals.Publish({"x"})` → `RefreshAsync` once; `{"y"}` → none), `Unsaved_input_marks_the_host_and_clears_on_disposal` (cascade `HostWindowId`), `Report_is_a_no_op_outside_a_window`. `PanelLoadFailureTests`: `Shows_the_message_as_an_error_empty_state`, `Try_again_invokes_retry`, `The_key_s_label_can_be_named`. All in `DeskTestContext` with `FakeWindowCatalog`.
- [ ] **Step 2: Run; fail (types missing).**
- [ ] **Step 3: Move the code; make `PanelBase` derive; delete `LoadFailure.razor`; sweep the 16 tags; update `SubjectLoadStateTests`.**
- [ ] **Step 4: Both test projects green** (`PanelLoadStateTests`, `UnsavedInputTests`, `SubjectLoadStateTests` unchanged in behaviour).
- [ ] **Step 5: Commit** — `refactor(desk): a window's load, retry and refresh plumbing is the Desk's DeskPanelBase`.

### Task 3: `LineOps.Desk.Hosting` — the access gate and the edge

**Files:**
- Create: `src/LineOps.Desk.Hosting/LineOps.Desk.Hosting.csproj`, `README.md`, `DeskAccessOptions.cs` (with `DeskAccessMode`, `DeskAccessRoles`), `DeskAccessCode.cs`, `DeskCircuitAccess.cs`, `DeskAccessEndpoints.cs`, `DeskSignInPage.cs`, `DeskEdge.cs` (with `DeskEdgeOptions`)
- Create: `tests/LineOps.Desk.Hosting.Tests/LineOps.Desk.Hosting.Tests.csproj`, `DeskAccessCodeTests.cs`, `DeskAccessRegistrationTests.cs`, `DeskSignInPageTests.cs`, `DeskEdgeTests.cs`, `ProjectShapeTests.cs`, `Support/Repo.cs` (copy of the Desk tests' helper)
- Delete: `src/LineOps.Web/Access/AccessCode.cs`, `AccessEndpoints.cs`, `AccessOptions.cs`, `CircuitDeskAccess.cs`; `src/LineOps.Web/Hosting/EdgeHardening.cs`
- Modify: `LineOps.slnx` (both projects in their folders), `src/LineOps.Web/LineOps.Web.csproj` (ProjectReference), `src/LineOps.Web/Program.cs`, `src/LineOps.Web/Dockerfile` (a `COPY src/LineOps.Desk.Hosting/LineOps.Desk.Hosting.csproj src/LineOps.Desk.Hosting/` line after the Desk's, line 10), `scripts/new-access-code.ps1` (doc comment line 11: "The hash is the format `LineOps.Desk.Hosting.DeskAccessCode` checks"), `tests/LineOps.Web.Tests/DeskAccessTests.cs` (delete the gate cases, lines ~115–184, the `NobodySignedIn` class and `using LineOps.Web.Access`), `tests/LineOps.Desk.Tests/DeskVocabularyTests.cs`

**The project:** `Microsoft.NET.Sdk`, `net10.0`, `Nullable`/`ImplicitUsings` on, `RootNamespace LineOps.Desk.Hosting`, `FrameworkReference Microsoft.AspNetCore.App`, `ProjectReference ..\LineOps.Desk\LineOps.Desk.csproj`; packable: `IsPackable`, `PackageId=LineOps.Desk.Hosting`, `Version=0.1.0`, `Description` ("The access gate and edge headers for a Desk on ASP.NET Core."), `GenerateDocumentationFile`, `NoWarn CS1591`, `PackageReadmeFile README.md` packed as in `LineOps.Desk.csproj`. A csproj comment says why it is separate from the Desk (pipeline concerns stay out of the component library; ADR 0018/0019).

**Interfaces produced (moved code, renamed; behaviour unchanged):**
- `DeskAccessOptions`: `const string SectionName = "Access"`; `Mode` (`DeskAccessMode.Open|Gated`), `AllowAnonymousVisitors`, `OperatorCodeHash`, `GuestCodeHash`, `SessionLifetime = 14 days`, **new** `CookieName = "desk.access"`, `SignInPath = "/signin"`, `SignOutPath = "/signout"`. `DeskAccessRoles.Operator/Visitor`.
- `DeskAccessCode.Hash/Matches/IsWellFormed` — `AccessCode.cs` verbatim.
- `DeskCircuitAccess(AuthenticationStateProvider, IOptions<DeskAccessOptions>) : IDeskAccess` — paths now from options.
- `DeskAccessEndpoints`: `IServiceCollection AddDeskAccess(this IServiceCollection, IConfiguration, Action<DeskAccessOptions>? configure = null)` (binds `Access`, then `configure`; open → registers only the options; gated → validates hashes with the same messages, adds cookie auth using `CookieName`/paths/lifetime, fallback policy unless anonymous visitors, cascading auth state, scoped `IDeskAccess = DeskCircuitAccess`, the `"desk-signin"` fixed-window limiter 5/min/IP); `bool IsDeskGated(this IConfiguration)`; `WebApplication UseDeskAccess(this WebApplication)` (gated → `UseAuthentication`, `UseAuthorization`, `UseRateLimiter`); `IEndpointRouteBuilder MapDeskAccess(this IEndpointRouteBuilder)` (gated → the three endpoints as `AccessEndpoints.cs:93–137`, logger category `"LineOps.Desk.Hosting.Access"`; open → nothing).
- `DeskSignInPage.Render(DeskBrand brand, string formFieldName, string requestToken, string action, bool failed) : string` — the HTML of `AccessEndpoints.cs:147–195`, everything HTML-encoded as now.
- `DeskEdgeOptions`: `string ContentSecurityPolicy = DeskEdge.DefaultContentSecurityPolicy` (today's string, `EdgeHardening.cs:80–90`), `IList<string> ExtraImageSources` (appended to `img-src`). `DeskEdge`: `bool IsBehindProxy(this IConfiguration)` (`Hosting:BehindProxy`), `AddDeskEdge(this IServiceCollection, IConfiguration, Action<DeskEdgeOptions>? configure = null)` (forwarded headers as today when behind a proxy; registers the options always), `UseDeskEdge(this WebApplication)`, `IReadOnlyDictionary<string,string> Headers(DeskEdgeOptions)` (the six headers of `EdgeHardening.cs:58–63`, CSP composed).
- **Program.cs:** `using LineOps.Desk.Hosting;` replaces `LineOps.Web.Access`/`LineOps.Web.Hosting`; line 58 → `builder.Services.AddDeskAccess(builder.Configuration, o => o.CookieName = "lineops.access");` (comment: keeps every existing sign-in); 59 → `AddDeskEdge`; 86 → `UseDeskEdge`; 98 → `IsBehindProxy()`; 101–107 → `app.UseDeskAccess();`; 111–112 → `app.MapDeskAccess();`. Order of the pipeline is unchanged.
- **DeskVocabularyTests:** both facts become `[Theory] [InlineData("LineOps.Desk")] [InlineData("LineOps.Desk.Hosting")]` over `Path.Combine(Repo.Root(), "src", project)`; `Words` gains `"espn", "mlb", "nfl", "ufc"`.

- [ ] **Step 1: Failing tests** (new project; create the csproj and test files first) — `DeskAccessCodeTests`: the four `AccessCode` cases from `DeskAccessTests.cs:117–155` verbatim (including the script-made hash). `DeskAccessRegistrationTests`: `Open_registers_no_gate` (no `IAuthenticationService`), `Gated_without_an_operator_hash_refuses_to_start` (message names `Access__OperatorCodeHash`), `Gated_registers_the_circuit_s_access`, `The_cookie_takes_its_configured_name` (`IOptionsMonitor<CookieAuthenticationOptions>.Get("Cookies").Cookie.Name == "lineops.access"` after `configure`), `The_circuit_access_names_its_gate` (from `DeskAccessTests.cs:157–178`, paths from options). `DeskSignInPageTests`: `Names_the_brand_and_encodes_it` (`<script>` in a tagline is encoded), `A_failed_attempt_says_so_as_an_alert`, `Posts_to_the_sign_in_path_with_the_token`. `DeskEdgeTests`: `Sends_the_six_headers`, `Extra_image_sources_join_img_src`, `Behind_proxy_reads_its_key`. `ProjectShapeTests.The_hosting_tests_reference_only_hosting`. The two `DeskVocabularyTests` theories.
- [ ] **Step 2: Run; fail.**
- [ ] **Step 3: Move and rename the code; write `README.md` (what it is; the four Program lines; options table; that the hash format is shared with any script that makes one); wire Program; delete the old files; trim `DeskAccessTests`; Dockerfile; script comment; slnx.**
- [ ] **Step 4: All four test projects green; `dotnet pack src/LineOps.Desk.Hosting -o $env:TEMP\lineops-e-pack`; the web host starts on port 5271 open, and again with `Access__Mode=Gated` and a hash from `scripts/new-access-code.ps1 -Code test-code-1` (sign-in page renders; `test-code-1` signs in; the cookie is `lineops.access`).**
- [ ] **Step 5: Commit** — `feat(hosting): the access gate and edge headers are a package of their own, LineOps.Desk.Hosting`.

### Task 4: `DeskTime` beside `DeskClock`

**Files:**
- Create: `src/LineOps.Desk/DeskTime.cs`, `tests/LineOps.Desk.Tests/DeskTimeTests.cs`
- Modify: `src/LineOps.Web/Components/DisplayTime.cs`

**Interfaces produced:**
- `namespace LineOps.Desk; public static class DeskTime` — extension methods on `DeskClock`: `string Stamp(this DeskClock clock, DateTimeOffset at)` → `$"{clock.Format(at, "MMM d HH:mm")} {clock.Label}"`; `string Day(this DeskClock clock, DateTimeOffset at)` → `"MMM d"`; `string Format(this DeskClock clock, DateTimeOffset at, string format)` → `clock.At(at).ToString(format)`. Doc: the formats the desk shows a moment in; a reading that names a clock time carries the label, a bare day does not.
- `DisplayTime`: add `public static readonly DeskClock Clock = new(LeagueClock.Zone, Label);`; `Stamp`/`Day`/`Format` delegate (`=> Clock.Stamp(at)` etc.). Public API and every panel unchanged; `Program.cs:30` unchanged (Task 3 owns Program in this wave).

- [ ] **Step 1: Failing tests** — `DeskTimeTests`: `A_stamp_names_its_zone` (a fixed clock: `new DeskClock(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "TST"), "TST")`): `2026-09-23T22:40Z` → `"Sep 23 17:40 TST"`; `A_day_is_bare`; `Format_uses_the_clock_s_zone`; `Utc_says_so` (`DeskClock.Utc.Stamp` ends " UTC").
- [ ] **Step 2: Run; fail. Step 3: Implement; delegate `DisplayTime`. Step 4: `DisplayTimeTests` (Web) and `DeskTimeTests` green.**
- [ ] **Step 5: Commit** — `feat(desk): DeskTime formats a moment in the desk's zone, and DisplayTime is LineOps's zone for it`.

---

## Wave 2 (four tasks)

### Task 5: `KeyRow` and `InlineConfirm` (owner of controls.css, PartsPanel.razor, README.md, PartsPanelTests.cs this wave)

**Files:**
- Create: `src/LineOps.Desk/Primitives/KeyRow.razor`, `InlineConfirm.razor`; `tests/LineOps.Desk.Tests/KeyRowTests.cs`, `InlineConfirmTests.cs`
- Modify: `src/LineOps.Desk/wwwroot/css/controls.css` (sections "Key row", "Inline confirm"; then the controller merges `scratch/task7-controls.css`)
- Modify: `src/LineOps.Desk/Windowing/PartsPanel.razor` (sections "Key row", "Inline confirm", "Load failure"; controller merges `scratch/task6-parts.razor`, `scratch/task7-parts.razor`), `src/LineOps.Desk/README.md` (rows `InlineConfirm`, `KeyRow`, `PanelLoadFailure`; controller merges `scratch/task6-readme.md`, `scratch/task7-readme.md`, keeping alphabetical order), `tests/LineOps.Desk.Tests/PartsPanelTests.cs` (add `"KeyRow"`, `"InlineConfirm"`, `"PanelLoadFailure"` to `NamedForWhatTheyAre`)

**Interfaces produced:**
- `KeyRow`: `[Parameter] RenderFragment? Secondary` (Plain/Tinted keys, Cancel first); `[Parameter] string? Reason` (why the primary waits, or what it does; null → no sentence); `[Parameter] RenderFragment<string?>? Primary` (the one Filled key; the context is the reason's element id, or null when there is no reason — the caller writes `aria-describedby="@context"` on its `DeskButton`); `[Parameter] DeskJustify Justify = DeskJustify.End`; `Class`; `Extra`. Markup: `<div class="keyrow keyrow--{start|end}" role="group">@Secondary @if (Reason) {<span class="keyrow__reason" id="@_reasonId">@Reason</span>} @Primary?.Invoke(Reason is null ? null : _reasonId)</div>`; `_reasonId = "keyrow-" + Guid.NewGuid().ToString("N")[..10]`, fixed for the instance's life. Header comment: order is secondary, reason, primary (the HIG's one Filled key, last); a disabled key's `title` never shows because a disabled MudButton takes no pointer events, so the reason is text.
- `InlineConfirm`: `[Parameter, EditorRequired] RenderFragment ChildContent` (the question, a sentence ending "?"); `[Parameter, EditorRequired] string ConfirmLabel` (a verb); `string? ConfirmIcon`; `DeskEmphasis ConfirmEmphasis = DeskEmphasis.Tinted`; `bool Destructive`; `bool Busy`; `EventCallback OnConfirm`; `EventCallback OnCancel`; `string CancelLabel = "Cancel"`; `Class`; `Extra`. Markup: `<div class="confirm" role="group" aria-labelledby="@_askId" @onkeydown="Escape → OnCancel"><span class="confirm__ask" id="@_askId">…</span><span class="confirm__keys"><DeskButton Size=Small OnClick=OnCancel>Cancel</DeskButton><DeskButton Size=Small Emphasis=ConfirmEmphasis Role="@(Destructive ? DeskRole.Destructive : DeskRole.Normal)" Icon Busy OnClick=OnConfirm>@ConfirmLabel</DeskButton></span></div>`.
- CSS: `.keyrow { display:flex; flex-wrap:wrap; align-items:center; gap: var(--space-2); }`, `.keyrow--end { justify-content:flex-end; }`, `.keyrow__reason { font-size: var(--text-subheadline); line-height: var(--leading-body); color: var(--text-secondary); max-width: 52ch; }`; `.confirm { display:flex; flex-wrap:wrap; align-items:center; gap: var(--space-2) var(--space-3); }`, `.confirm__ask { flex: 1 1 24ch; font-size: var(--text-body); color: var(--text-primary); }`, `.confirm__keys { display:flex; gap: var(--space-2); margin-left:auto; }`. Tokens only.

- [ ] **Step 1: Failing tests** — `KeyRowTests`: `Secondary_then_reason_then_primary` (DOM order), `The_reason_describes_the_primary` (context == the span's id), `No_reason_no_sentence_and_no_id` (context null), `Two_rows_never_share_an_id`, `Justify_start_and_end`. `InlineConfirmTests`: `Cancel_comes_before_the_confirm`, `Is_a_group_named_by_its_question`, `Confirm_and_cancel_invoke`, `Escape_cancels`, `A_destructive_confirm_is_marked` (`.desk-btn--destructive`). `PartsPanelTests` fail until the sections and rows exist.
- [ ] **Step 2: Run; fail. Step 3: Write both primitives and CSS; Parts bin sections in the file's voice (a paragraph of why, then the states: KeyRow with and without a reason, Justify start; InlineConfirm plain and destructive; PanelLoadFailure with a sentence); README rows.**
- [ ] **Step 4: After the controller merges Tasks 6–7 fragments, Desk.Tests green.**
- [ ] **Step 5: Commit** — `feat(desk): a key row that says why its key waits, and an inline confirm with Cancel first`.

### Task 6: `DeskJobMenu` over `IDeskJobs`; the pull menu is LineOps's jobs

**Files:**
- Create: `src/LineOps.Desk/DeskJobs.cs` (`IDeskJobs`, `DeskJob`, `DeskJobResult`, `DeskJobOutcome`, `DeskJobRunner`), `src/LineOps.Desk/Primitives/DeskJobMenu.razor`, `src/LineOps.Web/Services/IngestionDeskJobs.cs`, `tests/LineOps.Desk.Tests/DeskJobMenuTests.cs`
- Modify: `src/LineOps.Desk/DeskHost.cs` (`services.AddScoped<DeskJobRunner>();` in `AddDesk`), `src/LineOps.Web/Program.cs` (`AddScoped<IDeskJobs, IngestionDeskJobs>()` beside `IDeskSearch`, line 37), `src/LineOps.Web/Components/Panels/BoardPanel.razor` (:99 `<PullMenu …>` → `<DeskJobMenu @ref="_pull" Label="Pull data" Icon="@Icons.Material.Filled.CloudDownload" OnRan="LoadAsync" OnBusyChanged="OnPullBusy" />`; :528 field type `DeskJobMenu?`; and HIG 12 at :385 — wrap the `.value` div's content in `<DeskTooltip Text="@ValueTitle(context.Item, pick)">` and drop its `title`), `src/LineOps.Web/Components/Snippets/FighterView.razor` (:25–28) and `TaleOfTheTape.razor` (:44) (HIG 16), `src/LineOps.Web/Components/Panels/BoardForm.razor` (:45–48, HIG 16), `tests/LineOps.Web.Tests/BoardToolbarTests.cs` (:83 `"PullMenu"` → `"DeskJobMenu"`)
- Delete: `src/LineOps.Web/Components/Desk/PullMenu.razor`
- Scratch: `scratch/task6-parts.razor` (a "Job menu" section with an inline demo `IDeskJobs` in the Parts bin's `@code`: two available jobs, one unavailable with its reason, one that returns `Nothing`), `scratch/task6-readme.md` (`| \`DeskJobMenu\` | A key that opens a menu of named runs, each saying what it costs before it is chosen. | — |`)

**Interfaces produced:**
- `public sealed record DeskJob(string Key, string Label, string Description, string? Cost, bool Available, string? Unavailable);`
- `public enum DeskJobResult { Done, Nothing, Failed }`; `public sealed record DeskJobOutcome(DeskJobResult Result, string Summary);`
- `public interface IDeskJobs { Task<IReadOnlyList<DeskJob>> DescribeAsync(CancellationToken ct = default); Task<DeskJobOutcome> RunAsync(string key, CancellationToken ct = default); }`
- `public sealed class DeskJobRunner(IServiceProvider services, DeskToasts toasts, IDeskAccess access, ILogger<DeskJobRunner> log)`: `IDeskJobs? Jobs` (= `services.GetService<IDeskJobs>()`); `Task<bool> RunAsync(IDeskJobs jobs, DeskJob job)` (no-op false when `!access.CanOperate`; `Done` → `toasts.Success(summary)`, `Nothing` → `Info`, `Failed` → `Warn`; an exception is logged and `toasts.Fail($"{job.Label} did not finish. Try again.")`); `Task<bool> RunAsync(string key)` (re-describes, runs if available, false otherwise).
- `DeskJobMenu`: `[Parameter, EditorRequired] string Label`; `string? Icon`; `IDeskJobs? Jobs` (null → `Runner.Jobs`); `DeskPopoverAlign Align = End`; `EventCallback OnRan`; `EventCallback<bool> OnBusyChanged`; public `Task RunAsync(string key)` (unavailable or unknown → opens the menu, as `PullMenu.razor:99–122`). Renders nothing when `!Access.CanOperate`. Re-describes on open. Behaviour and comments as `PullMenu.razor`, made generic (no "ingestion", "credits" or "odds"; "a run", "what it costs").
- `IngestionDeskJobs(IngestionJobs jobs) : IDeskJobs` — maps `IngestionJob` → `DeskJob` with `Cost` = `PullMenu.Cost` (:79–82) and `JobOutcome` → `DeskJobOutcome` (Failed when `!Succeeded`, with `Detail ?? "{label}: {failures} source failed."`; Nothing when `Rows == 0`, `"{label}: nothing new to store."`; else Done, `Detail ?? "{label}: {rows:N0} rows."`). Needs the job's label for the sentence: look it up from the last `DescribeAsync` or re-describe.
- HIG 16: FighterView and TaleOfTheTape — when the profile is missing and `Access.CanOperate`, the sentence ends at "No profile yet." / "… has no profile yet." and a `DeskButton Size=Small Emphasis=Tinted Icon=PersonSearch` "Read fighter profiles" follows, calling `Runner.RunAsync(IngestionJobs.EspnFighters)` then reloading; for a visitor the sentence says the profile is read by the operator's pull. BoardForm: an `Action` with `DeskButton Size=Small Emphasis=Tinted Icon=History OnClick="() => Manager.OpenHistory()"` "Open History", as `OddsPanel.razor:195–200`, and the sentence drops "open the History window to backfill it".

- [ ] **Step 1: Failing tests** — `DeskJobMenuTests` (a `FakeJobs : IDeskJobs` in the test file): `Lists_each_run_with_its_cost`, `An_unavailable_run_is_dimmed_with_its_reason`, `Each_result_toasts_its_own_way` (`[Theory]` Done/Nothing/Failed, plus a throwing run → the Fail sentence; read `ISnackbar.ShownSnackbars`), `RunAsync_on_an_unavailable_key_opens_the_menu`, `A_visitor_sees_no_menu`, `Opening_rereads_the_runs`, `OnRan_and_OnBusyChanged_fire`. Web: `BoardToolbarTests` with the new name.
- [ ] **Step 2: Run; fail. Step 3: Implement; delete `PullMenu.razor`; wire Program and BoardPanel; the three HIG 16 sites and BoardPanel:385; write the scratch fragments.**
- [ ] **Step 4: Both test projects green; the Board's Pull data menu opens, lists costs, and a pull toasts, in the browser.**
- [ ] **Step 5: Commit** — `feat(desk): a job menu over IDeskJobs, and the Board's pull menu is this app's jobs on it`.

### Task 7: `DeskErrorPage` and `DeskNotFound`

**Files:**
- Create: `src/LineOps.Desk/Primitives/DeskErrorPage.razor`, `DeskNotFound.razor`; `tests/LineOps.Desk.Tests/DeskErrorPageTests.cs`
- Modify: `src/LineOps.Web/Components/Pages/Error.razor`, `NotFound.razor`
- Scratch: `scratch/task7-controls.css` ("Pages": `.deskpage { min-height: 60vh; display: grid; place-items: center; padding: var(--space-8) var(--space-4); }`, `.deskpage__title { margin: 0 0 var(--space-2); font-size: var(--text-title2); font-weight: var(--weight-semibold); color: var(--text-primary); }`, `.deskpage__ref { font-size: var(--text-footnote); color: var(--text-tertiary); }`), `scratch/task7-parts.razor` (a "Pages" section showing both inside a bordered-free frame of fixed height), `scratch/task7-readme.md` (two rows)

**Interfaces produced:**
- `DeskErrorPage`: `string Title = "Something went wrong"`; `RenderFragment? ChildContent` (default "The desk could not finish that request. Going back to it usually works."); `string? RequestId` (shown as "Reference <code>…</code>" in `.deskpage__ref` when set); `string HomeHref = "/"`; `string HomeLabel = "Back to the desk"`; `Class`; `Extra`. Markup: `<main class="deskpage"><EmptyState Kind="EmptyStateKind.Error"><ChildContent><h1 class="deskpage__title">…</h1>…</ChildContent><Action><DeskButton Emphasis=Tinted OnClick="() => Nav.NavigateTo(HomeHref, forceLoad: true)">@HomeLabel</DeskButton></Action></EmptyState></main>`.
- `DeskNotFound`: same shape, `Kind=Filtered`, `Title = "Nothing here"`, default sentence "That address does not open anything on this desk."
- `Error.razor`: keeps `@page "/Error"`, the `HttpContext` cascade and `RequestId` logic; body becomes `<PageTitle>Error</PageTitle><DeskErrorPage RequestId="@RequestId" />` — the Development-mode paragraphs go. `NotFound.razor`: `<PageTitle>Not found</PageTitle><DeskNotFound />`, keeps `@page "/not-found"` and `@layout MainLayout`.

- [ ] **Step 1: Failing tests** — `Error_page_says_what_happened_and_offers_the_way_back` (h1 text, `.empty--error` or the EmptyState error class, a key named "Back to the desk"), `The_reference_shows_only_when_known`, `Not_found_names_no_such_address`, `The_way_back_goes_home` (bUnit `FakeNavigationManager.Uri` ends "/").
- [ ] **Step 2: Run; fail. Step 3: Implement both; replace the two pages; write the scratch fragments.** **Step 4: Desk.Tests green (after the controller's merge); `/not-found` renders in the browser.**
- [ ] **Step 5: Commit** — `feat(desk): an error page and a not-found page of the Desk's own, and the stock template pages go`.

### Task 8: HIG razor quick fixes outside the key-row windows (owner of lineops.css this wave)

**Files:**
- Modify: `src/LineOps.Web/Components/Desk/PriceCell.razor`, `src/LineOps.Web/Components/Panels/GamePanel.razor`, `src/LineOps.Web/Components/Snippets/{FormSnippet,HeadToHeadSnippet,OddsSnippet,PlayerRecentSnippet}.razor`, `src/LineOps.Web/Components/Panels/{TeamPanel,PlayerPanel,HeadToHeadPanel,RunsPanel}.razor`, `src/LineOps.Web/Components/Panels/PanelBase.cs` (`Probables` only), `src/LineOps.Web/wwwroot/css/lineops.css`
- Modify tests: `tests/LineOps.Web.Tests/PriceCellTests.cs`, `tests/LineOps.Web.Tests/DeskAccessTests.cs` (`A_live_price_is_only_a_reading_on_a_visitor_desk`: no `button.price`), any test asserting the changed copy (grep "ATS ✗", "ATS P", `" v "`, "best of", "Started (ET)", "Reading recent")
- Create: `tests/LineOps.Web.Tests/HigQuickFixTests.cs` (source scans for 4 and 5; render tests for 2)

**Changes:**
- 2: `PriceCell.razor:99–105` — when `Takeable`, the cell is `<button type="button" class="price price--take …" title=… @onclick=ClickAsync @onclick:stopPropagation>`; otherwise the `div` as now. One `RenderFragment` holds the inside so both share it; the inner `div.price__top` and `div.rail` become `span`s (their classes already set `display`; add `display: flex`/`block` in `lineops.css` if a rule relied on the element). `KeyAsync`'s Enter/Space handling goes for the button (native). `GamePanel.razor:478–482` — `LineCell` likewise: `<button type="button" class="gamelines__cell gamelines__cell--take">` when `CanTake(offer)`, `div` otherwise; the `@onkeydown` lambda goes. CSS (`lineops.css`, beside `.price--take:focus-visible` ~1320): `button.price, button.gamelines__cell { appearance: none; border: 0; margin: 0; background: none; font: inherit; color: inherit; text-align: inherit; width: 100%; display: block; }` (adjust `display` to what `.price`/`.gamelines__cell` already declare).
- 4: `FormSnippet.razor:118, 124, 132` — drop the inline `style`, add `snippet__note` to the class (the Desk's `.snippet__note`, `desk.css:3348`).
- 5: the loading `EmptyState Style="padding:6px 0"` at `FormSnippet:20`, `HeadToHeadSnippet:22`, `OddsSnippet:20`, `PlayerRecentSnippet:20` → `<DeskSkeleton Lines="3" />` (`Lines="2"` for OddsSnippet), matching the loaded content's rows.
- 7: `TeamPanel.razor:66` and `PlayerPanel.razor:72` — when `_seasonOptions.Count > 5`, a `DeskSelect TValue="int" Label="Season"` of the same options (newest first) in place of the `DeskSwitch`; five or fewer keeps the switch.
- 21: `TeamPanel.razor:141` → `"Covered"`/`"Missed"`/`"Push"`; `HeadToHeadPanel.razor:142` and `HeadToHeadSnippet.razor:61` `"No"` → `"Missed"`, `"Cover"` → `"Covered"`; `HeadToHeadPanel.razor:61` and `GamePanel.razor:94` `" v "` → `" vs "`, and `PanelBase.Probables` (`PanelBase.cs:324`) likewise; `OddsSnippet.razor:43` "best of" → "Best of"; `RunsPanel.razor:84` `Title="@($"Started ({DisplayTime.Label})")"`.

- [ ] **Step 1: Failing tests** — `PriceCellTests`: `A_takeable_price_is_a_button` (`button[type=button].price`, its `aria-label` kept), `Pressing_it_takes_the_price` (`Click()` on the button invokes `OnTake` once), `A_closing_price_is_not_a_button`. `HigQuickFixTests`: `No_snippet_sizes_type_inline` (no `font-size:` in a `style=` under `Components/Snippets`), `No_snippet_loads_with_a_sentence` (no `EmptyState Style="padding:6px 0">Reading`). `DeskAccessTests` visitor case.
- [ ] **Step 2: Run; fail. Step 3: Make the changes.** **Step 4: Web.Tests green; in the browser, Tab to a Board price and press Space — the wager window opens and the Board does not scroll; Team window with a long history shows the select.**
- [ ] **Step 5: Commit** — `fix(desk): a takeable price is a button, snippets load in their shape, and the copy says what it means`.

**Controller, end of wave 2:** merge the scratch fragments (Task 6 → PartsPanel/README; Task 7 → controls.css/PartsPanel/README), run Desk.Tests and Web.Tests, then commit Tasks 5–7 in order.

---

## Wave 3 (two tasks)

### Task 9: Key rows and confirms in Ops, History, Incidents, Performance (owner of lineops.css this wave)

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/{OpsPanel,HistoryPanel,IncidentsPanel,PerformancePanel,PlayersPanel}.razor`, `src/LineOps.Web/wwwroot/css/lineops.css`
- Create: `tests/LineOps.Web.Tests/KeyRowAdoptionTests.cs`

**Sites (re-grep; HIG 1 and 3):**
- `OpsPanel.razor:63–69` — `<KeyRow Justify="DeskJustify.Start" Reason="Rolls today's figures up and runs the alert rules again."><Primary Context="why"><DeskButton Emphasis=Filled … aria-describedby="@why">Evaluate now</DeskButton></Primary></KeyRow>` inside the existing `.opsact` (or replacing it).
- `OpsPanel.razor:131–143` — `<InlineConfirm Class="wgroup__row" ConfirmLabel="Scan automatically" ConfirmIcon="@Icons.Material.Filled.Schedule" OnConfirm="() => SetPollingAsync(automatic: true)" OnCancel="() => _confirmAutomatic = false">Scan at this price until switched back, from this host and the worker alike?</InlineConfirm>`.
- `HistoryPanel.razor:151–158` (Gather history) — `KeyRow` with `Reason="@(eligible ? null : "No unmetered provider is available to walk — see Sources above.")"`; the `Title` goes. `:169–181` → `InlineConfirm ConfirmLabel="Re-walk" ConfirmIcon=Restore` with the sentence as `ChildContent` (keeps its `<span class="num">`). `:186–192` (Re-walk…) → `KeyRow Reason` "No day is held yet, so there is nothing to walk again." when `HeldDays == 0` (Title gone; the enabled-state title "Walk every held day again." goes too — the label says it). `:228–235` (range walk) → `KeyRow Reason` from `RangeAsked is null`.
- `IncidentsPanel.razor:211–216` (Add note) — `KeyRow Reason="@(string.IsNullOrWhiteSpace(_newNote) ? "Type the note first." : null)"`, the Add key as `Primary` (Plain emphasis as now). `:304–308` (Resolve) — `KeyRow Reason="@(CanResolve ? null : ResolveHint)"`, Title gone.
- `PerformancePanel.razor:159–163` (Set) — `KeyRow Reason="@(_bankrollInput == _startingBankroll ? "Type a different figure to set it." : null)"`, Title gone.
- HIG 22: `OpsPanel.razor:233` `<p class="opsnone">` → `<EmptyState>No open alerts. Every source is inside its SLO.</EmptyState>`; `IncidentsPanel.razor:189` → `<EmptyState Kind="EmptyStateKind.New">Nothing on the timeline yet.</EmptyState>`; `PlayersPanel.razor:112–114` `<p class="empty">` → `<Note>Showing the first @Limit by name. Narrow the search, or pick a sport, to see the rest.</Note>`.
- HIG 21: `OpsPanel.razor:342` "Run ingest &amp; evaluate" → "Run the drill".
- HIG 12: `HistoryPanel.razor:343` " · N pp" → " · N postponed", `title` dropped.
- CSS: delete rules left with no user (`.opsconfirm*`, `.opsact__why`, `.opsnone`, `p.empty` if unused — grep `src/LineOps.Web` first); add nothing the Desk's `.keyrow`/`.confirm` already give.

- [ ] **Step 1: Failing tests** — `KeyRowAdoptionTests` (source scans of the four panels): `No_disabled_key_explains_itself_only_in_a_title` (no `DeskButton` with both `Disabled=` and `Title=` in the files), `Every_inline_confirm_is_the_primitive` (no `opsconfirm`, `_confirmRewalk` drives an `<InlineConfirm`). Where a panel already has a render bench (`PerformancePanelTests`), add `The_set_key_says_why_it_waits` (the reason's text is in the DOM and the key's `aria-describedby` names it).
- [ ] **Step 2: Run; fail. Step 3: Adopt at every site; HIG 12/21/22; CSS cleanup.** **Step 4: Web.Tests green; the four windows in the browser, dark and light.**
- [ ] **Step 5: Commit** — `fix(desk): Ops, History, Incidents and Performance say why a key waits, and confirm with Cancel first`.

### Task 10: Key rows in the wager window, the Slip and the Journal

**Files:**
- Modify: `src/LineOps.Web/Components/Panels/{BoardWager,SlipPanel,JournalPanel}.razor`
- Scratch: `scratch/task10-lineops.css` — the replacement and deletion list the controller applies to `lineops.css` after Task 9: replace `.waction__keys` contexts with `.keyrow` (`.slip .keyrow` for :827–828, `.waction__keys > .waction__lead` :897, `.wsteps .keyrow { margin-top: var(--space-2); }` for :1231–1232, `.wsteps .keyrow__reason { text-align: right; }` for :1233, `.keyrow .desk-btn--plain .slip-badge` for :1235, the 480px stack :1476–1478 as `.wcard .keyrow { flex-direction: column; align-items: stretch; } .wcard .keyrow > .desk-btn--filled { order: -2; } .wcard .keyrow > .keyrow__reason { order: -1; text-align: center; }`); delete `.waction*` and `.wager-why`/`.slip-why` once unused
- Modify tests: `tests/LineOps.Web.Tests/SlipPanelTests.cs`, `JournalPanelTests.cs`, and any wager-window test asserting `.waction`/`.wager-why` (grep)

**Sites:**
- `BoardWager.razor:444–512` — the `.waction` wrapper keeps the warning `Note` and the `wfoot` sentence; `.waction__keys` (:464) → `<KeyRow Reason="@Waiting">` with `Secondary` = Add as leg / Add to slip (as now, conditional) and `Primary` = Add leg (parlay) or Log, each `aria-describedby="@context"`. The `Title`s on the keys that describe (not explain disabling) stay.
- `SlipPanel.razor:237–250` — `<KeyRow Reason="@Waiting">`: `Secondary` = Start over, `Primary` = Log (Filled, `aria-describedby`). The order flips to Start over, reason, Log (HIG 3); the comment above it is rewritten to say so.
- `JournalPanel.razor:134–145` and `:236–246` (Footer) — `KeyRow` with `Secondary` = Cancel, `Reason` = the existing sentence (when invalid), `Primary` = Save changes with `aria-describedby="@context"`; the fixed ids `save-why`/`parlay-save-why` go.
- HIG 14: `JournalPanel.razor:349–356` and `:447–452` — Edit, Duplicate, Delete fold into one `<DeskPullDown Label="Actions" Icon="@Icons.Material.Filled.MoreHoriz" Emphasis="DeskEmphasis.Plain" Size="DeskKeySize.Small" Align="DeskPopoverAlign.End">` of `DeskMenuItem`s "Edit", "Duplicate", and `Destructive="true"` "Delete…" (the alert still confirms); the Settle pull-down stays; two keys per row.
- HIG 17: `SlipPanel.razor:198–202` — `<option value="">Book</option>`; options show `BookNames.Display(book)` with `value="@book"`.
- HIG 12: `JournalPanel.razor:302` "manual" and `SlipPanel.razor:166` "×decimal" — wrap in `<DeskTooltip Text="…">` with the span made `tabindex="0"`; drop `title`.

- [ ] **Step 1: Failing tests** — `SlipPanelTests`: `Start_over_comes_before_log` (DOM order), `The_log_key_names_what_it_waits_for` (`aria-describedby` resolves to the reason's text), `The_book_select_shows_book_names`. `JournalPanelTests`: `Save_names_what_it_waits_for` (no element with id `save-why`; the key's `aria-describedby` resolves), `A_row_has_settle_and_one_actions_menu` (two keys; the menu's Delete is `.menu__item--destructive`). Update tests that find Edit/Delete by title to open the Actions menu first.
- [ ] **Step 2: Run; fail. Step 3: Adopt; HIG 12/14/17; write the scratch CSS.** **Step 4: After the controller's merge, Web.Tests and Desk.Tests green; the wager window at a side width and at 1280×800, the Slip and the Journal in the browser, dark and light.**
- [ ] **Step 5: Commit** — `fix(desk): the wager window, the Slip and the Journal order their keys one way and say why Log waits`.

**Controller, end of wave 3:** apply `scratch/task10-lineops.css` after Task 9's commit, run both suites, commit Task 10.

---

## Wave 4

### Task 11: README, Hosting section, versions, spec pointer

**Files:**
- Modify: `src/LineOps.Desk/README.md` — Wiring gains `IDeskJobs` (optional; the job menu and `DeskJobRunner` read it) and `DeskPanelBase` (a window's base class: what `Watches`, `WithActivityAsync`, `RetryAsync`, `MarkUnsaved` do, with a ten-line example over a generic list); `DeskTime` beside `DeskClock`; the Error/NotFound pages in the wiring snippet; a new "## Hosting" section after Wiring: what `LineOps.Desk.Hosting` is for, the four Program lines (`AddDeskAccess`, `AddDeskEdge`, `UseDeskEdge`, `UseDeskAccess`, `MapDeskAccess`), the options it reads, and a pointer to its README; the Rules table gains "Keys explain themselves" (held by `KeyRowTests` and `KeyRowAdoptionTests`).
- Modify: `src/LineOps.Desk/LineOps.Desk.csproj` — `Version` 0.2.0 → 0.3.0 (new public API: `DeskPanelBase`, `DeskTime`, `IDeskJobs`, five primitives); the `Description` names the job menu and pages.
- Modify: `docs/superpowers/specs/2026-10-26-desk-finalisation-design.md` — a "### Phase E — the shared desk" section after Phase D: two sentences and a pointer to the Phase E spec and this plan; "Verification, whole project" names `dotnet pack` of both packages and the four test projects.
- Modify: `src/LineOps.Desk/Windowing/PartsPanel.razor` only if a section from waves 2–3 needs its states completed (KeyRow with a disabled primary; DeskJobMenu busy).

- [ ] **Step 1:** `PartsPanelTests` and `DeskVocabularyTests` green with the README edits (the README is scanned in full).
- [ ] **Step 2:** `dotnet pack src/LineOps.Desk -o $env:TEMP\lineops-e-pack` and `dotnet pack src/LineOps.Desk.Hosting -o …`; open both `.nupkg`s and confirm the README, the XML docs and (Desk) `staticwebassets` with `controls.css` holding the Steps section.
- [ ] **Step 3: Commit** — `docs(desk): the README shows the panel base, the job menu, the pages and the Hosting package`.

### Task 12: Verification (controller)

- [ ] `dotnet build` clean: `src/LineOps.Web`, `src/LineOps.Worker`, `tests/LineOps.Tests`, `tests/LineOps.Web.Tests`, `tests/LineOps.Desk.Tests`, `tests/LineOps.Desk.Hosting.Tests`.
- [ ] `dotnet test` green on all four test projects; `dotnet run --project src/LineOps.Worker` starts (its `appsettings.json` comment keys unique).
- [ ] `dotnet pack` both packages; `dotnet format --verify-no-changes` in `mcr.microsoft.com/dotnet/sdk:10.0` from the pushed branch.
- [ ] `docker build -f src/LineOps.Web/Dockerfile .` restores and publishes (the Hosting csproj line).
- [ ] Browser pass on port 5271 (scheduler and evaluator off), 1280×800, dark and light: wager window (side and maximised), Slip, Journal (row menu, Save reason), Ops (Evaluate reason, Scan confirm), History (Gather reason, Re-walk confirm), Incidents (Add, Resolve), Performance (Set), Board (Space on a price; Pull data), Team (season select), Parts bin new sections, `/not-found`; gated sign-in with `test-code-1` and the `lineops.access` cookie.
- [ ] `git worktree list` shows one entry; `git branch -a` shows `main` and `LineX_Development` only; no stashes.

## Deferred to Phase F

- `LineOps.Desk.Testing` — convention scans (`AccessibleNameTests`, `GridConventionTests`, `StaticAssetsTests`, `DeskCssConventionTests`, `StylesheetOwnershipTests`) parameterised by source root, and `DeskTestContext`/fakes for consumers (audit #9).
- Moving the ~2,000 lines of desk-behaviour tests from `LineOps.Web.Tests` onto `FakeWindowCatalog` (#10).
- `dotnet new deskapp` template: App/Routes/Home/_Imports/Program wiring, then repo scaffolding with `--with-infra`, and a minimal launch profile (#11 rest, #12, #15).
- `PostgresDeskSignalSource` from `DataChangeListener` (#6).
- Skills distribution for apple-hig-components and apple-mudblazor (#13).
- ADRs 0007, 0008, 0013, 0016, 0018–0020 packaged with the Desk and Hosting (#14).
- The NuGet publish workflow (pack on a `desk-v*` tag, GitHub Packages, `nuget.config`).
- `FormGroup`/`FormRow` and its ~130-site adoption (judged L with adoption; a primitive without it is on paper).
- HIG 6 (game search as a popover listbox), 8 (Board sport filter as a switch), 9 (body actions into `PanelToolbar`), 15 (Slip's hand-rolled disclosure), the razor half of 18 (`Note Flush`, inline styles), the rest of 12; and the "board" mentions left in Desk comments (then add "board" to `DeskVocabularyTests`).
