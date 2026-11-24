using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using XLines.Core.Entities;

namespace XLines.Core.Sync;

/// <summary>
/// The journal as a file one machine writes and the other merges (ADR 0025): entries, parlays,
/// deletions and the operator's settings, in one versioned JSON document.
///
/// <para>
/// Nothing in it is a row id. An entry names its bet by <see cref="JournalEntry.SyncId"/>, its
/// parlay by the parlay's uuid, and its game and player the way a provider names them — ESPN's
/// event id, MLB's gamePk, The Odds API's id — with the league, the start and the two sides
/// beside them for a game that carries no id the other database knows. Row ids are this
/// database's counters; the laptop numbers its games itself once it pulls any.
/// </para>
/// </summary>
public sealed record JournalFile
{
    /// <summary>The shape this build writes and reads. A file with another number is refused, not guessed at.</summary>
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public DateTime ExportedAtUtc { get; init; }
    public string Machine { get; init; } = string.Empty;

    public List<JournalFileEntry> Entries { get; init; } = [];
    public List<JournalFileParlay> Parlays { get; init; } = [];
    public List<JournalFileTombstone> Tombstones { get; init; } = [];
    public List<JournalFileSetting> Settings { get; init; } = [];

    /// <summary>
    /// Settings that belong to the machine, not the operator, and do not travel. The polling
    /// switch decides whether this machine's scheduler spends credits by itself; a file from the
    /// other machine must never turn that on.
    /// </summary>
    public static readonly IReadOnlySet<string> MachineSettings = new HashSet<string> { AppSetting.LinePolling };

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>The file read, or why it cannot be: not JSON, or a format this build does not know.</summary>
    public static JournalFile Parse(string text)
    {
        using var probe = JsonDocument.Parse(text);
        if (!probe.RootElement.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number)
            throw new FormatException("Not a XLines journal file: it has no \"format\".");

        if (format.GetInt32() != CurrentFormat)
            throw new FormatException(
                $"This journal file is format {format.GetInt32()}; this build reads format {CurrentFormat}. Update this checkout, or export again from a build that matches.");

        return JsonSerializer.Deserialize<JournalFile>(text, Json)
               ?? throw new FormatException("The journal file is empty.");
    }
}

/// <summary>A game named the way providers name it, with a readable fallback.</summary>
public sealed record GameReference(
    string Sport,
    Dictionary<string, string> ExternalIds,
    DateTimeOffset StartsAtUtc,
    TeamReference Home,
    TeamReference Away,
    int? DoubleHeaderGame = null)
{
    /// <summary>"nfl 2025-09-07 Baltimore Ravens at Buffalo Bills (espn 401772918)" — how a report names it.</summary>
    public override string ToString()
    {
        var ids = string.Join(", ", ExternalIds.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}"));
        var day = StartsAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture);
        return $"{Sport} {day} {Away.Name} at {Home.Name}{(ids.Length > 0 ? $" ({ids})" : "")}";
    }
}

public sealed record TeamReference(string Name, string? Abbrev, Dictionary<string, string> ExternalIds);

public sealed record PlayerReference(string Sport, string FullName, Dictionary<string, string> ExternalIds, string? Team = null)
{
    public override string ToString() => $"{Sport} {FullName}{(Team is { Length: > 0 } t ? $" ({t})" : "")}";
}

/// <summary>One journal entry, straight or a leg, as the file carries it.</summary>
public sealed record JournalFileEntry
{
    public Guid SyncId { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public GameReference? Game { get; init; }
    public PlayerReference? Player { get; init; }

    /// <summary>The parlay this entry is a leg of, by the parlay's uuid; null for a straight bet.</summary>
    public Guid? ParlayId { get; init; }

    public string Market { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? FreeTextMarket { get; init; }
    public string Book { get; init; } = string.Empty;
    public decimal? LineTaken { get; init; }
    public decimal PriceTaken { get; init; }
    public bool ManualPrice { get; init; }
    public decimal Stake { get; init; }
    public DateTimeOffset PlacedAt { get; init; }
    public EntryResult Result { get; init; }
    public decimal? Payout { get; init; }
    public DateTimeOffset? SettledAt { get; init; }
    public DateTimeOffset? ClosingCapturedAt { get; init; }
    public int? ClosingPrice { get; init; }
    public decimal? ClosingPoints { get; init; }
    public string? ClosingBook { get; init; }
    public double? ClosingFairProbability { get; init; }
    public string? ClosingFairBasis { get; init; }
    public string? Note { get; init; }

    /// <summary>How a report names the bet: its game or market, the side and the book.</summary>
    public string Describe()
        => $"{(Game?.ToString() ?? FreeTextMarket ?? Market)}: {Outcome} at {Book}, placed {PlacedAt.UtcDateTime:yyyy-MM-dd}";
}

public sealed record JournalFileParlay
{
    public Guid Id { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string Book { get; init; } = string.Empty;
    public decimal Stake { get; init; }
    public decimal? PriceQuoted { get; init; }
    public DateTimeOffset PlacedAt { get; init; }
    public EntryResult Result { get; init; }
    public decimal? Payout { get; init; }
    public DateTimeOffset? SettledAt { get; init; }
    public string? Note { get; init; }

    public string Describe() => $"parlay at {Book}, {Stake.ToString("0.00", CultureInfo.InvariantCulture)} placed {PlacedAt.UtcDateTime:yyyy-MM-dd}";
}

public sealed record JournalFileTombstone(Guid SyncId, string Kind, DateTime DeletedAtUtc);

public sealed record JournalFileSetting(string Key, string Value, DateTime UpdatedAtUtc);
