using MulticlassRace.BackgroundServices;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Championships;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Notifications;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;
using MulticlassRace.Services.Settings;
using MulticlassRace.Services.Teams;
using MulticlassRace.Services.Updates;

namespace MulticlassRace.Services;

public static class ServiceRegistration
{
    /// <summary>Registers every feature's services and the background workers.</summary>
    public static IServiceCollection RegisterServices(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddChampionshipServices()
            .AddRaceServices()
            .AddSettingsServices()
            .AddTeamServices()
            .AddDriverServices()
            .AddAssettoCorsaServices()
            .AddResultServices()
            .AddUpdateServices(configuration)
            .AddNotificationServices()
            .AddBackgroundServices();
    }
}
