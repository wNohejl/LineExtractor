# ADR 0022 — The desk follows its operator onto the tailnet

**Status:** Accepted. Built as a trial on 2026-11-14 and merged into `LineX_Development`; not yet run on a real tailnet (checklist below).

**Builds on:** [ADR 0018](0018-an-operator-and-visitors.md), whose gate this puts in front of
every device but the desktop, and [ADR 0019](0019-one-vm-caddy-in-front-the-worker-schedules.md),
whose proxy-in-front posture this reuses with Tailscale in Caddy's place.

**Plan:** [Going public](../specs/2026-09-27-going-public-plan.md) §4 and §10b.

## Context

The project stays local (§10b). The sources that make it free, ESPN and the MLB Stats API, are
licensed for personal, non-commercial use, and §4 puts the line where it falls: personal use
behind a code is "the same exposure as the laptop, no more"; a desk the public can reach is not.

The operator uses the desk on a desktop and a laptop and would like it on a phone. The snapshot
workflow carries the data between the two computers, but a phone runs no Postgres, and a second
running copy of the desk is a second scheduler unless it is told not to be. What the operator
wants is the one desk, the desktop's, opened from wherever they are.

Putting it on the internet behind the gate would do that and would also republish ESPN-derived
data to anyone who finds the address. A code limits who signs in; it does not change that the
pages are served to the public.

## Decision

**The desk is reached over the operator's own Tailscale tailnet, with Tailscale Serve, never
Funnel.**

- A tailnet is a private network of the operator's own signed-in devices. Serve answers only on
  the node's tailnet address, with a certificate for its `*.ts.net` name; there is no public
  listener and no public DNS record to find. A phone on the tailnet opening the desktop's desk is
  the operator using their own copy from another room. Nothing is published, so §4's personal-use
  reading holds unchanged.
- Funnel would publish the same handler to the internet through Tailscale's relays. It is never
  used: `deploy/tailnet/serve.json` says `"AllowFunnel": false` for the one host it serves, and
  `scripts/serve-tailnet.ps1` takes the desk down rather than leave it served while Funnel is on.

**Two ways in, one posture.**

- From source on the desktop, `scripts/serve-tailnet.ps1` runs `tailscale serve --bg` from 443
  to the http launch profile's port on 127.0.0.1, and `-Off` stops it. The profile listens on
  localhost only, so only a process on the machine, tailscaled among them, reaches the port.
- In containers, `compose/tailnet.yml` overlays the laptop stack with a `tailscale/tailscale`
  sidecar, joined by an auth key from `.env`, state on a volume, userspace networking, and Serve
  configured from the committed JSON. Web listens on plain HTTP :8080 on the compose network and
  publishes nothing, exactly as behind Caddy (ADR 0019). The sidecar is its own node rather
  than sharing web's network namespace, so :8080 is never on the tailnet address beside Serve.

In both, web runs with `Hosting:BehindProxy`, so it believes the proxy's `X-Forwarded-For` and
`-Proto` (one hop: Serve appends the address it saw, and only that one is taken), and with
`Access:Mode` Gated. The tailnet is the operator's, but a shared node or an invited person is on
it too, and an open desk lets whoever reaches it spend odds credits and read the journal.

**Cloudflare Tunnel with Access was the alternative.** It also needs no open port, gates on
identity at the edge, and is free under 50 users. It was not chosen because the desk would then
be served from a public hostname on Cloudflare's edge: every request and page transits a third
party's network, and the address exists on the internet for anyone to try, with Access as the
only thing between it and the pages. That is a public deployment with a good lock, which is
the line §4 draws. Serve's traffic is WireGuard, encrypted end to end between the operator's
devices even when a relay carries it; Tailscale's coordination server knows which devices
exist, not what they say.

## Consequences

- One desk, one scheduler, one database. The phone and the laptop open the desktop's desk and
  spend nothing of their own; the snapshot workflow stays for working offline on the laptop.
- The desktop must be on and running the desk. That is the price of not having a server.
- `AllowedHosts` stays `*` by default. Serve passes the visitor's `Host` through, so setting it
  to `localhost;<machine>.<tailnet>.ts.net` (or `XLINES_TAILNET_HOST` for the compose overlay)
  refuses any other name; it is configuration ASP.NET Core already reads, so no code was added.
- Blazor's hub is same-origin and negotiates under the page's own URL. Serve terminates TLS and
  proxies the WebSocket upgrade as Caddy does, which ADR 0019 verified for the hosted case.
- Unverified until it is run against a real tailnet: the sidecar's hardening (`read_only`,
  `cap_drop: ALL` in userspace mode) and the WebSocket through Serve. If the sidecar fails to
  start, the hardening is the first thing to loosen, one line at a time.
- One-time setup is by hand: Tailscale on each device, MagicDNS and HTTPS certificates on in the
  admin console, an auth key in `.env` for the sidecar, and an operator code hash.
