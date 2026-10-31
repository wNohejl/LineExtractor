# LineOps.Desk.Hosting

What a Desk needs from the ASP.NET Core host it runs in, kept out of the Desk itself so that the
component library has no opinion about how its host is exposed:

- **The access gate** (ADR 0018) — a code in, a cookie out. The operator's code opens
  everything; an optional guest code opens a desk that reads and changes nothing. Open mode, the
  default, registers nothing but the options, so a host with one person at it runs as before.
- **The edge** (ADR 0019) — six security headers on every response, and, behind a reverse proxy,
  trust in its `X-Forwarded-For` and `-Proto`.

It references `LineOps.Desk` (for `IDeskAccess` and `DeskBrand`) and nothing else.

## Wiring

Four lines in `Program.cs`, in pipeline order:

```csharp
using LineOps.Desk.Hosting;

builder.Services.AddDeskAccess(builder.Configuration);   // optionally: o => o.CookieName = "my.access"
builder.Services.AddDeskEdge(builder.Configuration);     // optionally: o => o.ExtraImageSources.Add("https://images.example.com")

app.UseDeskEdge();          // first in the pipeline
// … exception handler, status pages, HTTPS redirect (skip it when configuration.IsBehindProxy())
app.UseDeskAccess();        // before UseAntiforgery
app.UseAntiforgery();
app.MapDeskAccess();        // the sign-in page, its form, and sign-out
```

The sign-in page takes its wordmark and tagline from the `DeskBrand` registered by `AddDesk`.

## Access options

Bound from the `Access` section (`DeskAccessOptions.SectionName`), then from the delegate.

| Option | Default | Meaning |
| --- | --- | --- |
| `Mode` | `Open` | `Open`: whoever reaches the desk operates it. `Gated`: a code decides. |
| `AllowAnonymousVisitors` | `false` | When gated, no code at all opens the read-only desk. |
| `OperatorCodeHash` | — | The operator's code, hashed. Required when gated; the host refuses to start without it. |
| `GuestCodeHash` | — | The read-only desk's code, hashed. Optional. |
| `SessionLifetime` | 14 days | How long a sign-in lasts without a visit (sliding). |
| `CookieName` | `desk.access` | The sign-in cookie. An application with visitors already signed in under another name passes that name, and nobody is signed out. |
| `SignInPath` | `/signin` | The sign-in page and the path its form posts to. |
| `SignOutPath` | `/signout` | Signs out and returns to the sign-in page. |

`configuration.IsDeskGated()` answers whether configuration asks for a gate. Five sign-in
attempts per minute per address are allowed; the sixth is answered 429.

## Edge options

| Option | Default | Meaning |
| --- | --- | --- |
| `ContentSecurityPolicy` | `DeskEdge.DefaultContentSecurityPolicy` | Everything from this origin, nothing from any other; inline style and script only because the framework needs them. |
| `ExtraImageSources` | empty | Origins appended to the policy's `img-src`. |

`configuration.IsBehindProxy()` reads `Hosting:BehindProxy`. `DeskEdge.Headers(options)` is the
pure function the middleware writes from.

## The hash format

`DeskAccessCode.Hash` writes PBKDF2 over SHA-256, 600,000 iterations, a random 16-byte salt and a
32-byte hash, as `pbkdf2-sha256.<iterations>.<salt>.<hash>` with salt and hash in base64. Dots,
not dollar signs, so a `.env` file does not read the iteration count as a variable. Configuration
holds only this string. Any script that makes a hash for a deployment must write this format;
`DeskAccessCode.Matches` is the check, and a test holds one script-made hash to it.
