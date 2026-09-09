namespace LineOps.Web.Components.Desk;

/// <summary>
/// One position on a <c>DeskSwitch</c>. Label is what the gate engraves on the channel;
/// Title carries the full wording for the tooltip when the label is an abbreviation.
/// </summary>
public sealed record DeskSwitchOption<TValue>(TValue Value, string Label, string? Title = null);

/// <summary>
/// Option sets more than one panel spends. A lookback window that reads "14d / 30d / 90d"
/// in one window and "2w / 1m / 3m" in another would make the same question look like two.
/// </summary>
public static class GateOptions
{
    public static readonly IReadOnlyList<DeskSwitchOption<int>> LookbackDays =
    [
        new(14, "14d", "Last 14 days"),
        new(30, "30d", "Last 30 days"),
        new(90, "90d", "Last 90 days")
    ];
}
