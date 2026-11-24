using XLines.Core.Contracts;
using XLines.Desk;
using XLines.Desk.Hosting;
using Microsoft.Extensions.Options;

namespace XLines.Web.Services;

/// <summary>Who may see data from a personal-use source. See ADR 0024.</summary>
public enum DataSharingMode
{
    /// <summary>
    /// Everyone at the desk sees everything. The default, so a laptop and a clone are unchanged:
    /// the one person at a local desk is the person the personal-use terms allow.
    /// </summary>
    Local,

    /// <summary>
    /// Others can reach this desk. Visitors see only what a displayable source supplied — the
    /// book market's odds and closes, and what is derived from them; the operator still sees all.
    /// </summary>
    Shared
}

/// <summary>
/// The <see cref="SectionName"/> section of configuration. It sits beside <c>Access</c>
/// (<see cref="DeskAccessOptions"/>, ADR 0018): access says who may change the desk, sharing
/// says what the rest may read.
/// </summary>
public sealed class DataSharingOptions
{
    public const string SectionName = "Sharing";

    public DataSharingMode Mode { get; set; } = DataSharingMode.Local;
}

/// <summary>
/// The web host's answer to <see cref="IDataTier"/>: a local desk sees everything, and a shared
/// one shows personal-use data to the operator alone. Read each time rather than captured, for
/// the reason <see cref="IDeskAccess.CanOperate"/> is: a scoped service can be built before the
/// circuit knows who signed in. <see cref="IDeskAccess"/> already fails closed, so this does too.
/// </summary>
public sealed class DeskDataTier(IOptions<DataSharingOptions> options, IDeskAccess access) : IDataTier
{
    public bool CanSeePersonalUseSources => Decide(options.Value.Mode, access.CanOperate);

    /// <summary>The rule on its own, so it can be pinned without a circuit.</summary>
    public static bool Decide(DataSharingMode mode, bool canOperate)
        => mode == DataSharingMode.Local || canOperate;
}

public static class DataSharingRegistration
{
    /// <summary>
    /// Binds <c>Sharing</c> and registers <see cref="DeskDataTier"/> for the readers. Shared mode
    /// refuses to start unless the desk is gated: an open desk makes everyone its operator, so
    /// "the operator alone" would be everyone, and the setting would promise what it cannot keep.
    /// </summary>
    public static IServiceCollection AddDataSharing(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration.GetSection(DataSharingOptions.SectionName).Get<DataSharingOptions>()?.Mode
                   ?? DataSharingMode.Local;

        if (mode == DataSharingMode.Shared && !configuration.IsDeskGated())
            throw new InvalidOperationException(
                "Sharing:Mode is Shared but Access:Mode is not Gated. A shared desk keeps personal-use " +
                "data to the operator, and an open desk has no one who is not the operator.");

        services.Configure<DataSharingOptions>(configuration.GetSection(DataSharingOptions.SectionName));
        services.AddScoped<IDataTier, DeskDataTier>();
        return services;
    }
}
