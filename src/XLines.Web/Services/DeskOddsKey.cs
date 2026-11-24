using System.Net;
using XLines.Ingestion.Services;
using Microsoft.AspNetCore.DataProtection;

namespace XLines.Web.Services;

/// <summary>
/// The Odds API key an operator types into the desk (Ops, Odds feeds), so getting prices does
/// not mean editing <c>.env</c> and starting the container again.
///
/// <para>
/// The key is checked against the provider's free sports list before it is kept, so a mistyped
/// key, or one from the similarly named odds-api.io, is refused at the field rather than
/// discovered as a board with no prices. A check spends no credit.
/// </para>
///
/// <para>
/// It is kept encrypted (Data Protection) in one file beside the Data Protection keys, and
/// never in the database: a key in a table would travel in every snapshot taken with
/// <c>-IncludePersonal</c> and in every journal export. It is this desk's alone. A Worker run
/// separately still takes its key from configuration.
/// </para>
/// </summary>
/// <param name="reconcile">
/// Run after the key changes: which sources are configured has changed with it, and the reference
/// rows follow (<see cref="ReferenceReconciler.ReconcileAsync"/>).
/// </param>
public sealed class DeskOddsKey(
    IDataProtectionProvider protection,
    RuntimeOddsKey runtime,
    Func<CancellationToken, Task> reconcile,
    IHttpClientFactory http,
    IConfiguration configuration,
    ILogger<DeskOddsKey> logger)
{
    /// <summary>The named client the check is made with.</summary>
    public const string CheckClient = "the-odds-api-key-check";

    public const string StorePathKey = "Odds:KeyStorePath";

    private const string FileName = "the-odds-api.key";

    private readonly IDataProtector _protector = protection.CreateProtector("XLines.DeskOddsKey.v1");

    /// <summary>The last four characters of the key in force here, or null when none is stored.</summary>
    public string? Hint => runtime.TheOddsApi is { Length: > 0 } key ? key[^Math.Min(4, key.Length)..] : null;

    private string StorePath
    {
        get
        {
            var directory = configuration[StorePathKey] is { Length: > 0 } configured ? configured
                : configuration["DataProtection:KeyPath"] is { Length: > 0 } keys ? keys
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LineOps");

            return Path.Combine(directory, FileName);
        }
    }

    /// <summary>
    /// Reads the stored key at start. A file that cannot be read back, because the Data
    /// Protection keys it was sealed with are gone, is no key: the desk starts unpriced and says
    /// so in Ops, which is better than not starting.
    /// </summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return;

            runtime.TheOddsApi = _protector.Unprotect(File.ReadAllText(StorePath).Trim());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The stored odds key could not be read; enter it again in Ops");
        }
    }

    /// <summary>Asks the provider whether it accepts <paramref name="key"/>. Spends no credit.</summary>
    public async Task<OddsKeyCheck> CheckAsync(string key, CancellationToken ct = default)
    {
        key = key.Trim();
        if (key.Length == 0)
            return new OddsKeyCheck(OddsKeyVerdict.Rejected, null);

        try
        {
            using var client = http.CreateClient(CheckClient);
            using var response = await client.GetAsync($"sports?apiKey={Uri.EscapeDataString(key)}", ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new OddsKeyCheck(OddsKeyVerdict.Rejected, null);

            if (!response.IsSuccessStatusCode)
                return new OddsKeyCheck(OddsKeyVerdict.Unreachable, null);

            int? remaining = response.Headers.TryGetValues("x-requests-remaining", out var values)
                             && int.TryParse(values.FirstOrDefault(), out var left)
                ? left
                : null;

            return new OddsKeyCheck(OddsKeyVerdict.Accepted, remaining);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The message of an HttpRequestException can carry the request line. It is not logged.
            logger.LogWarning("The Odds API could not be reached to check a key ({Kind})", ex.GetType().Name);
            return new OddsKeyCheck(OddsKeyVerdict.Unreachable, null);
        }
    }

    /// <summary>Checks <paramref name="key"/> and, only if the provider accepts it, keeps and uses it.</summary>
    public async Task<OddsKeyCheck> SaveAsync(string key, CancellationToken ct = default)
    {
        var check = await CheckAsync(key, ct);
        if (check.Verdict != OddsKeyVerdict.Accepted)
            return check;

        key = key.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await File.WriteAllTextAsync(StorePath, _protector.Protect(key), ct);

        runtime.TheOddsApi = key;
        await reconcile(ct);

        logger.LogInformation("The Odds API key was set from the desk");
        return check;
    }

    /// <summary>Forgets the desk's key. Configuration's key, if there is one, is in force again.</summary>
    public async Task RemoveAsync(CancellationToken ct = default)
    {
        if (File.Exists(StorePath))
            File.Delete(StorePath);

        runtime.TheOddsApi = null;
        await reconcile(ct);

        logger.LogInformation("The Odds API key set from the desk was removed");
    }
}

public enum OddsKeyVerdict
{
    /// <summary>The provider answered and took the key.</summary>
    Accepted,

    /// <summary>The provider answered and refused it: mistyped, revoked, or another provider's.</summary>
    Rejected,

    /// <summary>No answer either way. Nothing is known about the key and nothing is kept.</summary>
    Unreachable
}

/// <param name="CreditsRemaining">What the provider says is left of the month, when it said.</param>
public record OddsKeyCheck(OddsKeyVerdict Verdict, int? CreditsRemaining);
