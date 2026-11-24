using System.Globalization;
using System.Text.Json;
using XLines.Core.Analytics;
using XLines.Core.Contracts;
using XLines.Core.Entities;
using XLines.Ingestion.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion.Adapters;

/// <summary>
/// The default stats source (ADR 0026): The Odds API's fixtures and final scores, on the same key
/// and the same 500 credits a month as its prices. ESPN, when its operator turns it on, takes this
/// slot instead (one stats port at a time, ADR 0011), and this source is not registered at all.
///
/// <para>
/// Two endpoints, two prices, two jobs. <c>/events</c> — every upcoming and in-play game, by full team
/// name and start — does not count against the quota, so it runs on the schedule as ESPN's slate did.
/// <c>/scores</c> costs one credit, or two with <c>daysFrom</c>, which is the only way to see a
/// completed game; it is the separate, metered job, never something a schedule pull spends.
/// </para>
///
/// <para>
/// What it does not have: box scores, players, play-by-play, injuries, abbreviations, a season, or
/// any fight's result — the provider keeps no scores for MMA, and a fight is attached only to a bout
/// ESPN holds (ADR 0021), so a fought league is not scheduled from here at all. Seasons come from the
/// desk's <see cref="SeasonCalendar"/> (the resolver stamps a new game by it), abbreviations from
/// <see cref="TheOddsApiTeams"/>. A completed game older than <see cref="ScoresReachDays"/> days is
/// beyond what the provider returns: one never seen final here stays unfinished, and the desk says so.
/// </para>
///
/// <para>
/// Field names, costs and limits follow the provider's published v4 guide
/// (<see href="https://the-odds-api.com/liveapi/guides/v4/"/>, read 2026-11-20). The shapes are pinned
/// by fixtures built from that guide, not from a live capture.
/// </para>
/// </summary>
public sealed class TheOddsApiScheduleAdapter(
    HttpClient http,
    IOptions<IngestionOptions> options,
    ILogger<TheOddsApiScheduleAdapter> logger,
    Services.RuntimeOddsKey? runtimeKey = null) : IStatsSource, IScoreSource
{
    public const string SourceKey = SourceAccounts.TheOddsApiSchedule;

    /// <summary>
    /// How far ahead one schedule pull reads. The call is free and returns the whole window at once,
    /// so a week and a day covers an NFL week from any day of it.
    /// </summary>
    public static readonly TimeSpan FixtureHorizon = TimeSpan.FromDays(8);

    /// <summary><c>daysFrom</c>'s ceiling: the provider returns completed games from up to three days ago.</summary>
    public const int MaxDaysFrom = 3;

    /// <summary>"The usage quota cost is 2 if the daysFrom parameter is specified" — and it always is here.</summary>
    public const int CreditsWithDaysFrom = 2;

    private readonly IngestionOptions _options = options.Value;
    private readonly TheOddsApiKey _key = new(options.Value.TheOddsApi, runtimeKey);

    public string Key => SourceKey;

    /// <summary>Holds a key, from the desk or from configuration — the same answer the odds adapter gives.</summary>
    public bool IsConfigured => _key.IsConfigured;

    public bool CoversScores(string sportKey) => !FightSports.IsFight(sportKey);

    public int CreditsPerScoresCall => CreditsWithDaysFrom;

    public int ScoresReachDays => MaxDaysFrom;

    /// <summary>
    /// The league's fixtures from the start of <paramref name="date"/> (the league's own day) to
    /// <see cref="FixtureHorizon"/> after it, from <c>/events</c>: free. No status and no score — the
    /// call carries neither — so a game already held keeps both. A fought league asks nothing.
    /// </summary>
    public async Task<StatsFetchResult> FetchScheduleAsync(string sportKey, DateOnly date, CancellationToken ct)
    {
        if (FightSports.IsFight(sportKey))
            return Nothing;

        var apiKey = _key.Require(SourceKey);
        var from = LeagueClock.StartOf(date);

        using var response = await http.GetAsync(TheOddsApiWire.EventsUrl(sportKey, apiKey, from, from + FixtureHorizon), ct);
        response.EnsureSuccessStatusCode();

        var cost = TheOddsApiWire.CostOf(response, fallbackCredits: 0);
        var games = await ReadBilledAsync(response, sportKey, scores: false, cost, ct);

        logger.LogInformation("{Source}: {Sport} fixtures -> {Games} games, {Credits} credits",
            SourceKey, sportKey, games.Count, cost.Credits);

        return new StatsFetchResult(games, [], [], cost);
    }

    /// <summary>
    /// The league's live and recently completed games with their scores, from <c>/scores</c> with
    /// <c>daysFrom=3</c>: two credits, reaching back as far as the provider allows so a missed day is
    /// caught up by the next call. A completed game with both scores is final; anything else says
    /// nothing about its status or score.
    /// </summary>
    public async Task<StatsFetchResult> FetchScoresAsync(string sportKey, CancellationToken ct)
    {
        if (!CoversScores(sportKey))
            return Nothing;

        var apiKey = _key.Require(SourceKey);

        using var response = await http.GetAsync(TheOddsApiWire.ScoresUrl(sportKey, apiKey, MaxDaysFrom), ct);
        response.EnsureSuccessStatusCode();

        var cost = TheOddsApiWire.CostOf(response, fallbackCredits: CreditsWithDaysFrom);
        var games = await ReadBilledAsync(response, sportKey, scores: true, cost, ct);

        logger.LogInformation("{Source}: {Sport} scores -> {Games} games, {Final} final, {Credits} credits ({Remaining} remaining)",
            SourceKey, sportKey, games.Count, games.Count(g => g.Status == "final"), cost.Credits,
            cost.CreditsRemaining?.ToString(CultureInfo.InvariantCulture) ?? "?");

        return new StatsFetchResult(games, [], [], cost);
    }

    /// <summary>No players are named by this provider: nothing, and no request.</summary>
    public Task<StatsFetchResult> FetchRosterAsync(string sportKey, CancellationToken ct) => Task.FromResult(Nothing);

    /// <summary>No box scores either: nothing, and no request. Scores are <see cref="FetchScoresAsync"/>.</summary>
    public Task<StatsFetchResult> FetchBoxScoresAsync(string sportKey, DateOnly date, CancellationToken ct) => Task.FromResult(Nothing);

    private static StatsFetchResult Nothing => new([], [], [], new FetchCost(0));

    /// <summary>
    /// The answer read, or a failure that still says what the answer cost. The provider bills when
    /// it answers, not when the answer can be read, so a body that will not parse is thrown as
    /// <see cref="OddsFetchFailedException"/> with the cost the headers stated: the failed run then
    /// records the credits (<c>StatsIngestionService</c>) rather than reading as a free call.
    /// </summary>
    private async Task<List<CanonicalGame>> ReadBilledAsync(
        HttpResponseMessage response, string sportKey, bool scores, FetchCost cost, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(response, sportKey, scores, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OddsFetchFailedException(
                $"{SourceKey}: {sportKey} answered and could not be read ({ex.GetType().Name}: {ex.Message}).",
                cost,
                ex);
        }
    }

    private async Task<List<CanonicalGame>> ReadAsync(HttpResponseMessage response, string sportKey, bool scores, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        // A league held from its season's start only (Backfill:Seasons): the NBA's history begins at
        // the 2026-27 opening night, and a preseason game listed before it is not on the desk.
        DateOnly? seasonStart = _options.Backfill.Seasons.TryGetValue(sportKey, out var start) ? start : null;

        var games = new List<CanonicalGame>();

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (Parse(e, sportKey, scores) is not { } game)
                continue;

            if (seasonStart is { } opening && LeagueClock.DateOf(game.StartsAt) < opening)
                continue;

            games.Add(game);
        }

        return games;
    }

    /// <summary>
    /// One event as a game: both sides by name, with what the desk's table knows of each club; and,
    /// from <c>/scores</c>, the status and score. An event with no id, side or readable start is not
    /// a fixture (<see cref="TheOddsApiWire.ReadEvent"/>). A score that is not a whole number is no score.
    /// </summary>
    internal static CanonicalGame? Parse(JsonElement e, string sportKey, bool scores)
    {
        if (TheOddsApiWire.ReadEvent(e, sportKey, withRefs: true) is not { } game)
            return null;

        var home = game.HomeTeamName;
        var away = game.AwayTeamName;

        if (!scores)
            return game;

        // Only a completed game's score is written. A score in play is a snapshot the next call may be
        // a day away from refreshing (scores are pulled once a night, not polled), and a board that
        // read "Live 40-37" until tomorrow would say something untrue for most of that day.
        var completed = e.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.True;
        if (!completed)
            return game;

        var homeScore = ScoreOf(e, home);
        var awayScore = ScoreOf(e, away);

        // Both sides or neither: a final with one side's score is not a result to grade on.
        if (homeScore is null || awayScore is null)
            return game;

        return game with { Status = "final", HomeScore = homeScore, AwayScore = awayScore };
    }

    /// <summary>The side's score from the event's <c>scores</c> list, read by its name; a string in the payload.</summary>
    private static int? ScoreOf(JsonElement e, string side)
    {
        if (!e.TryGetProperty("scores", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String
                || !string.Equals(n.GetString(), side, StringComparison.Ordinal)
                || !entry.TryGetProperty("score", out var s))
                continue;

            var text = s.ValueKind switch
            {
                JsonValueKind.String => s.GetString(),
                JsonValueKind.Number => s.GetRawText(),
                _ => null
            };

            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var score) ? score : null;
        }

        return null;
    }
}
