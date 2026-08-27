using Microsoft.AspNetCore.Components;

namespace LineOps.Web.Components.Desk;

/// <summary>
/// One thing you can do from an open row.
///
/// <para>
/// An action normally answers its question <i>on the row</i>, in a snippet, and offers a way
/// out to a full view only if the snippet is not enough. That ordering is the whole point of
/// the pattern: glancing at a head-to-head record or a player's last five should not cost a
/// window slot, because spending one evicts something (ADR 0007) and browsing must not
/// rearrange the desk.
/// </para>
///
/// <para>
/// Both halves are optional and mean different things. A <see cref="Snippet"/> with no
/// <see cref="OpenWindow"/> is complete on the row — there is nothing more to show. An
/// <see cref="OpenWindow"/> with no snippet is a destination rather than a glance: team data
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

    /// <summary>What pressing this does. Name the consequence, not the colour.</summary>
    public DeskTone Tone { get; init; } = DeskTone.Neutral;

    /// <summary>
    /// What opens inline underneath. Null for an action that only opens a window.
    /// </summary>
    public RenderFragment? Snippet { get; init; }

    /// <summary>
    /// The escape to a full view. Rendered by <c>SnippetShell</c> when there is a snippet, and
    /// invoked immediately on press when there is not.
    /// </summary>
    public Func<Task>? OpenWindow { get; init; }

    /// <summary>Labels the escape, e.g. "Full history". Defaults to "Open window".</summary>
    public string? OpenLabel { get; init; }
}
