using System.Text.Json;

namespace LineOps.Desk.Windowing;

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

    public IReadOnlyList<DeskLayoutWindow> Windows { get; init; } = [];

    /// <summary>Index into <see cref="Windows"/> of the focused one, if any.</summary>
    public int? Focused { get; init; }

    public IReadOnlyList<DeskLayoutWorkspace> Workspaces { get; init; } = [];

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

/// <summary>One open window. Parameters are the ids a window was opened on — a game, a team.</summary>
public sealed record DeskLayoutWindow(
    string Key,
    string? TitleOverride,
    double Weight,
    bool Minimised,
    IReadOnlyDictionary<string, JsonElement>? Parameters);

/// <summary>A workspace the operator saved: a name and the windows it opens.</summary>
public sealed record DeskLayoutWorkspace(string Name, IReadOnlyList<string> WindowKeys);
