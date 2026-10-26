using Bunit;
using LineOps.Data.CrossReference;
using LineOps.Web.Components.Desk;
using LineOps.Web.Components.Snippets;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LineOps.Web.Tests;

/// <summary>
/// A snippet whose load failed offers the way to load it again. Once a row's views became a
/// segmented control, pressing the chosen view a second time did nothing, so "Try again." in
/// the sentence named an action the row no longer had. Each snippet now carries a Try again
/// key that re-runs its own load, and the sentence says only what did not load.
///
/// The services the snippets resolve are registered as factories that count and throw, so
/// every attempt is a failure the test can count without a database.
/// </summary>
public class SnippetRetryTests : DeskTestContext
{
    private int _attempts;

    private void FailBoard()
        => Services.AddScoped<BoardService>(_ =>
        {
            _attempts++;
            throw new InvalidOperationException("connection refused (raw text)");
        });

    private void FailLog()
        => Services.AddScoped<GameLogService>(_ =>
        {
            _attempts++;
            throw new InvalidOperationException("connection refused (raw text)");
        });

    private void AssertRetries<TSnippet>(IRenderedComponent<TSnippet> snippet, string sentence)
        where TSnippet : IComponent
    {
        snippet.WaitForAssertion(() => Assert.NotNull(snippet.Find(".note")));
        Assert.Equal(1, _attempts);

        var note = snippet.Find(".note");
        Assert.Contains(sentence, note.TextContent);
        Assert.DoesNotContain("Try again.", note.TextContent);
        Assert.DoesNotContain("connection refused", snippet.Markup);

        var key = snippet.Find(".note button");
        Assert.Equal("Try again", key.TextContent.Trim());
        Assert.Contains("desk-btn--plain", key.ClassName);
        Assert.Contains("desk-btn--sm", key.ClassName);

        key.Click();

        snippet.WaitForAssertion(() => Assert.Equal(2, _attempts));
        snippet.WaitForAssertion(() => Assert.NotNull(snippet.Find(".note button")));
    }

    [Fact]
    public void The_odds_snippet_tries_again()
    {
        FailBoard();
        AssertRetries(RenderComponent<OddsSnippet>(p => p.Add(s => s.GameId, 7)), "The market did not load.");
    }

    [Fact]
    public void The_form_snippet_tries_again()
    {
        FailBoard();
        AssertRetries(RenderComponent<FormSnippet>(p => p.Add(s => s.GameId, 7)), "Recent form did not load.");
    }

    [Fact]
    public void The_head_to_head_snippet_tries_again()
    {
        FailLog();
        AssertRetries(RenderComponent<HeadToHeadSnippet>(p => p.Add(s => s.GameId, 7)), "Previous meetings did not load.");
    }

    [Fact]
    public void The_player_snippet_tries_again()
    {
        FailLog();
        AssertRetries(RenderComponent<PlayerRecentSnippet>(p => p.Add(s => s.PlayerId, 7)), "Recent games did not load.");
    }

    [Fact]
    public void The_key_does_not_reach_the_row_behind_it()
    {
        // A snippet sits under a grid row whose click opens the game. The strip and the
        // Board's hosted child row already stop clicks; the failure stops its own as well, so
        // a snippet hosted anywhere else cannot open a window by asking to load again.
        var rowClicks = 0;
        var retries = 0;

        var host = Render(builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "row-under-test");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => rowClicks++));
            builder.OpenComponent<SnippetFailure>(3);
            builder.AddAttribute(4, nameof(SnippetFailure.Message), "The market did not load.");
            builder.AddAttribute(5, nameof(SnippetFailure.OnRetry), EventCallback.Factory.Create(this, () => retries++));
            builder.CloseComponent();
            builder.CloseElement();
        });

        host.Find(".note button").Click();

        Assert.Equal(1, retries);
        Assert.Equal(0, rowClicks);
    }
}
