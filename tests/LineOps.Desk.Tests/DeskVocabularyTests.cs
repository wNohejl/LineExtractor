using System.Text;
using System.Text.RegularExpressions;
using LineOps.Desk.Tests.Support;

namespace LineOps.Desk.Tests;

/// <summary>
/// The Desk is a window system, not a sportsbook. A word of this app's domain in its code — a
/// placeholder that says "game", a parameter named for a wager — is a piece of LineOps that would
/// ship to anyone else who used the Desk, and nothing renders wrong to catch it. Read from the
/// source, outside comments: a comment may say where an idea came from, but a string, a name or a
/// key is what a consumer gets. Its comments are held to the same words: they are the package's
/// documentation, read in a tooltip and in the source by someone who has never seen LineOps.
/// </summary>
public class DeskVocabularyTests
{
    private static readonly string[] Words = ["game", "book", "odds", "wager", "journal", "team", "player", "fight", "bout", "stake", "parlay", "slip", "league", "bet", "espn", "mlb", "nfl", "ufc"];

    [Theory]
    [InlineData("LineOps.Desk")]
    [InlineData("LineOps.Desk.Hosting")]
    [InlineData("LineOps.Desk.Testing")]
    public void The_desk_s_code_knows_no_sport(string project)
    {
        var root = Path.Combine(Repo.Root(), "src", project);
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".js"))
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                              && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var code = StripComments(File.ReadAllText(file));

            foreach (var w in Words)
            {
                foreach (Match hit in Regex.Matches(code, $@"\b{w}s?\b", RegexOptions.IgnoreCase))
                {
                    var line = code[..hit.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{relative}:{line}: {w}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "A domain word in the Desk's code:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The other half: the prose. Every comment in the Desk's C#, Razor and JavaScript (what
    /// <see cref="CommentsOnly"/> keeps), every <c>/* … */</c> in its stylesheets, every
    /// <c>&lt;!-- … --&gt;</c> in its project file, and the whole of its README. A comment that
    /// explains a list by "a wager on the slip" teaches the next reader that the Desk is about
    /// wagers; a generic example teaches the same idea and ships anywhere.
    /// </summary>
    [Theory]
    [InlineData("LineOps.Desk")]
    [InlineData("LineOps.Desk.Hosting")]
    [InlineData("LineOps.Desk.Testing")]
    public void The_desk_s_docs_know_no_sport(string project)
    {
        var root = Path.Combine(Repo.Root(), "src", project);
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                              && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            var text = File.ReadAllText(file);
            string? prose = Path.GetExtension(file) switch
            {
                ".cs" or ".razor" or ".js" => CommentsOnly(text),
                ".css" => Matching(text, @"/\*.*?\*/"),
                ".csproj" => Matching(text, @"<!--.*?-->"),
                ".md" => text,
                _ => null,
            };
            if (prose is null)
                continue;

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (var w in Words)
            {
                foreach (Match hit in Regex.Matches(prose, $@"\b{w}s?\b", RegexOptions.IgnoreCase))
                {
                    var line = prose[..hit.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{relative}:{line}: {w}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"A domain word in the Desk's documentation ({offenders.Count}):\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("var a = 1; // a game\nvar b = 2;", "var a = 1; \nvar b = 2;")]
    [InlineData("/// <summary>A game.</summary>\nint x;", "\nint x;")]
    [InlineData("a /* the\ngame */ b", "a \n b")]
    [InlineData("<p>x</p>\n@* the\ngame *@\n<p>y</p>", "<p>x</p>\n\n\n<p>y</p>")]
    [InlineData("var s = \"// a game\";", "var s = \"// a game\";")]
    [InlineData("var s = '/* a game */';", "var s = '/* a game */';")]
    [InlineData("var s = `a ${x} // game`;", "var s = `a ${x} // game`;")]
    [InlineData("var s = @\"a \"\"//\"\" game\";", "var s = @\"a \"\"//\"\" game\";")]
    [InlineData("var c = '\"'; // game", "var c = '\"'; ")]
    [InlineData("<span>don't panic</span> // a game", "<span>don't panic</span> ")]
    public void Comments_go_and_string_literals_stay(string source, string expected)
        => Assert.Equal(expected, StripComments(source));

    [Theory]
    [InlineData("var a = 1; // a game\nvar b = 2;", "// a game\n")]
    [InlineData("/// <summary>A game.</summary>\nint x;", "/// <summary>A game.</summary>\n")]
    [InlineData("a /* the\ngame */ b", "/* the\ngame */")]
    [InlineData("<p>x</p>\n@* the\ngame *@\n<p>y</p>", "\n@* the\ngame *@\n")]
    [InlineData("var s = \"// a game\";", "")]
    [InlineData("var s = `a ${x} // game`;", "")]
    [InlineData("<span>don't panic</span> // a game", "// a game")]
    public void Comments_stay_and_everything_else_goes(string source, string expected)
        => Assert.Equal(expected, CommentsOnly(source));

    [Fact]
    public void Matching_keeps_the_matches_and_the_line_breaks()
        => Assert.Equal("\n/* a */\n\n/* b\nc */", Matching(".x {\n  color: red; /* a */\n}\n/* b\nc */ .y {}", @"/\*.*?\*/"));

    /// <summary>
    /// The source with every comment blanked — <c>//</c> and <c>///</c> to the line's end,
    /// <c>/* … */</c> and Razor's <c>@* … *@</c> — and every string literal kept as it is, quotes
    /// and all, because a literal is exactly what a consumer of the Desk reads. Line breaks inside
    /// a comment are kept so a line number still points at the source.
    /// </summary>
    /// <remarks>
    /// A small lexer, not a parser. Double-quoted, verbatim, raw and backtick strings are
    /// literals, and so is a single-quoted one (a C# <c>char</c>, a JavaScript string) unless the quote
    /// is an apostrophe in prose ("don't"). A string that is not verbatim, raw or a template ends at
    /// its line's end at the latest, so a stray quote in markup cannot swallow the rest of the file.
    /// </remarks>
    internal static string StripComments(string source) => Lex(source, keepComments: false);

    /// <summary>
    /// The inverse of <see cref="StripComments"/>: every comment kept, delimiters and all, and
    /// everything else — code, markup and string literals — blanked to its line breaks, so a line
    /// number still points at the source.
    /// </summary>
    internal static string CommentsOnly(string source) => Lex(source, keepComments: true);

    /// <summary>
    /// The text of every match of <paramref name="pattern"/>, with everything between them blanked
    /// to its line breaks — the comments of a language <see cref="Lex"/> does not read.
    /// </summary>
    private static string Matching(string source, string pattern)
    {
        var output = new StringBuilder();
        var at = 0;
        foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Singleline))
        {
            output.Append('\n', source.AsSpan(at, m.Index - at).Count('\n'));
            output.Append(m.Value);
            at = m.Index + m.Length;
        }
        output.Append('\n', source.AsSpan(at).Count('\n'));
        return output.ToString();
    }

    // One lexer, two outputs: keepComments chooses which side of the comment line survives as
    // text; the other side keeps only its line breaks.
    private static string Lex(string source, bool keepComments)
    {
        var output = new StringBuilder(source.Length);
        var i = 0;

        void Keep(int start, int length, bool asText)
        {
            if (asText)
            {
                output.Append(source, start, length);
                return;
            }
            foreach (var ch in source.AsSpan(start, length))
                if (ch == '\n') output.Append('\n');
        }
        void Code(int start, int length) => Keep(start, length, !keepComments);
        void Comment(int start, int length) => Keep(start, length, keepComments);

        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                var start = i;
                while (i < source.Length && source[i] != '\n') i++;
                Comment(start, i - start);
                continue;
            }

            if ((c == '/' && next == '*') || (c == '@' && next == '*'))
            {
                var close = c == '/' ? "*/" : "*@";
                var end = source.IndexOf(close, i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Comment(i, end - i);
                i = end;
                continue;
            }

            if (c == '"' && source.AsSpan(i).StartsWith("\"\"\""))
            {
                var end = source.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 3;
                Code(i, end - i);
                i = end;
                continue;
            }

            if (c == '@' && next == '"' || (c is '$' or '@' && next is '$' or '@' && i + 2 < source.Length && source[i + 2] == '"'))
            {
                var start = i;
                i = source.IndexOf('"', i) + 1;
                while (i < source.Length)
                {
                    if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                    if (source[i] == '"') { i++; break; }
                    i++;
                }
                Code(start, i - start);
                continue;
            }

            if (c == '"' || c == '`' || (c == '\'' && OpensQuote(source, i)))
            {
                var start = i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (source[i] == c) { i++; break; }
                    if (source[i] == '\n' && c != '`') break;
                    i++;
                }
                i = Math.Min(i, source.Length);
                Code(start, i - start);
                continue;
            }

            Code(i, 1);
            i++;
        }

        return output.ToString();
    }

    // A single quote opens a literal only where it reads as one: not straight after a letter or
    // digit, where it is an apostrophe ("don't", "operators'"), and only if it closes on its line.
    private static bool OpensQuote(string source, int i)
    {
        if (i > 0 && char.IsLetterOrDigit(source[i - 1]))
            return false;

        var close = source.IndexOf('\'', i + 1);
        var newline = source.IndexOf('\n', i + 1);
        return close > i && (newline < 0 || close < newline);
    }
}
