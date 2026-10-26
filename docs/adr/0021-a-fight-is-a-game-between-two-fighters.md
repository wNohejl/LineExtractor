# ADR 0021 — A fight is a game between two fighters

**Status:** Accepted

**Builds on:** [ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md) — ESPN is
the stats port, odds come from a book market, and ESPN's close is a reference for what no market
priced.

## Context

The desk tracked two team leagues. The operator wanted UFC fights and the bets on them, from ESPN,
with a small fighter record but all of ESPN's fighter statistics and recent fights available for
comparison, and every fight week from UFC 300 (13 April 2024) held.

A fight differs from a game in four ways that matter to the schema: there are no teams; a card
holds a dozen bouts that share an event; the result is a winner, a method, a round and a clock
rather than a score; and the books list every MMA promotion under one key without saying which.

Spec: `docs/superpowers/specs/2026-10-26-ufc-fights-design.md`.

## Decision

**A bout is a `Game`; each fighter is a `Team` and a `Player` on it.** The red corner (ESPN's
order 1, also the side ESPN's odds call home) is the home side. A win scores 1–0, a draw 0–0, a no
contest nothing. Every reader that follows a side — head to head, a team's log and record, the
journal, the wager window, CLV, the board — works for a fight without knowing it is one, and a
bout's fight stats are an ordinary `PlayerGameStat` line, so results-owed and data quality read
them as they read a box score.

**What a game has no column for is a `Bout` row**, keyed by the game: card, segment, place on the
card, weight class, scheduled rounds, title, each record going in, and the result's outcome,
method, round and clock. `FightSports` is the one place that knows a sport is fought.

**A fighter profile is small in columns and whole in content.** `FighterProfile` types the facts a
tale of the tape opens on and keeps ESPN's career rates and every recorded fight with its full
36-column stat sheet as jsonb, unmodelled.

**ESPN's fight center is the box score.** MMA has no summary endpoint; the fight center returns a
whole card — results, order, fight stats and bios — in one request. ESPN's development shows filed
under the same league (Dana White's Contender Series, Road to UFC) are not read.

**Books attach; they never create.** A book's fight is matched to a bout ESPN holds by both
fighters in either corner, names folded for accents and suffixes, and its sides renamed to the
bout's fighters. A fight no bout matches — another promotion — is dropped.

**Grading follows the fight.** Moneyline: the winner wins, a draw pushes, a no contest voids. The
total is in rounds and graded on how long the fight lasted. Method and round props stay free-text.

## Consequences

- A card is bet all week and no book can put one on the desk, so the slate pass for a fought sport
  reads the week ahead, and the scheduler re-reads it every six hours.
- Fighter names collide (two Bruno Silvas have fought in the UFC). Team names stay unique per
  sport, so the second is stored qualified by weight class, and resolution never takes a name
  already claimed by another id from the same source.
- ESPN carries no odds for many recent cards; those bouts have no reference close, and nothing
  fabricates one.
- A fighter's career rates are ESPN's as of the last refresh, not as of a past bout; the tape says
  so. The per-fight averages and recent fights are cut at the bout.
