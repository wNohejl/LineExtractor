using System.Text.Json;

namespace LineOps.Core.Contracts;

/// <summary>
/// How a fighter's figures are written into <see cref="Entities.FighterProfile"/>'s jsonb and read
/// back out — one set of options, so the writer and every reader agree on the shape (ADR 0021).
/// </summary>
public static class FighterPayload
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write(FighterCareer career) => JsonSerializer.Serialize(career, Options);

    public static string Write(IReadOnlyList<FighterFight> history) => JsonSerializer.Serialize(history, Options);

    /// <summary>The career figures, or none where the column holds nothing readable.</summary>
    public static FighterCareer ReadCareer(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? Empty
                : JsonSerializer.Deserialize<FighterCareer>(json, Options) is { } career
                    ? new FighterCareer(career.Rates ?? [], career.Record ?? [])
                    : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>Every recorded fight, newest first, or none where the column holds nothing readable.</summary>
    public static IReadOnlyList<FighterFight> ReadHistory(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : (JsonSerializer.Deserialize<List<FighterFight>>(json, Options) ?? [])
                    .Select(f => f with { Stats = f.Stats ?? [] })
                    .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly FighterCareer Empty = new([], []);
}
