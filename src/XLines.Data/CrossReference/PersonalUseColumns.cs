using XLines.Core.Contracts;
using XLines.Core.Entities;

namespace XLines.Data.CrossReference;

/// <summary>
/// The columns a personal-use source wrote onto a row the desk otherwise shows, blanked for a
/// reader who may not see them (ADR 0024, amendment of 2026-11-15).
///
/// <para>
/// <see cref="DataTierQueries.FromVisible{T}"/> leaves out a row a personal-use source supplied.
/// These are not rows: ESPN's state of play and MLB's probable pitchers are columns on
/// <see cref="Game"/>, and a bout's card is a row with no <c>SourceId</c> because ESPN is the only
/// source that writes one. A reader that projects such an entity for the desk passes it through
/// here, so the condition is written once and a visitor's game is the fixture and its score —
/// the spine — with nothing ESPN or MLB said about it.
/// </para>
///
/// <para>
/// Each method changes the entity it is handed, in place, and returns it. Only ever hand it an
/// untracked entity (<c>AsNoTracking</c>): a tracked one would save the blanks. For a reader who
/// may see everything it changes nothing, so a local desk is exactly as it was.
/// </para>
/// </summary>
public static class PersonalUseColumns
{
    /// <summary>
    /// The game with ESPN's state of play and MLB's probable pitchers cleared when
    /// <paramref name="tier"/> may not see personal-use sources. The teams, the start, the status
    /// and the score stay: they are the schedule every reader hangs off (see the ADR's spine).
    /// </summary>
    public static Game ForTier(this Game game, IDataTier tier)
    {
        if (tier.CanSeePersonalUseSources)
            return game;

        // MLB Stats API.
        game.HomeProbablePitcher = null;
        game.AwayProbablePitcher = null;

        // ESPN's scoreboard: the whole state-of-play block on Game.
        game.Period = null;
        game.DisplayClock = null;
        game.ClockSeconds = null;
        game.StatusDetail = null;
        game.PossessionTeamId = null;
        game.Down = null;
        game.Distance = null;
        game.YardLine = null;
        game.DownDistanceText = null;
        game.PossessionText = null;
        game.IsRedZone = null;
        game.HomeTimeouts = null;
        game.AwayTimeouts = null;
        game.LastPlay = null;
        game.Linescore = null;
        game.Situation = null;
        game.LiveUpdatedAt = null;

        return game;
    }

    /// <summary>
    /// The bout with ESPN's card cleared when <paramref name="tier"/> may not see personal-use
    /// sources: the event, the segment and place on it, the weight, the distance, the records going
    /// in and how it ended. What stays is that the game is a fight — the desk bills it red corner
    /// first and offers the tale of the tape — and its outcome, which is to a bout what the final
    /// score is to a game. Its game, when loaded, is cleared by <see cref="ForTier(Game, IDataTier)"/>.
    /// </summary>
    public static Bout? ForTier(this Bout? bout, IDataTier tier)
    {
        if (bout is null || tier.CanSeePersonalUseSources)
            return bout;

        bout.EventName = string.Empty;
        bout.EventShortName = null;
        bout.CardSegment = null;
        bout.MatchNumber = null;
        bout.WeightClass = null;
        // Zero rather than the entity's default of three: "3 rounds" on a five-round main event
        // would be a fact nobody supplied. The Game window draws no distance for zero.
        bout.ScheduledRounds = 0;
        bout.TitleFight = false;
        bout.HomeRecord = null;
        bout.AwayRecord = null;
        bout.Method = null;
        bout.MethodDetail = null;
        bout.EndRound = null;
        bout.EndSeconds = null;

        bout.Game?.ForTier(tier);

        return bout;
    }
}
