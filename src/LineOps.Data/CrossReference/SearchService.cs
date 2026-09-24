using LineOps.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LineOps.Data.CrossReference;

/// <summary>
/// Finds teams, players and games by what a person would type. One place for it, so the command
/// palette, the journal's game picker and the board's team filter agree on what "mets" matches.
///
/// <para>
/// Every word must match, in any of the fields a word could mean — "mets braves" is the matchup,
/// "judge nyy" the player on that team. Typed <c>%</c> and <c>_</c> are characters to find, not
/// wildcards. Only enabled sports are searched: a league the desk hides from its pickers should
/// not come back through the search.
/// </para>
/// </summary>
public class SearchService(LineOpsDbContext db)
{
    public async Task<IReadOnlyList<Team>> TeamsAsync(string text, int take = 6, CancellationToken ct = default)
    {
        var query = db.Teams.AsNoTracking().Include(t => t.Sport).Where(t => t.Sport!.Enabled);

        foreach (var pattern in Patterns(text))
            query = query.Where(t => EF.Functions.ILike(t.Name, pattern, Escape) || EF.Functions.ILike(t.Abbrev, pattern, Escape));

        return await query.OrderBy(t => t.Name).Take(take).ToListAsync(ct);
    }

    /// <summary>
    /// Players by name, or name and team. Ones who have actually played lead: ESPN leaves a
    /// long tail of rostered names with no stat line, and "smith" should find a starter first.
    /// </summary>
    public async Task<IReadOnlyList<Player>> PlayersAsync(string text, int take = 6, CancellationToken ct = default)
    {
        var query = db.Players.AsNoTracking()
            .Include(p => p.Team).Include(p => p.Sport)
            .Where(p => p.Sport!.Enabled);

        foreach (var pattern in Patterns(text))
            query = query.Where(p => EF.Functions.ILike(p.FullName, pattern, Escape)
                                     || (p.Team != null && (EF.Functions.ILike(p.Team.Name, pattern, Escape)
                                                            || EF.Functions.ILike(p.Team.Abbrev, pattern, Escape))));

        return await query
            .OrderByDescending(p => db.PlayerGameStats.Count(s => s.PlayerId == p.Id))
            .ThenBy(p => p.FullName)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Games whose teams match every word, across a season's reach — two hundred days back, a
    /// month ahead — nearest to now first: the game being looked for is usually today's, and the
    /// one being logged late usually last week's.
    /// </summary>
    public async Task<IReadOnlyList<Game>> GamesAsync(string text, int take = 20, CancellationToken ct = default)
    {
        var patterns = Patterns(text).ToList();
        if (patterns.Count == 0)
            return [];

        var now = DateTimeOffset.UtcNow;
        var query = db.Games.AsNoTracking()
            .Include(g => g.HomeTeam).Include(g => g.AwayTeam).Include(g => g.Sport)
            .Where(g => g.Sport!.Enabled
                        && g.StartsAt >= now.AddDays(-200)
                        && g.StartsAt <= now.AddDays(30));

        foreach (var pattern in patterns)
        {
            query = query.Where(g =>
                EF.Functions.ILike(g.HomeTeam!.Name, pattern, Escape)
                || EF.Functions.ILike(g.AwayTeam!.Name, pattern, Escape)
                || EF.Functions.ILike(g.HomeTeam!.Abbrev, pattern, Escape)
                || EF.Functions.ILike(g.AwayTeam!.Abbrev, pattern, Escape));
        }

        // A team's whole reach is under two hundred games, so the nearest-first order is taken in
        // memory rather than asked of the database as an interval expression.
        var found = await query.OrderByDescending(g => g.StartsAt).Take(200).ToListAsync(ct);

        return found.OrderBy(g => (g.StartsAt - now).Duration()).Take(take).ToList();
    }

    /// <summary>
    /// The same rule in memory, for a list already loaded: every word appears in at least one of
    /// the fields. The Board filters its slate this way rather than asking the database again.
    /// </summary>
    public static bool MatchesAllWords(string text, params string?[] fields)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(f => f is not null && f.Contains(word, StringComparison.OrdinalIgnoreCase)));

    private const string Escape = @"\";

    /// <summary>One contains-pattern per word, with LIKE's own characters escaped.</summary>
    public static IEnumerable<string> Patterns(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => $"%{w.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%");
}
