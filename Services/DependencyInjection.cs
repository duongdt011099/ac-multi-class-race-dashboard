using Microsoft.Extensions.Configuration;
using MulticlassRace.Configuration;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.Services.Browser;
using MulticlassRace.Services.Teams;

namespace MulticlassRace.Services;

public static class DependencyInjection
{
    public static IServiceCollection RegisterServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IClassSettingService, ClassSettingService>();
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<IChampionshipService, ChampionshipService>();
        services.AddScoped<ISeasonService, SeasonService>();
        services.AddScoped<IRaceService, RaceService>();
        services.AddScoped<IRaceSessionLauncher, RaceSessionLauncher>();
        services.AddScoped<IRaceResultService, RaceResultService>();
        services.AddScoped<IGridPresetService, GridPresetService>();
        services.AddScoped<ITrackService, TrackService>();
        services.AddScoped<IAssettoCorsaGameConfigService, AssettoCorsaGameConfigService>();
        services.AddScoped<IAssettoCorsaContentService, AssettoCorsaContentService>();
        services.AddScoped<IPointSettingService, PointSettingService>();
        services.AddScoped<ITeamLogoStorage>(sp =>
            new TeamLogoStorage(UploadsEndpointExtensions.ResolveRoot(sp.GetRequiredService<IWebHostEnvironment>())));
        services.AddScoped<BrowserStorage>();
        services.AddScoped<ToastService>();
        services.AddScoped<GameConfigChangeNotifier>();
        services.AddScoped<DriverDataChangeNotifier>();
        services.AddScoped<UpdateNotificationRequest>();
        services.AddScoped<IAppUpdatePreferenceService, AppUpdatePreferenceService>();
        services.AddScoped<ILuaResultImportService, LuaResultImportService>();

        // Singletons because they hold state that has to be shared: which session was last handed
        // off to the game, which import is already running, and which session changed.
        services.AddSingleton<IGameSessionLauncher, GameSessionLauncher>();
        services.AddSingleton<AssettoCorsaPathResolver>();
        services.AddSingleton<SessionLaunchTracker>();
        services.AddSingleton<LuaResultImportState>();
        services.AddSingleton<RaceSessionChangeNotifier>();
        services.AddSingleton<ResultScanBaseline>();
        services.AddSingleton<LuaResultSweeper>();

        services.AddSingleton<AppVersionProvider>();
        services.AddSingleton<UpdateStateService>();

        services.Configure<UpdateCheckOptions>(configuration.GetSection(UpdateCheckOptions.SectionName));
        services.AddHttpClient(UpdateCheckWorker.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<UpdateCheckWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<UpdateCheckWorker>());
        services.AddHostedService<LuaResultImportWorker>();
        services.AddHostedService<LuaResultCleanupService>();

        return services;
    }
}
