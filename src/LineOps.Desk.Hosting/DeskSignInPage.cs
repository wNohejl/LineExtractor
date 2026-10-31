using System.Net;

namespace LineOps.Desk.Hosting;

/// <summary>
/// The sign-in page, as a string. A plain HTML form rather than a desk window, because the
/// cookie it leads to is set on an HTTP response and a circuit has none. Pure, so it is tested
/// without a server; everything the brand or the request supplies is HTML-encoded.
/// </summary>
public static class DeskSignInPage
{
    /// <param name="brand">The wordmark, its emphasis and the tagline under it.</param>
    /// <param name="formFieldName">The antiforgery form field's name.</param>
    /// <param name="requestToken">The antiforgery token the form posts back.</param>
    /// <param name="action">The path the form posts to.</param>
    /// <param name="failed">Whether the last attempt was refused, which the page says as an alert.</param>
    public static string Render(DeskBrand brand, string formFieldName, string requestToken, string action, bool failed)
    {
        var e = (string s) => WebUtility.HtmlEncode(s);
        var notice = failed
            ? """<p class="notice" role="alert">That code opens nothing here.</p>"""
            : "";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <meta name="color-scheme" content="dark light" />
            <meta name="robots" content="noindex" />
            <title>Sign in · {{e(brand.Wordmark + brand.Emphasis)}}</title>
            <style>
              :root { --bg:#1c1c1e; --panel:#2c2c2e; --text:#f5f5f7; --dim:#98989d; --line:#3a3a3c; --accent:#0a84ff; --bad:#ff453a; }
              @media (prefers-color-scheme: light) {
                :root { --bg:#f2f2f7; --panel:#ffffff; --text:#1c1c1e; --dim:#6e6e73; --line:#d1d1d6; --accent:#007aff; --bad:#d70015; }
              }
              * { box-sizing: border-box; }
              body { margin:0; min-height:100vh; display:grid; place-items:center; padding:16px;
                     background:var(--bg); color:var(--text);
                     font: 15px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, system-ui, sans-serif; }
              main { width:100%; max-width:360px; background:var(--panel); border:1px solid var(--line);
                     border-radius:14px; padding:28px 24px; }
              .mark { font-weight:700; letter-spacing:.08em; font-size:20px; margin:0 0 4px; }
              .mark em { font-style:normal; color:var(--accent); }
              .tag { color:var(--dim); margin:0 0 24px; font-size:13px; }
              label { display:block; font-size:13px; color:var(--dim); margin-bottom:6px; }
              input[type=password] { width:100%; padding:10px 12px; border-radius:8px; border:1px solid var(--line);
                     background:var(--bg); color:var(--text); font:inherit; }
              input[type=password]:focus { outline:2px solid var(--accent); outline-offset:1px; }
              button { margin-top:16px; width:100%; padding:10px 12px; border:0; border-radius:8px;
                       background:var(--accent); color:#fff; font:inherit; font-weight:600; cursor:pointer; }
              .notice { color:var(--bad); font-size:13px; margin:12px 0 0; }
              .foot { color:var(--dim); font-size:12px; margin:20px 0 0; }
            </style>
            </head>
            <body>
            <main>
              <p class="mark">{{e(brand.Wordmark)}}<em>{{e(brand.Emphasis)}}</em></p>
              <p class="tag">{{e(brand.Tagline)}}</p>
              <form method="post" action="{{e(action)}}">
                <input type="hidden" name="{{e(formFieldName)}}" value="{{e(requestToken)}}" />
                <label for="code">Access code</label>
                <input id="code" name="code" type="password" autocomplete="current-password" required autofocus />
                {{notice}}
                <button type="submit">Open the desk</button>
              </form>
              <p class="foot">A guest code opens a desk that reads and changes nothing.</p>
            </main>
            </body>
            </html>
            """;
    }
}
