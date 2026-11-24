using System.Text.Json;
using XLines.Core.Entities;
using XLines.Core.Sync;
using XLines.Data;
using Microsoft.EntityFrameworkCore;

namespace XLines.Ingestion.Services;

/// <summary>
/// Finds the game or player a journal file names in this database (ADR 0025), or says it cannot.
///
/// <para>
/// By provider first: a game whose <c>ExternalIds</c> holds the file's ESPN event id, MLB gamePk
/// or Odds API id is that game, whatever this database numbered it. Only where no provider id
/// matches does the readable fallback apply — the same league, both sides by name or by their
/// own provider ids, and a start within <see cref="JournalTransfer.FallbackStartTolerance"/> —
/// and only if exactly one game fits. Two that fit, or a game whose id for the same provider
/// differs, is not a match: a wrong game would grade the bet against someone else's score, and
/// an entry without a game is the honest answer the report then names.
/// </para>
/// </summary>
public sealed class ReferenceResolver(XLinesDbContext db)
{
    private readonly Dictionary<string, int?> _games = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int?> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int?> _sports = new(StringComparer.Ordinal);

    public async Task<int?> GameAsync(GameReference? reference, CancellationToken ct = default)
    {
        if (reference is null)
            return null;

        var key = JsonSerializer.Serialize(reference, JournalFile.Json);
        if (_games.TryGetValue(key, out var known))
            return known;

        return _games[key] = await FindGameAsync(reference, ct);
    }

    public async Task<int?> PlayerAsync(PlayerReference? reference, CancellationToken ct = default)
    {
        if (reference is null)
            return null;

        var key = JsonSerializer.Serialize(reference, JournalFile.Json);
        if (_players.TryGetValue(key, out var known))
            return known;

        return _players[key] = await FindPlayerAsync(reference, ct);
    }

    private async Task<int?> SportAsync(string key, CancellationToken ct)
    {
        if (_sports.TryGetValue(key, out var id))
            return id;

        return _sports[key] = await db.Sports.Where(s => s.Key == key).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> FindGameAsync(GameReference reference, CancellationToken ct)
    {
        if (await SportAsync(reference.Sport, ct) is not { } sportId)
            return null;

        var byProvider = new HashSet<int>();
        foreach (var (provider, id) in reference.ExternalIds)
        {
            var matches = await db.Games
                .FromSql($"""SELECT * FROM "Games" WHERE "SportId" = {sportId} AND "ExternalIds" @> jsonb_build_object({provider}::text, {id}::text)""")
                .Select(g => g.Id)
                .ToListAsync(ct);
            byProvider.UnionWith(matches);
        }

        if (byProvider.Count == 1)
            return byProvider.Single();

        if (byProvider.Count > 1)
            return null;

        var from = reference.StartsAtUtc - JournalTransfer.FallbackStartTolerance;
        var to = reference.StartsAtUtc + JournalTransfer.FallbackStartTolerance;

        var near = await db.Games.AsNoTracking()
            .Include(g => g.HomeTeam)
            .Include(g => g.AwayTeam)
            .Where(g => g.SportId == sportId && g.StartsAt >= from && g.StartsAt <= to)
            .ToListAsync(ct);

        var fits = near
            .Where(g => !Disagree(reference.ExternalIds, g.ExternalIds)
                        && SameTeam(reference.Home, g.HomeTeam!)
                        && SameTeam(reference.Away, g.AwayTeam!)
                        && (reference.DoubleHeaderGame is null || g.DoubleHeaderGame is null || reference.DoubleHeaderGame == g.DoubleHeaderGame))
            .Select(g => g.Id)
            .ToList();

        return fits.Count == 1 ? fits[0] : null;
    }

    private async Task<int?> FindPlayerAsync(PlayerReference reference, CancellationToken ct)
    {
        if (await SportAsync(reference.Sport, ct) is not { } sportId)
            return null;

        var byProvider = new HashSet<int>();
        foreach (var (provider, id) in reference.ExternalIds)
        {
            var matches = await db.Players
                .FromSql($"""SELECT * FROM "Players" WHERE "SportId" = {sportId} AND "ExternalIds" @> jsonb_build_object({provider}::text, {id}::text)""")
                .Select(p => p.Id)
                .ToListAsync(ct);
            byProvider.UnionWith(matches);
        }

        if (byProvider.Count == 1)
            return byProvider.Single();

        if (byProvider.Count > 1)
            return null;

        var name = reference.FullName.ToLower();
        var named = await db.Players.AsNoTracking()
            .Include(p => p.Team)
            .Where(p => p.SportId == sportId && p.FullName.ToLower() == name)
            .ToListAsync(ct);

        var fits = named
            .Where(p => !Disagree(reference.ExternalIds, p.ExternalIds)
                        && (reference.Team is null || p.Team is null || string.Equals(p.Team.Name, reference.Team, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id)
            .ToList();

        return fits.Count == 1 ? fits[0] : null;
    }

    /// <summary>A team on the file's side and one here are the same: a provider id in common, else the same name, and no provider id that differs.</summary>
    internal static bool SameTeam(TeamReference reference, Team team)
    {
        if (Disagree(reference.ExternalIds, team.ExternalIds))
            return false;

        return reference.ExternalIds.Any(k => team.ExternalIds.TryGetValue(k.Key, out var id) && id == k.Value)
               || string.Equals(reference.Name, team.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Both name the row under one provider, with different ids: two different things.</summary>
    internal static bool Disagree(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        => a.Any(k => b.TryGetValue(k.Key, out var other) && other != k.Value);
}
