# LineOps.Desk.Testing

Test fixtures and convention checks for an application built on
[LineOps.Desk](../LineOps.Desk/README.md). Reference it from an xunit test project beside the
application.

## The test project

An xunit project beside the application, referencing it and this package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="LineOps.Desk.Testing" Version="0.2.0" />
    <ProjectReference Include="..\Acme\Acme.csproj" />
  </ItemGroup>
  <!-- bUnit 1.x brings AngleSharp 1.2.0, under GHSA-pgww-w46g-26qg. The fixed AngleSharp (1.5)
       breaks bUnit 1.40 at runtime, and a test only parses markup it rendered itself, so the
       advisory has nothing to act on here. NuGet does not carry a package's suppression to the
       projects that reference it, so a test project says so itself. -->
  <ItemGroup>
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-pgww-w46g-26qg" />
  </ItemGroup>
</Project>
```

The facts below need `using Xunit;` and `using LineOps.Desk.Testing;` (or global usings).

## The conventions, in one fact

```csharp
public class DeskConventionTests
{
    [Fact]
    public void The_desk_conventions_hold() => DeskConventions.All(
        Path.Combine(DeskConventions.FindAbove("Acme.sln"), "src"),
        "Acme.Web/Components/App.razor", "Acme.Web/wwwroot/css/acme.css");
}
```

`All` reads the source and reports every broken rule at once:

| Check | What it guards |
| --- | --- |
| `EveryFieldHasAName` | Every `DeskField`, `DeskSelect`, `DeskTextArea` and `DeskSearch` has a Label, an aria-label or an aria-labelledby. |
| `SortKeysAreSortable` | A `TemplateColumn` with `SortBy` says `Sortable="true"`, or MudBlazor never sorts it. |
| `NoBootstrapShips` | No `wwwroot/lib`, and nothing names the template's Bootstrap under it. |
| `AppSheetSpendsTokens` | The application's sheet paints no literal colour; every colour is a Desk token. |
| `AppSheetTargetsAreReachable` | A `__trigger`, `__opt` or `--take` target is never under 24px tall. |
| `AppSheetKeepsOneToolbarFold` | The window toolbar folds by the Desk's container query alone. |
| `AppSheetLeavesDeskComponentsAlone` | The application restyles none of the selectors the Desk's components emit. |
| `AppHeadLoadsDeskFirst` | `<DeskHead />` comes before every stylesheet, and the Desk's sheets are not linked by hand. |

Each is also a method of its own, for a project that wants one fact per rule.
`SelectorLivesInAppSheet(appSheet, selector)` is the other direction of ownership: a selector named
for one of the application's windows is in its sheet and in none of the Desk's, which this package
carries inside it.

## The bench

`DeskTestContext` is a bUnit `TestContext` with MudBlazor's services, the theme, a brand, a UTC
clock, an open desk, a window manager, the follow-up layer and the popovers registered, and JS
interop loose. An application's bench derives from it and registers its own brand, clock and
catalogue in its constructor; the last registration wins.

```csharp
public abstract class AcmeTestContext : DeskTestContext
{
    protected AcmeTestContext()
    {
        Services.AddSingleton(new DeskBrand("ACME", "DESK", "test desk"));
        Services.AddSingleton<IWindowCatalog, AcmeWindowCatalog>();
    }
}
```

The fakes stand in for the seams the Desk asks its host for:

- `FakeWindowCatalog` — one window that opens on its own, two that are about something, one
  workspace. Pass your own definitions to the constructor for a larger desk.
- `FakeAlerts` — a confirm that answers as told and records each question.
- `FakeSheets` — sheets recorded rather than drawn, with a handle the test closes to answer.
