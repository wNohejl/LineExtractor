namespace LineOps.Web.Access;

/// <summary>Who can reach this desk, and what they need to get in. See ADR 0018.</summary>
public enum AccessMode
{
    /// <summary>
    /// Whoever reaches the desk operates it. Right for one person behind <c>127.0.0.1</c>, which
    /// is what compose publishes, and the default so a clone behaves as it always has.
    /// </summary>
    Open,

    /// <summary>
    /// A code decides: the operator's opens everything, the guest code opens a desk that reads
    /// and changes nothing. With <see cref="AccessOptions.AllowAnonymousVisitors"/>, no code at
    /// all opens the guest desk too.
    /// </summary>
    Gated
}

public sealed class AccessOptions
{
    public const string SectionName = "Access";

    public AccessMode Mode { get; set; } = AccessMode.Open;

    /// <summary>
    /// When gated, whether a visitor with no code gets the read-only desk rather than the
    /// sign-in page. Off until the data on the desk is data that may be shown to anyone
    /// (going-public plan §4).
    /// </summary>
    public bool AllowAnonymousVisitors { get; set; }

    /// <summary>
    /// The operator's code, hashed by <see cref="AccessCode.Hash"/> — never the code itself.
    /// <c>scripts/new-access-code.ps1</c> makes one. Required when gated.
    /// </summary>
    public string? OperatorCodeHash { get; set; }

    /// <summary>The code that opens the read-only desk, hashed. Optional: none means no guest code works.</summary>
    public string? GuestCodeHash { get; set; }

    /// <summary>How long a sign-in lasts without a visit before it has to be made again.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>The two roles a signed-in desk can hold, as claim values.</summary>
public static class AccessRoles
{
    public const string Operator = "operator";
    public const string Visitor = "visitor";
}
