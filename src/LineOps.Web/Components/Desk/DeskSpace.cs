namespace LineOps.Web.Components.Desk;

/// <summary>
/// A rung on the desk's 4px spacing ramp.
///
/// One ramp, named steps, no pixels at the call site. The gaps in the numbering are
/// deliberate: between 16px and 24px there is a decision to make, between 16px and
/// 20px there is only a preference, and a scale that offers both collects both.
/// Mirrors <c>--space-1</c> … <c>--space-16</c> in lineops.css.
/// </summary>
public enum DeskSpace
{
    /// <summary>No gap. For a row whose children own their own separation.</summary>
    None,

    /// <summary>4px — items that belong to one another, like a value and its unit.</summary>
    Space1,

    /// <summary>8px — the desk's default gap, and what a bare .row already uses.</summary>
    Space2,

    /// <summary>12px — related controls that are still separate controls.</summary>
    Space3,

    /// <summary>16px — between groups inside one panel section.</summary>
    Space4,

    /// <summary>24px — between sections of a panel.</summary>
    Space6,

    /// <summary>32px — between things the operator reads at different times.</summary>
    Space8,

    /// <summary>48px — page-level separation.</summary>
    Space12,

    /// <summary>64px — the largest rung; if you want more, you want a divider.</summary>
    Space16
}

/// <summary>Cross-axis alignment for a <c>DeskRow</c> / <c>DeskStack</c>.</summary>
public enum DeskAlign
{
    /// <summary>The row's default: children centred against one another.</summary>
    Center,

    Start,
    End,
    Stretch,

    /// <summary>Text of different sizes sitting on one line.</summary>
    Baseline
}

/// <summary>Main-axis distribution for a <c>DeskRow</c>.</summary>
public enum DeskJustify
{
    Start,

    /// <summary>Pushes the row to the right — the existing <c>.row--end</c> idiom, named.</summary>
    End,

    Center,

    /// <summary>Label on the left, control on the right.</summary>
    Between
}
