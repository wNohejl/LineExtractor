using System.Text.RegularExpressions;
using LineOps.Desk.Theming;
using LineOps.Web.Tests.Support;

namespace LineOps.Web.Tests;

/// <summary>
/// The boot script that stamps the stored theme on <c>&lt;html&gt;</c> before the first paint.
/// ThemeService owns the policy; the script is a copy of it in another language, so these tests
/// are what keep the copy reading the same keys and naming the same token blocks.
/// </summary>
public class ThemeBootTests
{
    private static readonly string Boot = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "js", "theme-boot.js"));
    private static readonly string Theme = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Desk", "wwwroot", "js", "theme.js"));

    [Theory]
    [InlineData(DeskThemeFamily.Standard)]
    [InlineData(DeskThemeFamily.Nord)]
    [InlineData(DeskThemeFamily.Solarized)]
    [InlineData(DeskThemeFamily.RosePine)]
    [InlineData(DeskThemeFamily.HighContrast)]
    public void The_boot_script_knows_every_family_slug(DeskThemeFamily family)
        => Assert.Contains($"'{DeskThemeFamilies.Slug(family)}'", Boot);

    // The stored value is the enum member's name, read case-insensitively as ThemeService reads it.
    [Theory]
    [InlineData(DeskThemeFamily.Standard)]
    [InlineData(DeskThemeFamily.Nord)]
    [InlineData(DeskThemeFamily.Solarized)]
    [InlineData(DeskThemeFamily.RosePine)]
    [InlineData(DeskThemeFamily.HighContrast)]
    public void The_boot_script_reads_every_family_by_its_stored_name(DeskThemeFamily family)
        => Assert.Contains($"{family.ToString().ToLowerInvariant()}:", Boot);

    [Fact]
    public void The_boot_script_reads_the_same_keys_as_theme_js()
    {
        var keys = Regex.Matches(Theme, @"const \w*KEY = '([^']+)'").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.Contains($"'{key}'", Boot);
    }

    // The keys carry the Desk's name now, and a browser that stored its theme under the old one
    // must still come up in it: both scripts know both keys, the new one is read first, and the
    // old one is only ever read.
    [Theory]
    [InlineData("desk.theme")]
    [InlineData("desk.theme.family")]
    [InlineData("lineops.theme")]
    [InlineData("lineops.theme.family")]
    public void Both_scripts_know_the_desk_s_key_and_the_one_before_it(string key)
    {
        Assert.Contains($"'{key}'", Theme);
        Assert.Contains($"'{key}'", Boot);
    }

    [Theory]
    [InlineData("desk.theme", "lineops.theme")]
    [InlineData("desk.theme.family", "lineops.theme.family")]
    public void The_boot_script_reads_the_new_key_and_falls_back_to_the_old(string key, string old)
        => Assert.Matches($@"stored\('{Regex.Escape(key)}'\)\s*\|\|\s*stored\('{Regex.Escape(old)}'\)", Boot);

    [Fact]
    public void Theme_js_reads_the_new_key_before_the_old_and_writes_only_the_new()
    {
        Assert.Matches(@"const KEY = 'desk\.theme'", Theme);
        Assert.Matches(@"const FAMILY_KEY = 'desk\.theme\.family'", Theme);
        Assert.Matches(@"readKey\(KEY, OLD_KEY\)", Theme);
        Assert.Matches(@"readKey\(FAMILY_KEY, OLD_FAMILY_KEY\)", Theme);
        Assert.DoesNotMatch(@"setItem\(OLD_", Theme);
    }

    [Fact]
    public void App_loads_the_boot_script_before_any_stylesheet()
    {
        var app = File.ReadAllText(Path.Combine(Repo.Root(), "src", "LineOps.Web", "Components", "App.razor"));
        var boot = app.IndexOf("theme-boot.js", StringComparison.Ordinal);

        Assert.True(boot >= 0, "App.razor does not load theme-boot.js.");
        Assert.True(boot < app.IndexOf("<link rel=\"stylesheet\"", StringComparison.Ordinal));
    }
}
