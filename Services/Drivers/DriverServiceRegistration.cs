namespace MulticlassRace.Services.Drivers;

public static class DriverServiceRegistration
{
    public static IServiceCollection AddDriverServices(this IServiceCollection services)
    {
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<DriverDataChangeNotifier>();

        return services;
    }
}
