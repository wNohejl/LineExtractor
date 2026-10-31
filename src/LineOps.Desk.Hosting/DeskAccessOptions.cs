namespace LineOps.Desk.Hosting;

/// <summary>Who can reach this desk, and what they need to get in. See ADR 0018.</summary>
public enum DeskAccessMode
{
    /// <summary>
    /// Whoever reaches the desk operates it. Right for one person behind <c>127.0.0.1</c>, and
    /// the default so a fresh clone behaves as it always has.
    /// </summary>
    Open,

    /// <summary>
    /// A code decides: the operator's opens everything, the guest code opens a desk that reads
    /// and changes nothing. With <see cref="DeskAccessOptions.AllowAnonymousVisitors"/>, no code
    /// at all opens the guest desk too.
    /// </summary>
    Gated
}

/// <summary>
/// The gate's settings, bound from the <see cref="SectionName"/> section of configuration and
/// then from the delegate an application passes to
/// <see cref="DeskAccessEndpoints.AddDeskAccess"/>.
/// </summary>
public sealed class DeskAccessOptions
{
    public const string SectionName = "Access";

    public DeskAccessMode Mode { get; set; } = DeskAccessMode.Open;

    /// <summary>
    /// When gated, whether a visitor with no code gets the read-only desk rather than the
    /// sign-in page. Off until the data on the desk is data that may be shown to anyone.
    /// </summary>
    public bool AllowAnonymousVisitors { get; set; }

    /// <summary>
    /// The operator's code, hashed by <see cref="DeskAccessCode.Hash"/> — never the code itself.
    /// Required when gated.
    /// </summary>
    public string? OperatorCodeHash { get; set; }

    /// <summary>The code that opens the read-only desk, hashed. Optional: none means no guest code works.</summary>
    public string? GuestCodeHash { get; set; }

    /// <summary>How long a sign-in lasts without a visit before it has to be made again.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The name of the sign-in cookie. An application that already has signed-in visitors under
    /// another name passes that name here, so moving to this package signs nobody out.
    /// </summary>
    public string CookieName { get; set; } = "desk.access";

    /// <summary>The sign-in page, and the path its form posts to.</summary>
    public string SignInPath { get; set; } = "/signin";

    /// <summary>The path that signs a visitor out and returns them to <see cref="SignInPath"/>.</summary>
    public string SignOutPath { get; set; } = "/signout";
}

/// <summary>The two roles a signed-in desk can hold, as claim values.</summary>
public static class DeskAccessRoles
{
    public const string Operator = "operator";
    public const string Visitor = "visitor";
}
