namespace LineOps.Desk.Primitives;

/// <summary>
/// One section of a window, as offered by <c>DeskTabs</c>.
/// </summary>
/// <param name="Value">The value this tab selects.</param>
/// <param name="Label">What the tab is called. Short — tabs share the bar's width equally.</param>
/// <param name="Available">
/// Whether there is anything behind this tab. A section with nothing in it is not
/// offered at all: a tab that opens onto "no data" spends a click to say so, and the
/// desk's rule is that a control you can see is a control you can use.
/// </param>
/// <param name="Count">
/// How many items the section holds, when that is worth seeing from the bar. Drawn as a
/// <c>DeskBadge</c> after the label rather than written into it, so the tab's name stays the
/// same as the count moves. Null or zero: no badge.
/// </param>
/// <param name="Enabled">
/// Whether the section can be opened now. The other way of not offering a section: the tab
/// stays in the bar, in its place and with its name, so the bar does not change shape as a
/// section empties and fills — but it is disabled, dim, out of the arrow ring, and a click
/// does nothing. For a section that is sometimes empty (an inbox, a queue), this is the one to
/// use; <paramref name="Available"/> is for a section the window simply does not have.
/// </param>
public sealed record DeskTab<TValue>(TValue Value, string Label, bool Available = true, int? Count = null, bool Enabled = true);
