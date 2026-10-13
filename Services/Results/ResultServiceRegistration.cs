namespace MulticlassRace.Services.Results;

public static class ResultServiceRegistration
{
    public static IServiceCollection AddResultServices(this IServiceCollection services)
    {
        services.AddScoped<ILuaResultImportService, LuaResultImportService>();

        // Singletons because they hold state that has to be shared: which import is already running,
        // what was seen at the last scan, and what the sweeper has already handled.
        services.AddSingleton<LuaResultImportState>();
        services.AddSingleton<ResultScanBaseline>();
        services.AddSingleton<LuaResultSweeper>();

        return services;
    }
}
