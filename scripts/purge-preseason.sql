-- Removes exhibition games ingested before the adapter learned to exclude them.
--
-- EspnStatsAdapter now drops season type 1 at the boundary, so nothing new arrives. This clears
-- what landed before that, which is not cosmetic: a spring training game is a real ESPN event
-- with a real box score, so it reads as an ordinary game everywhere downstream. Its effects are
--
--   * team records counting wins and losses from games that were never standings games;
--   * player form averaging at-bats taken against minor-league pitching in the seventh inning of
--     a March exhibition, alongside ones that counted;
--   * a "last 5" strip that can be entirely exhibition in the opening week of a season.
--
-- Identified by date rather than by a stored season type, because the rows predate the field
-- being read at all. The cutoff is MLB's 2026 opening day, confirmed against ESPN directly:
-- 24 March returns events marked preseason, 25 March returns the opener. The bound is
-- per-sport, so leagues whose seasons start elsewhere in the calendar are untouched.
--
-- Idempotent: re-running finds nothing left to do.

BEGIN;

CREATE TEMP TABLE exhibition ON COMMIT DROP AS
SELECT g."Id"
FROM "Games" g
JOIN "Sports" s ON s."Id" = g."SportId"
WHERE s."Key" = 'mlb'
  AND g."StartsAt" < TIMESTAMPTZ '2026-03-25 00:00:00+00';

\echo '-- removing'
SELECT
    (SELECT count(*) FROM exhibition)                                        AS games,
    (SELECT count(*) FROM "PlayerGameStats" WHERE "GameId" IN (SELECT "Id" FROM exhibition)) AS stat_rows;

DELETE FROM "ClosingLines"    WHERE "GameId" IN (SELECT "Id" FROM exhibition);
DELETE FROM "OddsSnapshots"   WHERE "GameId" IN (SELECT "Id" FROM exhibition);
DELETE FROM "PlayerGameStats" WHERE "GameId" IN (SELECT "Id" FROM exhibition);
DELETE FROM "JournalEntries"  WHERE "GameId" IN (SELECT "Id" FROM exhibition);
DELETE FROM "Games"           WHERE "Id"     IN (SELECT "Id" FROM exhibition);

-- Players whose only appearances were exhibitions now have none. They were created because they
-- appeared in a box score, which is the rule StatsIngestionService enforces on the way in; with
-- that box score gone the row is one nothing will ever read again.
DELETE FROM "Players" p
WHERE NOT EXISTS (SELECT 1 FROM "PlayerGameStats" s WHERE s."PlayerId" = p."Id");

\echo '-- after'
SELECT
    min("StartsAt")::date AS earliest_mlb_game,
    count(*)              AS mlb_games
FROM "Games" g JOIN "Sports" s ON s."Id" = g."SportId" WHERE s."Key" = 'mlb';

COMMIT;
