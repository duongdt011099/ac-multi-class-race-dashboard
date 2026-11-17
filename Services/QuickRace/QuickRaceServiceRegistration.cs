namespace MulticlassRace.Services.QuickRace;

public static class QuickRaceServiceRegistration
{
    public static IServiceCollection AddQuickRaceServices(this IServiceCollection services)
    {
        services.AddScoped<IQuickRaceService, QuickRaceService>();
        services.AddScoped<IQuickRaceLauncher, QuickRaceLauncher>();

        // Only reads a data file, and the names are shared by every circuit.
        services.AddSingleton<IRandomNameService, RandomNameService>();

        return services;
    }
}
