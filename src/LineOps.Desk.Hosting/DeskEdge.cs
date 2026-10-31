using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineOps.Desk.Hosting;

/// <summary>What the edge sends on every response.</summary>
public sealed class DeskEdgeOptions
{
    /// <summary>The Content-Security-Policy, before <see cref="ExtraImageSources"/> join it.</summary>
    public string ContentSecurityPolicy { get; set; } = DeskEdge.DefaultContentSecurityPolicy;

    /// <summary>
    /// Origins the desk may load images from beyond its own, appended to the policy's
    /// <c>img-src</c> — for an application whose windows show pictures served elsewhere.
    /// </summary>
    public IList<string> ExtraImageSources { get; } = new List<string>();
}

/// <summary>
/// What the web host does differently when something other than its own machine can reach it.
/// See ADR 0019.
///
/// <para>
/// <c>Hosting:BehindProxy</c> says TLS ends at a reverse proxy on a network nothing else can
/// reach. The host then believes the proxy's <c>X-Forwarded-For</c> and <c>-Proto</c>, so the
/// sign-in limit counts the visitor's address rather than the proxy's and a cookie marked Secure
/// is issued over what the visitor sees as HTTPS, and it leaves redirecting to HTTPS to the proxy
/// that owns port 80.
/// </para>
///
/// <para>
/// The security headers are sent in every mode: the desk is never framed, loads nothing from
/// another origin, and has no reason to tell another site where a visitor came from.
/// </para>
/// </summary>
public static class DeskEdge
{
    /// <summary>
    /// Everything from this origin, nothing from any other. Two allowances, both forced by the
    /// framework rather than chosen: inline styles, because MudBlazor positions and sizes with
    /// <c>style</c> attributes; and inline script, because Blazor's <c>ImportMap</c> is an inline
    /// <c>&lt;script type="importmap"&gt;</c> whose content changes with every build, so it can be
    /// neither hashed in advance nor given a nonce. No script can arrive from anywhere else, and
    /// nothing is fetched, framed or posted cross-origin.
    /// </summary>
    public const string DefaultContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static bool IsBehindProxy(this IConfiguration configuration)
        => configuration.GetValue("Hosting:BehindProxy", false);

    /// <summary>
    /// The edge's options, always; and, behind a proxy, trust in its forwarded headers.
    /// </summary>
    public static IServiceCollection AddDeskEdge(
        this IServiceCollection services, IConfiguration configuration, Action<DeskEdgeOptions>? configure = null)
    {
        var edge = services.AddOptions<DeskEdgeOptions>();
        if (configure is not null)
            edge.Configure(configure);

        if (!configuration.IsBehindProxy())
            return services;

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // The proxy's address is whatever the container network gives it, so it cannot be
            // listed in advance. Trusting any sender is safe only because the web container
            // publishes no port: the proxy is the only thing that can reach it. ADR 0019 says
            // why it must stay that way.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            forwarded.ForwardLimit = 1;
        });

        return services;
    }

    /// <summary>First in the pipeline, so everything after it sees the visitor, not the proxy.</summary>
    public static WebApplication UseDeskEdge(this WebApplication app)
    {
        if (app.Configuration.IsBehindProxy())
            app.UseForwardedHeaders();

        var headers = Headers(app.Services.GetRequiredService<IOptions<DeskEdgeOptions>>().Value);

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                foreach (var (name, value) in headers)
                    context.Response.Headers[name] = value;
                return Task.CompletedTask;
            });
            await next();
        });

        return app;
    }

    /// <summary>The six headers every response carries, with the policy's image sources joined.</summary>
    public static IReadOnlyDictionary<string, string> Headers(DeskEdgeOptions options) => new Dictionary<string, string>
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
        ["Cross-Origin-Opener-Policy"] = "same-origin",
        ["Content-Security-Policy"] = Compose(options),
    };

    private static string Compose(DeskEdgeOptions options)
    {
        if (options.ExtraImageSources.Count == 0)
            return options.ContentSecurityPolicy;

        var extra = string.Join(' ', options.ExtraImageSources);
        var directives = options.ContentSecurityPolicy.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
        var image = directives.FindIndex(d => d.StartsWith("img-src", StringComparison.OrdinalIgnoreCase));

        if (image < 0)
            directives.Add("img-src " + extra);
        else
            directives[image] += " " + extra;

        return string.Join("; ", directives);
    }
}
