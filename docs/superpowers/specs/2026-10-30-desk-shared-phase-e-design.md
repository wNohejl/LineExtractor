# The shared desk (finalisation Phase E) — design

30 October 2026. Phases A–D of `2026-10-26-desk-finalisation-design.md` left `src/LineOps.Desk` a
packable library with its own tests, gallery and README. Two reviews that day — an extraction
audit of `LineOps.Web` and a HIG review of every window — found what still keeps the Desk from
being lifted out whole, and the repeated hand-built patterns behind the HIG review's two worst
findings. This document is the design for closing both in one sitting. The task plan is
`docs/superpowers/plans/2026-10-30-desk-final-e-shared.md`. File and line citations below were
re-read at `4e50a0d`; where the audits' numbers had drifted they are corrected here.

## Intent

1. **A consumer gets everything generic from packages.** After Phase E a second Blazor Server
   app references `LineOps.Desk` and `LineOps.Desk.Hosting` and gets the window system, a panel
   base class with load/retry/refresh plumbing, a job menu, error and not-found pages, the
   access gate and the edge headers — without copying a file out of `LineOps.Web`.
2. **Every key explains itself where it is drawn.** A disabled key says why in visible text that
   a screen reader is pointed at, and every key row orders the same way: secondary first, the
   reason, the one Filled key last (HIG findings 1 and 3). This is done once, as primitives,
   and adopted at every site the review cites.

## What moves into the Desk, and why

| What (verified site) | Becomes | Why |
|---|---|---|
| `.wsteps`/`.wstep*` rules, `lineops.css:1041–1133` and the two step lines of the `@container (max-width: 480px)` block (1468, 1470) | a "Steps" section in `controls.css`, names unchanged | **A bug:** `DeskSteps`/`DeskStep` ship in the package but are styled only by the app's sheet. The Desk emits these names, so they keep them. |
| `.wtabs + .tabs__panel { container-type }` (`lineops.css:1457`) | `.tabs__panel { container-type: inline-size; }` in `controls.css` | `DeskTabs` emits `.tabs__panel`; every tab body should be a container, not only the wager window's. |
| `.deskgrid--board .deskgrid__child*` (`lineops.css:720–721`) | `.deskgrid__child > td` / `.deskgrid__child-cell` in `desk.css` beside `.deskgrid--clickable` (3503) | `DeskGrid` emits the child row (`DeskGrid.razor:325–330`) and the Desk gives it no style at all. |
| `PanelBase.cs:26–289` (window-id cascade, `DeskSignals` subscription with a 1 s settle, `RequestRefresh`, `LoadError`/`Failed`/`Loaded`, `WithActivityAsync` with its generation guard, `RetryAsync`, `Report`, `MarkUnsaved`, disposal) | `LineOps.Desk.Windowing.DeskPanelBase` | Generic plumbing every consumer's window needs. The app's `PanelBase : DeskPanelBase` keeps lines 291–386 (`Starts`, `Day`, `Matchup`, `Probables`, `Surname`, `MatchupWithDate`, `StateStatus`×2, `Phase`, `Price`, `Line`, `Age`). |
| `Components/Panels/LoadFailure.razor` (16 call sites in 15 panels) | `PanelLoadFailure` primitive | No coupling; it is `EmptyState Kind=Error` with a Try again key. |
| `Components/DisplayTime.cs` | `LineOps.Desk.DeskTime` (`Stamp`/`Day`/`Format` over a `DeskClock`), beside `DeskClock` in `DeskHost.cs` | The formatting is generic; only the zone is LineOps's. `DisplayTime` keeps its static API and delegates, so no panel changes. |
| `Components/Desk/PullMenu.razor` over `IngestionJobs` | `DeskJobMenu` over `IDeskJobs`/`DeskJob`/`DeskJobOutcome`, plus a scoped `DeskJobRunner` | The menu, busy key, toasts and "unavailable opens the menu" are generic. The app keeps `IngestionDeskJobs : IDeskJobs`, which supplies labels, cost text and the outcome sentence. `PullMenu.razor` is deleted. |
| `Pages/Error.razor`, `Pages/NotFound.razor` (stock Bootstrap template text) | `DeskErrorPage`, `DeskNotFound` on `EmptyState` | Every consumer needs both and the stock ones do not look like the desk. |
| Key rows with a "why" (`opsact__why`, `wager-why`, `slip-why`, `#save-why`) | `KeyRow` | Fixes order and wires `aria-describedby` once; generates a unique reason id (today `save-why` would collide with two Journals open). |
| Inline confirms (`OpsPanel.razor:131–143`, `HistoryPanel.razor:169–181`) | `InlineConfirm` | Same sentence/Cancel/confirm shape twice, both with the confirm before Cancel. |

