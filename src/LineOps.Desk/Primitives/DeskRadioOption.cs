namespace LineOps.Desk.Primitives;

/// <summary>
/// One choice in a <c>DeskRadioGroup</c>.
/// </summary>
/// <param name="Value">The value choosing this option reports.</param>
/// <param name="Label">
/// What the option is called, in sentence case. It is the radio's accessible name when the group
/// draws the option itself, and what a template is handed to write when it draws its own.
/// </param>
/// <param name="Title">The tooltip, for the longer wording a short label leaves out.</param>
public sealed record DeskRadioOption<TValue>(TValue Value, string Label, string? Title = null);
