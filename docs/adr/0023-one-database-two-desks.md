# ADR 0023 — One database, two desks: the laptop reads the desktop's Postgres over the tailnet

**Status:** Proposed (trial, 2026-11-14)

**Builds on:** [ADR 0014](0014-a-real-feed-a-credit-budget-and-what-counts-as-a-game.md), whose
credit budget is the thing a second scheduler spends, and [ADR 0019](0019-one-vm-caddy-in-front-the-worker-schedules.md),
whose rule that one process schedules is the rule this ADR carries to two machines.

**Plan:** [Going public](../superpowers/specs/2026-09-27-going-public-plan.md) §4 and §10b: the
project stays local, and the work that matters is portability.

## Context

The developer runs the desk on a desktop and a laptop. Each has its own Docker Postgres, and the
data travels between them as a committed snapshot: `publish-data.ps1` dumps it on the machine
that has it, `restore-data.ps1` loads it on the other (CLAUDE.md). That works, and it has three
costs.

- **It is a copy.** The laptop sees the data as it was when the snapshot was taken, hours or
  days ago. Anything the laptop ingests is lost at the next restore, because a restore is not a
  merge.
- **It is two schedulers.** Each machine's desk hosts the schedule and the evaluator by default.
  Credit pacing takes the provider's reported remainder (§10b), so the two do not spend the
  month twice, but they do spend it from two places, run two gamecast feeds against ESPN, and
  open incidents in two databases that disagree.
- **It is ESPN- and MLB-derived data in a public repository.** §4 names the snapshot as the one
  terms item that is live today. Making the repository private contains it; it does not stop it
  being a copy.

Hosting the data publicly is ruled out: ESPN's and MLBAM's terms allow personal, non-commercial
use, and a desk on the developer's own machines is exactly that (§10b).

## Options

**(a) The committed snapshot, as today.** No new moving parts, works offline, works with the
desktop switched off. The costs are the three above. The snapshot republishes the data to
whoever can read the repository.

**(b) One Postgres on the desktop, reached from the laptop over a private Tailscale tailnet.**
The desktop's compose publishes Postgres on its tailnet address only (`compose.tailnet-db.yml`).
The laptop's desk takes a connection string to that address and runs with the scheduler and the
evaluator switched off (`scripts/connect-remote-db.ps1`). There is one dataset, live, and one
scheduler. The rows never leave the developer's own two machines: they cross the network only
inside WireGuard, between devices signed in to the developer's own tailnet. Tailscale's
coordination server sees which devices exist and their keys, not the traffic. The laptop's desk
is unusable when the desktop is off or asleep.

**(c) A hosted Postgres, for example Neon's free tier (1 GB per project as of October 2026).**
Both desks point at it, from anywhere, whether the desktop is on or not. The database is about
the right size today and would outgrow the free tier as seasons accumulate. The decisive
difference is where the rows live. The games, box scores and closing lines are ESPN- and
MLB-derived, and on (c) they sit on a third party's servers, under that party's retention,
backups and staff access, for as long as the account exists. Nothing is shown to the public, so
it is still arguably personal use, but it is no longer the developer's machines. It is the
reading §10b relies on, stretched: "personal use on my own machines" becomes "personal use,
stored with a vendor". The terms are silent on storage rather than permissive about it, so (c)
stands on a weaker reading than (a) or (b). It also brings a credential that works from the whole
internet, where (b)'s works only inside the tailnet.

## Decision

**(b), with (a) kept as the fallback.**

- **The desktop owns the database and the schedule.** It runs Postgres with
  `compose.tailnet-db.yml` added to its compose list, bound to `TAILNET_IP` from its `.env` and
  never to `0.0.0.0`. It keeps `compose.dev.yml`, so its own desk still reaches the database on
  loopback. It is the only machine with `Ingestion:HostScheduler` and `Reliability:HostEvaluator`
  on.
- **The laptop's desk is a reader that can press buttons.** `connect-remote-db.ps1 -Host <desktop>`
  writes the remote connection string into the gitignored `appsettings.Local.json` with both
  switches off. A pull pressed on the laptop still runs, against the one database and the one
  budget; only the unattended schedule is the desktop's. `-Local` puts the laptop back on its own
  database.
- **A second spender is refused, not trusted to configuration.** `SchedulerPlacement` decides
  from the connection string whether the database is this machine's own: loopback, `localhost`,
  the compose service name `postgres`, or a socket. Against any other host, the web host refuses
  to start while it would schedule or evaluate, and the Worker refuses to run its schedule. The
  message names `Ingestion:AllowRemoteScheduling=true` as the deliberate override, for the one
  scheduler that reaches its own database by a non-loopback name. A single `--job` on the Worker
  is a press, not a schedule, and is not refused.
- **No Postgres TLS over the tailnet.** The laptop connects with `SSL Mode=Disable`. WireGuard
  already encrypts and authenticates every packet device to device, and the container requires
  SCRAM, so the password is never sent in the clear either. If TLS is wanted anyway, the overlay
  says how: a `tailscale cert` for the desktop's MagicDNS name, `ssl=on`, and `VerifyFull` on the
  client.
- **The snapshot stays.** It is what a laptop restores when the desktop is off for a trip, and
  what a fresh clone starts from.

## Consequences

- One dataset, current to the second, and one scheduler. The laptop no longer needs a restore to
  see last night's games, and loses nothing it pulls.
- **The laptop's desk does not work when the desktop is off**, asleep, or off the tailnet. It
  fails at startup, on the migration step, with a connection error. The fallback is
  `connect-remote-db.ps1 -Local` and a snapshot restore, which is today's workflow.
- **Both desks migrate the one database.** Each host migrates at startup. A laptop on a branch
  with a new migration applies it to the desktop's database, and the desktop's desk then runs
  older code against a newer schema. Keep both machines on the same commit of `LineX_Development`
  while connected, or switch the laptop to `-Local` while working on a migration.
- `restore-data.ps1` and `publish-data.ps1` name only `compose.dev.yml`. Run on the desktop, the
  restore's `up -d postgres` recreates the container without the tailnet port until it is
  brought up again with the overlay.
- The tailnet port binds only if the Tailscale address exists when the container starts. A boot
  where Docker starts before Tailscale leaves Postgres unpublished on the tailnet, or, depending
  on Docker Desktop, not started at all.
- Nothing is published. The repository's snapshot question (§4, decision 2) is unchanged, and
  becomes less pressing, since the snapshot is now refreshed only for the fallback.
- If this holds up, the snapshot could become a manual export rather than a regular commit,
  which takes the derived data out of the repository's history going forward.
