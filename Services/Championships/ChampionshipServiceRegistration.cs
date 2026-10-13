namespace MulticlassRace.Services.Championships;

public static class ChampionshipServiceRegistration
{
    public static IServiceCollection AddChampionshipServices(this IServiceCollection services)
    {
        services.AddScoped<IChampionshipService, ChampionshipService>();
        services.AddScoped<ISeasonService, SeasonService>();

        return services;
    }
}
