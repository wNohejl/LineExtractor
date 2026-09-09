namespace LineOps.Web.Components.Desk;

/// <summary>
/// Which kind of nothing a panel is showing.
///
/// "Empty" reads like one state and is at least three. A panel that has never held
/// anything wants to explain what it is for; a panel emptied by a filter wants to say
/// which filter, so the reader looks at the control rather than at the feed; a panel
/// that failed to load wants to say so outright, because silence there reads as "no
/// data" and sends the reader to fix the wrong thing.
///
/// Naming the kind at the call site is what lets the desk answer all three consistently
/// — and makes the review question a real one: "is this Filtered or New?" is answerable,
/// "is this empty?" is not.
/// </summary>
public enum EmptyStateKind
{
    /// <summary>
    /// A plain note in the desk's quiet voice. Renders exactly as the hand-written
    /// <c>&lt;p class="empty"&gt;</c> always did — the right pick for a loading line, or
    /// wherever the sentence already carries the whole situation.
    /// </summary>
    Default,

    /// <summary>
    /// Nothing exists yet. Explain what would live here and why it is worth having;
    /// hang the first step off <c>Action</c>.
    /// </summary>
    New,

    /// <summary>
    /// Rows exist — this filter, search or window excluded all of them. Say which, and
    /// let <c>Action</c> put it back.
    /// </summary>
    Filtered,

    /// <summary>
    /// The load failed. Name what failed rather than reporting an absence; <c>Action</c>
    /// carries the retry. The only kind drawn differently, because it is the only one a
    /// reader would otherwise misread as "nothing here yet".
    /// </summary>
    Error
}
