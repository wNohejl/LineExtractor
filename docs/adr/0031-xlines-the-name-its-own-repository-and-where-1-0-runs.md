# ADR 0031 — XLines: the name, its own repository, and where 1.0 runs

**Status:** Accepted, 2026-11-22. The name and the version are built: the desk says XLines and
the hosts are 1.0.0. The deployment is decided as written below — a new repository at 1.0.0, a
static site, 1.0 as a desk each person runs, a hosted demo on the existing VM kit if one is
wanted, and no Azure App Service for 1.0. Phases B to D are not yet carried out: each creates
or changes something outside this checkout, and is the developer's to start (§ What is decided).

**Amended 2026-11-23:** the identifiers are renamed after all — see
[§ Amendment](#amendment-2026-11-23-the-identifiers-are-renamed-the-stored-values-are-not). §1's
table below is the record of what each rename costs, and the amendment keeps every row of it
that costs data.

**Supersedes:** the going-public plan's §8 to §10b
([2026-09-27](../specs/2026-09-27-going-public-plan.md)) as the statement of where the
project is going, and the open decisions at the end of the multi-device research
([2026-11-14](../specs/2026-11-14-multi-device-access-research.md) §5, §6). Both stay
as the record of how the question was worked.

**Builds on:** [ADR 0018](0018-an-operator-and-visitors.md) (the gate),
[ADR 0019](0019-one-vm-caddy-in-front-the-worker-schedules.md) (the hosted stack),
[ADR 0024](0024-data-tiered-by-licence.md) (what a visitor may read) and
[ADR 0026](0026-the-odds-api-is-the-default-source.md) (a clone that works on the user's own key).

## Context

The desk is the nearest it has been to finished: four leagues, a default source anyone may use on
their own free key, a private journal that merges between machines, twenty-three theme families,
and a hosting kit that has been built since September and never used. The developer wants to call
it **XLines**, call this version 1.0, and asks whether it should have its own repository, a
website, and an Azure App Service.

Three things in the existing documents answer most of that.

1. **The reason it stayed local no longer binds every desk.** The plan's §10b (2026-10-24) kept
   the project local because its only stats source, ESPN, is licensed for personal use. Since
   2026-11-20 ESPN is off unless its operator turns it on, and the default is The Odds API
   (ADR 0026), whose terms allow display, commercial display included (plan §4). A desk that
   never turns ESPN on holds nothing it may not show. A desk with ESPN on is exactly where §10b
   left it: the operator's alone.
2. **The repository is the one live exposure, and has been since September.** The plan's
   decision 2 (make it private) was never taken. The working tree is clean today — `git ls-files
   data` is empty and the snapshot travels through the private journal repository — but fourteen
   commits in the history of `wNohejl/LineExtractor` touch `data/snapshots/`, and each holds an
   ESPN- and MLB-derived dump that anyone can check out. `CLAUDE.md` forbids rewriting history a
   second time.
3. **Almost nothing in the code needs to be called XLines.** `LineOps` is in 694 source files:
   eleven projects' namespaces, the solution file, four published NuGet package ids
   (`LineOps.Desk` 0.5.0 and its three companions), two container images, the database name, the
   volumes, the Data Protection application name and the browser's saved-layout key. The
   product's visible name was one line, `new DeskBrand("LINE", "OPS", …)` in
   `src/LineOps.Web/Program.cs`, a page title, and three sentences that say the desk places no
   wager.

## Decision

### 1. The name: XLines is the product, LineOps is what it is built on

**XLines** is what the desk, the README, the site and the repository are called. The wordmark is
`X` with `LINES` as its emphasis. `LineOps` stays as the name of the platform underneath — the
Desk packages, the reliability and ingestion libraries, the solution — which is the split the plan's
§7 already drew between a vertical and the kit it consumes.

The identifiers are not renamed for 1.0, because each one is a migration with its own cost and
none of them is seen by a user:

| Identifier | What renaming it costs |
|---|---|
| Namespaces, project folders, `LineOps.slnx` | 694 files; every open branch conflicts |
| NuGet ids `LineOps.Desk*` | Published ids cannot be renamed, only abandoned; TicketMiser's reuse plan names them |
| Data Protection application name `LineOps` (`Program.cs`) | Every protected payload becomes unreadable: the odds key saved in Ops, every sign-in cookie |
| Local key folder `%LOCALAPPDATA%\LineOps` (`DeskOddsKey`) | The saved key is not found until moved |
| Layout key `lineops.desk` (derived from the brand in `Desk.razor`) | Saved layouts and workspaces start empty |
| Database, role, volumes `lineops-*`, images `lineops-web`/`-worker` | A dump and restore on both machines; the hosted compose and `images.yml` follow |

The layout key is the one the brand change itself would have moved, since it was computed from
the wordmark. `DeskBrand.StorageKey` pins it: unset, the key is made from the wordmark as before;
the Web host sets it to `lineops.desk`, so the name changed and nobody's desk emptied.

**Built with this ADR:** `DeskBrand("X", "LINES", …) { StorageKey = "lineops.desk" }`; the page
title; the no-wager sentences in the Journal, the Slip and the wager window; the README's title
and scope line; `<Version>1.0.0</Version>` on `LineOps.Web` and `LineOps.Worker`. `StorageKey` is
an addition to the `LineOps.Desk` package, which wants a version of its own when it is next
packed.

DESIGN §1's framing rules carry over unchanged: XLines is a sports-data ingestion and analytics
operations platform, the journal records bets placed elsewhere, and nothing in the name, the site
or the README says "betting app", "picks" or "wagering".

### 2. The repository: yes, a new one, started at 1.0

A new public repository, `wNohejl/XLines`, whose first commit is the 1.0.0 tree. The old
repository, `wNohejl/LineExtractor`, is made private and archived, and remains the history of
record.

This is the only option that closes decision 2 without breaking a rule:

| Option | History public | Dumps in public history | Rewrites history |
|---|---|---|---|
| Rename `LineExtractor` in place, stay public | Yes | **Yes** | No |
| Rename in place, make private | No | No | No — but then there is no public project |
| Filter the dumps out, force-push | Yes | No | **Yes** (`CLAUDE.md` forbids it) |
| **New repository from the 1.0.0 tree; old one private** | No | No | No |

What it gives up is the public commit log, which DESIGN §1 counts as evidence of how the system
was operated. The ADRs, the runbook and DESIGN §11 carry that story in prose and move with the
tree; the log itself stays readable to the developer and to anyone invited to the private
repository.

The working rules move as they are: `main` and `LineX_Development`, commits under the developer's
own identity, the private journal clone at `data/personal`, which is a repository of its own and
does not change.

### 3. The version: 1.0.0

SemVer, three parts; the assembly version reads 1.0.0.0. `<Version>` is set on the Web and Worker
hosts; the four Desk packages keep their own versions, since they
release on their own. The tag `v1.0.0` on `main` is the release, and `images.yml` tags the images
with it beside `main` and `sha-<commit>`.

### 4. The website: yes, a static page, not the desk

A landing page on GitHub Pages from the new repository: what XLines is, screenshots of the desk,
the Quick start, and the data notice from the README. It costs nothing, needs no server, and is
what a search engine can read — a Blazor Server page is empty to a crawler (plan, Phase 3). A
domain is optional and about $15 a year (plan §8, decision 4).

### 5. Hosting: 1.0 is software people run, not a service; Azure App Service is not the first step

**1.0 ships as a self-hosted desk.** Clone, set up, enter your own free key. That is what ADR 0026
built, it is the use every provider's terms allow, and it needs nothing that is not already
written. A public hosted desk is not part of 1.0, for reasons that are not about the cloud:

- **One key is one month.** 500 credits serve one operator. A desk priced for the public wants the
  20K tier at $30 a month (plan §8, decision 5).
- **One journal has one owner.** There are no accounts and no `OwnerId` (plan §5.2); a visitor
  reads and changes nothing (ADR 0018).
- **The employment question is still open** (plan §8, decision 1), and gates anything paid.

**A hosted demo is now possible, and is optional.** With ESPN off, `Sharing:Mode=Shared`, the gate
on and the worker as the only scheduler, `deploy/compose.yml` serves a desk that shows fixtures,
prices, closes and CLV from a displayable source and nothing else. It never needs the provenance
column ADR 0024 left open, because a database that has never had ESPN on holds no personal-use
row. It is worth doing when there is someone to show it to.

**Where, if it is hosted.**

| | One VM, `deploy/` (ADR 0019) | Azure App Service |
|---|---|---|
| State | Built and verified locally 2026-09-27; never deployed | Nothing built |
| Blazor Server | Caddy proxies the WebSocket; one replica | Supported: WebSockets must be switched on (off by default), session affinity left on; Azure SignalR Service is not required¹ |
| TLS and a custom domain | Caddy and Let's Encrypt | Managed certificate and custom domain from Basic B1 up² |
| The scheduler | The worker container, the only one | A second app or WebJob, or the web host schedules in-process — which needs Always On, so not the Free or Shared tier |
| Postgres | In the same compose file, nightly `pg_dump` | A separate service, Azure Database for PostgreSQL flexible server, billed separately |
| What must change | DNS and `.env` | Data Protection keys to persistent storage; `Hosting:BehindProxy`; the read-only root filesystem and `cap_drop` of ADR 0006 do not carry over; `deploy.sh`, `backup.sh` and `restore.sh` are replaced |
| Monthly cost | $6–12, everything (plan §3.1) | About $10–13 for the web tier alone, before the database (plan §3.1); re-price before deciding |

¹ <https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/server/#azure-app-service>
² <https://learn.microsoft.com/azure/app-service/tutorial-secure-domain-certificate>

The VM stays the recommendation: it is the stack already written, at half the price or less, and
it is the same compose model the laptop runs. **Azure App Service is the right choice for one
reason only: if the point is to have run this stack on Azure**, which DESIGN §1 would count,
since the purpose it names is the developer's own résumé. In that case it is a Linux custom
container from the GHCR image on B1, a burstable flexible server, the web host scheduling, and an
ADR of its own amending 0019 — about a week, and it should follow 1.0 rather than hold it up.

## The phases, replacing the plan's §9

Each ends with something that can be pointed at.

**Phase A — Close 1.0 where it is.**
- The remaining styling work: `docs/evidence/styling_improvements.pdf` is a set of outside proposals, and
  [the 1.0 styling triage](../specs/2026-11-22-xlines-1-0-styling.md) says which of
  them the desk already does, which close 1.0, and which contradict an accepted decision. Its
  five build items are done and measured in a browser
  ([evidence](../evidence/2026-11-22-xlines-1-0-styling.md)).
- Collapse to two branches: `GamecastAndStyling` (four commits ahead), `DesignImprovements` and
  `analytics-py-port` are merged into `LineX_Development` or dropped, and deleted.
- One run against The Odds API with a real key: ADR 0026 says its adapter was never checked
  against the live service.
- The brand and the version: done (§1, §3).
- `CLAUDE.md`'s verification list; `dotnet format` in a Linux
  container; merge to `main`, which is the CI run and the image build.

*Exit:* `main` is green, the desk says XLines, and the tree is the one to publish.

**Phase B — The repository.**
- Audit the tree before it becomes a first commit: no dump, no `.env`, no journal, and a decision
  about the recorded provider payloads under `tests/` (they are samples of ESPN's and The Odds
  API's responses, kept so parsing is tested offline).
- Create `wNohejl/XLines`; push the 1.0.0 tree as one commit to `main`; tag `v1.0.0`; branch
  `LineX_Development` from it.
- Repoint what names the old repository: the clone lines in `README.md` (two), `CLAUDE.md`,
  `.claude/skills/lineops-setup/SKILL.md` and `deploy/README.md`; GHCR image owners follow the
  repository by themselves.
- Make `LineExtractor` private, then archive it. Re-clone on the laptop and the desktop;
  `data/personal` is cloned into the new checkout as before.

*Exit:* a public repository with no provider's data in any commit, and decision 2 closed.

**Phase C — The site.** A static page on GitHub Pages; a domain if wanted. The page is written:
`site/index.html`, one self-contained file with nothing fetched from another origin and every
team and price in its illustration invented, and `.github/workflows/pages.yml`, which publishes
`site/` only when run by hand. It is not published. It names the repository in one place, a
`<meta>` in its head, to be changed at Phase B; publishing it is then two steps on GitHub:
Settings → Pages → source "GitHub Actions", and a run of the Pages workflow.

*Exit:* a URL that says what XLines is to someone who has not cloned it.

**Phase D — A hosted demo (optional).** `deploy/README.md` on a VM, or the Azure path above;
ESPN off, `SHARING_MODE=Shared`, a guest code; an uptime monitor and one restore drill on record
(plan, Phase 1).

*Exit:* a link and a code for a reviewer.

**Shelved, unchanged:** the licensed stats port, the public anonymous desk, accounts, billing
(plan Phases 2, 3 and 5), and extracting the kit into its own repository (Phase 4), which the Desk
packages have already half done.

## What is decided, and what still needs the developer's hand

Decided here, from the documents this ADR builds on:

| Question | Decision |
|---|---|
| Its own repository? | Yes: `wNohejl/XLines`, new, from the 1.0.0 tree; `LineExtractor` private and archived |
| A website? | Yes: a static page on GitHub Pages from that repository; no domain is needed to start |
| Azure App Service? | No, not for 1.0. Revisit only as a deliberate Azure exercise after it |
| A hosted desk? | Not part of 1.0. A demo, when wanted, is `deploy/` on one VM with ESPN off and `SHARING_MODE=Shared` |
| Recorded fixtures in the public tree | Keep them: they are small samples the tests parse, not a dataset, and trimming them weakens the tests. Re-read each provider's terms at Phase B, as the plan's §10 asks |
| Order | Phase A, then B, then C; D only when there is someone to show it to |

| The "Place wager" key and the "Bet a game" workspace | They stay. Four accepted ADRs name them, the window logs and sends nothing, and three notes beside it say so; DESIGN §11 records the label as the one exception to §1's wording rule |

**The name, checked 2026-11-22.** `github.com/wNohejl/XLines` does not exist, so the repository
name is free. A GitHub search finds sixteen repositories named `xlines`, none about sports or
odds (the largest is a Python teaching project). A web search finds no sports-data, odds or
betting product called XLines. `xlines.com` and `xlines.io` resolve, so they are taken;
`xlines.app`, `xlines.dev` and `xlines.net` do not resolve, which suggests but does not prove
they are free. GitHub Pages needs none of them.

Not something a document can settle:

1. **Whether the name is clear as a mark.** A web search of USPTO records on 2026-11-22 found no
   record for XLINES. It found several for the singular XLINE and X LINE, in unrelated goods:
   one live application for oil-and-gas wireline software (serial 98481651, Class 042), one for
   metal building materials, and dead registrations for sunglasses and waterproofing compounds.
   That is a search through a third-party index, not the USPTO's own, and not advice; a
   near-identical live mark in software is the one to read before the name is used commercially.
   If the name must change, only the repository's name and the wordmark do.
2. **Carrying out Phase B.** Creating a repository and making another private are done on the
   developer's GitHub account, by the developer.
3. **Employment** (plan §8, decision 1): unchanged, and still gates anything paid.

## Consequences

- The project has one public name and one public repository, and neither carries a provider's
  data.
- `LineOps` remains in every path and command. The README says so once, at the top, so a reader
  is not surprised by `dotnet test LineOps.slnx`.
- "It stays local" (plan §10b, `CLAUDE.md`) remains true of the owner's desk, which has ESPN on.
  It is no longer true of the project, which anyone may run, and of a desk on the default source,
  which may be shown.
- Not done by this ADR: no identifier is renamed, no repository is created, nothing is deployed.
  The brand and the version are the first change of Phase A; the rest of it is next.

## Amendment 2026-11-23: the identifiers are renamed, the stored values are not

The developer chose to finish the rename before the tree becomes the first commit of
`wNohejl/XLines`, rather than carry two names into the new repository. §1's reason for waiting —
every open branch would conflict — is weakest at exactly that moment: the new repository starts
with no branches but `main` and `LineX_Development`.

**Renamed:** every project, folder and namespace (`XLines.Core` … `XLines.Worker`, the tests),
the solution `XLines.slnx`, `XLinesDbContext`, the `AddXLines*` registrations, the telemetry
source, meter and metric names (`xlines.*`), the stylesheet `xlines.css`, the container names
and images (`xlines-web`, `xlines-worker`), the environment variables (`XLINES_HOST`,
`XLINES_TAILNET_HOST`, `XLINES_CONNECTION`, `XLINES_COMPOSE_FILES`), the setup skill
(`xlines-setup`), the hosted stack's paths and project, and every document and clone line,
which now name `https://github.com/wNohejl/XLines.git` cloned into `XLines`.

**The Desk packages take new ids:** `XLines.Desk`, `XLines.Desk.Hosting`, `XLines.Desk.Templates`
and `XLines.Desk.Testing`, at the versions they had. The `LineOps.Desk*` ids on the feed stay as
published; a consumer such as TicketMiser moves to the new ids when it next updates.

**Kept, because each one is a value saved on a machine or in another repository:**

| Kept as | Why |
|---|---|
| Database and role `lineops`; `Database=lineops;Username=lineops` | Renaming is a dump and restore on both machines |
| Compose project `name: lineops`, volumes `lineops-pgdata`, `lineops-keys` | Pinned in `docker-compose.yml`, so a checkout folder named `XLines` still finds the existing volumes |
| Connection string `ConnectionStrings:LineOps` | It is in each machine's user-secrets, `appsettings.Local.json` and `.env`-built environment |
| Data Protection application name `LineOps` | The odds key saved in Ops and every sign-in cookie |
| Key folder `%LOCALAPPDATA%\LineOps` | Where `DeskOddsKey` finds the saved key |
| Layout key `lineops.desk`, cookie `lineops.access`, theme keys `lineops.theme*` | Saved layouts, signed-in visitors, saved themes |
| Postgres function `lineops_ensure_odds_partition` | It exists in every database the initial migration made |
| Journal `SyncId` prefix `lineops-journal-entry:` | Entries already synced keep their ids, so the merge stays idempotent |
| Repository `lineops-journal`, snapshot `snapshots/lineops.dump` | They live in the private repository, which does not change |
| Certificate file `LineOps.Web.pfx` | `setup.ps1` made it on each machine; `CERT_PATH` points at it |

A `.env` that sets `LINEOPS_TAILNET_HOST` needs it renamed to `XLINES_TAILNET_HOST`; unset, the
tailnet overlay accepts any host name, as it did before the variable was set.
