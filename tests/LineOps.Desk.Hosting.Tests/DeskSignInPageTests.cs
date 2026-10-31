namespace LineOps.Desk.Hosting.Tests;

/// <summary>The sign-in page is a string; everything an application or a request supplies is encoded.</summary>
public class DeskSignInPageTests
{
    private static readonly DeskBrand Brand = new("ACME", "DESK", "the <script>alert(1)</script> desk");

    [Fact]
    public void Names_the_brand_and_encodes_it()
    {
        var html = DeskSignInPage.Render(Brand, "__token", "abc", "/signin", failed: false);

        Assert.Contains("<title>Sign in · ACMEDESK</title>", html);
        Assert.Contains("""<p class="mark">ACME<em>DESK</em></p>""", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void A_failed_attempt_says_so_as_an_alert()
    {
        Assert.DoesNotContain("role=\"alert\"", DeskSignInPage.Render(Brand, "f", "t", "/signin", failed: false));

        var failed = DeskSignInPage.Render(Brand, "f", "t", "/signin", failed: true);
        Assert.Contains("""<p class="notice" role="alert">That code opens nothing here.</p>""", failed);
    }

    [Fact]
    public void Posts_to_the_sign_in_path_with_the_token()
    {
        var html = DeskSignInPage.Render(Brand, "__RequestVerificationToken", "tok\"en", "/gate/in", failed: false);

        Assert.Contains("""<form method="post" action="/gate/in">""", html);
        Assert.Contains("""<input type="hidden" name="__RequestVerificationToken" value="tok&quot;en" />""", html);
        Assert.Contains("""<label for="code">Access code</label>""", html);
        Assert.Contains("""name="code" type="password" """, html);
    }
}
