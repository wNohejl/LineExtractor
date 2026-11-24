namespace XLines.Desk.Theming;

/// <summary>
/// One position of a <see cref="DeskLook"/>: what is stored, what is stamped, what the operator reads.
/// </summary>
/// <param name="Stored">What is kept in <c>localStorage</c> — the enum member's name — read case-insensitively.</param>
/// <param name="Attribute">
/// The value stamped on <c>&lt;html&gt;</c>, or null for the default: what every block already
/// declares is not a choice the inspector should show.
/// </param>
/// <param name="Label">The word on the Window manager's gate.</param>
public sealed record DeskLookOption(string Stored, string? Attribute, string Label);

/// <summary>
/// One appearance setting that belongs to every family and changes the desk's make rather than its
/// palette — the glass, the corners, the air, the ground, the face — in one row: ThemeService, the
/// boot script, the Window manager and the tests all read this and nothing else, so adding a setting
/// is adding a row to <see cref="DeskLooks.All"/> and a block to the stylesheet (README, "Adding an
/// appearance setting").
/// </summary>
/// <remarks>
/// The mode and the family are not looks: the mode resolves against the machine, the family carries
/// its palettes and an accent list, and both read keys from before the Desk carried its own name.
/// </remarks>
/// <param name="Label">The Window manager's row, and its gate's accessible name.</param>
/// <param name="StorageKey">The <c>localStorage</c> key the choice is kept under.</param>
/// <param name="Attribute">The attribute stamped on <c>&lt;html&gt;</c>: <c>data-shape</c>.</param>
/// <param name="Options">Every position in the gate's order, the default — the one stamping nothing — first.</param>
/// <param name="YieldsTo">
/// A machine setting, as its media query, under which the look stamps nothing and is not offered —
/// the glass under Reduce Transparency. Null for a look no machine setting takes off. One of
/// <see cref="MachineSettings"/> or the look throws as it is built.
/// </param>
public sealed record DeskLook(
    string Label,
    string StorageKey,
    string Attribute,
    IReadOnlyList<DeskLookOption> Options,
    string? YieldsTo = null)
{
    /// <summary>
    /// The machine settings a look may yield to: a closed set, because naming one is a promise in
    /// three places. theme.js must watch the query so a change reaches ThemeService, ThemeService
    /// must answer it, and the boot script asks the browser whatever it is handed. A query only the
    /// boot script heard would take a look off at the first paint and put it back when the circuit
    /// opened. Today that is Reduce Transparency alone; theme.js's other two queries are the mode's
    /// and the family's, not a look's.
    /// </summary>
    public static IReadOnlyList<string> MachineSettings { get; } = [DeskLooks.ReducedTransparency];

    /// <summary>The machine setting that takes the look off, held to <see cref="MachineSettings"/>; null for none.</summary>
    public string? YieldsTo { get; } = YieldsTo is null || MachineSettings.Contains(YieldsTo)
        ? YieldsTo
        : throw new ArgumentException(
            $"{Label} yields to {YieldsTo}, which is not a machine setting the desk watches: {string.Join(", ", MachineSettings)}.",
            nameof(YieldsTo));

    /// <summary>The position a fresh browser has: the one that stamps nothing.</summary>
    public DeskLookOption Default => Options.First(option => option.Attribute is null);

    /// <summary>
    /// The position stored as this, or null when it is no choice. Whatever is stored is either a
    /// name this version defines, read case-insensitively, or not a choice at all — and a number is
    /// never a name, so "1" is refused outright, as the mode's and the family's are.
    /// </summary>
    public DeskLookOption? Find(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || int.TryParse(stored, out _))
            return null;

        var wanted = stored.Trim();
        return Options.FirstOrDefault(option => string.Equals(option.Stored, wanted, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The appearance settings that are looks, in the order the Window manager shows them.</summary>
public static class DeskLooks
{
    /// <summary>The machine's Reduce Transparency, as theme.js and the boot script ask for it.</summary>
    public const string ReducedTransparency = "(prefers-reduced-transparency: reduce)";

    /// <summary>What the materials are made of (<see cref="DeskGlass"/>). Taken off by Reduce Transparency.</summary>
    public static DeskLook Glass { get; } = Of("Glass", "desk.glass", "data-glass", DeskGlass.Clear,
        [(DeskGlass.Clear, "Clear"), (DeskGlass.Tinted, "Tinted")], ReducedTransparency);

    /// <summary>The desk's corners (<see cref="DeskShape"/>).</summary>
    public static DeskLook Shape { get; } = Of("Corners", "desk.shape", "data-shape", DeskShape.Standard,
        [(DeskShape.Standard, "Standard"), (DeskShape.Sharp, "Sharp"), (DeskShape.Round, "Round")]);

    /// <summary>The desk's air (<see cref="DeskDensity"/>).</summary>
    public static DeskLook Density { get; } = Of("Density", "desk.density", "data-density", DeskDensity.Standard,
        [(DeskDensity.Standard, "Standard"), (DeskDensity.Compact, "Compact"), (DeskDensity.Roomy, "Roomy")]);

    /// <summary>
    /// The desk's ground (<see cref="DeskBackdrop"/>). Dots, the grid the desk has always drawn, is
    /// the default, so it leads its gate.
    /// </summary>
    public static DeskLook Backdrop { get; } = Of("Backdrop", "desk.backdrop", "data-backdrop", DeskBackdrop.Dots,
        [(DeskBackdrop.Dots, "Dots"), (DeskBackdrop.Plain, "Plain"), (DeskBackdrop.Gradient, "Gradient"), (DeskBackdrop.Grid, "Grid")]);

    /// <summary>The desk's face (<see cref="DeskTypeface"/>). Stamped as <c>data-font</c>, the name it always had.</summary>
    public static DeskLook Typeface { get; } = Of("Typeface", "desk.font", "data-font", DeskTypeface.System,
        [(DeskTypeface.System, "System"), (DeskTypeface.Humanist, "Humanist"), (DeskTypeface.Serif, "Serif"), (DeskTypeface.Mono, "Mono")]);

    /// <summary>Every look, in the Window manager's order: the glass, then the four makes.</summary>
    public static IReadOnlyList<DeskLook> All { get; } = [Glass, Shape, Density, Backdrop, Typeface];

    /// <summary>
    /// A look over an enum: each member stored by its name and stamped as its name in lower case,
    /// the default stamping nothing.
    /// </summary>
    private static DeskLook Of<T>(string label, string key, string attribute, T byDefault,
        (T Value, string Label)[] options, string? yieldsTo = null) where T : struct, Enum
        => new(label, key, attribute,
            options.Select(o => new DeskLookOption(
                o.Value.ToString(),
                EqualityComparer<T>.Default.Equals(o.Value, byDefault) ? null : o.Value.ToString().ToLowerInvariant(),
                o.Label)).ToList(),
            yieldsTo);
}
