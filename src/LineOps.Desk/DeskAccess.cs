namespace LineOps.Desk;

/// <summary>
/// What the person at this desk may do: read it, or also operate it.
///
/// <para>
/// The desk asks this and never answers it. An application that runs for one person on their
/// own machine leaves the default, <see cref="OpenDeskAccess"/>, in place; one that is reachable
/// by others registers its own, scoped, reading whoever signed in to the circuit. The answer is
/// read each time rather than captured, because a scoped service can be constructed before the
/// circuit's identity is known.
/// </para>
///
/// <para>
/// A visitor never sees a window marked <see cref="Windowing.WindowDefinition.OperatorOnly"/>:
/// <see cref="Windowing.WindowManager.Catalog"/> leaves them out, so the strip, the command
/// palette, workspaces, a restored layout and every follow-up lose them together. Controls that
/// write are the application's to hide, by asking <see cref="CanOperate"/> where it draws them.
/// Blazor Server only dispatches an event to a handler in the render tree it sent, so a control
/// that was not drawn cannot be invoked — hiding it is the enforcement, not a veneer over one.
/// </para>
///
/// <para>
/// The gate itself — whether there is one, and the pages that sign in and out of it — is the
/// application's too. The desk only links to it: the Window manager says who is at the desk and
/// offers the way in or out when <see cref="IsGated"/> is true. The three answers default to "no
/// gate", so an application with one person at the desk implements <see cref="CanOperate"/> and
/// nothing else.
/// </para>
/// </summary>
public interface IDeskAccess
{
    /// <summary>True when this desk may change things: log, pull, switch, resolve, inject.</summary>
    bool CanOperate { get; }

    /// <summary>
    /// True when the desk can be reached by more than one person and a code decides who operates
    /// it. Only then does the Window manager say who is at the desk.
    /// </summary>
    bool IsGated => false;

    /// <summary>The page a visitor signs in on, as a path; a page load, since the cookie it sets needs an HTTP response.</summary>
    string? SignInPath => null;

    /// <summary>The page that signs the operator out, as a path.</summary>
    string? SignOutPath => null;
}

/// <summary>A desk with one person at it, who may do everything. The default.</summary>
public sealed class OpenDeskAccess : IDeskAccess
{
    public static readonly OpenDeskAccess Instance = new();

    public bool CanOperate => true;
}
