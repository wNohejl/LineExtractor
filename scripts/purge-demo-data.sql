-- Removes everything the demo fixture source ever wrote.
--
-- Run once, when a real odds provider is configured. The demo source stands itself down as soon
-- as one is (see IngestionServiceCollectionExtensions), but standing down stops it writing more
-- rather than removing what it already wrote — and fabricated prices sitting in the same table as
-- real ones, under a different source id, are indistinguishable to every reader downstream. The
-- board would shop them against each other.
--
-- Two different things are being removed, and only the second is destructive in a way worth
-- pausing over:
--
--   1. Prices the demo source invented. Always safe: they were never real.
--
--   2. Fixtures the demo source invented, from before it was taught to price the real schedule.
--      Identified as carrying a 'demo' external id and no 'espn' one — a game ESPN has never
--      confirmed exists. These are the out-of-season NBA/NFL/NHL matchups sitting on a July
--      slate. Anything referencing them goes too, because a journal entry against a game that
--      was never played is not a record of a wager.
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

-- The source row itself stays. It is referenced by historical IngestionRun rows, which are the
-- feed the reliability KPIs are computed from — deleting it would rewrite the past to say those
-- runs never happened. Disabling it says the true thing instead.
UPDATE "Sources" SET "Enabled" = false WHERE "Key" = 'demo';

\echo '-- after'
SELECT
    (SELECT count(*) FROM "OddsSnapshots" o JOIN "Sources" s ON s."Id" = o."SourceId"
      WHERE s."Key" = 'demo')                                              AS demo_prices,
    (SELECT count(*) FROM "Games"
      WHERE "ExternalIds" ? 'demo' AND NOT ("ExternalIds" ? 'espn'))       AS invented_games,
    (SELECT count(*) FROM "Games" WHERE "ExternalIds" ? 'espn')            AS real_games;

COMMIT;
