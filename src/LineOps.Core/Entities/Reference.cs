namespace LineOps.Core.Entities;

/// <summary>A league we track, e.g. NFL, NBA, MLB, NHL.</summary>
public class Sport
{
    public int Id { get; set; }

    /// <summary>Stable slug used in config and source mappings, e.g. "nfl".</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public List<Team> Teams { get; set; } = [];
    public List<Game> Games { get; set; } = [];
}

public class Team
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Abbrev { get; set; } = string.Empty;

    /// <summary>Per-source identifiers, keyed by source key. Cross-source entity resolution lives here.</summary>
    public Dictionary<string, string> ExternalIds { get; set; } = [];
}

public class Player
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    /// <summary>Null when the player is a free agent or unassigned.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }

    /// <summary>active | injured | inactive — as reported by the stats source.</summary>
    public string Status { get; set; } = "active";

    public Dictionary<string, string> ExternalIds { get; set; } = [];
}

public enum SourceKind
{
    Odds,
    Stats
}

/// <summary>An external data provider. Rate limits and budgets are enforced against these rows.</summary>
public class Source
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SourceKind Kind { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Free-tier ceiling. Null means unmetered.</summary>
    public int? RateLimitPerHour { get; set; }
    public int? RateLimitPerDay { get; set; }

    /// <summary>Monthly credit budget for credit-billed providers (e.g. The Odds API).</summary>
    public int? MonthlyCreditBudget { get; set; }

    /// <summary>Dev-only failure injection (Phase 5). Never enabled in production config.</summary>
    public string? FailureMode { get; set; }
}

public enum GameStatus
{
    Scheduled,
    Live,
    Final,
    Postponed
}

public class Game
{
    public int Id { get; set; }
    public int SportId { get; set; }
    public Sport? Sport { get; set; }

    public int HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }
    public int AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    public DateTimeOffset StartsAt { get; set; }
    public GameStatus Status { get; set; } = GameStatus.Scheduled;

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public Dictionary<string, string> ExternalIds { get; set; } = [];
}
