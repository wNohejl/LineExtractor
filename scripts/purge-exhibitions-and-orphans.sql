-- Removes the All-Star Game, and any team or player left referencing nothing.
--
-- EspnStatsAdapter now rejects competition type ALLSTAR at the boundary. This clears the one
-- that landed before it did, plus the wreckage two earlier purges left behind.
--
-- The All-Star Game is not merely a game that did not count. Because its competitors are not
-- franchises, resolving it *created* two: "American All-Stars" and "National All-Stars" sat in
-- the team list beside the thirty real ones, each with a one-game record, each reachable from
-- the team drill-down.
--
-- Orphans are then swept generically rather than by name. A team with no games and a player
-- with no stat lines are rows nothing can reach — the eight NFL teams the demo source's invented
-- fixtures left behind are the same shape of problem as the All-Star pair, and one rule removes
-- both. This is the invariant StatsIngestionService enforces on the way in, applied to what is
-- already stored.
--
-- Idempotent: re-running finds nothing left to do.

BEGIN;

CREATE TEMP TABLE allstar ON COMMIT DROP AS
SELECT DISTINCT g."Id"
FROM "Games" g
JOIN "Teams" t ON t."Id" IN (g."HomeTeamId", g."AwayTeamId")
WHERE t."Name" LIKE '%All-Stars';

DELETE FROM "ClosingLines"    WHERE "GameId" IN (SELECT "Id" FROM allstar);
DELETE FROM "OddsSnapshots"   WHERE "GameId" IN (SELECT "Id" FROM allstar);
DELETE FROM "PlayerGameStats" WHERE "GameId" IN (SELECT "Id" FROM allstar);
DELETE FROM "JournalEntries"  WHERE "GameId" IN (SELECT "Id" FROM allstar);
DELETE FROM "Games"           WHERE "Id"     IN (SELECT "Id" FROM allstar);

DELETE FROM "Teams" t
WHERE NOT EXISTS (
    SELECT 1 FROM "Games" g WHERE g."HomeTeamId" = t."Id" OR g."AwayTeamId" = t."Id");

DELETE FROM "Players" p
WHERE NOT EXISTS (
    SELECT 1 FROM "PlayerGameStats" s WHERE s."PlayerId" = p."Id");

COMMIT;

SELECT sp."Key" AS sport, count(*) AS teams
FROM "Teams" t JOIN "Sports" sp ON sp."Id" = t."SportId"
GROUP BY 1 ORDER BY 1;
