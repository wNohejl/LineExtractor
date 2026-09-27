using Microsoft.AspNetCore.HttpOverrides;

namespace LineOps.Web.Hosting;

/// <summary>
/// What the web host does differently when something other than its own machine can reach it.
/// See ADR 0019.
///
/// <para>
/// <c>Hosting:BehindProxy</c> says TLS ends at a reverse proxy on a network nothing else can
/// reach — the hosted topology in <c>deploy/</c>. The host then believes the proxy's
/// <c>X-Forwarded-For</c> and <c>-Proto</c>, so the sign-in limit counts the visitor's address
/// rather than the proxy's and a cookie marked Secure is issued over what the visitor sees as
/// HTTPS, and it leaves redirecting to HTTPS to the proxy that owns port 80.
/// </para>
///
/// <para>
/// The security headers are sent in every mode: the desk is never framed, loads nothing from
/// another origin, and has no reason to tell another site where a visitor came from.
/// </para>
/// </summary>
public static class EdgeHardening
{
    public static bool BehindProxy(this IConfiguration configuration)
        => configuration.GetValue("Hosting:BehindProxy", false);

    public static IServiceCollection AddEdgeHardening(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.BehindProxy())
            return services;

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // The proxy's address is whatever Docker gives it on the compose network, so it cannot
            // be listed in advance. Trusting any sender is safe only because the web container
            // publishes no port: the proxy is the only thing that can reach it. deploy/compose.yml
            // keeps it that way, and ADR 0019 says why it must.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            forwarded.ForwardLimit = 1;
        });

        return services;
    }

    /// <summary>First in the pipeline, so everything after it sees the visitor, not the proxy.</summary>
    public static WebApplication UseEdgeHardening(this WebApplication app)
    {
        if (app.Configuration.BehindProxy())
            app.UseForwardedHeaders();

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                return Task.CompletedTask;
            });
            await next();
        });

        return app;
    }

    /// <summary>
    /// Everything from this origin, nothing from any other. Two allowances, both forced by the
    /// framework rather than chosen: inline styles, because MudBlazor positions and sizes with
    /// <c>style</c> attributes; and inline script, because Blazor's <c>ImportMap</c> is an inline
    /// <c>&lt;script type="importmap"&gt;</c> whose content changes with every build, so it can be
    /// neither hashed in advance nor given a nonce. No script can arrive from anywhere else, and
    /// nothing is fetched, framed or posted cross-origin.
    /// </summary>
    public const string ContentSecurityPolicy =
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
}
