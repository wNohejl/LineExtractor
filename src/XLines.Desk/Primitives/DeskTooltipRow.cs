namespace XLines.Desk.Primitives;

/// <summary>
/// One row of a structured <c>DeskTooltip</c>: a label leading, a value trailing in tabular
/// numerals, and an optional quiet qualifier after the value — "North  +185  −1.2% margin".
/// </summary>
/// <param name="Label">What the row is about, in words — a name, not a code.</param>
/// <param name="Value">The figure, right-aligned so a column of them reads down by place.</param>
/// <param name="Qualifier">A short secondary reading after the value, drawn quietly. Optional.</param>
/// <param name="Emphasis">
/// The row the tooltip is about — the best of the set, the one the trigger shows. Marked by
/// weight and full-strength text, never by hue alone. What makes it the one is for the caption
/// or the label to say, so the spoken text carries it too.
/// </param>
public sealed record DeskTooltipRow(string Label, string Value, string? Qualifier = null, bool Emphasis = false);
