using Bunit;
using LineOps.Core.Entities;
using LineOps.Desk.Primitives;
using LineOps.Web.Components.Desk;
using System.Text.RegularExpressions;

namespace LineOps.Web.Tests;

/// <summary>
/// Three small pieces of expression (Task 14): a live game's tag carries a pulsing dot, a
/// result reads as one capsule wherever it is drawn, and a snippet arrives rather than appears.
/// The motion itself is CSS, so the rules that hold it — including the reduced-motion ones —
/// are pinned against lineops.css.
/// </summary>
public class LiveAndResultTests : DeskTestContext
{
    private static readonly string Css =
        File.ReadAllText(Path.Combine(RepoRoot(), "src/LineOps.Desk/wwwroot/css/lineops.css"));

    // ---- Live ----------------------------------------------------------------------------

    [Fact]
    public void Live_tag_draws_a_dot_before_its_word_and_keeps_the_word()
    {
        var cut = RenderComponent<Tag>(p => p
            .Add(t => t.State, DeskState.Warning)
            .Add(t => t.Live, true)
            .AddChildContent("Live"));

        var tag = cut.Find("span.tag");
        Assert.Contains("tag--live", tag.ClassList);
        Assert.Contains("tag--warn", tag.ClassList);

        var dot = tag.FirstElementChild;
        Assert.NotNull(dot);
        Assert.Contains("tag__live", dot!.ClassList);
        Assert.Equal("Live", tag.TextContent.Trim());
    }

