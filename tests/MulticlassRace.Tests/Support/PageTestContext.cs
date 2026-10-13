using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MulticlassRace.Data;
using MulticlassRace.Repositories;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.Services.Browser;
using MulticlassRace.Services.Teams;

namespace MulticlassRace.Tests.Support;

/// <summary>bUnit context wired to real repositories and services on an in-memory SQLite database.</summary>
public class PageTestContext : TestContext
{
    protected PageTestContext()
    {
        Db = new TestDb();
        UploadsRoot = Path.Combine(Path.GetTempPath(), "mcr-tests", Guid.NewGuid().ToString("N"));
        DriverService = new FakeDriverService();

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddScoped(_ => Db.CreateContext());
        Services.RegisterRepositories();
        Services.AddScoped<IChampionshipService, ChampionshipService>();
        Services.AddScoped<ISeasonService, SeasonService>();
        Services.AddScoped<IClassSettingService, ClassSettingService>();
        Services.AddScoped<IPointSettingService, PointSettingService>();
        Services.AddScoped<ITeamService, TeamService>();
        Services.AddSingleton<IDriverService>(DriverService);
        Services.AddScoped<ITeamLogoStorage>(_ => new TeamLogoStorage(UploadsRoot));
        Services.AddScoped<DriverDataChangeNotifier>();
        Services.AddScoped<GameConfigChangeNotifier>();
        Services.AddSingleton<IAssettoCorsaContentService>(new FakeContentService());
        Services.AddSingleton<IAssettoCorsaGameConfigService>(new FakeGameConfigService());

        RaceService = new FakeRaceService();
        TrackService = new FakeTrackService();
        Services.AddSingleton<IRaceService>(RaceService);
        Services.AddSingleton<IRaceSessionLauncher>(RaceService);
        Services.AddSingleton<IRaceResultService>(RaceService);
        Services.AddSingleton<IGridPresetService>(RaceService);
        Services.AddSingleton<ITrackService>(TrackService);
        Services.AddSingleton<ILuaResultImportService>(new FakeLuaResultImportService());
        Services.AddSingleton<RaceSessionChangeNotifier>();
        Services.AddScoped<BrowserStorage>();
        Services.AddScoped<ToastService>();
    }

    protected TestDb Db { get; }

    protected string UploadsRoot { get; }

    protected FakeDriverService DriverService { get; }

    protected FakeRaceService RaceService { get; }

    protected FakeTrackService TrackService { get; }

    protected ToastService Toast => Services.GetRequiredService<ToastService>();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Db.Dispose();

            if (Directory.Exists(UploadsRoot))
            {
                Directory.Delete(UploadsRoot, recursive: true);
            }
        }

        base.Dispose(disposing);
    }
}
