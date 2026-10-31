namespace LineOps.Desk.Hosting.Tests;

/// <summary>The code itself: made once, stored hashed, checked in constant time.</summary>
public class DeskAccessCodeTests
{
    [Fact]
    public void A_code_matches_the_hash_made_from_it_and_nothing_else()
    {
        var stored = DeskAccessCode.Hash("open-sesame");

        Assert.True(DeskAccessCode.IsWellFormed(stored));
        Assert.True(DeskAccessCode.Matches("open-sesame", stored));
        Assert.False(DeskAccessCode.Matches("open-sesame ", stored));
        Assert.False(DeskAccessCode.Matches("", stored));
        Assert.False(DeskAccessCode.Matches(null, stored));
    }

    [Fact]
    public void Two_hashes_of_one_code_differ_because_each_is_salted()
        => Assert.NotEqual(DeskAccessCode.Hash("same"), DeskAccessCode.Hash("same"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open-sesame")]
    [InlineData("pbkdf2-sha256.0.AAAA.AAAA")]
    [InlineData("pbkdf2-sha256.600000.not base64.AAAA")]
    [InlineData("md5.1.AAAA.AAAA")]
    public void A_missing_or_malformed_hash_matches_nothing(string? stored)
        => Assert.False(DeskAccessCode.Matches("open-sesame", stored));

    /// <summary>
    /// Made by <c>scripts/new-access-code.ps1 -Code 'test-code-1'</c>. The script and this class
    /// are two implementations of one format; this is what keeps them one.
    /// </summary>
    [Fact]
    public void A_hash_made_by_the_setup_script_is_one_the_desk_accepts()
    {
        const string fromScript =
            "pbkdf2-sha256.600000.WS+gSSSb5yllgWZUr3o8XA==.E3SPAqf5NZGklCRLyeVnTXgvfy9d0DhRC8ZzXFUquHQ=";

        Assert.True(DeskAccessCode.Matches("test-code-1", fromScript));
        Assert.False(DeskAccessCode.Matches("test-code-2", fromScript));
    }
}