## The Hosting package boundary

`src/LineOps.Desk.Hosting` (PackageId `LineOps.Desk.Hosting`, 0.1.0) holds what needs ASP.NET Core's
request pipeline — authentication, antiforgery, rate limiting, response headers — so that the
Desk itself stays a Razor class library with no opinion about how a host is exposed (ADR 0018,
ADR 0019). It references `LineOps.Desk` (for `IDeskAccess` and `DeskBrand`) and nothing else.

- **Access gate** (from `Web/Access/*`): `DeskAccessOptions` (section `Access` by default; `Mode`,
  `AllowAnonymousVisitors`, `OperatorCodeHash`, `GuestCodeHash`, `SessionLifetime`, and new
  `CookieName = "desk.access"`, `SignInPath = "/signin"`, `SignOutPath = "/signout"`),
  `DeskAccessCode` (PBKDF2, format unchanged), `DeskCircuitAccess : IDeskAccess`,
  `AddDeskAccess(configuration, configure?)`, `UseDeskAccess()`, `MapDeskAccess()`,
  `configuration.IsDeskGated()`. The sign-in page takes its wordmark and tagline from `DeskBrand`
  (already true) and is rendered by a pure `DeskSignInPage.Render(...)` so it can be tested.
  LineOps passes `CookieName = "lineops.access"` so nobody is signed out by the move. The logger
  category becomes `LineOps.Desk.Hosting.Access`.
