namespace XLines.Desk.Primitives;

/// <summary>
/// One choice in a <c>DeskPopUp</c>: what it is called and, where the choice is between values
/// that are compared, the value itself and what qualifies it.
/// </summary>
/// <param name="Value">What choosing it sets.</param>
/// <param name="Label">Its name, leading the row and the closed key; typeahead reads it.</param>
public sealed record DeskPopUpOption<TValue>(TValue Value, string Label)
{
    /// <summary>The value the option stands for, trailing in tabular figures, right-aligned down the list.</summary>
    public string? Figure { get; init; }

    /// <summary>A small state word beside the figure, such as "Best": on the row and on the closed key.</summary>
    public string? Mark { get; init; }

    public DeskState MarkState { get; init; } = DeskState.Positive;

    /// <summary>A secondary line under the name, for what qualifies the option.</summary>
    public string? Detail { get; init; }

    /// <summary>A second figure, under the first, in the state's ink: a difference worth comparing.</summary>
    public string? DetailFigure { get; init; }

    public DeskState DetailFigureState { get; init; } = DeskState.Neutral;

    /// <summary>
    /// The option as it is heard, when its drawn words would run together aloud. It re-words what
    /// is drawn and never adds to it: nothing may be known only by a screen reader.
    /// </summary>
    public string? Spoken { get; init; }

    /// <summary>Listed, dimmed, and not choosable; <see cref="DisabledReason"/> says why in the row.</summary>
    public bool Disabled { get; init; }

    public string? DisabledReason { get; init; }
}
