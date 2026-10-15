using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MulticlassRace.Configuration;
using MulticlassRace.Data;
using MulticlassRace.Infrastructure;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Web;
using multi_class_race_dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

// A shortcut carries --open-dashboard so that starting the app from one shows the window. The sign-in
// entry deliberately does not pass it: starting in the background should not steal focus every time
// the machine boots. A copy that lost the startup race already asked the running copy to show its
// window before it exited, so this only concerns the copy that actually started.
var openOnStart = args.Any(argument => string.Equals(argument, "--open-dashboard", StringComparison.OrdinalIgnoreCase));

// The app also starts when you sign in, so a shortcut click is usually a second copy. It asks the
// running copy to show its window and steps aside. Checked before the host is built, so a duplicate
// click never reaches the database.
using var instance = DashboardInstance.TryClaimOrHandOver();

if (instance is null)
{
    return;
}

// The dashboard is reachable only from its own window. The server must listen on a local port, so
// every request has to carry this secret, which only that window is given.
var accessToken = new AppAccessToken();
builder.Services.AddSingleton(accessToken);

// Added after the guard so a duplicate click does not fight over the log file.
builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogFilePath));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.RegisterDbContext(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.RegisterRepositories();
builder.Services.RegisterServices(builder.Configuration);

var app = builder.Build();

// First, so nothing else in the pipeline ever answers a request that is not from the dashboard window.
app.UseAppAccessGuard();

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
    // The port is chosen by the system, so the address only exists once Kestrel has started.
    await app.StartAsync();

    var dashboardUrl = app.Urls.First();

    // The window is the only way in, and it lives on the tray thread. The tray icon is also how the
    // window is shown again after being closed, and how the app is exited.
    using var tray = DashboardTray.Start(
        app.Services.GetRequiredService<ILogger<DashboardTray>>(),
        app.Lifetime,
        dashboardUrl,
        accessToken,
        openOnStart);

    if (tray is null)
    {
        throw new InvalidOperationException("The dashboard window could not be created, so there is no way to open the dashboard.");
    }

    // Registered only now so that a request sent by a second copy during startup is held and then
    // delivered, rather than dropped before there is a window to show.
    instance.ListenForShowRequests(tray.ShowDashboard);

    await app.WaitForShutdownAsync();
}
catch (Exception ex)
{
    StartupFailure.Report(app.Services, ex);
}
