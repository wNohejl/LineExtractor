using LineOps.Core.Entities;
using LineOps.Web.Components.Desk;

namespace LineOps.Web.Components;

/// <summary>
/// How the desk's recurring outcomes read as a <see cref="DeskState"/>.
///
/// These are the mappings that more than one view had reached independently — did the team win,
/// did the side cover — and the reason they live together is that they are judgements, not
/// formatting. "An unfinished game is not a loss" and "a push is not a failure to cover" are
/// decisions about what the operator is being told, and a decision made in six files is a
/// decision that will eventually disagree with itself.
///
/// This sits beside the views rather than in <c>Desk/</c> because it knows about games and
/// entries. The primitives underneath it stay ignorant of the domain, which is what lets them
/// be reused by anything.
/// </summary>
public static class TagTones
{
    /// <summary>
    /// Won, lost, or not yet decided.
    ///
    /// Null is deliberately not <see cref="DeskState.Negative"/>: a game that has not finished
    /// has not been lost, and colouring it as a loss would put a red column in front of an
    /// operator scanning for real ones.
    /// </summary>
    public static DeskState Won(bool? won) => won switch
    {
        true => DeskState.Positive,
        false => DeskState.Negative,
        _ => DeskState.Info
    };

    /// <summary>
    /// A settled bet's outcome — used for against-the-spread and total results.
    ///
    /// Push takes <see cref="DeskState.Info"/> rather than being rounded toward either side,
    /// because a push returns the stake: it is genuinely neither result, and saying so is more
    /// useful than picking the nearer one.
    /// </summary>
    public static DeskState Result(EntryResult result) => result switch
    {
        EntryResult.Win => DeskState.Positive,
        EntryResult.Loss => DeskState.Negative,
        _ => DeskState.Info
    };
}
