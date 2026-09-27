using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using LineOps.Desk;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LineOps.Web.Access;

/// <summary>
/// The gate: a code in, a cookie out. See ADR 0018.
///
/// <para>
/// Registered only when <see cref="AccessOptions.Mode"/> is <see cref="AccessMode.Gated"/>; open
/// mode registers nothing, so the laptop runs exactly as before. Sign-in is a plain form posted
/// to a minimal endpoint rather than a desk window, because a cookie is set on an HTTP response
/// and a circuit has none.
/// </para>
/// </summary>
public static class AccessEndpoints
{
    public const string SignInPath = "/signin";
    public const string SignOutPath = "/signout";
    private const string SignInLimiter = "signin";

    /// <summary>
    /// Authentication, the fallback policy that sends a visitor without a code to the sign-in
    /// page, the desk's access answer, and a rate limit on guessing. Validates the configuration
    /// first: gated with no operator code would be a desk nobody can operate.
    /// </summary>
    public static IServiceCollection AddLineOpsAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AccessOptions.SectionName);
        services.Configure<AccessOptions>(section);
        var options = section.Get<AccessOptions>() ?? new AccessOptions();

        if (options.Mode != AccessMode.Gated)
            return services;

        if (!AccessCode.IsWellFormed(options.OperatorCodeHash))
            throw new InvalidOperationException(
                "Access:Mode is Gated but Access:OperatorCodeHash is missing or not a hash. " +
                "Make one with scripts/new-access-code.ps1 and supply it as Access__OperatorCodeHash.");

        if (!string.IsNullOrWhiteSpace(options.GuestCodeHash) && !AccessCode.IsWellFormed(options.GuestCodeHash))
            throw new InvalidOperationException("Access:GuestCodeHash is set but is not a hash.");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "lineops.access";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: a link to the desk from elsewhere should arrive signed in.
                // Cross-site posts and the circuit's own connection still go without it.
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.LoginPath = SignInPath;
                cookie.LogoutPath = SignOutPath;
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

        services.AddScoped<IDeskAccess, CircuitDeskAccess>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(SignInLimiter, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static bool IsGated(this IConfiguration configuration)
        => configuration.GetSection(AccessOptions.SectionName).Get<AccessOptions>()?.Mode == AccessMode.Gated;

    /// <summary>The sign-in page, the form it posts, and sign-out. Anonymous by definition.</summary>
    public static IEndpointRouteBuilder MapLineOpsAccess(this IEndpointRouteBuilder app)
    {
        app.MapGet(SignInPath, (HttpContext context, IAntiforgery antiforgery, DeskBrand brand) =>
                Page(context, antiforgery, brand, failed: false))
            .AllowAnonymous();

        app.MapPost(SignInPath, async (
                [FromForm] string? code,
                HttpContext context,
                IAntiforgery antiforgery,
                IOptions<AccessOptions> options,
                DeskBrand brand,
                ILoggerFactory loggers) =>
            {
                var role = AccessCode.Matches(code, options.Value.OperatorCodeHash) ? AccessRoles.Operator
                    : AccessCode.Matches(code, options.Value.GuestCodeHash) ? AccessRoles.Visitor
                    : null;

                var log = loggers.CreateLogger("LineOps.Web.Access");
                if (role is null)
                {
                    log.LogWarning("Sign-in refused from {Address}", context.Connection.RemoteIpAddress);
                    return Page(context, antiforgery, brand, failed: true);
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

        app.MapGet(SignOutPath, async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect(SignInPath);
            })
            .AllowAnonymous();

        return app;
    }

    private static IResult Page(HttpContext context, IAntiforgery antiforgery, DeskBrand brand, bool failed)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var e = (string s) => WebUtility.HtmlEncode(s);
        var notice = failed
            ? """<p class="notice" role="alert">That code opens nothing here.</p>"""
            : "";

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <meta name="color-scheme" content="dark light" />
            <meta name="robots" content="noindex" />
            <title>Sign in · {{e(brand.Wordmark + brand.Emphasis)}}</title>
            <style>
              :root { --bg:#1c1c1e; --panel:#2c2c2e; --text:#f5f5f7; --dim:#98989d; --line:#3a3a3c; --accent:#0a84ff; --bad:#ff453a; }
              @media (prefers-color-scheme: light) {
                :root { --bg:#f2f2f7; --panel:#ffffff; --text:#1c1c1e; --dim:#6e6e73; --line:#d1d1d6; --accent:#007aff; --bad:#d70015; }
              }
              * { box-sizing: border-box; }
              body { margin:0; min-height:100vh; display:grid; place-items:center; padding:16px;
                     background:var(--bg); color:var(--text);
                     font: 15px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, system-ui, sans-serif; }
              main { width:100%; max-width:360px; background:var(--panel); border:1px solid var(--line);
                     border-radius:14px; padding:28px 24px; }
              .mark { font-weight:700; letter-spacing:.08em; font-size:20px; margin:0 0 4px; }
              .mark em { font-style:normal; color:var(--accent); }
              .tag { color:var(--dim); margin:0 0 24px; font-size:13px; }
              label { display:block; font-size:13px; color:var(--dim); margin-bottom:6px; }
              input[type=password] { width:100%; padding:10px 12px; border-radius:8px; border:1px solid var(--line);
                     background:var(--bg); color:var(--text); font:inherit; }
              input[type=password]:focus { outline:2px solid var(--accent); outline-offset:1px; }
              button { margin-top:16px; width:100%; padding:10px 12px; border:0; border-radius:8px;
                       background:var(--accent); color:#fff; font:inherit; font-weight:600; cursor:pointer; }
              .notice { color:var(--bad); font-size:13px; margin:12px 0 0; }
              .foot { color:var(--dim); font-size:12px; margin:20px 0 0; }
            </style>
            </head>
            <body>
            <main>
              <p class="mark">{{e(brand.Wordmark)}}<em>{{e(brand.Emphasis)}}</em></p>
              <p class="tag">{{e(brand.Tagline)}}</p>
              <form method="post" action="{{SignInPath}}">
                <input type="hidden" name="{{e(tokens.FormFieldName)}}" value="{{e(tokens.RequestToken ?? "")}}" />
                <label for="code">Access code</label>
                <input id="code" name="code" type="password" autocomplete="current-password" required autofocus />
                {{notice}}
                <button type="submit">Open the desk</button>
              </form>
              <p class="foot">A guest code opens a desk that reads and changes nothing.</p>
            </main>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8",
            statusCode: failed ? StatusCodes.Status401Unauthorized : StatusCodes.Status200OK);
    }
}
