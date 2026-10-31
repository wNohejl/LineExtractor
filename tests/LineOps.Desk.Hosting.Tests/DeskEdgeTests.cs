using Microsoft.Extensions.Configuration;

namespace LineOps.Desk.Hosting.Tests;

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
}
