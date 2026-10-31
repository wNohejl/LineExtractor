# LineOps.Desk.Templates

`dotnet new deskapp` starts a Blazor Server application on the Desk in one command: the
`LineOps.Desk` and `LineOps.Desk.Hosting` packages, wired the way the Desk's README says, with
one example window to build the next from.

## Install and create

The template package is published to the same GitHub Packages feed as the Desk, so the feed's
credentials (`DESK_FEED_USER`, and `DESK_FEED_TOKEN` with `read:packages`) are needed for both.

```bash
dotnet new install LineOps.Desk.Templates --nuget-source https://nuget.pkg.github.com/wNohejl/index.json
dotnet new deskapp -n Acme --product ACME --cookie acme.access
cd Acme
dotnet run --launch-profile http
```

From a local build instead, install the `.nupkg` that `dotnet pack src/LineOps.Desk.Templates`
writes: `dotnet new install ./LineOps.Desk.Templates.0.1.0.nupkg`.

| Option | Default | What it sets |
| --- | --- | --- |
| `-n`, `--name` | the folder's name | The project, its namespace and its assembly. |
| `--product` | the name | The wordmark, and the example window's title. |
| `--emphasis` | `DESK` | The part of the wordmark the accent falls on. |
| `--cookie` | the name in lower case, then `.access` | The access gate's sign-in cookie. |
| `--httpPort` | a free port between 5100 and 5999 | The `http` launch profile and `.claude/launch.json`. |

## What it makes

- `Program.cs` — `AddDesk` with a `DeskBrand`, the `IWindowCatalog`, `AddDeskAccess` (open by
  default; `Access:Mode=Gated` and a hash turn the gate on) and `AddDeskEdge`, and the pipeline
  in the order the Hosting package asks for, with the error and not-found pages behind it.
- `Components/` — `App.razor` (the Desk's head and scripts around this application's sheet),
  `Routes.razor` (the Desk's `MainLayout`), and `Pages/` with `Home` (the one
  route, `<Desk />`), `Error` and `NotFound`.
- `Windows/` — `AppWindowCatalog`, over a Welcome window and the Desk's own Parts bin, and
  `WelcomePanel`, a window on `DeskPanelBase`.
- `_Imports.razor` — at the root, so the Desk's namespaces reach `Components/` and `Windows/`
  alike.
- `wwwroot/css/app.css` — the application's sheet, which spends the Desk's tokens.
- `Directory.Packages.props` — central package versions for the Desk's two packages.
- `nuget.config` — the Desk's packages from GitHub Packages, everything else from nuget.org.
- `.editorconfig`, `.gitattributes`, `.gitignore`.
- `.claude/launch.json` — the `http` profile as a preview server, and
  `.claude/skills/apple-hig-components` and `apple-mudblazor`, the component rules and the
  token seam every window here is drawn to.

## Packing

`dotnet pack src/LineOps.Desk.Templates` writes the package. The skills and the `nuget.config`
are read from this repository's `.claude/skills` and `docs/desk` as it packs, so each has one
source. The Desk packages workflow packs and publishes it beside the Desk and Hosting; the
template's `Directory.Packages.props` names the versions they declare, and a Desk test holds it
to them.
