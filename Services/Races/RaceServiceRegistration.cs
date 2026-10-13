namespace MulticlassRace.Services.Races;

public static class RaceServiceRegistration
{
    public static IServiceCollection AddRaceServices(this IServiceCollection services)
    {
        services.AddScoped<IRaceService, RaceService>();
        services.AddScoped<IRaceSessionLauncher, RaceSessionLauncher>();
        services.AddScoped<IRaceResultService, RaceResultService>();
        services.AddScoped<IGridPresetService, GridPresetService>();

        // Singletons because they hold state that has to be shared: which session was last handed
        // off to the game, and which session changed.
        services.AddSingleton<SessionLaunchTracker>();
        services.AddSingleton<RaceSessionChangeNotifier>();

        return services;
    }
}
