-- Removes the fixtures the demo source invented.
--
-- The demo sources themselves are gone from the codebase, and DatabaseInitializer deletes their
-- 'demo' and 'demo-stats' Sources rows — along with every price, stat line, run, checkpoint, KPI
-- row and alert recorded against them — on the next start. That part needs no script and no
-- decision: those rows were never real.
--
-- This script is what the application deliberately will not do on its own. Games the demo source
-- invented, from before it was taught to price the real schedule, are identified as carrying a
-- 'demo' external id and no 'espn' one — a game ESPN has never confirmed exists. These are the
-- out-of-season NBA/NFL/NHL matchups sitting on a July slate. Anything referencing them goes too,
-- including journal entries, because a wager recorded against a game that was never played is not
-- a record of anything. Deleting what someone wrote is their call, so it is run by hand.
--
-- The price and closing-line deletes below are kept as a belt-and-braces pass for a database
-- where the source row was disabled by an earlier version of this script rather than deleted.
--
-- Idempotent: re-running finds nothing left to do.

BEGIN;

\echo '-- before'
SELECT
    (SELECT count(*) FROM "OddsSnapshots" o JOIN "Sources" s ON s."Id" = o."SourceId"
      WHERE s."Key" = 'demo')                                              AS demo_prices,
    (SELECT count(*) FROM "Games"
      WHERE "ExternalIds" ? 'demo' AND NOT ("ExternalIds" ? 'espn'))       AS invented_games,
    (SELECT count(*) FROM "Games" WHERE "ExternalIds" ? 'espn')            AS real_games;

CREATE TEMP TABLE invented_games ON COMMIT DROP AS
SELECT "Id" FROM "Games"
WHERE "ExternalIds" ? 'demo' AND NOT ("ExternalIds" ? 'espn');

-- Prices first: fabricated numbers, wherever they landed.
DELETE FROM "ClosingLines" c
 USING "Sources" s WHERE s."Id" = c."SourceId" AND s."Key" = 'demo';

DELETE FROM "OddsSnapshots" o
 USING "Sources" s WHERE s."Id" = o."SourceId" AND s."Key" = 'demo';

-- Then the invented fixtures, and what hangs off them. Ordered so no foreign key is left
-- pointing at a row that has already gone.
DELETE FROM "ClosingLines"    WHERE "GameId" IN (SELECT "Id" FROM invented_games);
DELETE FROM "OddsSnapshots"   WHERE "GameId" IN (SELECT "Id" FROM invented_games);
DELETE FROM "PlayerGameStats" WHERE "GameId" IN (SELECT "Id" FROM invented_games);
DELETE FROM "JournalEntries"  WHERE "GameId" IN (SELECT "Id" FROM invented_games);
DELETE FROM "Games"           WHERE "Id"     IN (SELECT "Id" FROM invented_games);

-- The source rows are not touched here: DatabaseInitializer removes them, together with the runs,
-- checkpoints, KPI rows and alerts that referenced them, so the Ops panel stops reporting a feed
-- that no longer exists. Doing it in the application means an existing database self-heals on the
-- next start rather than waiting for someone to remember this file.

\echo '-- after'
SELECT
    (SELECT count(*) FROM "OddsSnapshots" o JOIN "Sources" s ON s."Id" = o."SourceId"
      WHERE s."Key" = 'demo')                                              AS demo_prices,
    (SELECT count(*) FROM "Games"
      WHERE "ExternalIds" ? 'demo' AND NOT ("ExternalIds" ? 'espn'))       AS invented_games,
    (SELECT count(*) FROM "Games" WHERE "ExternalIds" ? 'espn')            AS real_games;

COMMIT;
