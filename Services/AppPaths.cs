namespace MulticlassRace.Services;

/// <summary>
/// Where the app writes its log. It lives next to the executable so there is one folder to look in
/// when something goes wrong, and the installer only has to leave that folder alone.
/// </summary>
internal static class AppPaths
{
    public static string LogsDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    public static string LogFilePath => Path.Combine(LogsDirectory, "dashboard.log");
}
