# UFC: fights, fighters and the bets on them

Date: 2026-10-26. Goal set by the operator the same day: track UFC fights and the bets on them,
with ESPN as the source, a small fighter record, ESPN's full fighter statistics and recent fights
for comparison, and every fight week from UFC 300 (13 April 2024) onward held.

## Why

The desk holds two team leagues. A fight has no teams: two people meet once, on a card of a
dozen bouts, and the result is a winner, a method, a round and a time rather than a score. The
bets are the same shape as a team sport's — a moneyline on a side, an over/under on a number —
so the journal, CLV and the board all apply, as long as a fight can be read as a game.

## What ESPN gives (probed 2026-10-26)

| Endpoint | One request returns |
|---|---|
| `site.api.espn.com/apis/site/v2/sports/mma/ufc/scoreboard?dates=YYYYMMDD` | The cards on that US-Eastern day (usually none, sometimes one); each card's bouts with fighter ids, names, records, weight class, scheduled rounds, status, round and clock. No method, no card order. |
| `site.web.api.espn.com/apis/common/v3/sports/mma/ufc/fightcenter/{eventId}` | The whole card: per bout its segment (Main Card / Prelims / Early Prelims), match number (1 = main event), note ("Main Event - Title Fight"), title belts, status, **result** (KO/TKO · Punch, Submission · Rear Naked Choke, Decision - Split), round and clock, each fighter's bio (height, reach, stance, age, country, weight class, record going in) and **fight stats** (KD, total, significant, head, body, leg, control time, takedowns, submissions). |
| `sports.core.api.espn.com/v2/sports/mma/leagues/ufc/events/{e}/competitions/{c}/odds` | Per bout, by book: open and close moneyline per fighter (`homeAthleteOdds`/`awayAthleteOdds`, home = ESPN's order 1) and the rounds total with its over/under prices. |
| `site.web.api.espn.com/apis/common/v3/sports/mma/athletes/{id}` | A fighter's bio, nickname, gym, W-L-D with (T)KO and submission splits, and **every recorded fight** — UFC and before — with date, event, opponent, W/L, method, round and clock, title flag. |
| `site.web.api.espn.com/apis/common/v3/sports/mma/athletes/{id}/stats` | ESPN's **full per-fight stat sheet** for every one of those fights: 36 columns across striking (distance head/body/leg landed/attempted, totals, knockdowns, target shares), clinch (strikes, reversals, slam rate, takedowns) and ground (strikes, advances, submissions). |
| `sports.core.api.espn.com/v2/sports/mma/athletes/{id}/statistics` | The career rates: significant strikes landed per minute, striking accuracy, takedown average and accuracy, submission average, KO/TKO/decision percentages. |

`site.api.espn.com/.../summary` returns 404 for MMA, so the box-score path a team sport uses does
not exist; the fight center replaces it and is one request per card rather than one per game.

## Decisions

### A fight is a game between two fighters

- **Sport** `ufc`, seeded beside the four majors; `FightSports.IsFight(key)` is the one place
  that says a sport is fought rather than played.
- **A fighter is a `Team` row** — the competitor in one corner — keyed by ESPN's athlete id. The
  red corner (ESPN's order 1) is `Home`, the blue corner `Away`. The abbreviation is the
  surname. Every reader that already follows a side — head to head, a team's log and record,
  the journal's game search, the board's chips — works for a fighter unchanged.
- **The fighter is also a `Player` row on that `Team`**, so a bout's fight stats are an ordinary
  `PlayerGameStat` line. Results-owed, data quality ("finals without stats") and the box score
  all read the line they already read.
- **A bout is a `Game`**, starting at its card segment's start. A finished bout scores **1–0** to
  its winner, **0–0** for a draw, and no score for a no contest. That is what makes a fighter's
  record (W-L-D) and head to head fall out of the existing readers, and what grades a moneyline:
  a draw pushes, as US books settle a two-way fight line.
- **A `Bout` row** (one per fight `Game`) holds what a game has no column for: the card (ESPN
  event id, name, short name), segment, match number, weight class, scheduled rounds, title
  fight, each fighter's record going in, and the result — outcome (win, draw, no contest),
  method, method detail, round, clock.

### A fighter record is small; ESPN's statistics are kept whole

`FighterProfile` (one per fighter `Team`) carries the few facts a comparison leads with as
columns — nickname, country, gym, stance, weight class, height, reach, date of birth, record —
and ESPN's two statistics payloads as jsonb, unmodelled: the career rates and the fight history
with each fight's full stat sheet merged onto it. Nothing in ESPN's sheet is dropped and nothing
is given a column it would only fill for one sport. A profile is refreshed when its fighter has
fought since it was taken, and taken for any fighter on a card ahead.

### Odds attach to a bout; they never create one

The Odds API lists every MMA promotion under `mma_mixed_martial_arts` and does not say which.
A book's fight is therefore matched to a UFC bout ESPN already holds — both fighters, either
corner, within a day and a half — and dropped when there is none. Names are compared with
accents folded ("Jiří Procházka" is "Jiri Prochazka") and fall back to surname plus first
initial. A matched outcome is stored under the bout's own fighter name, so the board pairs it.

ESPN's per-bout close (moneyline and rounds total) is the reference close for every bout no
market priced, exactly as for a team sport (ADR 0011): written under ESPN's source, never mixed
into a market.

### Grading a fight

- **Moneyline:** the winner wins, a draw pushes, a **no contest voids** the bet.
- **Total (rounds):** a fight that ends at 3:14 of round 1 lasted 0.65 rounds; one that goes
  the distance lasted every scheduled round. Over 1.5 wins once the fight passes 2:30 of round
  2. Halves cannot land on the number, so a total pushes only on a whole-number line reached
  exactly, which the books do not hang.
- Method-of-victory and round props are logged as free-text markets, as any prop is today, and
  settled by hand.

### Backfill: every fight week from UFC 300

`Backfill:Seasons:ufc = 2024-04-13`. The day walk reads one scoreboard per day (most are empty)
and, on a card day, the fight center and each bout's odds. The fighter profiles are a separate
pass — three requests a fighter, for every fighter who has fought since UFC 300 — run after the
walk, by the same job the results sweep uses after each card.

### The desk

- **Board:** a bout reads as two fighter chips with each record going in beneath; the score
  column states the result (winner heavier, method and round), and the card name leads the
  row's tags. Spread folds away (fights have none); Over/Under is the rounds total.
- **Game window:** the header names the card, segment, weight class, rounds and title; a
  finished bout says how it ended. Its tabs are Lines, **Tale of the tape** and **Fight stats**.
- **Tale of the tape** (also the Board's Form): the two fighters side by side — record, age,
  height, reach, stance, gym, then ESPN's career rates, then averages per fight over each
  fighter's recorded fights (significant strikes landed and absorbed, takedowns, control time,
  knockdowns), then the last five fights each, then any common opponents and how each fared.
  The better reading of each row is marked, as a reading, not a recommendation.
- **Fighter window:** a fighter name opens the Team window, which for a fighter shows the
  profile, the career rates, and the whole recorded history with each fight's stat sheet one
  disclosure away.

## Non-goals

- Other promotions (PFL, Bellator, ONE). The odds feed carries them; nothing stores them.
- Round-by-round stats and judges' scorecards.
- Method and round props as priced markets.
- A spread market for fights.

## Verification

- Unit: fight center parsing (UFC 300 fixture), athlete-odds closes, fight grading (finish
  inside the line, distance, draw, no contest), fighter name matching.
- Postgres: bouts and fighters persist and re-walk idempotently; an odds fight resolves onto an
  ESPN bout in either corner and is dropped when there is none.
- Data: every UFC event from UFC 300 to the last completed card held, every final bout with a
  result method and fight stats, no bout stuck Live, a close on every bout ESPN priced.
