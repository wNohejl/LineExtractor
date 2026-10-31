using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LineOps.Desk.Hosting;

/// <summary>
/// The gate: a code in, a cookie out. See ADR 0018.
///
/// <para>
/// Three calls, in pipeline order: <see cref="AddDeskAccess"/> with the services,
/// <see cref="UseDeskAccess"/> before antiforgery, and <see cref="MapDeskAccess"/> with the
/// endpoints. Open mode registers only the options and the other two do nothing, so a host with
/// one person at it runs exactly as before. Sign-in is a plain form posted to a minimal endpoint
/// rather than a desk window, because a cookie is set on an HTTP response and a circuit has none.
/// </para>
/// </summary>
public static class DeskAccessEndpoints
{
    private const string SignInLimiter = "desk-signin";
    private const string LoggerCategory = "LineOps.Desk.Hosting.Access";

    /// <summary>
    /// Binds <see cref="DeskAccessOptions.SectionName"/>, then <paramref name="configure"/>. When
    /// gated: authentication by cookie, the fallback policy that sends a visitor without a code
    /// to the sign-in page, the desk's access answer, and a rate limit on guessing. Validates the
    /// configuration first: gated with no operator code would be a desk nobody can operate.
    /// </summary>
    public static IServiceCollection AddDeskAccess(
        this IServiceCollection services, IConfiguration configuration, Action<DeskAccessOptions>? configure = null)
    {
        var section = configuration.GetSection(DeskAccessOptions.SectionName);
        services.Configure<DeskAccessOptions>(section);
        if (configure is not null)
            services.Configure(configure);

        var options = section.Get<DeskAccessOptions>() ?? new DeskAccessOptions();
        configure?.Invoke(options);

        if (options.Mode != DeskAccessMode.Gated)
            return services;

        if (!DeskAccessCode.IsWellFormed(options.OperatorCodeHash))
            throw new InvalidOperationException(
                "Access:Mode is Gated but Access:OperatorCodeHash is missing or not a hash. " +
                "Make one with scripts/new-access-code.ps1 and supply it as Access__OperatorCodeHash.");

        if (!string.IsNullOrWhiteSpace(options.GuestCodeHash) && !DeskAccessCode.IsWellFormed(options.GuestCodeHash))
            throw new InvalidOperationException("Access:GuestCodeHash is set but is not a hash.");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = options.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: a link to the desk from elsewhere should arrive signed in.
                // Cross-site posts and the circuit's own connection still go without it.
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.LoginPath = options.SignInPath;
                cookie.LogoutPath = options.SignOutPath;
                cookie.ExpireTimeSpan = options.SessionLifetime;
                cookie.SlidingExpiration = true;
            });

        services.AddAuthorization(authorization =>
        {
            // Every endpoint that does not say otherwise needs a signed-in desk — the page, and
            // the circuit's hub with it. Anonymous visitors, once allowed, need nothing.
            if (!options.AllowAnonymousVisitors)
                authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        services.AddCascadingAuthenticationState();

        services.AddScoped<IDeskAccess, DeskCircuitAccess>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(SignInLimiter, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    /// <summary>Whether configuration asks for a gated desk, read from <see cref="DeskAccessOptions.SectionName"/>.</summary>
    public static bool IsDeskGated(this IConfiguration configuration)
        => configuration.GetSection(DeskAccessOptions.SectionName).Get<DeskAccessOptions>()?.Mode == DeskAccessMode.Gated;

    /// <summary>When gated: authentication, authorization and the rate limiter, in that order. Before antiforgery.</summary>
    public static WebApplication UseDeskAccess(this WebApplication app)
    {
        if (!IsGated(app.Services))
            return app;

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        return app;
    }

    /// <summary>When gated: the sign-in page, the form it posts, and sign-out. Anonymous by definition.</summary>
    public static IEndpointRouteBuilder MapDeskAccess(this IEndpointRouteBuilder app)
    {
        if (!IsGated(app.ServiceProvider))
            return app;

        var paths = app.ServiceProvider.GetRequiredService<IOptions<DeskAccessOptions>>().Value;

        app.MapGet(paths.SignInPath, (HttpContext context, IAntiforgery antiforgery, IOptions<DeskAccessOptions> options, DeskBrand brand) =>
                Page(context, antiforgery, options.Value, brand, failed: false))
            .AllowAnonymous();

        app.MapPost(paths.SignInPath, async (
                [FromForm] string? code,
                HttpContext context,
                IAntiforgery antiforgery,
                IOptions<DeskAccessOptions> options,
                DeskBrand brand,
                ILoggerFactory loggers) =>
            {
                var role = DeskAccessCode.Matches(code, options.Value.OperatorCodeHash) ? DeskAccessRoles.Operator
                    : DeskAccessCode.Matches(code, options.Value.GuestCodeHash) ? DeskAccessRoles.Visitor
                    : null;

                var log = loggers.CreateLogger(LoggerCategory);
                if (role is null)
                {
                    log.LogWarning("Sign-in refused from {Address}", context.Connection.RemoteIpAddress);
                    return Page(context, antiforgery, options.Value, brand, failed: true);
                }

                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)],
                    CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(new ClaimsPrincipal(identity));
                log.LogInformation("Signed in as {Role} from {Address}", role, context.Connection.RemoteIpAddress);

                return Results.Redirect("/");
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInLimiter);

        app.MapGet(paths.SignOutPath, async (HttpContext context, IOptions<DeskAccessOptions> options) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect(options.Value.SignInPath);
            })
            .AllowAnonymous();

        return app;
    }

    // The options as the application left them, configure delegate included — not only as
    // configuration has them.
    private static bool IsGated(IServiceProvider services)
        => services.GetRequiredService<IOptions<DeskAccessOptions>>().Value.Mode == DeskAccessMode.Gated;

    private static IResult Page(HttpContext context, IAntiforgery antiforgery, DeskAccessOptions options, DeskBrand brand, bool failed)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var html = DeskSignInPage.Render(brand, tokens.FormFieldName, tokens.RequestToken ?? "", options.SignInPath, failed);

        return Results.Content(html, "text/html; charset=utf-8",
            statusCode: failed ? StatusCodes.Status401Unauthorized : StatusCodes.Status200OK);
    }
}
