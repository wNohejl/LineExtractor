# Reaching the desk from more than one device — research and trials

**Date:** 2026-11-14
**Status:** Research done; the three trials and the second round are built and merged (§5, §6; ADRs 0022 to 0025). The open decisions in §5 and "What remains" in §6 are taken up by [ADR 0031](../adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md); the snapshot left the public repository on 2026-11-20 and the displayable spine was built as ADR 0026.
**Builds on:** the going-public plan (`2026-09-27-going-public-plan.md` §4, §10b), ADR 0018
(an operator and visitors), ADR 0019 (one VM, Caddy in front).

## 1. The constraint, restated

The desk is one person's tool on one machine. Its data has two licence tiers:

| Tier | Sources | Terms, as published | May be shown to others? |
|---|---|---|---|
| Displayable | The Odds API, balldontlie | Display in a UI allowed, commercial included; no resale as raw data | Yes |
| Personal use | ESPN undocumented JSON, MLB Stats API | Automated access prohibited (Disney); individual, non-commercial, non-bulk (MLBAM) | No. Same exposure as the laptop only |

A hosted public desk republishes the second tier, which is why the project stays local
(plan §10b). "Another device" is a different question from "another person": the developer
reading their own data from their own laptop is the same personal use, reachable from
somewhere else. The options below are ordered by how little they change that reading.

## 2. Options

| # | Option | Who else sees the data | Freshness on the laptop | Spends credits twice? | Cost | Verdict |
|---|---|---|---|---|---|---|
| A | **Committed snapshot** (today: `publish-data.ps1` / `restore-data.ps1`) | Anyone who clones the repository, which is public | As of the last publish | Yes unless the laptop's scheduler is off | 0 | Keep as the fallback. The public repo is the real exposure here (plan §4 item 3) |
| B | **Tailscale Serve** — desktop runs the desk; laptop and phone open `https://<desktop>.<tailnet>.ts.net` | Nobody. Serve is tailnet-only; Funnel would be public and is not used | Live | No. One desk, one scheduler | 0 (free tier covers 3 users / 100 devices) | **Recommended first step.** Trial 1 |
| C | **One shared Postgres over the tailnet** — laptop's desk reads the desktop's database | Nobody | Live | No, if the laptop's scheduler is forced off (the trial adds a guard) | 0 | Good when the laptop needs its own desk process (development). Trial 2 |
| D | **Cloudflare Tunnel + Access** (identity-gated, free under 50 users) | Cloudflare's edge terminates TLS and sees the pages | Live | No | 0 | Works, but the data transits a third party and the hostname is public. Prefer B |
| E | **Hosted Postgres** (Neon free tier, 1 GB/project as of 2026-10) | The provider holds ESPN-derived rows | Live | No | 0 | Puts personal-use data on someone else's servers. Not pursued |
| F | **Licence-tiered data in the application** — `Shared` mode shows visitors only the displayable tier | Visitors see odds, closes from the book market, CLV; never box scores or stat lines | n/a | n/a | code | The only option that makes a desk *other people* may see lawful. Trial 3; also the "restructure the data" the goal names |
| G | Remote Desktop / Parsec to the desktop | Nobody | Live | No | 0 | Works today with nothing built; poor on a phone |

