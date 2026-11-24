using System.Text.Json;

namespace XLines.Desk.Windowing;

/// <summary>
/// A desk as it can be written down: what is open, in what order and at what share, the
/// operator's settings, and the workspaces they saved.
///
/// <para>
/// Kept in the browser (<c>localStorage</c>), so it survives a reload and a restarted server —
/// the circuit that used to hold it is gone at both — and belongs to the machine it was laid out
/// on. Widths are stored as weights, the same fractions of the row the manager works in, so a
/// layout saved on a monitor still fits a laptop.
/// </para>
/// </summary>
public sealed record DeskLayout
{
    /// <summary>Bumped when the shape changes; a layout from another version is ignored, not misread.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public int MaxConcurrentWindows { get; init; }
    public bool CeilingChosen { get; init; }
    public string? PrimaryWindowKey { get; init; }
    public double PrimaryShare { get; init; }
    public ResolutionMode ResolutionMode { get; init; }
    public int CustomWidth { get; init; }
    public int CustomHeight { get; init; }

    /// <summary>
    /// Added without a version bump: a layout written before scale existed has neither field, reads
    /// them as 0, and <see cref="DeskSettings.ClampScale"/> turns 0 into 100%. Bumping the version
    /// would instead have thrown every operator's saved row away to add two numbers.
    /// </summary>
    public double UiScale { get; init; }

    public double TextScale { get; init; }

    public IReadOnlyList<DeskLayoutWindow> Windows { get; init; } = [];

    /// <summary>Index into <see cref="Windows"/> of the focused one, if any.</summary>
    public int? Focused { get; init; }

    public IReadOnlyList<DeskLayoutWorkspace> Workspaces { get; init; } = [];

    /// <summary>
    /// The name of the workspace the desk was arranged from (<see cref="WindowManager.ActiveWorkspace"/>).
    /// Added without a version bump, as the scales were: a layout written before it reads null.
    /// </summary>
    public string? ActiveWorkspace { get; init; }

    /// <summary>
    /// Whether <see cref="ActiveWorkspace"/> names one the operator saved rather than one of the
    /// application's, so a saved workspace with an application workspace's name restores as itself.
    /// Optional: a layout without it reads false and looks in the application's first.
    /// </summary>
    public bool ActiveWorkspaceSaved { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialise() => JsonSerializer.Serialize(this, Json);

    /// <summary>Null for anything unreadable or from another version: a bad layout costs the layout, not the desk.</summary>
    public static DeskLayout? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var layout = JsonSerializer.Deserialize<DeskLayout>(json, Json);
            return layout?.Version == CurrentVersion ? layout : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// One open window. Parameters are the ids a window was opened on — an order, a customer.
///
/// <para>
/// <paramref name="Column"/> and <paramref name="Rank"/> place it in a tab group: the column's
/// index in the row, and its place in that column's strip, 0 for the tab showing. Added without a
/// version bump, as the scales were: a layout saved before tabs has neither, reads them as null,
/// and every window gets a column of its own — folded into tabs left to right if the row is
/// longer than the limit.
/// </para>
/// </summary>
public sealed record DeskLayoutWindow(
    string Key,
    string? TitleOverride,
    double Weight,
    bool Minimised,
    IReadOnlyDictionary<string, JsonElement>? Parameters,
    int? Column = null,
    int? Rank = null);

/// <summary>A workspace the operator saved: a name, the windows it opens, and the column each goes in (null before tabs).</summary>
public sealed record DeskLayoutWorkspace(string Name, IReadOnlyList<string> WindowKeys, IReadOnlyList<int>? Columns = null);
