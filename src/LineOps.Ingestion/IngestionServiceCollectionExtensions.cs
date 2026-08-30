using System.Net.Http.Headers;
using LineOps.Core.Contracts;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using LineOps.Ingestion.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingestion pipeline. Adapters are registered only when configured, so a
    /// cold clone with no API keys still starts — it runs on ESPN, which needs none, and has
    /// no odds feed until a key is supplied. No odds source is <i>unconfigured</i>, not broken;
    /// the reliability layer is careful to say so (see <c>AlertEngine</c>).
    /// </summary>
    public static IServiceCollection AddLineOpsIngestion(
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

        services.AddScoped<EntityResolver>();
        services.AddScoped<CreditBudgetGuard>();
        services.AddScoped<OddsIngestionService>();
        services.AddScoped<StatsIngestionService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<OddsRetentionService>();
        services.AddScoped<OddsFeedStatus>();
        services.AddScoped<LinePollPlanner>();

        // Singleton: it creates a scope per job, so it has no scoped state and both the
        // scheduler and the desk's pull menu can hold it directly.
        services.AddSingleton<IngestionJobs>();
        services.AddScoped<SourceRegistry>();
        // Singleton: it creates a scope per day rather than holding one, so it has no scoped
        // state of its own and the History panel can inject it directly.
        services.AddSingleton<HistoryBackfillService>();

        // Singleton and hosted: a backfill outlives the circuit that started it, and only one
        // may run at a time. Registered here rather than with the scheduler because the web
        // app starts backfills on demand whether or not it owns the schedule.
        services.AddSingleton<BackfillCoordinator>();
        services.AddHostedService(sp => sp.GetRequiredService<BackfillCoordinator>());

        var options = configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()
                      ?? new IngestionOptions();

        if (options.Espn.Enabled)
        {
            services.AddHttpClient<EspnStatsAdapter>((sp, client) =>
                {
                    client.BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/sports/");
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // ESPN refuses a request whose client it does not recognise. HttpClient
                    // sends no User-Agent at all by default, and on 2 August 2026 that began
                    // returning 403 on every call — the port had always been one policy change
                    // away from breaking, and the policy changed. See SourceOptions.UserAgent
                    // for why the value has to name a real HTTP client rather than this app.
                    //
                    // A malformed override is corrected rather than obeyed: an unparseable
                    // value would leave the header absent, which is the precise condition that
                    // caused the outage. Failing back to a working default and saying so is
                    // better than a typo silently reproducing the bug it was added to fix.
                    ApplyUserAgent(client, options.Espn, EspnStatsAdapter.SourceKey, sp);
                })
                .AddStandardResilienceHandler();

            services.AddScoped<IStatsSource>(sp =>
            {
                var adapter = sp.GetRequiredService<EspnStatsAdapter>();
                adapter.RequestDelay = options.Espn.RequestDelay;
                return adapter;
            });
        }

        // The entity spine. Registered as itself rather than as an IStatsSource: it issues
        // identity, and the box-score job stays ESPN's (ADR 0011). Nothing here competes for the
        // stats-source slot, so it cannot displace what already works.
        if (options.MlbStatsApi.Enabled)
        {
            services.AddHttpClient<MlbStatsApiAdapter>(client =>
                {
                    client.BaseAddress = new Uri("https://statsapi.mlb.com/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                })
                .AddStandardResilienceHandler();
        }

        if (options.OddsApiIo.Enabled && !string.IsNullOrWhiteSpace(options.OddsApiIo.ApiKey))
        {
            services.AddHttpClient<OddsApiIoAdapter>(client =>
                {
                    client.BaseAddress = new Uri("https://api.odds-api.io/v3/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                })
                // Standard resilience handler: retry with jittered backoff, a circuit breaker
                // that trips on sustained failure, and per-attempt timeouts. A tripped breaker
                // is what the reliability layer surfaces as a degraded source.
                .AddStandardResilienceHandler();

            services.AddScoped<IOddsSource>(sp => sp.GetRequiredService<OddsApiIoAdapter>());
        }

        if (options.TheOddsApi.Enabled && !string.IsNullOrWhiteSpace(options.TheOddsApi.ApiKey))
        {
            services.AddHttpClient<TheOddsApiAdapter>(client =>
                {
                    client.BaseAddress = new Uri("https://api.the-odds-api.com/v4/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                })
                .AddStandardResilienceHandler();

            services.AddScoped<IOddsSource>(sp => sp.GetRequiredService<TheOddsApiAdapter>());
        }

        return services;
    }

    /// <summary>
    /// Adds the unattended scheduler. Separate from <see cref="AddLineOpsIngestion"/> so the
    /// web app can trigger ingestion manually without also owning the schedule — which is what
    /// lets the worker be split into its own process later without touching either codebase.
    /// </summary>
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

    public static IServiceCollection AddLineOpsIngestionScheduler(this IServiceCollection services)
    {
        services.AddHostedService<IngestionScheduler>();
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

        foreach (var source in new[] { options.OddsApiIo, options.TheOddsApi, options.BallDontLie, options.Espn })
            source.Bookmakers = Unique(source.Bookmakers);
    }

    /// <summary>Resolves the effective ingestion options without needing a scope.</summary>
    public static IngestionOptions GetIngestionOptions(this IServiceProvider provider)
        => provider.GetRequiredService<IOptions<IngestionOptions>>().Value;
}
