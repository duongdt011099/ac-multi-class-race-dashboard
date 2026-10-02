using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MulticlassRace.Configuration;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;
using multi_class_race_dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

// A shortcut carries --open-dashboard so that starting the app from one lands on the page a person
// asked for. The sign-in entry deliberately does not pass it: starting in the background should not
// steal focus every time the machine boots. A copy that lost the startup race already opened the
// page before it exited, so this only concerns the copy that actually got the port.
var openOnStart = args.Any(argument => string.Equals(argument, "--open-dashboard", StringComparison.OrdinalIgnoreCase));

// The app also starts when you sign in, so a shortcut click is usually a second copy. It steps aside
// and opens the dashboard instead of dying on a port conflict. Checked before the host is built, so
// a duplicate click never reaches the database.
var dashboardUrl = DashboardInstance.ResolveUrl(builder.Configuration);
using var instance = await DashboardInstance.TryClaimOrHandOverAsync(dashboardUrl);

if (instance is null)
{
    return;
}

// Added after the guard so a duplicate click does not fight over the log file.
builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogFilePath));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.RegisterDbContext(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.RegisterRepositories();
builder.Services.RegisterServices(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.UseGameFolderGate();
app.UseUploads(app.Environment);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGameContentImageEndpoints();

try
{
    // Start first, then read the address Kestrel actually bound to: asking the host beats reading
    // appsettings.json, because a command line or environment override would otherwise be ignored.
    await app.StartAsync();

    // Only now is the page actually served, so opening it here cannot land on a refused connection.
    if (openOnStart)
    {
        DashboardInstance.OpenDashboard(app.Urls.FirstOrDefault() ?? dashboardUrl);
    }

    // The dashboard has no window of its own, so the tray icon is how a user opens it again and how
    // it is closed. It lives for as long as the web app does.
    using var tray = DashboardTray.Start(
        app.Services.GetRequiredService<ILogger<DashboardTray>>(),
        app.Lifetime,
        app.Urls.FirstOrDefault() ?? dashboardUrl);

    await app.WaitForShutdownAsync();
}
catch (Exception ex)
{
    StartupFailure.Report(app.Services, ex);
}
