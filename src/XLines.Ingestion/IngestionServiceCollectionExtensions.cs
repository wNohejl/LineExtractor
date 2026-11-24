using System.Net;
using System.Net.Http.Headers;
using XLines.Core.Contracts;
using XLines.Ingestion.Adapters;
using XLines.Ingestion.Configuration;
using XLines.Ingestion.Services;
using XLines.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XLines.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingestion pipeline. Adapters are registered only when configured, so a
    /// cold clone with no API keys still starts — with no stats source until its operator opts in
    /// to ESPN (<see cref="EspnOptIn"/>), and no odds feed until a key is supplied. A missing
    /// source is <i>unconfigured</i>, not broken; the reliability layer is careful to say so
    /// (see <c>AlertEngine</c>).
    /// </summary>
    public static IServiceCollection AddXLinesIngestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        // Every list-shaped option defaults to empty and resolves its fallback in code, because
        // the configuration binder *appends* to a non-empty default rather than replacing it —
        // so a default of ["draftkings", "fanduel"] cannot be narrowed to ["betmgm"] from
        // config, it becomes all three. That is the real fix; this pass only removes duplicates
        // someone typed by hand, which the binder would otherwise send straight to the wire.
        services.PostConfigure<IngestionOptions>(Normalise);

        // The reliability layer cannot see the ingestion options, and should not be configured
        // twice by hand to agree with them: when odds are pulled only on request, an odds feed
        // that has not run today is idle, and its freshness rule stands down.
        services.AddOptions<ReliabilityOptions>()
            .PostConfigure<IOptions<IngestionOptions>>((reliability, ingestion) =>
                reliability.OddsOnDemand = !ingestion.Value.LinePolling.RunsUnattended);

        services.AddScoped<EntityResolver>();
        services.AddScoped<GamecastWriter>();
        services.AddScoped<CreditBudgetGuard>();
        services.AddScoped<OddsIngestionService>();
        services.AddScoped<StatsIngestionService>();
        services.AddScoped<FighterProfileService>();
        services.AddScoped<InjuryReportService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<JournalService>();
        services.AddScoped<JournalTransfer>();
        services.AddScoped<OddsRetentionService>();
        services.AddScoped<OddsFeedStatus>();
        services.AddScoped<LinePollPlanner>();
        services.AddScoped<CreditAllowance>();

        // Singleton: it creates a scope per job, so it has no scoped state and both the
        // scheduler and the desk's pull menu can hold it directly.
        services.AddSingleton<IngestionJobs>();
        services.AddScoped<SourceRegistry>();
        // Singleton: it creates a scope per day rather than holding one, so it has no scoped
        // state of its own and the History panel can inject it directly.
        services.AddSingleton<HistoryBackfillService>();

        // Singleton for the same reason: a scope per run and per game. Here rather than with the
        // scheduler because the Pull menu and the Worker's --job run it on demand on any host.
        services.AddSingleton<GamecastBackport>();
        services.AddSingleton<GamecastLanes>(); // windows' interest and the feed's lanes; resolvable where the feed does not run

        // Singleton: a scope per day, and the one gate that keeps a host to one range at a time,
        // whichever of the desk's sheet and the Worker's --job espn:range asked.
        services.AddSingleton<RangePull>();

        // Singleton and hosted: a backfill outlives the circuit that started it, and only one
        // may run at a time. Registered here rather than with the scheduler because the web
        // app starts backfills on demand whether or not it owns the schedule.
        services.AddSingleton<BackfillCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<BackfillCoordinator>());

        // Runs once at start, after the initialiser has seeded the reference rows: the
        // enabled flags on sports and sources follow the configuration rather than a hand edit.
        // A singleton as well as hosted, so a host whose odds key can change while it runs can
        // ask for the same reconciliation again (AddRuntimeOddsKey).
        services.AddSingleton<ReferenceReconciler>();
        services.AddHostedService(sp => sp.GetRequiredService<ReferenceReconciler>());

        var options = configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()
                      ?? new IngestionOptions();

        if (options.Espn.Enabled)
        {
            // ESPN refuses a request whose client it does not recognise. HttpClient sends no
            // User-Agent at all by default, and on 2 August 2026 that began returning 403 on every
            // call — the port had always been one policy change away from breaking, and the policy
            // changed. See SourceOptions.UserAgent for why the value has to name a real HTTP client
            // rather than this app.
            //
            // A malformed override is corrected rather than obeyed: an unparseable value would
            // leave the header absent, which is the precise condition that caused the outage.
            // Failing back to a working default and saying so is better than a typo silently
            // reproducing the bug it was added to fix.
            AddProviderClient<EspnStatsAdapter>(services, EspnBaseUrl,
                (sp, client) => ApplyUserAgent(client, options.Espn, EspnStatsAdapter.SourceKey, sp));

            // One adapter behind every ESPN port, each resolved at the configured pace: the delay is
            // ESPN's patience, not the port's, so no port may reach it faster than the others.
            EspnStatsAdapter Paced(IServiceProvider sp)
            {
                var adapter = sp.GetRequiredService<EspnStatsAdapter>();
                adapter.RequestDelay = options.Espn.RequestDelay;
                return adapter;
            }

            services.AddScoped<IStatsSource>(Paced);

            // The same port describes fighters (ADR 0021), at the same pace.
            services.AddScoped<IFighterSource>(Paced);

            // And relays the league's injury report, for the starter the Board names.
            services.AddScoped<IInjurySource>(Paced);

            // And narrates a game in play, for the gamecast feed (spec 2026-10-24 §6.3).
            services.AddScoped<IGamecastSource>(Paced);
        }

        // The entity spine. Registered as itself rather than as an IStatsSource: it issues
        // identity, and the box-score job stays ESPN's (ADR 0011). Nothing here competes for the
        // stats-source slot, so it cannot displace what already works.
        //
        // Only when ESPN is on (EspnOptIn): it annotates the games ESPN writes, so without ESPN it
        // has nothing to match, and its data is personal-use as ESPN's is — one opt-in covers both.
        if (options.MlbStatsApi.Enabled && options.Espn.Enabled)
        {
            AddProviderClient<MlbStatsApiAdapter>(services, "https://statsapi.mlb.com/");

            services.AddScoped<MlbSpineService>();
        }

        if (options.OddsApiIo.Enabled && !string.IsNullOrWhiteSpace(options.OddsApiIo.ApiKey))
        {
            AddProviderClient<OddsApiIoAdapter>(services, "https://api.odds-api.io/v3/");

            services.AddScoped<IOddsSource>(sp => sp.GetRequiredService<OddsApiIoAdapter>());
        }

        if (options.TheOddsApi.Enabled && !string.IsNullOrWhiteSpace(options.TheOddsApi.ApiKey))
            AddTheOddsApi(services);

        // The default stats source (ADR 0026): The Odds API's fixtures and scores, on the same key —
        // only while ESPN is off. One stats port at a time (ADR 0011): ESPN on, this is not registered,
        // so nothing it would write or spend can reach a desk that runs on ESPN.
        if (!options.Espn.Enabled && options.TheOddsApi.Enabled && !string.IsNullOrWhiteSpace(options.TheOddsApi.ApiKey))
            AddTheOddsApiSchedule(services);

        return services;
    }

    /// <summary>
    /// The primary handler every provider client shares, so the five registrations cannot drift.
    ///
    /// <para>
    /// It asks for, and unpacks, compressed bodies. .NET sends no <c>Accept-Encoding</c> unless
    /// asked, so every provider has been answering in full: ESPN's NFL scoreboard is 236,335
    /// bytes plain and 20,345 gzipped, a game summary 451,157 and 43,092 — and the gamecast feed
    /// reads a summary per followed game every 15 seconds. The Odds API's documentation says to
    /// send <c>Accept-Encoding: gzip</c>. Roughly a tenth of the bytes, for one line of setup.
    /// </para>
    ///
    /// <para>
    /// A custom primary handler replaces the factory's default, so what that default did is
    /// restated here. Cookies stay off, as the factory has them: these are keyed, stateless
    /// APIs and a cookie jar would only carry a session from one scan into the next. Pooled
    /// connections are retired every two minutes, the factory's own handler lifetime, so a
    /// provider that moves behind a new address is followed rather than dialled at the old one
    /// until the process restarts.
    /// </para>
    /// </summary>
    internal static SocketsHttpHandler ProviderHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };

    private const string EspnBaseUrl = "https://site.api.espn.com/apis/site/v2/sports/";

    /// <summary>Both of The Odds API's adapters, odds and schedule, are one account on one host.</summary>
    private const string TheOddsApiBaseUrl = "https://api.the-odds-api.com/v4/";

    /// <summary>
    /// Registers a provider's typed client the one way every provider is registered: its base
    /// address, a 30-second timeout, the shared <see cref="ProviderHandler"/>, and the standard
    /// resilience handler — retry with jittered backoff, a circuit breaker that trips on sustained
    /// failure, and per-attempt timeouts. A tripped breaker is what the reliability layer surfaces
    /// as a degraded source. One method, so five registrations cannot drift apart.
    /// </summary>
    /// <param name="extra">Anything one provider needs beyond that, such as ESPN's User-Agent.</param>
    private static IHttpClientBuilder AddProviderClient<T>(
        IServiceCollection services, string baseUrl, Action<IServiceProvider, HttpClient>? extra = null)
        where T : class
    {
        var builder = services.AddHttpClient<T>((sp, client) =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                extra?.Invoke(sp, client);
            })
            .ConfigurePrimaryHttpMessageHandler(ProviderHandler);

        builder.AddStandardResilienceHandler();
        return builder;
    }

    private static void AddTheOddsApi(IServiceCollection services)
    {
        AddProviderClient<TheOddsApiAdapter>(services, TheOddsApiBaseUrl);

        services.AddScoped<IOddsSource>(sp => sp.GetRequiredService<TheOddsApiAdapter>());
    }

    /// <summary>The fixtures-and-scores adapter, as the stats port and the score port, on the odds key's client settings.</summary>
    private static void AddTheOddsApiSchedule(IServiceCollection services)
    {
        AddProviderClient<TheOddsApiScheduleAdapter>(services, TheOddsApiBaseUrl);

        services.AddScoped<IStatsSource>(sp => sp.GetRequiredService<TheOddsApiScheduleAdapter>());
        services.AddScoped<IScoreSource>(sp => sp.GetRequiredService<TheOddsApiScheduleAdapter>());
    }

    /// <summary>
    /// Lets this host's operator supply The Odds API key while it runs (the desk's Ops window),
    /// instead of only through configuration at start. Call after <see cref="AddXLinesIngestion"/>.
    ///
    /// <para>
    /// The adapter is registered here if configuration did not already register it, and says for
    /// itself whether it holds a key (<see cref="IOddsSource.IsConfigured"/>), which is what the
    /// registry lists by. A host that does not call this is unchanged: no key at start, no adapter.
    /// </para>
    /// </summary>
    public static IServiceCollection AddRuntimeOddsKey(this IServiceCollection services)
    {
        services.AddSingleton<RuntimeOddsKey>();

        if (!services.Any(d => d.ServiceType == typeof(TheOddsApiAdapter)))
            AddTheOddsApi(services);

        // And, while ESPN is off, the default stats source on the same key, which likewise says for
        // itself whether it holds one. ESPN registered means ESPN is on, and then it is the stats port.
        if (!services.Any(d => d.ServiceType == typeof(EspnStatsAdapter))
            && !services.Any(d => d.ServiceType == typeof(TheOddsApiScheduleAdapter)))
            AddTheOddsApiSchedule(services);

        return services;
    }

    /// <summary>
    /// Sets a client's identification, correcting an unusable override rather than obeying it.
    ///
    /// <para>
    /// The failure this guards against is specific. An absent <c>User-Agent</c> is what ESPN
    /// began refusing, so a configured value that cannot be parsed must not be allowed to leave
    /// the header unset — that would turn a typo into the exact three-week outage the option was
    /// added to prevent. The default is applied instead and the substitution is logged, because
    /// silently ignoring configuration is its own kind of bug.
    /// </para>
    /// </summary>
    private static void ApplyUserAgent(
        HttpClient client, SourceOptions source, string sourceKey, IServiceProvider services)
    {
        var configured = source.EffectiveUserAgent;

        if (client.DefaultRequestHeaders.UserAgent.TryParseAdd(configured))
            return;

        services.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(IngestionServiceCollectionExtensions))
            .LogWarning(
                "{Source}: configured User-Agent '{Configured}' is not a valid header value; "
                + "using '{Fallback}'. An absent User-Agent is refused by this provider.",
                sourceKey, configured, SourceOptions.DefaultUserAgent);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(SourceOptions.DefaultUserAgent);
    }

    /// <summary>
    /// Adds the unattended scheduler. Separate from <see cref="AddXLinesIngestion"/> so the
    /// web app can trigger ingestion manually without also owning the schedule — which is what
    /// lets the worker be split into its own process later without touching either codebase.
    ///
    /// <para>
    /// The gamecast feed comes with it. It is the scheduler's question again — which host polls
    /// unattended — answered the same way: the Worker, or the web host when it hosts the schedule
    /// (<c>Ingestion:HostScheduler</c>), never both. Two feeds would write every play twice over
    /// and spend ESPN's patience doubly, as two schedulers would spend the odds credits.
    /// </para>
    /// </summary>
    public static IServiceCollection AddXLinesIngestionScheduler(this IServiceCollection services)
    {
        services.AddHostedService<IngestionScheduler>();
        services.AddHostedService<GamecastFeed>();
        return services;
    }

    /// <summary>
    /// De-duplicates every list-shaped option, preserving order and ignoring case.
    ///
    /// See the note at the <c>PostConfigure</c> call for why this is necessary. Order is kept
    /// because for sources it is a preference order, and case is ignored because a provider
    /// slug typed two ways is still one provider.
    /// </summary>
    private static void Normalise(IngestionOptions options)
    {
        static string[] Unique(string[] values)
            => values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        options.Sports = Unique(options.Sports);
        options.Backfill.Sports = Unique(options.Backfill.Sports);
        options.Backfill.Sources = Unique(options.Backfill.Sources);
        options.Gamecast.Sports = Unique(options.Gamecast.Sports);

        foreach (var source in new[] { options.OddsApiIo, options.TheOddsApi, options.BallDontLie, options.Espn })
            source.Bookmakers = Unique(source.Bookmakers);
    }

    /// <summary>Resolves the effective ingestion options without needing a scope.</summary>
    public static IngestionOptions GetIngestionOptions(this IServiceProvider provider)
        => provider.GetRequiredService<IOptions<IngestionOptions>>().Value;
}
