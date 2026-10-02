using Microsoft.Extensions.Configuration;
using MulticlassRace.Configuration;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public static class DependencyInjection
{
    public static IServiceCollection RegisterServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<ITeamClassService, TeamClassService>();
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<IChampionshipService, ChampionshipService>();
        services.AddScoped<ISeasonService, SeasonService>();
        services.AddScoped<IRaceService, RaceService>();
        services.AddScoped<ITrackService, TrackService>();
        services.AddScoped<IAssettoCorsaGameConfigService, AssettoCorsaGameConfigService>();
        services.AddScoped<IAssettoCorsaContentService, AssettoCorsaContentService>();
        services.AddScoped<IPointSettingService, PointSettingService>();
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
        services.AddHostedService<UpdateCheckWorker>();
        services.AddHostedService<LuaResultImportWorker>();
        services.AddHostedService<LuaResultCleanupService>();

        return services;
    }
}
