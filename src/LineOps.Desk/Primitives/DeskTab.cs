namespace LineOps.Desk.Primitives;

/// <summary>
/// One section of a window, as offered by <c>DeskTabs</c>.
/// </summary>
/// <param name="Value">The value this tab selects.</param>
/// <param name="Label">What the tab is called. Short — tabs share the bar's width equally.</param>
/// <param name="Available">
/// Whether the window has this section at all. A section it does not have is not offered:
/// the tab never renders, and with one section left the bar folds away. For a section the
/// window has but which is empty for now, use <paramref name="Enabled"/> instead, so the bar
/// keeps its shape. Either way the desk's rule holds: a control you can see is a control you
/// can use, or one that plainly says it cannot be used yet.
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
