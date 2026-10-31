using Bunit;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Snippets;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// The tale of the tape reads a bout once per bout. The Game window hands it a new lambda for
/// its fighter link on every render, and a parameter that changes identity is a parameter set:
/// the tape read the bout again each time the Game window drew, which is every pulse of the
/// window's chrome. A new bout — the window turning its page — is still a new read.
/// </summary>
public class TaleOfTheTapeTests : DeskTestContext
{
    /// <summary>A fight card on no database: every bout reads as "not a fight", and is counted.</summary>
    private sealed class CountedFights() : FightService(null!)
    {
        public List<int> Reads { get; } = [];

        public override Task<FightTape?> TapeAsync(int gameId, CancellationToken ct = default)
        {
            Reads.Add(gameId);
            return Task.FromResult<FightTape?>(null);
        }
    }

    private readonly CountedFights _fights = new();

    public TaleOfTheTapeTests()
    {
        Services.AddSingleton<FightService>(_fights);
        // A missing profile offers its read in place, through the desk's job runner.
        Services.AddScoped<LineOps.Desk.Primitives.DeskToasts>();
        Services.AddScoped<LineOps.Desk.DeskJobRunner>();
    }

    /// <summary>What the Game window passes on each render: a fresh delegate for the same link.</summary>
    private EventCallback<(int TeamId, string Name)> FreshLink()
        => EventCallback.Factory.Create<(int TeamId, string Name)>(this, (_) => { });

    [Fact]
    public void The_same_bout_with_a_new_link_is_not_read_again()
    {
        var tape = RenderComponent<TaleOfTheTape>(p => p
            .Add(x => x.GameId, 7)
            .Add(x => x.OnFighter, FreshLink()));
        tape.WaitForAssertion(() => Assert.Contains("not a fight", tape.Markup));

        for (var i = 0; i < 3; i++)
            tape.SetParametersAndRender(p => p.Add(x => x.OnFighter, FreshLink()));

        Assert.Equal([7], _fights.Reads);
    }

    [Fact]
    public void A_new_bout_is_read()
    {
        var tape = RenderComponent<TaleOfTheTape>(p => p
            .Add(x => x.GameId, 7)
            .Add(x => x.OnFighter, FreshLink()));
        tape.WaitForAssertion(() => Assert.Contains("not a fight", tape.Markup));

        tape.SetParametersAndRender(p => p
            .Add(x => x.GameId, 8)
            .Add(x => x.OnFighter, FreshLink()));

        tape.WaitForAssertion(() => Assert.Equal([7, 8], _fights.Reads));
    }
}
