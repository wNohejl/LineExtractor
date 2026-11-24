namespace XLines.Desk.Tests;

/// <summary>
/// The key a desk saves its layout under. It is made from the wordmark unless the application
/// names one, so a product that changes its name can keep the layouts saved under the old one.
/// </summary>
public class DeskBrandTests
{
    [Fact]
    public void LayoutKey_defaults_to_the_lower_cased_wordmark()
        => Assert.Equal("acmedesk.desk", new DeskBrand("ACME", "DESK", "c").LayoutKey);

    [Fact]
    public void A_set_StorageKey_is_the_layout_key_verbatim()
        => Assert.Equal("Old.Desk", new DeskBrand("X", "LINES", "c") { StorageKey = "Old.Desk" }.LayoutKey);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_StorageKey_falls_back_to_the_wordmark(string blank)
        => Assert.Equal("xlines.desk", new DeskBrand("X", "LINES", "c") { StorageKey = blank }.LayoutKey);
}
