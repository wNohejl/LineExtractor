using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineOps.Desk.Hosting.Tests;

/// <summary>
/// What <see cref="DeskAccessEndpoints.AddDeskAccess"/> registers in each mode. Open registers
/// the options and nothing else, so a single-person host runs as it always has; gated refuses to
/// start without an operator code, and otherwise brings the cookie and the circuit's answer.
/// </summary>
public class DeskAccessRegistrationTests
{
    // Made by scripts/new-access-code.ps1 -Code 'test-code-1'; no PBKDF2 run per test.
    private const string Hash =
        "pbkdf2-sha256.600000.WS+gSSSb5yllgWZUr3o8XA==.E3SPAqf5NZGklCRLyeVnTXgvfy9d0DhRC8ZzXFUquHQ=";

    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IConfiguration Gated() => Config(("Access:Mode", "Gated"), ("Access:OperatorCodeHash", Hash));

    [Fact]
    public void Open_registers_no_gate()
    {
        var services = new ServiceCollection();

        services.AddDeskAccess(Config());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAuthenticationService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDeskAccess));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(DeskAccessMode.Open, provider.GetRequiredService<IOptions<DeskAccessOptions>>().Value.Mode);
        Assert.False(Config().IsDeskGated());
    }

    [Fact]
    public void Gated_without_an_operator_hash_refuses_to_start()
    {
        var services = new ServiceCollection();

        var refused = Assert.Throws<InvalidOperationException>(
            () => services.AddDeskAccess(Config(("Access:Mode", "Gated"))));

        Assert.Contains("Access__OperatorCodeHash", refused.Message);
    }

    [Fact]
    public void Gated_registers_the_circuit_s_access()
    {
        var services = new ServiceCollection();

        services.AddDeskAccess(Gated());

        Assert.True(Gated().IsDeskGated());
        Assert.Contains(services, d => d.ServiceType == typeof(IAuthenticationService));
        var access = Assert.Single(services, d => d.ServiceType == typeof(IDeskAccess));
        Assert.Equal(typeof(DeskCircuitAccess), access.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, access.Lifetime);
    }

    /// <summary>An application that already has signed-in visitors keeps them by naming its cookie.</summary>
    [Fact]
    public void The_cookie_takes_its_configured_name()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDeskAccess(Gated(), o => o.CookieName = "lineops.access");

        using var provider = services.BuildServiceProvider();
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal("lineops.access", cookie.Cookie.Name);
        Assert.Equal("/signin", cookie.LoginPath);
        Assert.Equal("/signout", cookie.LogoutPath);
        Assert.Equal(TimeSpan.FromDays(14), cookie.ExpireTimeSpan);
        Assert.Equal("lineops.access", provider.GetRequiredService<IOptions<DeskAccessOptions>>().Value.CookieName);
    }

    [Fact]
    public void The_default_cookie_is_the_desk_s()
        => Assert.Equal("desk.access", new DeskAccessOptions().CookieName);

    /// <summary>The circuit's access answers the Desk's three questions from its own gate.</summary>
    [Fact]
    public void The_circuit_access_names_its_gate()
    {
        var options = new DeskAccessOptions { Mode = DeskAccessMode.Gated, SignInPath = "/in", SignOutPath = "/out" };
        IDeskAccess access = new DeskCircuitAccess(new NobodySignedIn(), Options.Create(options));

        Assert.True(access.IsGated);
        Assert.False(access.CanOperate);
        Assert.Equal("/in", access.SignInPath);
        Assert.Equal("/out", access.SignOutPath);

        IDeskAccess open = new DeskCircuitAccess(new NobodySignedIn(), Options.Create(new DeskAccessOptions()));
        Assert.False(open.IsGated);
        Assert.True(open.CanOperate);

        // The Desk's own default has no gate and nowhere to sign in.
        IDeskAccess desk = OpenDeskAccess.Instance;
        Assert.False(desk.IsGated);
        Assert.Null(desk.SignInPath);
        Assert.Null(desk.SignOutPath);
    }

    private sealed class NobodySignedIn : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }
}
