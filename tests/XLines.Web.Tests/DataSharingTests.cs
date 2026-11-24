using XLines.Core.Contracts;
using XLines.Desk;
using XLines.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace XLines.Web.Tests;

/// <summary>
/// Who may see data from a personal-use source (ADR 0024). A local desk is the developer's own,
/// so everyone at it sees everything, as before. A shared desk shows it to the operator alone.
/// </summary>
public class DataSharingTests
{
    private sealed class Access(bool canOperate) : IDeskAccess
    {
        public bool CanOperate => canOperate;
    }

    [Theory]
    [InlineData(DataSharingMode.Local, true, true)]
    [InlineData(DataSharingMode.Local, false, true)]
    [InlineData(DataSharingMode.Shared, true, true)]
    [InlineData(DataSharingMode.Shared, false, false)]
    public void Personal_use_data_is_shown_locally_or_to_the_operator(DataSharingMode mode, bool canOperate, bool sees)
    {
        Assert.Equal(sees, DeskDataTier.Decide(mode, canOperate));

        var tier = new DeskDataTier(Options.Create(new DataSharingOptions { Mode = mode }), new Access(canOperate));
        Assert.Equal(sees, tier.CanSeePersonalUseSources);
    }

    [Fact]
    public void Local_is_the_default()
        => Assert.Equal(DataSharingMode.Local, new DataSharingOptions().Mode);

    /// <summary>The answer is read each time, so a circuit that learns who signed in after it was built gets it.</summary>
    [Fact]
    public void The_answer_follows_the_access_it_is_asked_with()
    {
        var access = new Switchable();
        var tier = new DeskDataTier(Options.Create(new DataSharingOptions { Mode = DataSharingMode.Shared }), access);

        Assert.False(tier.CanSeePersonalUseSources);
        access.CanOperate = true;
        Assert.True(tier.CanSeePersonalUseSources);
    }

    private sealed class Switchable : IDeskAccess
    {
        public bool CanOperate { get; set; }
    }

    [Fact]
    public void The_web_host_registers_its_answer_in_place_of_the_default()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeskAccess>(new Access(false));
        services.AddDataSharing(Configuration(("Sharing:Mode", "Shared"), ("Access:Mode", "Gated")));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var tier = scope.ServiceProvider.GetRequiredService<IDataTier>();
        Assert.IsType<DeskDataTier>(tier);
        Assert.False(tier.CanSeePersonalUseSources);
    }

    /// <summary>
    /// An open desk makes everyone its operator, so "the operator alone" would be everyone. Shared
    /// on an open desk would promise what it cannot keep, so the host refuses to start with it.
    /// </summary>
    [Fact]
    public void Shared_refuses_a_desk_that_is_not_gated()
    {
        var services = new ServiceCollection();

        var refused = Assert.Throws<InvalidOperationException>(() =>
            services.AddDataSharing(Configuration(("Sharing:Mode", "Shared"))));
        Assert.Contains("Gated", refused.Message);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
