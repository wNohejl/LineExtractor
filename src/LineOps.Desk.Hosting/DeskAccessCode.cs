using System.Security.Cryptography;
using System.Text;

namespace LineOps.Desk.Hosting;

/// <summary>
/// Hashing and checking an access code.
///
/// <para>
/// PBKDF2 over SHA-256 at OWASP's 2023 iteration count, with a random salt per hash, written as
/// <c>pbkdf2-sha256.iterations.salt.hash</c> (salt and hash in base64) so the iteration count
/// can rise later without invalidating a stored code. Dots, not the usual dollar signs: Compose
/// reads <c>$600000</c> in a <c>.env</c> file as a variable and would quietly empty it.
/// Configuration holds only this string; the code itself is shown once, when it is made, and
/// is kept nowhere.
/// </para>
/// </summary>
public static class DeskAccessCode
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(code), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return $"{Scheme}.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Whether <paramref name="code"/> is the one <paramref name="stored"/> was made from. A
    /// missing or malformed stored value matches nothing, rather than throwing on a visitor's
    /// request.
    /// </summary>
    public static bool Matches(string? code, string? stored)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Whether a configured value is a hash this class can check, for startup validation.</summary>
    public static bool IsWellFormed(string? stored)
        => stored?.Trim().Split('.') is [Scheme, var n, _, _] && int.TryParse(n, out var i) && i > 0;
}
