# The documents, and which ones are true now

XLines is the product; XLines is the platform and the name in every path
([ADR 0031](adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md)). This page says
which document answers which question, and which are kept only as the record of how something
was decided. As of 2026-11-23.

## Current: these describe the system as it is

| Question | Read |
|---|---|
| What is it, and how do I run it? | [README](../README.md); for an agent, [`xlines-setup`](../.claude/skills/xlines-setup/SKILL.md) |
| What runs, where, and on what? | [INFRASTRUCTURE.md](INFRASTRUCTURE.md) |
| Why is it built this way, and what changed during the build? | [DESIGN.md](DESIGN.md) (§11 for the divergences) |
| An alert fired. What now? | [runbook.md](runbook.md) |
| Where is it going: the name, the repository, the site, hosting? | [ADR 0031](adr/0031-xlines-the-name-its-own-repository-and-where-1-0-runs.md) |
| How is it hosted, if it is? | [deploy/README.md](../deploy/README.md), [ADR 0019](adr/0019-one-vm-caddy-in-front-the-worker-schedules.md) |
| How does an agent or a contributor work in this repository? | [CLAUDE.md](../CLAUDE.md) |
| How is the Desk package used on its own? | `src/XLines.Desk*/README.md` |

## Decisions: `adr/`

One decision each, numbered in the order taken. A later ADR that amends an earlier one says so
in its header. By subject:

| Subject | ADRs |
|---|---|
| Data model and odds storage | 0001, 0002, 0003, 0010 |
| Sources, budget and jobs | 0005, 0009, 0011, 0012, 0014, 0017, 0021, 0026 |
| Reliability and telemetry | 0015 |
| The desk and its design system | 0004, 0007, 0008, 0013, 0016, 0020, 0027, 0028, 0029, 0030, 0032 |
| Security, access and who may see what | 0006, 0018, 0024 |
| Hosting and other devices | 0019, 0022, 0023, 0025 |
| The product: name, repository, release | 0031 |

## Record: `specs/`, `plans/`, `evidence/`

Dated research (`specs/`), implementation plans (`plans/`) and measured results (`evidence/`).
Each was true on its date and is not kept up to date; where one disagrees with a document above,
the document above is right. The ones still worth opening:

| Document | Why |
|---|---|
| [Going-public plan](specs/2026-09-27-going-public-plan.md) (2026-09-27) | The reading of each provider's terms (§4), the hosted architecture (§3), the product and platform analysis (§6, §7). Its phases and open decisions are superseded by ADR 0031 |
| [Multi-device access research](specs/2026-11-14-multi-device-access-research.md) (2026-11-14) | Why the tailnet, the shared database and the licence tiers; what was verified and what was not |
| [MLB and NFL seasons research](specs/2026-09-04-mlb-nfl-seasons-research.md) (2026-09-04) | §2 holds the coverage queries `CLAUDE.md` requires for a data change |
| [XLines 1.0 styling triage](specs/2026-11-22-xlines-1-0-styling.md) (2026-11-22) | What is left to style before 1.0, and which outside proposals were turned down and why |
| [Desk theming contrast audit](specs/2026-11-20-desk-theming-contrast-audit.md) (2026-11-20) | The measured contrast of every theme family |
| [Ticket-tracker desk reuse plan](specs/2026-09-11-ticket-tracker-desk-reuse-plan.md) (2026-09-11) | What the Desk packages were extracted for |
| [XLines 1.0 styling evidence](evidence/2026-11-22-xlines-1-0-styling.md) (2026-11-22) | What the brand and the five styling items measured in a browser |
| [Desk-final evidence](evidence/2026-10-desk-final.md) (2026-10) | What the desk finalisation measured |

Everything else in those three folders is the design and plan for a feature that has since been
built, changed or dropped; the ADR for that feature is the place to start.

## Not in this repository

- The journal, parlays, operator settings and the database snapshot: the owner's private
  repository, cloned at `data/personal` ([ADR 0025](adr/0025-the-journal-merges-and-says-what-changed.md),
  `CLAUDE.md`).
- Any provider's data. This repository carries none.
