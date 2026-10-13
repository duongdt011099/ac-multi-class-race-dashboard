using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Tests.Support;

/// <summary>bUnit context wired to real repositories and services on an in-memory SQLite database.</summary>
public class PageTestContext : TestContext
{
    protected PageTestContext()
    {
        Db = new TestDb();

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddScoped(_ => Db.CreateContext());
        Services.RegisterRepositories();
        Services.AddScoped<IChampionshipService, ChampionshipService>();
        Services.AddScoped<ISeasonService, SeasonService>();
        Services.AddScoped<IClassSettingService, ClassSettingService>();
        Services.AddScoped<IPointSettingService, PointSettingService>();
        Services.AddScoped<ITeamService, TeamService>();
        Services.AddScoped<ToastService>();
    }

    protected TestDb Db { get; }

    protected ToastService Toast => Services.GetRequiredService<ToastService>();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Db.Dispose();
        }

        base.Dispose(disposing);
    }
}
