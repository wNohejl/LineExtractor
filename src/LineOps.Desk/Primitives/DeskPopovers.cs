namespace LineOps.Desk.Primitives;

/// <summary>Which edge of its opener a popover's panel lines up with.</summary>
public enum DeskPopoverAlign
{
    /// <summary>The panel's leading edge under the opener's leading edge — the usual case.</summary>
    Start,

    /// <summary>
    /// The panel's trailing edge under the opener's trailing edge, for an opener near the right
    /// of the desk, where a leading-aligned panel would run off the screen.
    /// </summary>
    End
}

/// <summary>
/// The one popover a circuit has open.
///
/// <para>
/// The HIG asks for one popover at a time, and for a click on a second opener to close the
/// first and open the second in the same click. The hand-built menus this replaced each laid a
/// scrim over the desk to catch the outside click, so the second opener's first click landed on
/// the first menu's scrim and did nothing else. With no scrim, something has to remember which
/// popover is up so that opening another can put it away; that is all this does.
/// </para>
///
/// <para>
/// Scoped, like the rest of the desk's services: which menu is open is one operator's state,
/// and a singleton would let one operator's menu close another's.
/// </para>
/// </summary>
public sealed class DeskPopovers
{
    private DeskPopover? _current;

    /// <summary>The popover that is open now, if any.</summary>
    public DeskPopover? Current => _current;

    /// <summary>Records <paramref name="popover"/> as the open one, closing whichever was open before.</summary>
    public async Task OpenAsync(DeskPopover popover)
    {
        var previous = _current;
        _current = popover;

        if (previous is not null && !ReferenceEquals(previous, popover))
            await previous.CloseAsync();
    }

    /// <summary>Forgets <paramref name="popover"/> if it is the open one; a stale close changes nothing.</summary>
    public void Closed(DeskPopover popover)
    {
        if (ReferenceEquals(_current, popover))
            _current = null;
    }
}
