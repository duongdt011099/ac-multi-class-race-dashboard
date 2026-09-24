using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public static class DependencyInjection
{
    public static IServiceCollection RegisterRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamClassRepository, TeamClassRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IChampionshipRepository, ChampionshipRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<IRaceRepository, RaceRepository>();
        services.AddScoped<IAssettoCorsaGameConfigRepository, AssettoCorsaGameConfigRepository>();
        return services;
    }
}