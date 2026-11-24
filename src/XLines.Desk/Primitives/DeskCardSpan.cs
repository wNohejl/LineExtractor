namespace XLines.Desk.Primitives;

/// <summary>
/// How much of a <c>.wflow</c> flow's row a section card (<see cref="FormGroup"/>) takes. The flow
/// gives a window as many columns as its width allows, so a wide window gets more cards side by
/// side rather than wider ones; a card only leaves its track when what it holds is a grid of its own.
/// </summary>
public enum DeskCardSpan
{
    /// <summary>One track of the flow — every card that holds rows.</summary>
    Track,

    /// <summary>The whole row, at every width: a card whose body is a wrapping grid (a library of tiles).</summary>
    Row,

    /// <summary>
    /// The rest of the row after the flow's first card, once two tracks fit; the whole row below
    /// that. For the one large card that sits beside the card that leads the window.
    /// </summary>
    Rest
}
