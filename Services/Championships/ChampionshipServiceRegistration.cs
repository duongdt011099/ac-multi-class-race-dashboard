namespace MulticlassRace.Services.Championships;

public static class ChampionshipServiceRegistration
{
    public static IServiceCollection AddChampionshipServices(this IServiceCollection services)
    {
        services.AddScoped<IChampionshipService, ChampionshipService>();
        services.AddScoped<ISeasonService, SeasonService>();
        services.AddScoped<ISeasonCompletionService, SeasonCompletionService>();
        services.AddScoped<IAchievementService, AchievementService>();

        // Singleton so a result imported in the background reaches whichever page is open.
        services.AddSingleton<SeasonCompletionNotifier>();

        return services;
    }
}
