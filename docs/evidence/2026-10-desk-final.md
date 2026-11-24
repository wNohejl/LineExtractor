# The Phase A walk — 26 October 2026

Every window, in the browser pane against this machine's database, after the seven Phase A
commits (716a41b … b0e81b2). Screenshots were taken and stayed in the session's scratchpad;
they show the operator's own journal and are not committed. What is recorded here is the
result of the checklist each window was held to.

**The checklist**, run by a script in the page on each window's body: no element with its own
edge inside another edged element (controls, tables, buttons and menus excepted); at most one
Filled key visible; every input, select and text area with an accessible name; no uppercase
text; no icon-only button without a name; no button that begins "Yes," or ends with "!"; and a
count of text under 11px. Then a look at the screenshot for the HIG review checklist's
remaining items (one title, grouped forms, footers under groups, quiet success, specific
errors).

Sizes: 1280×800 (every window, dark; the form-heavy windows again in light) and 1920×1080
(Board, Head to head). Light was reached by storing `Light` and reloading: the page came up
`apple-light` with the boot script ahead of the first stylesheet, so there was no dark frame.

| Window | 1280 dark | 1280 light | 1920 | Notes |
|---|---|---|---|---|
| Board | pass | pass | pass | 27 games in the 7-day window; row actions open as before |
| Every book | pass | — | — | floating; price cells as the Board's |
| Place wager | pass | — | — | the wager card's step titles sit inside the card by design (out of scope) |
| Slip | pass | pass | — | |
| Recent form | pass | — | — | floating; both rosters |
| Game | pass | pass | — | Lines tab is one group with the book under each price |
| Team | pass | pass | — | |
| Player | pass | pass | — | |
| Head to head | pass | — | pass | |
| Line movement | pass | pass | — | skeleton on load; status "6 moves" |
| Players | pass | — | — | Sync stats is the one Filled key |
| Journal | pass | pass | — | Edit entry: title, "Placed …" detail, Entry section, typed-price footer, Save changes the one Filled key |
| Performance | pass | pass | — | bankroll is a labelled row with its footer; Set is Plain |
| Ops | pass | pass | — | Scanning is a section with its cost as footer; Evaluate now the one Filled key |
| Incidents | pass | — | — | no open incident, so the RCA form was not reachable; OperationsWindowsTests covers it |
| Runs | pass | — | — | |
| History | pass | — | — | Run is a section: Past days / Held days; "Re-walk" |
| Window manager | pass | pass | — | the stepper's capsule inside a group is a control, not a box |
| Parts bin | pass | — | — | step titles inside the wizard card by design |

**Text under 11px.** The script counted 10px text in the Board's price cells (`price__sel`,
`price__book`, `price__edge`, `price__ev`), the starter role on a matchup chip and
`value__bet` — all set from `--text-caption`, which lineops.css pins at 10px as rendered,
"the macOS floor (HIG Typography)", and ThemeContrastTests asserts. Kept: the desk keeps
macOS density, and 10pt is macOS's caption. Nothing renders below it.

**Axe.** Not run: axe-core is not in the repository and the page's CSP allows no CDN. The
accessible-name and icon-name checks above are the part of it the desk was most likely to
fail; `AccessibleNameTests` now holds the first for every Razor file.

**Not reachable in this walk.** An open incident's RCA form (none open); a failed save (the
database was healthy) — both covered by tests in the Phase A commits.

## Phase D — rendering cost, 30 October 2026

Measured in the browser pane on port 5271 at 1280×800, dark, before (ed469eb) and after
(ed8a656) the four Phase D commits; the render counts are what `WindowRenderCountTests`
observed on the bUnit bench with two windows open.

| Measure | Before | After |
|---|---|---|
| Desk's own renders on one window's status change | 1 (plus every window, the header, the bar, the footer) | 0 (that window and the bar only) |
| Renders per window on `Relayout()` that moves nothing | 1 each | 0 each |
| Resize handlers | one measure and one `.NET` call per `resize` event, in three files | one `requestAnimationFrame` pass per burst, trailing edge |
| Glide observer on a grid of N rows | N subtree queries | 1 sweep per frame |
| Head CSS excluding MudBlazor (uncompressed) | 330 KB | 330 KB (desk 156, app 80, bridge 40, controls 24, themes 21) — under the 350 KB target before and after; the split moved bytes, it did not add them |
| Regular Inter face requested | 116 ms after the first stylesheet (after parse) | with the first stylesheet (preloaded) |
| `wwwroot/lib` (Bootstrap) | 8.5 MB published, 0 requests | gone |
| `DeskGrid` renders on mount / on a same-items parameter set | 2 / +1 | 2 / +1, now pinned by a test |
| Performance window reads per load | 2 | 1 |
| Tale of the tape reads per Game render | 1 per render | 1 per bout |
| History reloads per backfill day walked | 1 per day, unbounded | at most 1 per second |
| Incident list | every incident, tracked | every open one plus the newest 200 resolved, untracked |

Console after the rebuild: no errors from the new page (the buffer still held the
connection-refused lines from the server restart). The resize burst (1200 → 1300 → 1280)
produced no error and the layout settled at the last size. Not measured: SignalR message
counts per resize — the shape is held by `DeskScriptsTests` instead.
