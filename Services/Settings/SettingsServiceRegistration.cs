namespace MulticlassRace.Services.Settings;

public static class SettingsServiceRegistration
{
    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        services.AddScoped<IClassSettingService, ClassSettingService>();
        services.AddScoped<IPointSettingService, PointSettingService>();

        return services;
    }
}
