using MulticlassRace.BackgroundServices;
using MulticlassRace.Configuration;

namespace MulticlassRace.Services.Updates;

public static class UpdateServiceRegistration
{
    public static IServiceCollection AddUpdateServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<UpdateNotificationRequest>();
        services.AddScoped<IAppUpdatePreferenceService, AppUpdatePreferenceService>();

        services.AddSingleton<AppVersionProvider>();
        services.AddSingleton<UpdateStateService>();

        services.Configure<UpdateCheckOptions>(configuration.GetSection(UpdateCheckOptions.SectionName));
        services.AddHttpClient(UpdateCheckWorker.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
