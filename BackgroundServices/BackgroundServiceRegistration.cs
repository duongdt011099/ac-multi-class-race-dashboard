namespace MulticlassRace.BackgroundServices;

public static class BackgroundServiceRegistration
{
    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        // Also a singleton in its own right: the update dialog asks it to check on demand.
        services.AddSingleton<UpdateCheckWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<UpdateCheckWorker>());

        services.AddHostedService<LuaResultImportWorker>();
        services.AddHostedService<LuaResultCleanupService>();

        return services;
    }
}
