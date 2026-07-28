namespace LineOps.Web.Components.Desk;

/// <summary>
/// What a key does, stated as its consequence rather than its colour.
///
/// The desk's rule is that hue means state everywhere, so a key names the outcome and
/// the palette follows — a caller writes <c>Tone.Stop</c>, never "red". That keeps the
/// meaning stable when the palette moves, and it makes a review question answerable:
/// "should this be Stop?" is a real question, "should this be red?" is not.
/// </summary>
public enum DeskTone
{
    /// <summary>Graphite. The default: does something, costs nothing.</summary>
    Neutral,

    /// <summary>Iris. The one key in a group that carries the intent — save, apply, open.</summary>
    Action,

    /// <summary>Steam. Starts work, or confirms a healthy outcome — ingest, sync, resolve.</summary>
    Go,

    /// <summary>Drift. Destroys, cancels in-flight work, or is hard to undo.</summary>
    Stop,

    /// <summary>Flag. Proceeds, but the operator should know the cost first.</summary>
    Caution
}

/// <summary>How much of the panel a key claims. Chrome is small, a page's intent is large.</summary>
public enum DeskKeySize
{
    Small,
    Medium,
    Large
}
