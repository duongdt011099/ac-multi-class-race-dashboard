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

        // The downloaded installer is large, so it gets a client without the 10 second check timeout.
        services.AddHttpClient(UpdateInstallerService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        // Also a singleton in its own right: the update dialog drives and observes it.
        services.AddSingleton<IUpdateInstallerEnvironment, WindowsUpdateInstallerEnvironment>();
        services.AddSingleton<UpdateInstallerService>();
        services.AddHostedService(sp => sp.GetRequiredService<UpdateInstallerService>());

        return services;
    }
}
