using LineOps.Ingestion;
using LineOps.Ingestion.Adapters;
using LineOps.Ingestion.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Tests.Adapters;

/// <summary>
/// ESPN refuses a request that does not identify its client.
///
/// <para>
/// On 2 August 2026 the port stopped working and every run recorded
/// <c>403 (Forbidden)</c>. Nothing had changed on our side — ESPN began rejecting requests
/// whose <c>User-Agent</c> it does not recognise, and <see cref="HttpClient"/> sends no
/// <c>User-Agent</c> at all by default. The port had therefore always been one policy change
/// away from breaking, and the policy changed.
/// </para>
///
/// <para>
/// Probing the endpoint showed the rule is narrower than "send something": an absent header, a
/// bespoke token (<c>LineOps/1.0</c>) and a full Chrome string are all refused, while the
/// product tokens of ordinary HTTP clients — <c>curl</c>, <c>python-requests</c>,
/// <c>Go-http-client</c>, <c>.NET</c> — are served. So the header has to name a real client,
/// and the honest one is the client we actually are. Impersonating a browser is both a lie and,
/// as it happens, blocked.
/// </para>
///
/// <para>
/// This is a configuration test rather than a live one: it pins that the header is set without
/// making the suite depend on ESPN being reachable, which would make an outage look like a
/// regression.
/// </para>
/// </summary>
public class EspnUserAgentTests
{
    private static HttpClient EspnClientFrom(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLineOpsIngestion(configuration);

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(EspnStatsAdapter));
    }

    [Fact]
    public void The_espn_client_identifies_itself()
    {
        var client = EspnClientFrom(("Ingestion:Espn:Enabled", "true"));

        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
    }

    /// <summary>
    /// A bespoke product token is refused by ESPN just as an absent header is, so naming the
    /// application here would look like a fix and restore nothing.
    /// </summary>
    [Fact]
    public void The_espn_client_names_a_real_http_client_rather_than_the_application()
    {
        var client = EspnClientFrom(("Ingestion:Espn:Enabled", "true"));

        var agent = client.DefaultRequestHeaders.UserAgent.ToString();

        Assert.Contains(".NET", agent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LineOps", agent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Claiming to be a browser is a lie, and ESPN refuses it anyway — so it must never be the
    /// thing someone reaches for the next time this breaks.
    /// </summary>
    [Fact]
    public void The_espn_client_does_not_impersonate_a_browser()
    {
        var client = EspnClientFrom(("Ingestion:Espn:Enabled", "true"));

        var agent = client.DefaultRequestHeaders.UserAgent.ToString();

        Assert.DoesNotContain("Mozilla", agent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Chrome", agent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Safari", agent, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void A_configured_user_agent_is_used()
    {
        var client = EspnClientFrom(
            ("Ingestion:Espn:Enabled", "true"),
            ("Ingestion:Espn:UserAgent", "curl/8.5.0"));

        Assert.Equal("curl/8.5.0", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    /// <summary>
    /// The whole point of the option is that ESPN policy can change again without a rebuild, so
    /// a value the default would never produce has to survive binding untouched.
    /// </summary>
    [Fact]
    public void A_configured_user_agent_is_not_merged_with_the_default()
    {
        var client = EspnClientFrom(
            ("Ingestion:Espn:Enabled", "true"),
            ("Ingestion:Espn:UserAgent", "python-requests/2.31"));

        var agent = client.DefaultRequestHeaders.UserAgent.ToString();

        Assert.Equal("python-requests/2.31", agent);
        Assert.DoesNotContain(".NET", agent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The failure mode worth guarding. An unparseable override that simply threw or was
    /// swallowed would leave the header absent — which is precisely the condition that caused
    /// the outage this option exists to prevent. A typo must not reproduce the bug.
    /// </summary>
    [Theory]
    [InlineData("has spaces and \"quotes")]
    [InlineData("(")]
    [InlineData("   ")]
    public void An_unusable_user_agent_falls_back_rather_than_leaving_the_header_absent(string bad)
    {
        var client = EspnClientFrom(
            ("Ingestion:Espn:Enabled", "true"),
            ("Ingestion:Espn:UserAgent", bad));

        // Equality with the default, not merely "non-empty": if an unusable value were
        // accepted verbatim the header would hold it, and this would fail rather than pass
        // for the wrong reason.
        Assert.Equal(SourceOptions.DefaultUserAgent, client.DefaultRequestHeaders.UserAgent.ToString());
    }
}
