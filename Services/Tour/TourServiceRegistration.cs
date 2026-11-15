namespace MulticlassRace.Services.Tour;

public static class TourServiceRegistration
{
    public static IServiceCollection AddTourServices(this IServiceCollection services)
    {
        services.AddSingleton<ITourDefinitionProvider, TourDefinitionProvider>();
        services.AddScoped<GuidedTourRequest>();

        return services;
    }
}
