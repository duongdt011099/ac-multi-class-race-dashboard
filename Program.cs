using Microsoft.AspNetCore.Http;
using multi_class_race_dashboard.Components;
using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;

var builder = WebApplication.CreateBuilder(args);

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
