using Microsoft.AspNetCore.Components;

namespace XLines.Desk.Primitives;

/// <summary>
/// One thing you can do from an open row.
///
/// <para>
/// An action normally answers its question <i>on the row</i>, in a snippet, and offers a way
/// out to a full view only if the snippet is not enough. That ordering is the whole point of
/// the pattern: glancing at a side-by-side record or an item's last five should not cost a
/// window slot, because at the column limit a new slot covers a column as a tab (ADR 0027),
/// and browsing must not rearrange the desk.
/// </para>
///
/// <para>
/// Both halves are optional and mean different things. A <see cref="Snippet"/> with no
/// <see cref="OpenWindow"/> is complete on the row — there is nothing more to show. An
/// <see cref="OpenWindow"/> with no snippet is a destination rather than a glance: a full record
/// is somewhere to work, not something to peek at, so it opens directly.
/// </para>
/// </summary>
public sealed record RowAction
{
    /// <summary>Identifies the action within its row. Also what the row records as open.</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>A MudBlazor icon string, drawn through <c>Glyph</c> like every other key.</summary>
    public required string Icon { get; init; }

    /// <summary>How much weight this action claims in the row's action bar.</summary>
    public DeskEmphasis Emphasis { get; init; } = DeskEmphasis.Plain;

    /// <summary>Whether this action destroys something. Renders in red.</summary>
    public DeskRole Role { get; init; } = DeskRole.Normal;

    /// <summary>
    /// What opens inline underneath. Null for an action that only opens a window.
    /// </summary>
    public RenderFragment? Snippet { get; init; }

    /// <summary>
    /// The snippet draws section cards of its own (several FormGroups in a <c>.wflow</c>), so its
    /// <c>SnippetShell</c> is an unframed pane header over them rather than the card around them:
    /// a card inside a card is forbidden. Off, the shell is the card and the snippet its rows.
    /// </summary>
    public bool SnippetCards { get; init; }

    /// <summary>
    /// The escape to a full view. Rendered by <c>SnippetShell</c> when there is a snippet, and
    /// invoked immediately on press when there is not. Default (no delegate) for an action with
    /// nowhere further to go.
    ///
    /// <para>
    /// An <see cref="EventCallback"/>, made by the host with <c>EventCallback.Factory.Create(this,
    /// …)</c>, not a plain delegate: the press is the strip's event, and a delegate run from it
    /// redrew only the strip. A command that changes its host's own state — a take opened under
    /// the row, a key marked busy — then showed nothing until something else redrew the host, and
    /// the key read as dead. The callback carries its owner, so the host is drawn after every
    /// command, as it is after any event of its own.
    /// </para>
    /// </summary>
    public EventCallback OpenWindow { get; init; }

    /// <summary>
    /// The work this command started is still running: its key shimmers and takes no further
    /// press, so a press on the row is answered on the row rather than only somewhere above it.
    /// </summary>
    public bool Busy { get; init; }

    /// <summary>Labels the escape, e.g. "Full history". Defaults to "Open window".</summary>
    public string? OpenLabel { get; init; }
}
