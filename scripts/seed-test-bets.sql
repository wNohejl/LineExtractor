-- Seeds a handful of journal rows so the wager window, the slip and the Journal can be exercised
-- against real games while the journal is otherwise empty. See
-- docs/superpowers/specs/2026-09-26-slip-and-advisory-checks-design.md §10.
--
-- What it inserts, from games the database already holds:
--   * two pending moneyline bets on the same side of the next scheduled NFL game, an hour apart,
--     so the duplicate check fires on that side;
--   * one settled (Win) moneyline bet on the most recent Final game, so the grid and Performance
--     have a graded row;
--   * one pending two-leg parlay across the next two scheduled games (any league).
--
-- Every row carries the note below, and the first statement deletes rows carrying it, so the
-- script is idempotent and reversible in one run. Run by hand with psql; never by the app, and
-- never on a machine about to run publish-data.ps1 — a test bet in the snapshot is a test bet
-- on the laptop.
--
--   $pw = (Select-String -Path .env -Pattern '^POSTGRES_PASSWORD=(.+)$').Matches[0].Groups[1].Value.Trim()
--   Get-Content scripts/seed-test-bets.sql -Raw | docker exec -i -e "PGPASSWORD=$pw" lineops-postgres psql -U lineops -d lineops -X -v ON_ERROR_STOP=1
--
-- To take the test bets out again without adding them back:
--   'DELETE FROM "JournalEntries" WHERE "Note" = ''test bet (seed-test-bets.sql)''; DELETE FROM "Parlays" WHERE "Note" = ''test bet (seed-test-bets.sql)'';' |
--     docker exec -i -e "PGPASSWORD=$pw" lineops-postgres psql -U lineops -d lineops -X

BEGIN;

\set note '''test bet (seed-test-bets.sql)'''

DELETE FROM "JournalEntries" WHERE "Note" = :note;
DELETE FROM "Parlays"        WHERE "Note" = :note;

-- The next scheduled NFL game, home side.
CREATE TEMP TABLE next_nfl ON COMMIT DROP AS
SELECT g."Id" AS game_id, t."Name" AS outcome
FROM "Games" g
JOIN "Sports" s ON s."Id" = g."SportId"
JOIN "Teams"  t ON t."Id" = g."HomeTeamId"
WHERE s."Key" = 'nfl' AND g."Status" = 'Scheduled' AND g."StartsAt" > now()
ORDER BY g."StartsAt"
LIMIT 1;

-- The most recent Final game, home side — graded as a win regardless of the score; this is a
-- row for the grid to draw, not a claim about the game.
CREATE TEMP TABLE last_final ON COMMIT DROP AS
SELECT g."Id" AS game_id, t."Name" AS outcome, g."StartsAt" AS starts_at
FROM "Games" g
JOIN "Teams" t ON t."Id" = g."HomeTeamId"
WHERE g."Status" = 'Final'
ORDER BY g."StartsAt" DESC
LIMIT 1;

-- The next two scheduled games in any league, for the parlay's legs.
CREATE TEMP TABLE next_two ON COMMIT DROP AS
SELECT g."Id" AS game_id, t."Name" AS outcome
FROM "Games" g
JOIN "Teams" t ON t."Id" = g."AwayTeamId"
WHERE g."Status" = 'Scheduled' AND g."StartsAt" > now()
ORDER BY g."StartsAt"
LIMIT 2;

-- Two bets, same side, an hour apart: the duplicate check's case.
INSERT INTO "JournalEntries"
    ("GameId", "Market", "Outcome", "Book", "LineTaken", "PriceTaken", "Stake", "PlacedAt", "Result", "Note")
SELECT game_id, 'h2h', outcome, 'draftkings', NULL::numeric, -130, 50, now() - interval '3 hours', 'Pending', :note
FROM next_nfl
UNION ALL
SELECT game_id, 'h2h', outcome, 'draftkings', NULL::numeric, -128, 25, now() - interval '2 hours', 'Pending', :note
FROM next_nfl;

-- One graded row: $100 at -110 returns $190.91.
INSERT INTO "JournalEntries"
    ("GameId", "Market", "Outcome", "Book", "LineTaken", "PriceTaken", "Stake", "PlacedAt",
     "Result", "Payout", "SettledAt", "Note")
SELECT game_id, 'h2h', outcome, 'fanduel', NULL::numeric, -110, 100, starts_at - interval '2 hours',
       'Win', 190.91, starts_at + interval '4 hours', :note
FROM last_final;

-- One parlay, two legs at -110 each: combined +264 on $25.
INSERT INTO "Parlays" ("Id", "Book", "Stake", "PriceQuoted", "PlacedAt", "Result", "Note")
SELECT '11111111-2222-4333-8444-555555555555'::uuid, 'betmgm', 25, NULL, now() - interval '1 hour', 'Pending', :note
WHERE (SELECT count(*) FROM next_two) = 2;

INSERT INTO "JournalEntries"
    ("GameId", "Market", "Outcome", "Book", "LineTaken", "PriceTaken", "Stake", "PlacedAt", "Result", "Note", "ParlayGroupId")
SELECT game_id, 'h2h', outcome, 'betmgm', NULL::numeric, -110, 0, now() - interval '1 hour', 'Pending', :note,
       '11111111-2222-4333-8444-555555555555'::uuid
FROM next_two
WHERE (SELECT count(*) FROM next_two) = 2;

\echo '-- seeded'
SELECT "Market", "Outcome", "Book", "PriceTaken", "Stake", "Result", "ParlayGroupId" IS NOT NULL AS leg
FROM "JournalEntries" WHERE "Note" = :note ORDER BY "PlacedAt";

COMMIT;
