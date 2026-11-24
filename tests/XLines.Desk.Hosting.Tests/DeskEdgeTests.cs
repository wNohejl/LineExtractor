using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace XLines.Desk.Hosting.Tests;

/// <summary>The headers every response carries, and the one setting that says a proxy stands in front.</summary>
public class DeskEdgeTests
{
    [Fact]
    public void Sends_the_six_headers()
    {
        var headers = DeskEdge.Headers(new DeskEdgeOptions());

        Assert.Equal(6, headers.Count);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", headers["Permissions-Policy"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal(DeskEdge.DefaultContentSecurityPolicy, headers["Content-Security-Policy"]);
        Assert.Contains("frame-ancestors 'none'", DeskEdge.DefaultContentSecurityPolicy);
        Assert.Contains("img-src 'self' data:;", DeskEdge.DefaultContentSecurityPolicy);
    }

    [Fact]
    public void Extra_image_sources_join_img_src()
    {
        var options = new DeskEdgeOptions();
        options.ExtraImageSources.Add("https://images.example.com");
        options.ExtraImageSources.Add("https://cdn.example.org");

        var csp = DeskEdge.Headers(options)["Content-Security-Policy"];

        Assert.Contains("img-src 'self' data: https://images.example.com https://cdn.example.org;", csp);
        Assert.Contains("default-src 'self';", csp);
        Assert.EndsWith("frame-ancestors 'none'", csp);
    }

    /// <summary>
    /// The Development policy differs from the default in one directive, and only lets the
    /// tooling's sockets reach the developer's own machine: nothing else is widened.
    /// </summary>
    [Fact]
    public void Development_policy_lets_the_tooling_sockets_through_and_nothing_else()
    {
        var dev = DeskEdge.DevelopmentContentSecurityPolicy;

        Assert.Contains("connect-src 'self' http://localhost:* https://localhost:* ws://localhost:* wss://localhost:*;", dev);

        static IEnumerable<string> Others(string policy) => policy.Split(';')
            .Select(d => d.Trim())
            .Where(d => d.Length > 0 && !d.StartsWith("connect-src", StringComparison.Ordinal));

        Assert.Equal(Others(DeskEdge.DefaultContentSecurityPolicy), Others(dev));
    }

    [Fact]
    public void Behind_proxy_reads_its_key()
    {
        static IConfiguration With(string? value) => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Hosting:BehindProxy", value)])
            .Build();

        Assert.True(With("true").IsBehindProxy());
        Assert.False(With("false").IsBehindProxy());
        Assert.False(With(null).IsBehindProxy());
    }

    private static IConfiguration BehindProxy(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Hosting:BehindProxy", value)])
        .Build();

    /// <summary>
    /// One request through the forwarded-headers middleware as <see cref="DeskEdge.AddDeskEdge"/>
    /// configured it, arriving from <paramref name="proxy"/>: what the rest of the pipeline sees.
    /// </summary>
    private static async Task<HttpContext> Forwarded(IConfiguration configuration, IPAddress proxy, string forwardedFor)
    {
        var services = new ServiceCollection();
        services.AddDeskEdge(configuration);
        using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("xlines.example-tailnet.ts.net");
        context.Connection.RemoteIpAddress = proxy;
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task Behind_proxy_the_visitor_s_address_and_scheme_replace_the_proxy_s()
    {
        // A sidecar on the compose network (Caddy, or the tailnet's Serve proxy) is not on
        // loopback, so this also proves the framework's loopback-only trust was cleared.
        var context = await Forwarded(BehindProxy("true"), IPAddress.Parse("172.18.0.5"), "100.101.102.103");

        Assert.Equal(IPAddress.Parse("100.101.102.103"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
        Assert.True(context.Request.IsHttps);
    }

    [Fact]
    public async Task Behind_proxy_only_the_hop_the_proxy_added_is_believed()
    {
        // A proxy appends the address it saw to whatever the client sent, so with one hop
        // trusted an address the client wrote in itself cannot dodge the sign-in limit. This is
        // also the host-side case: tailscaled reaches `dotnet run` from loopback.
        var context = await Forwarded(BehindProxy("true"), IPAddress.Loopback, "203.0.113.9, 100.101.102.103");

        Assert.Equal(IPAddress.Parse("100.101.102.103"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task Not_behind_a_proxy_forwarded_headers_change_nothing()
    {
        var context = await Forwarded(BehindProxy(null), IPAddress.Parse("172.18.0.5"), "100.101.102.103");

        Assert.Equal(IPAddress.Parse("172.18.0.5"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }
}
