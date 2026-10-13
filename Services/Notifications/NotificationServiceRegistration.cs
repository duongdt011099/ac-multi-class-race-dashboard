using MulticlassRace.Services.Browser;

namespace MulticlassRace.Services.Notifications;

public static class NotificationServiceRegistration
{
    /// <summary>Per-circuit helpers the pages use to talk to the user and to the browser.</summary>
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddScoped<ToastService>();
        services.AddScoped<BrowserStorage>();

        return services;
    }
}
