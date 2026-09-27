namespace LineOps.Desk.Windowing;

/// <summary>
/// The application's catalogue as the person at this desk may see it.
///
/// <para>
/// An operator sees it whole. A visitor's has no operator-only window in it, and no workspace
/// arrangement that names one — a workspace keeps the windows it may show and disappears if
/// that is none of them. Access is read on every call, never captured: the manager is built
/// when the circuit is, which can be before the circuit knows who signed in.
/// </para>
/// </summary>
internal sealed class AccessCatalog(IWindowCatalog inner, IDeskAccess access) : IWindowCatalog
{
    public IReadOnlyList<WindowDefinition> All => access.CanOperate
        ? inner.All
        : inner.All.Where(d => !d.OperatorOnly).ToList();

    public IReadOnlyList<Workspace> Workspaces => access.CanOperate
        ? inner.Workspaces
        : inner.Workspaces
            .Select(w => w with { WindowKeys = w.WindowKeys.Where(k => Find(k) is not null).ToList() })
            .Where(w => w.WindowKeys.Count > 0)
            .ToList();

    /// <summary>
    /// The application's primary, unless a visitor may not open it — then the first window a
    /// visitor can open cold, so an empty desk still has something to show.
    /// </summary>
    public string DefaultPrimary => Find(inner.DefaultPrimary) is not null
        ? inner.DefaultPrimary
        : All.FirstOrDefault(d => !d.RequiresSubject)?.Key ?? inner.DefaultPrimary;

    public WindowDefinition? Find(string key) => inner.Find(key) is { } definition
        && (!definition.OperatorOnly || access.CanOperate)
            ? definition
            : null;
}
