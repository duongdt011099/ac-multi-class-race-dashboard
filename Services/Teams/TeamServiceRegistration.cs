using MulticlassRace.Web;

namespace MulticlassRace.Services.Teams;

public static class TeamServiceRegistration
{
    public static IServiceCollection AddTeamServices(this IServiceCollection services)
    {
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<ITeamLogoStorage>(sp =>
            new TeamLogoStorage(UploadsEndpointExtensions.ResolveRoot(sp.GetRequiredService<IWebHostEnvironment>())));

        return services;
    }
}
