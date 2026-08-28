namespace LineOps.Web.Components.Desk;

/// <summary>
/// What the placeholder is standing in for. Named after the shape of the real content,
/// because that is the only thing a caller has to get right: a placeholder that does not
/// match the final layout is noise rather than a promise.
/// </summary>
public enum DeskSkeletonShape
{
    /// <summary>A run of text — one bar per line, the last one short.</summary>
    Line,

    /// <summary>A solid element: a chart, a tile, a thumbnail.</summary>
    Block
}
