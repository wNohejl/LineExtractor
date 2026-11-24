namespace XLines.Ingestion.Services;

/// <summary>
/// An odds key supplied while the host is running, rather than by configuration at start.
///
/// <para>
/// Configuration is read once: an adapter is registered when its key is present at start, and a
/// key added to <c>.env</c> afterwards needs the container started again. A host that lets its
/// operator type the key in (the desk, in Ops) registers this, sets it, and the adapter reads it
/// on every call. Only a host that calls <c>AddRuntimeOddsKey</c> has one; every other host is
/// exactly as it was.
/// </para>
///
/// <para>
/// Held in memory only. Where the key is kept between starts is the host's business, and it is
/// never the database: a key in a table travels in every dump and every journal export.
/// </para>
/// </summary>
public sealed class RuntimeOddsKey
{
    private volatile string? _theOddsApi;

    /// <summary>The Odds API key in force, or null to leave the choice to configuration.</summary>
    public string? TheOddsApi
    {
        get => _theOddsApi;
        set => _theOddsApi = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
