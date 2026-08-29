namespace LineOps.Web.Components.Desk;

/// <summary>
/// Where a panel heading sits relative to whatever is above it, stated as the relationship
/// rather than a pixel count.
///
/// Call sites used to spell this as an inline <c>margin-top</c>, and six different numbers
/// accumulated for what were only ever three intentions. Naming the intention means the
/// rhythm can be tuned in one place later.
/// </summary>
public enum PanelHeaderGap
{
    /// <summary>
    /// The heading opens the panel, or follows another heading — the stylesheet's own
    /// rules already space it, so nothing is added.
    /// </summary>
    Default,

    /// <summary>
    /// A sub-heading that belongs to the block it follows: a team inside a market, a
    /// split inside a form table. Close enough to read as part of the same thing.
    /// </summary>
    Nested,

    /// <summary>
    /// A new section of the panel, after content that has ended. The same air two
    /// stacked headings get from each other.
    /// </summary>
    Section
}
