using multi_class_race_dashboard.Components;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.RegisterDbContext(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.RegisterRepositories();
builder.Services.RegisterServices();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var isProtected = path is "/" or "/teams" or "/drivers" or "/races" or "/pointsettings";
    if (isProtected)
    {
        var configService = context.RequestServices.GetRequiredService<IAssettoCorsaGameConfigService>();
        var config = await configService.GetAsync();
        if (config is null || string.IsNullOrWhiteSpace(config.GamePath))
        {
            context.Response.Redirect("/gameconfig");
            return;
        }
    }

    await next();
});

var uploadsRoot = Path.Combine(
    app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/car-image", async (
    string? car,
    string? skin,
    string? type,
    IAssettoCorsaGameConfigService configService) =>
{
    var fileName = type switch
    {
        "livery" => "livery.png",
        "car" => "preview.jpg",
        _ => null
    };

    if (fileName is null ||
        string.IsNullOrWhiteSpace(car) ||
        string.IsNullOrWhiteSpace(skin) ||
        car.Contains('\\') || car.Contains('/') ||
        skin.Contains('\\') || skin.Contains('/'))
    {
        return Results.NotFound();
    }

    var config = await configService.GetAsync();

    if (config is null || string.IsNullOrWhiteSpace(config.GamePath))
    {
        return Results.NotFound();
    }

    var carsRoot = Path.GetFullPath(Path.Combine(config.GamePath.Trim(), "content", "cars"));
    var fullPath = Path.GetFullPath(Path.Combine(carsRoot, car.Trim(), "skins", skin.Trim(), fileName));

    if (!fullPath.StartsWith(carsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    var contentType = type == "livery" ? "image/png" : "image/jpeg";
    return Results.File(fullPath, contentType);
});

app.Run();
