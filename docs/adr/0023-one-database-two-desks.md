# ADR 0023 — One database, two desks: the laptop reads the desktop's Postgres over the tailnet

**Status:** Accepted. Built as a trial on 2026-11-14, amended 2026-11-15, and merged into `LineX_Development`; not yet run over a real tailnet connection.

**Builds on:** [ADR 0014](0014-a-real-feed-a-credit-budget-and-what-counts-as-a-game.md), whose
credit budget is the thing a second scheduler spends, and [ADR 0019](0019-one-vm-caddy-in-front-the-worker-schedules.md),
whose rule that one process schedules is the rule this ADR carries to two machines.

**Plan:** [Going public](../specs/2026-09-27-going-public-plan.md) §4 and §10b: the
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
The desktop's compose publishes Postgres on its tailnet address only (`compose/tailnet-db.yml`).
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
  `compose/tailnet-db.yml` added to its compose list, bound to `TAILNET_IP` from its `.env` and
  never to `0.0.0.0`. It keeps `compose/dev.yml`, so its own desk still reaches the database on
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
  while connected, or switch the laptop to `-Local` while working on a migration. *(Superseded by
  the amendment below: the remote desk no longer migrates, it refuses.)*
- `restore-data.ps1` and `publish-data.ps1` name only `compose/dev.yml`. Run on the desktop, the
  restore's `up -d postgres` recreates the container without the tailnet port until it is
  brought up again with the overlay. *(Fixed by the amendment below.)*
- The tailnet port binds only if the Tailscale address exists when the container starts. A boot
  where Docker starts before Tailscale leaves Postgres unpublished on the tailnet, or, depending
  on Docker Desktop, not started at all.
- Nothing is published. The repository's snapshot question (§4, decision 2) is unchanged, and
  becomes less pressing, since the snapshot is now refreshed only for the fallback.
- If this holds up, the snapshot could become a manual export rather than a regular commit,
  which takes the derived data out of the repository's history going forward.

## Amendment (2026-11-15): only the owner migrates, and the scripts keep the overlays

Two of the consequences above were the cost of trusting both machines to be on the same commit
and the scripts to know which machine they were on. Neither needs trusting.

**A desk on another machine's database does not migrate it.** The initializer asks the same
question the scheduler guard does, from the same classifier (`DatabasePlacement`, in the data
layer, which `SchedulerPlacement` now calls), and on a remote database it migrates nothing,
seeds nothing and cleans nothing; it only keeps the odds partitions ahead of the clock, as every
pull already does. It compares the migrations the database has applied with the
ones this code holds (`MigrationPlacement.Decide`, pure and tested branch by branch):

- the same set: the desk starts and uses the database as it is;
- this code holds migrations the database has not applied, so this code is newer: it refuses to
  start, names them, and says to migrate from the machine that owns the database;
- the database has migrations this code does not know, so this code is older: it refuses, and
  says to pull the newer commit;
- both at once, two branches that each added a migration: it refuses and names both sides.

The failure moves from the desktop, later and somewhere else, to the laptop, at startup, with
the remedy in the message. `Data:AllowRemoteMigration=true` is the deliberate way past it, for a
machine that is meant to migrate a database it reaches by a non-loopback name; with it the
initializer runs exactly as on a local database. The Worker initializes on every run, a single
`--job` included, so a press from a terminal on the laptop follows the same rule. A local
database is migrated exactly as before, without being asked anything first.

**The snapshot scripts start Postgres with the machine's own compose list.** `_pg.ps1`'s
`Get-ComposeFiles` returns `docker-compose.yml` and `compose/dev.yml`, and `compose/tailnet-db.yml`
when `.env` has an uncommented, non-empty `TAILNET_IP`: the machine that set its tailnet address
is the one the laptop reaches. `restore-data.ps1` brings Postgres up with that list, and the
messages in `publish-data.ps1` and `publish-journal.ps1` print it, so a restore on the desktop
no longer takes the tailnet port away. `XLINES_COMPOSE_FILES`, semicolon-separated, replaces the
list for a machine whose overlays are not the ones `.env` implies.

## Amendment, 2026-11-15: the database is forwarded, not published

Research against Tailscale's and Docker's own documentation (recorded in
`docs/specs/2026-11-14-multi-device-access-research.md` §6) found the overlay's
premise weak on the machine it was written for. Docker Desktop on Windows does not document
binding a published port to a VPN adapter's address; the address does not exist until
Tailscale connects, and Docker does not retry a bind that failed at boot; and the inbound
connection needs a firewall rule on the Tailscale interface.

So on the desktop Postgres stays where `compose/dev.yml` puts it, on `127.0.0.1:5433`, and
`scripts/serve-tailnet.ps1 -Database` asks Tailscale Serve to forward TCP 5433 on the tailnet
to it. Serve is tailnet-only, as it is for the desk (ADR 0022). `compose/tailnet-db.yml`
remains for a Linux host.

One thing moves with it. Behind the forward Postgres sees every client as loopback, so
`pg_hba.conf` can no longer tell the laptop from the phone. The tailnet policy does that: a
grant for `tcp:5433` from the laptop to the desktop and from nothing else.

The laptop's connection string gains `Keepalive=30;Tcp Keepalive=true`, for a connection that
crosses a sleeping lid or a relay.
