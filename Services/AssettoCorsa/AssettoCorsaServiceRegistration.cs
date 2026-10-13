namespace MulticlassRace.Services.AssettoCorsa;

public static class AssettoCorsaServiceRegistration
{
    public static IServiceCollection AddAssettoCorsaServices(this IServiceCollection services)
    {
        services.AddScoped<ITrackService, TrackService>();
        services.AddScoped<IAssettoCorsaGameConfigService, AssettoCorsaGameConfigService>();
        services.AddScoped<IAssettoCorsaContentService, AssettoCorsaContentService>();
        services.AddScoped<GameConfigChangeNotifier>();

        // The game folder is resolved once and shared; launching has no state of its own.
        services.AddSingleton<IGameSessionLauncher, GameSessionLauncher>();
        services.AddSingleton<AssettoCorsaPathResolver>();

        return services;
    }
}