    [Fact]
    public void Live_dot_is_hidden_from_assistive_tech()
    {
        var cut = RenderComponent<Tag>(p => p.Add(t => t.Live, true).AddChildContent("Live"));

        Assert.Equal("true", cut.Find(".tag__live").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_tag_that_is_not_live_has_no_dot()
    {
        var cut = RenderComponent<Tag>(p => p.AddChildContent("Final"));

        Assert.Empty(cut.FindAll(".tag__live"));
        Assert.DoesNotContain("tag--live", cut.Find("span.tag").ClassList);
    }

    [Fact]
    public void Live_dot_pulses_and_holds_still_under_reduced_motion()
    {
        var rule = Block(Css, @"\.tag__live\s*\{");
        Assert.Contains("width: 6px", rule);
        Assert.Contains("height: 6px", rule);
        Assert.Contains("var(--state-warning)", rule);
        Assert.Matches(@"animation:\s*tag-live\s+1\.2s\s+ease-in-out\s+infinite\s+alternate", rule);

        var keys = Block(Css, @"@keyframes\s+tag-live\s*\{");
        Assert.Matches(@"opacity:\s*1\b", keys);
        Assert.Matches(@"opacity:\s*\.35", keys);

        Assert.Matches(new Regex(
            @"@media \(prefers-reduced-motion: reduce\)\s*\{[^@]*\.tag__live\s*\{\s*animation:\s*none;"),
            Css);
    }

    // ---- Results -------------------------------------------------------------------------

    [Theory]
    [InlineData(ResultKind.Win, DeskState.Positive, "W", "tag--good")]
    [InlineData(ResultKind.Loss, DeskState.Negative, "L", "tag--bad")]
    [InlineData(ResultKind.Push, DeskState.Neutral, "P", null)]
    [InlineData(ResultKind.Tie, DeskState.Neutral, "T", null)]
    public void Result_reads_as_its_capsule(ResultKind kind, DeskState state, string letter, string? css)
    {
        Assert.Equal(state, ResultTag.StateOf(kind));
        Assert.Equal(letter, ResultTag.LetterOf(kind));

        var cut = RenderComponent<ResultTag>(p => p.Add(r => r.Result, kind));
        var tag = cut.Find("span.tag");

        Assert.Equal(letter, tag.TextContent.Trim());
        Assert.Contains("tag--result", tag.ClassList);

        // P and T are the neutral capsule: no state wash at all.
        var washes = tag.ClassList.Where(c => c is "tag--good" or "tag--bad" or "tag--warn" or "tag--info");
        Assert.Equal(css is null ? [] : [css], washes);
    }

    [Fact]
    public void A_capsule_can_say_more_than_its_letter_and_keep_its_state()
    {
        var cut = RenderComponent<ResultTag>(p => p
            .Add(r => r.Result, ResultKind.Loss)
            .Add(r => r.Title, "Against the closing spread")
            .AddChildContent("No"));

        var tag = cut.Find("span.tag");
        Assert.Equal("No", tag.TextContent.Trim());
        Assert.Contains("tag--bad", tag.ClassList);
        Assert.Equal("Against the closing spread", tag.GetAttribute("title"));
    }

    [Theory]
    [InlineData(true, 5, 3, ResultKind.Win)]
    [InlineData(false, 3, 5, ResultKind.Loss)]
    // The lookups grade a final as f > a, so a tie arrives as "not won"; the score says tie.
    [InlineData(false, 20, 20, ResultKind.Tie)]
    public void A_final_score_names_its_result(bool won, int scoreFor, int against, ResultKind expected)
        => Assert.Equal(expected, ResultTag.Of(won, scoreFor, against));

    [Fact]
    public void An_unfinished_game_has_no_result()
        => Assert.Null(ResultTag.Of(null, null, null));

    [Theory]
    [InlineData(EntryResult.Win, ResultKind.Win)]
    [InlineData(EntryResult.Loss, ResultKind.Loss)]
    [InlineData(EntryResult.Push, ResultKind.Push)]
    public void A_graded_bet_names_its_result(EntryResult result, ResultKind expected)
        => Assert.Equal(expected, ResultTag.Of(result));

    [Theory]
    [InlineData(EntryResult.Pending)]
    [InlineData(EntryResult.Void)]
    public void An_ungraded_bet_has_no_result(EntryResult result)
        => Assert.Null(ResultTag.Of(result));

    // ---- Snippet arrival -----------------------------------------------------------------

    [Fact]
    public void A_snippet_arrives()
    {
        var cut = RenderComponent<SnippetShell>(p => p
            .Add(s => s.Title, "Recent form")
            .AddChildContent("body"));

        var root = cut.Find("[role=region]");
        Assert.Contains("snippet", root.ClassList);
        Assert.Contains("snippet--arrive", root.ClassList);
    }

    [Fact]
    public void Snippet_arrival_is_180ms_ease_out_and_opacity_only_under_reduced_motion()
    {
        var rule = Block(Css, @"\.snippet--arrive\s*\{");
        Assert.Matches(@"animation:\s*snippet-arrive\s+180ms\s+ease-out", rule);

        var keys = Block(Css, @"@keyframes\s+snippet-arrive\s*\{");
        Assert.Matches(@"from\s*\{\s*opacity:\s*0;\s*transform:\s*translateY\(-4px\);\s*\}", keys);
        Assert.Matches(@"to\s*\{\s*opacity:\s*1;\s*transform:\s*none;\s*\}", keys);

        var fade = Block(Css, @"@keyframes\s+snippet-fade\s*\{");
        Assert.DoesNotContain("transform", fade);

        Assert.Matches(new Regex(
            @"@media \(prefers-reduced-motion: reduce\)\s*\{[^@]*\.snippet--arrive\s*\{\s*animation-name:\s*snippet-fade;"),
            Css);
    }

    // ---- helpers -------------------------------------------------------------------------

    /// <summary>The body of the first block whose opener matches, braces balanced.</summary>
    private static string Block(string css, string opener)
    {
        var m = Regex.Match(css, opener);
        Assert.True(m.Success, $"no block matching {opener} in lineops.css");

        var depth = 1;
        var i = m.Index + m.Length;
        var start = i;
        for (; i < css.Length && depth > 0; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}') depth--;
        }

        return css[start..(i - 1)];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

/// <summary>A game's state reads in sentence case, the same word on the Board and the Team log.</summary>
public class StatusWordTests
{
    [Theory]
    [InlineData(GameStatus.Live, "Live")]
    [InlineData(GameStatus.Final, "Final")]
    [InlineData(GameStatus.Postponed, "Postponed")]
    [InlineData(GameStatus.Scheduled, "Scheduled")]
    public void Status_reads_in_sentence_case(GameStatus status, string word)
        => Assert.Equal(word, LineOps.Web.Components.TagStates.StatusWord(status));
}

/// <summary>The wager window's header draws Live as every other window does: warning, with the dot.</summary>
public class WagerLiveTagTests : LineOps.Web.Tests.Support.WagerTestContext
{
    [Fact]
    public void A_live_game_in_the_wager_header_pulses_like_everywhere_else()
    {
        Data.Row!.Game.Status = GameStatus.Live;

        var cut = RenderComponent<LineOps.Web.Components.Panels.BoardWager>(p =>
            p.Add(x => x.GameId, LineOps.Web.Tests.Support.BoardRows.GameId));

        var live = cut.FindAll("span.tag").Single(t => t.TextContent.Trim() == "Live");
        Assert.Contains("tag--warn", live.ClassList);
        Assert.Contains("tag--live", live.ClassList);
        Assert.NotNull(live.QuerySelector(".tag__live[aria-hidden=true]"));
    }
}

/// <summary>
/// The Board's matchup cell holds two clubs, a line each. In a tiled Board the grid's two-line
/// clamp on a link label and the link's reserved shortcut slot squeezed it to "▪ / Cowbo…".
/// </summary>
public class BoardMatchupCellTests
{
    private static readonly string Css = File.ReadAllText(Path.Combine(Root(), "src/LineOps.Desk/wwwroot/css/lineops.css"));
    private static readonly string Razor = File.ReadAllText(Path.Combine(Root(), "src/LineOps.Web/Components/Panels/BoardPanel.razor"));

    [Fact]
    public void Each_club_stays_on_one_line()
        => Assert.Matches(@"\.board__team\s*\{[^}]*white-space:\s*nowrap;", Css);

    [Fact]
    public void The_matchup_label_is_not_clamped_to_two_lines()
    {
        // Five classes, to outrank mud-bridge's .deskgrid .mud-table-body .mud-table-cell .desklink__label.
        Assert.Matches(
            @"\.deskgrid--board \.mud-table-body \.mud-table-cell \.board__matchup-link \.desklink__label\s*\{[^}]*display:\s*block;[^}]*-webkit-line-clamp:\s*unset;[^}]*line-clamp:\s*unset;",
            Css);
        Assert.Contains("<DeskLink Destination=\"game\" Class=\"board__matchup-link\"", Razor);
    }

    [Fact]
    public void The_matchup_links_shortcut_is_out_of_the_flow()
    {
        Assert.Matches(@"\.board__matchup-link\s*\{\s*position:\s*relative;", Css);
        Assert.Matches(@"\.board__matchup-link \.desklink__shortcut\s*\{[^}]*position:\s*absolute;[^}]*left:\s*100%;", Css);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

/// <summary>
/// The Board's matchup cell names each club's starter under its chip: the probable pitcher MLB
/// announces, or the quarterback an NFL club started its last game with.
/// </summary>
public class BoardStarterTests
{
    private static LineOps.Data.CrossReference.BoardRow Row(Action<LineOps.Core.Entities.Game>? game = null,
        string? awayQb = null, string? homeQb = null)
    {
        var row = Support.BoardRows.Row();
        game?.Invoke(row.Game);
        return row with { AwayQuarterback = awayQb, HomeQuarterback = homeQb };
    }

    [Fact]
    public void A_baseball_side_shows_its_probable_pitcher()
    {
        var row = Row(g => { g.AwayProbablePitcher = "Robert Gasser"; g.HomeProbablePitcher = "Freddy Peralta"; });

        Assert.Equal(("SP", "Gasser", "Probable starting pitcher: Robert Gasser"),
            LineOps.Web.Components.Panels.BoardPanel.Starter(row, home: false));
        Assert.Equal("Peralta", LineOps.Web.Components.Panels.BoardPanel.Starter(row, home: true)!.Value.Name);
    }

    [Fact]
    public void A_side_not_yet_named_reads_TBD_so_both_starters_line_up()
    {
        var row = Row(g => g.HomeProbablePitcher = "Freddy Peralta");

        Assert.Equal("TBD", LineOps.Web.Components.Panels.BoardPanel.Starter(row, home: false)!.Value.Name);
    }

    [Fact]
    public void A_football_side_shows_its_last_starting_quarterback()
    {
        var row = Row(awayQb: "Dak Prescott", homeQb: "Jalen Hurts");

        Assert.Equal(("QB", "Prescott", "Started at quarterback in the club's last game: Dak Prescott"),
            LineOps.Web.Components.Panels.BoardPanel.Starter(row, home: false));
        Assert.Equal("Hurts", LineOps.Web.Components.Panels.BoardPanel.Starter(row, home: true)!.Value.Name);
    }

    [Fact]
    public void A_game_with_no_starters_known_draws_none()
        => Assert.Null(LineOps.Web.Components.Panels.BoardPanel.Starter(Row(), home: true));
}
