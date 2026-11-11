namespace MulticlassRace.Services.Database;

public static class DatabaseServiceRegistration
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.AddScoped<IDatabaseTransferService, DatabaseTransferService>();

        return services;
    }
}
