# ADR 0018 — An operator and visitors: a gated desk that reads without writing

**Status:** Accepted

**Builds on:** [ADR 0007](0007-window-manager-ui.md), whose catalogue is the one place every
window is reached from, and [ADR 0006](0006-container-security-and-https.md), whose loopback-only
binding this ADR is the first step away from.

**Plan:** [Going public](../superpowers/specs/2026-09-27-going-public-plan.md) §5.

## Context

The desk had no identity at all. It was reachable only on `127.0.0.1`, so the only person who
could open it was the person who owned the machine, and every control assumed that person. Pull
data spent odds credits. The Ops window switched automatic polling on, injected faults and
opened incidents. History started a backfill. The Journal logged and deleted bets.

The going-public plan puts the desk on a server where other people can reach it: first
reviewers holding a code, later anyone. The first visitor to a desk built like that could spend
the month's credits in one press, and could read the operator's bets and bankroll.

Accounts are the usual answer and the wrong one at this stage. They bring a user table,
password resets and an owner column on every personal row. The plan defers all of that to a
product that may never be built (§5.2). What the showcase needs is smaller. There are two kinds
of person at the desk. One may change things. The other may only read, and must never see
anything personal.

## Decision

**Two roles and three modes, set by `Access:Mode`.**

- `Open` (the default) registers nothing. Whoever reaches the desk operates it, so a clone
  and the laptop run exactly as before.
- `Gated` sends a visitor without a code to a sign-in page. The operator's code opens the
  whole desk. The guest code opens a desk that reads and changes nothing. With
  `AllowAnonymousVisitors`, no code at all opens the guest desk too.

**The desk asks and never answers.** `LineOps.Desk` defines `IDeskAccess.CanOperate`, and
`WindowDefinition.OperatorOnly` marks a window as personal. The desk's default is
`OpenDeskAccess`. The web host registers `CircuitDeskAccess`, which reads the circuit's
signed-in user each time and fails closed. A state that is not yet known, or that throws,
reads as a visitor.

**Personal windows are filtered at the catalogue, not at each button.** `WindowManager.Catalog`
wraps the application's catalogue in `AccessCatalog`, so a visitor's copy has no operator-only
window in it. The strip, the command palette, workspaces, a restored layout, follow-ups and every
shortcut all read that one list. A link a panel forgot to hide therefore resolves to nothing,
not to the window. `WindowManager.Open` refuses an operator window on a visitor's desk outright,
because arriving there means a control was drawn that should not have been.

Journal, Performance, Slip and Place wager are operator-only. The Game window stops reading the
journal for a visitor, so "Your bets on this game" has nothing to draw.

**Controls that write are not drawn for a visitor.** These are hidden:

- the pull menu and the board's pull and wager actions
- price cells as doors to the wager window
- in Ops: evaluate, the polling switch, "Open incident" and the drill
- incident notes, the RCA editor and resolution; a visitor reads a resolved incident's RCA
- History's walk controls
- Players' stats sync

Blazor Server dispatches an event only to a handler in the render tree it sent, so a control that
was not drawn cannot be invoked from the browser. Hiding it is the enforcement. The run methods
behind the pull menu check again anyway, since those are the ones that spend.

**Codes, hashed, and a cookie.** Configuration holds `Access:OperatorCodeHash` and optionally
`Access:GuestCodeHash`, never a code. Each hash is PBKDF2-SHA-256 at 600,000 iterations with a
per-hash salt, written `pbkdf2-sha256.<iterations>.<salt>.<hash>`. The separator is a dot
because Compose would read `$600000` in a `.env` file as a variable. `scripts/new-access-code.ps1`
makes a code, prints it once and prints the hash. A test checks a hash from the script against
`AccessCode`, so the two implementations stay one format.

Sign-in is a plain HTML form posted to a minimal endpoint, because a cookie is set on an HTTP
response and a circuit has none. The form is antiforgery-protected and rate-limited to five
attempts a minute per address. The cookie it sets is HTTP-only, Secure and `SameSite=Lax`, and
slides over 14 days. Gated mode refuses to start without a well-formed operator hash.

## Consequences

- The desk can be put where others reach it without handing them the controls. Stage 1 of the
  plan (the invite-only showcase) needs nothing else from the application.
- There is still no user. Everything personal is the operator's, which is true today and
  stops being true only with accounts. At that point `OperatorOnly` becomes "owned", and
  `CanOperate` becomes a question about the row rather than about the desk.
- A guest code is shared, not personal: it can be passed on and cannot be revoked for one
  person. Rotating it means making a new hash and restarting. That is acceptable for a
  showcase and is the reason accounts come before a product.
- A new panel that writes must ask `Manager.Access.CanOperate` where it draws its control. The
  catalogue protects windows; a control inside a window visitors may see is the panel's to hide.
