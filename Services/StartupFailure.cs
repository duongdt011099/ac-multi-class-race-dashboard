using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace MulticlassRace.Services;

/// <summary>
/// How a startup failure reaches the user. Without this the app fails silently: no window, no
/// console, nothing in the tray because the tray never started. The usual cause is a second copy
/// starting while the first one still holds the port.
/// </summary>
public static class StartupFailure
{
    public static void Report(IServiceProvider services, Exception exception)
    {
        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogCritical(exception, "The dashboard could not start.");

        try
        {
            MessageBox.Show(
                $"The Endurance Race dashboard could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Endurance Race",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // No interactive session to show it in. The log already has the reason.
        }
    }
}
