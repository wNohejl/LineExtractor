using DeskApp.Components;
using DeskApp.Windows;
using LineOps.Desk;
using LineOps.Desk.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Local overrides, last so they win: the place for keys and codes. The file is gitignored.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The desk, and MudBlazor's services under it: what this application is called, the zone it tells
// time in, and the windows it can open. Name a zone with new DeskClock(zone, label); left as it
// is, the desk tells UTC and says so.
builder.Services.AddDesk(
    new DeskBrand("ACME", "EMPHASIS", "what this desk is for", "Go to a window"),
    DeskClock.Utc);
builder.Services.AddSingleton<IWindowCatalog, AppWindowCatalog>();

// Who may reach the desk (Access:Mode, Open by default) and the edge headers. In their defaults
// both register little beyond their options, so a laptop runs with neither in the way.
builder.Services.AddDeskAccess(builder.Configuration, o => o.CookieName = "COOKIE_NAME");
builder.Services.AddDeskEdge(builder.Configuration);

var app = builder.Build();

app.UseDeskEdge();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Behind a proxy, the proxy owns the redirect; the host has no HTTPS port to send anyone to.
if (!app.Configuration.IsBehindProxy())
    app.UseHttpsRedirection();

app.UseDeskAccess();

app.UseAntiforgery();

app.MapDeskAccess();

// The sign-in page and the reconnect notice need the stylesheets before anyone has signed in.
app.MapStaticAssets().AllowAnonymous();

// MudBlazor does not support static server rendering, so interactivity is declared once, at the root.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