Sources read 2026-11-14: Tailscale Serve vs Funnel guides ([ssdnodes](https://www.ssdnodes.com/learn/lang/uk/tailscale-serve-vs-funnel),
[openwebui](https://docs.openwebui.com/reference/https/tailscale.md)); Cloudflare Access plans
([cloudflare.com](https://www.cloudflare.com/en-au/products/cloudflare-access),
[tunnel + access walkthrough](https://somethinghitme.com/2026/01/04/cloudflare-tunnels-and-access-controls/));
Neon free plan ([neon.com FAQ](https://neon.com/faqs/free-plan-limits-and-quotas)); Disney terms summary
([conductatlas](https://conductatlas.com/platform/disney/disney-terms-of-use/provision/CA-P-022520/prohibition-on-automated-access-and-ai-scraping/)).
A reading of published terms, not legal advice.

## 3. Recommendation

1. **B now.** Install Tailscale on the desktop, laptop and phone; serve the desk from the
   desktop with `Access:Mode=Gated` and `Hosting:BehindProxy=true`. Nothing is published; one
   scheduler; the phone works. The snapshot (A) stays the fallback for when the desktop is off.
2. **C for development.** When the laptop must run its own desk process against live data,
   point it at the desktop's Postgres over the tailnet with its scheduler and evaluator off.
3. **F before anyone else is let in.** Tier the sources by licence and gate the readers, so a
   `Shared` desk can be shown to a reviewer without the personal-use tier. This is the data
   restructuring; it is also what Stage 1 of the going-public plan actually needs.
4. **Make the repository private** (plan §8 decision 2) regardless: the committed snapshot is
   the one live republication today.

## 4. Trials

Each runs on a short-lived branch in its own worktree under `.claude/worktrees/`, built by an
Opus subagent, and is merged into `LineX_Development` and deleted, or dropped, in the same
sitting (CLAUDE.md).

| Branch | Answers | Deliverables |
|---|---|---|
| `trial/tailnet-serve` | B | `compose/tailnet.yml`, `scripts/serve-tailnet.ps1`, forwarded-headers test, ADR 0022 |
| `trial/shared-database` | C | `compose/tailnet-db.yml`, `scripts/connect-remote-db.ps1`, a remote-database scheduler guard with tests, ADR 0023 |
| `trial/source-tiering` | F | `Source.Licence` + migration, `Sharing:Mode`, gated readers with tests, `publish-data.ps1 -DisplayableOnly`, ADR 0024 |

Results are recorded in §5 when the trials return.

## 5. Results (2026-11-14, all three merged into `LineX_Development`)

| Trial | Verdict | Verified | Not verified |
|---|---|---|---|
| Tailnet Serve (ADR 0022) | Viable; no `src/` change needed. Overlay publishes no ports; `AllowFunnel: false`; the host-side script reads port 5263 from the launch profile and refuses if Funnel is on | compose config renders; 6 DeskEdge tests green | Anything on a real tailnet: Tailscale is not installed on this machine |
| Shared database (ADR 0023) | Viable and small. `SchedulerPlacement` refuses to schedule or evaluate against a database that is not this machine's own unless `Ingestion:AllowRemoteScheduling=true`; the Worker exits 3 with the same message unless given a single `--job` | 30 tests green; Worker starts; connect script round-trips `appsettings.Local.json` under PowerShell 7 and 5.1 | A real tailnet connection; Docker binding to a 100.x address at boot; both desks migrate the one database, so both machines must sit on the same commit |
| Licence tiering (ADR 0024) | Necessary, not yet sufficient. `Source.Licence` (migration `20261114161918_SourceLicence`), `Sharing:Mode`, Board, GameLog and Matchup gated for a visitor in Shared mode; `publish-data.ps1 -DisplayableOnly` cleans a throwaway restore before dumping (0.5 MB, 3,210 Odds API closes kept, 22,226 ESPN closes dropped) | Full suites green on the branch (898 + 765); Worker starts; migration checked on a fresh database and on the committed dump | Web host in Shared+Gated with a real sign-in. Remaining readers: FightService, GamecastService, Game live-state and probable-pitcher columns, Player injury fields, PlayersPanel, SearchService, OddsPanel/MarketMovement, DataQuality. The schedule, scores and names themselves are still ESPN/MLB-derived |

Open decisions for the developer:

1. Make the repository private (plan §8 decision 2), or switch the committed snapshot to `-DisplayableOnly`.
2. Should the hosted `deploy/compose.yml` default to `Sharing:Mode=Shared`? It stays `Local`.
3. Should a desk on a remote database skip migrations at startup?
4. Should `restore-data.ps1` and `publish-data.ps1` honour the tailnet overlays (`COMPOSE_FILE`)?
5. Tag the Tailscale auth key and restrict the ACL to the laptop and phone.

## 6. Second round (2026-11-15): research against primary documentation, and what was built

Four Opus agents: one read Tailscale's, Docker's, PostgreSQL's and Npgsql's own documentation
and source; three implemented. All merged into `LineX_Development`; suites green (982, 803,
26, 979). Private use between the developer's own devices stays fully featured: nothing below
hides anything unless `Sharing:Mode=Shared` is set, which only a public desk would do.

### What the research changed

| Finding | Source | Change |
|---|---|---|
| An untagged node's key expires after about 180 days; a tagged node's does not, and a tagged node cannot be shared with another person's tailnet | tailscale.com/kb/1068/tags, /kb/1085/auth-keys, /kb/1084/sharing | `compose/tailnet.yml` advertises `tag:xlines` and signs in once (`TS_AUTH_ONCE`) |
| Docker Desktop on Windows does not document binding a port to a VPN address; the bind fails at boot when Docker starts first and is not retried | docs.docker.com/desktop/features/networking, Tailscale forum 4616 | `serve-tailnet.ps1 -Database` forwards TCP 5433 to loopback; `compose/tailnet-db.yml` kept for Linux |
| Serve's config schema, `${TS_CERT_DOMAIN}` substitution and WebSocket forwarding are as the trial assumed | tailscale `ipn/serve.go`, `cmd/containerboot/serve.go`, Go `httputil/reverseproxy.go` | None; the config directory is mounted rather than the file, as the docs ask |
| Without unattended mode Tailscale stops at Windows sign-out | tailscale.com/kb/1088/run-unattended | The script warns and names `tailscale set --unattended=true` |
| HTTPS certificates publish `<machine>.<tailnet>.ts.net` in certificate-transparency logs | tailscale.com/kb/1153/enabling-https | Default node name is the neutral `desk`; documented beside the setting |
| Traffic is end-to-end WireGuard, relays cannot read it; Tailscale gives no advice to add TLS inside a tailnet | tailscale.com/kb/1232/derp-servers, /blog/tls-certs | `SSL Mode=Disable` stays; `Keepalive=30;Tcp Keepalive=true` added |
| Taildrop is still alpha, works between a user's own untagged devices, lands in Downloads | tailscale.com/kb/1106/taildrop | Not adopted; the journal file travels by private repository or by hand |

Still unverified, because Tailscale is not installed on the desktop: everything on a real
tailnet, including whether Serve in the container resolves `http://web:8080` by Docker DNS name.

Tailnet policy to paste before first use (replace the allow-all default; no `funnel` attribute):

```json
{
  "tagOwners": { "tag:xlines": ["autogroup:admin"] },
  "hosts": { "desktop": "100.x.x.x", "laptop": "100.y.y.y" },
  "grants": [
    { "src": ["autogroup:member"], "dst": ["tag:xlines"], "ip": ["tcp:443"] },
    { "src": ["autogroup:member"], "dst": ["desktop"], "ip": ["tcp:443"] },
    { "src": ["laptop"], "dst": ["desktop"], "ip": ["tcp:5433"] }
  ]
}
```

### What was built

| Slice | Result |
|---|---|
| A desk on another machine's database (ADR 0023 amendment) | Never migrates it. Refuses to start when the schema is older or newer than its code, naming the migrations; `Data:AllowRemoteMigration=true` is the deliberate override. `restore-data.ps1` keeps the overlays `.env` implies |
| The journal merges (ADR 0025) | `SyncId` and `UpdatedAtUtc` on entries, a tombstone table for deletions, an append-only `JournalAudit` written by an interceptor, and `DataTransfers` logging each publish, restore, export and import. `journal:export` / `journal:import` write and merge a JSON file that names games by provider id, so it survives the two machines numbering games differently. Migration `JournalSync`; existing entries get an id derived from their row id and placement time, so both machines' copies of one bet agree |
| Licence tier, remaining readers (ADR 0024 amendment) | Gamecast, fights, a game's live-state and probable-pitcher columns, injuries and the panels' direct reads keep to the tier in Shared mode. A guard test fails when a new reader of a tiered table does not ask the tier |

### What remains

1. A first run on a real tailnet (checklist in ADR 0022).
2. The displayable spine: schedules, scores and names are still ESPN/MLB. The Odds API's free
   events endpoint and its scores endpoint (about 180 credits a month) cover fixtures and
   three days of scores, not players, abbreviations, UFC results or older history. Needed
   only before a public desk.
3. Nothing in the desk reads `JournalAudit` or `DataTransfers` yet; an Ops panel saying how
   stale this machine's data is would be their first reader.
4. The merge is last-writer-wins per row: a note edited on one machine is lost to a later
   settlement on the other, though the audit keeps it.
5. The repository is still public (plan §8 decision 2).
