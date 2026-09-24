using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public static class DependencyInjection
{
    public static IServiceCollection RegisterServices(this IServiceCollection services)
    {
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<ITeamClassService, TeamClassService>();
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<IChampionshipService, ChampionshipService>();
        services.AddScoped<ISeasonService, SeasonService>();
        services.AddScoped<IRaceService, RaceService>();
        services.AddScoped<IAssettoCorsaGameConfigService, AssettoCorsaGameConfigService>();
        services.AddScoped<ToastService>();

        return services;
    }
}