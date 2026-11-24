namespace XLines.Desk.Theming;

/// <summary>
/// Which face the desk is set in, chosen.
///
/// <para>
/// Two tokens carry it: <c>--face-ui</c>, everything, and <c>--face-display</c>, the titles (a
/// window's name, a panel's title, a sheet's and an alert's heading, the empty desk's headline). A
/// typeface is one block that sets both. Every stack is local — faces the operating system already
/// has, named in the order Apple's, Windows' and the rest of the world's machines carry them — so
/// choosing one fetches nothing. The bundled Inter belongs to System alone.
/// </para>
///
/// <para>
/// Each one has to look different on Windows 11, where the owner reads it: Humanist lands on
/// Calibri, Serif on Sitka (Cambria where it is missing), Mono on Cascadia Mono (Consolas). There is
/// no Rounded: Windows carries no rounded face of its own, and a stack that fell through to Segoe UI
/// there would be a choice that changed nothing. Figures stay tabular in every face, because the
/// columns that need them say so themselves.
/// </para>
/// </summary>
public enum DeskTypeface
{
    /// <summary>SF on a Mac, the bundled Inter everywhere else. The default, and stamps nothing.</summary>
    System,

    /// <summary>A humanist sans — Seravek, Gill Sans Nova, Calibri — stamped as <c>data-font="humanist"</c>.</summary>
    Humanist,

    /// <summary>A text serif with a display cut for titles — New York, Charter, Sitka — <c>data-font="serif"</c>.</summary>
    Serif,

    /// <summary>The whole desk in a monospace — SF Mono, Cascadia Mono, Consolas — <c>data-font="mono"</c>.</summary>
    Mono
}