- **Edge** (from `Web/Hosting/EdgeHardening.cs`): `DeskEdgeOptions` (`ContentSecurityPolicy`
  defaulting to today's string, `ExtraImageSources` appended to `img-src`), `AddDeskEdge`,
  `UseDeskEdge`, `configuration.IsBehindProxy()` (key `Hosting:BehindProxy` unchanged), and a pure
  `DeskEdge.Headers(options)` the middleware writes.
- **Tests** go to a new `tests/LineOps.Desk.Hosting.Tests`, not into `LineOps.Desk.Tests`:
  `ProjectShapeTests.The_desk_tests_reference_only_the_desk` asserts that project has exactly one
  reference, and that is the point of it. The new project references only the Hosting project.
- `scripts/new-access-code.ps1` stays in the repo (it is how this deployment makes a hash); its
  doc comment names `LineOps.Desk.Hosting.DeskAccessCode` as the format it must match.

## What stays in the app, and why

- `.whead`, `.wcard`, `.wreceipt` — **a deliberate change from the audit**, which suggested
  renaming them `.deskcard*`. No Desk component emits them: they are the wager window's two
  cards (a finalisation Non-goal) and only `BoardWager.razor` uses them. A `DeskCard` primitive
  with one consumer is a primitive on paper. `.waction` is not renamed either; it is replaced by
  `KeyRow` at its two sites and deleted.
- Sports helpers on `PanelBase`, `IngestionDeskJobs`, `CircuitDeskAccess`'s replacement wiring in
  `Program.cs`, `DisplayTime` (the league zone), and everything in the audit's "Not shared" list.
- **FormGroup/FormRow is deferred.** The primitive is small, but the grouped form is hand-written
  ~130 times across ten files; shipping it without that adoption breaks the finalisation
  anti-goal ("a primitive that exists only on paper"), and the adoption is L, not S/M.

## HIG findings closed in this phase

CSS (one task, the sheet's single owner): 10 `.wstep__title` fill only, no border; 11
`.book-picker__trigger` one rule at 28px (today 32px at :1199 and 22px at :1425), `.gate__opt`
min-height 24px (:1148, :1367); 13 `.gamelines__cell--take:focus-visible` (:1605) uses
`box-shadow: var(--focus-ring)`; 18 (CSS) delete `.whead .ptitle__row .tag` (:973), `.runbook`
radius `var(--radius-sm)` (:482), `.slip-badge` radius `999px` (:1262); 19 `.price__edge`/`.price__ev`
transitions (:119, :136) on `var(--dur-fast) var(--ease-standard)`; 20 delete `.book-chips` and
`.book-chip__price` (:904–905), used nowhere.

Razor: 1 and 3 through `KeyRow`/`InlineConfirm`; 2 takeable prices become `<button type="button">`
(`PriceCell.razor:99–105`, `GamePanel.razor:478–482`); 4 FormSnippet's inline sizes (:118, :124,
:132) use the existing `.snippet__note`; 5 snippet loading sentences (`FormSnippet:20`,
`HeadToHeadSnippet:22`, `OddsSnippet:20`, `PlayerRecentSnippet:20`) become `DeskSkeleton`; 7 season
pickers (`TeamPanel:66`, `PlayerPanel:72`) fall back to `DeskSelect` past five seasons; 12 only
where a `DeskTooltip` or spelling-out is one line (`HistoryPanel:343` "pp", `JournalPanel:302`,
`SlipPanel:166`, `BoardPanel:385`); 14 Journal row keys (`JournalPanel:349–356`, `:447–452`) fold
Edit/Duplicate/Delete into one pull-down with Delete destructive; 16 empty states gain their key
(`FighterView:25–28`, `TaleOfTheTape:44`, `BoardForm:45–48`); 17 Slip's book select (`SlipPanel:198–202`)
shows `BookNames.Display` and the placeholder "Book"; 21 copy (`TeamPanel:141`, `HeadToHeadPanel:61,142`,
`HeadToHeadSnippet:61`, `GamePanel:94`, `PanelBase.Probables`, `OddsSnippet:43`, `RunsPanel:84`,
`OpsPanel:342`); 22 hand-rolled empty text (`OpsPanel:233`, `IncidentsPanel:189`, `PlayersPanel:112–114`).

## Risks

- **Moving the step styles changes their place in the cascade.** They load from `controls.css`
  (after `desk.css`, before `themes.css`, `mud-bridge.css` and the app sheet) instead of last. The
  app's wizard overrides (`.wsteps .wgroup`, `.wcard …`, the 480px block) still come later and win;
  a moved rule that beat a `mud-bridge.css` rule only by loading after it now loses. The task
  greps `mud-bridge.css` for the `.note`, `.desklink` and `.mud-` selectors the moved rules touch,
  and the browser check is the wager window and the Journal at 1280×800.
- **`.tabs__panel` as a container everywhere** could shrink a tab body that sized to content.
  Every `DeskTabs` host is block-level and full width; checked in the browser on the wager window,
  the Journal and the Parts bin.
- **Cookie name.** Leaving the default would sign out every gated visitor on deploy; the Program
  wiring passes `lineops.access` and a Hosting test asserts the option reaches the cookie.
- **Dockerfile restore.** `src/LineOps.Web/Dockerfile` copies each csproj before `dotnet restore`;
  without a line for the Hosting csproj the image build fails. Added in the Hosting task.
- **Buttons for prices.** A `<button>` cannot hold `<div>`s validly, and its UA styles differ.
  The cell's inner `div`s become `span`s with their display set by their existing classes, and
  one reset rule makes `button.price` look exactly like `div.price`.
- **Parallel agents.** `lineops.css` and `controls.css` each have one owner per wave; other tasks
  hand CSS and gallery fragments to the controller as scratch files.

## Deferred to Phase F

- `LineOps.Desk.Testing` (audit #9): convention scans and `DeskTestContext`/fakes as a package.
- Moving the ~2,000 lines of desk-behaviour tests out of `LineOps.Web.Tests` (audit #10).
- ~~The `dotnet new deskapp` template, repo scaffolding and launch profile (#11 rest, #12, #15).~~
  Done: `src/LineOps.Desk.Templates`, with `.editorconfig`, `.gitattributes`, `.claude/launch.json`
  and the two HIG skills, which also closes the distribution half of #13.
- `PostgresDeskSignalSource` from `DataChangeListener` (#6).
- Distributing the HIG skills (#13) and packaging the ADRs with the packages (#14).
- ~~A NuGet publish workflow (pack on a `desk-v*` tag, push to GitHub Packages, `nuget.config`).~~
  Done: `.github/workflows/desk-packages.yml` and `docs/desk/nuget.config`; the Desk README's
  Releasing section says how to cut one.
- `FormGroup`/`FormRow` and its ~130-site adoption.
- HIG findings 6 (game search as a popover listbox), 8 (Board sport filter as a switch), 9
  (actions into `PanelToolbar`), 15 (Slip's hand-rolled disclosure), the razor half of 18
  (`Note Flush`, inline styles), the rest of 12, and the "board" comments still in the Desk.

## Verification

`dotnet build` clean on `src/LineOps.Web`, `src/LineOps.Worker` and all four test projects;
`dotnet test` green (including `AccessibleNameTests`, `DeskCssConventionTests`,
`StylesheetOwnershipTests`, `ProjectShapeTests`, `DeskVocabularyTests`, `PartsPanelTests`); the
Worker starts; `dotnet format --verify-no-changes` clean in the `mcr.microsoft.com/dotnet/sdk:10.0`
container; `dotnet pack` succeeds for `src/LineOps.Desk` and `src/LineOps.Desk.Hosting`; a browser
pass at 1280×800 dark and light of the wager window, Journal, Slip, Ops, History, Incidents,
Performance, Board and the Parts bin's new sections; with `Access:Mode=Gated` on a scratch port,
the sign-in page renders, a code signs in, and the cookie is `lineops.access`; `git worktree list`
shows one entry and `git branch` shows two.
