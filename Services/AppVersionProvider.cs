using System.Reflection;

namespace MulticlassRace.Services;

public class AppVersionProvider
{
    private static readonly Lazy<string> _currentVersion = new(ResolveCurrentVersion);

    public string CurrentVersion => _currentVersion.Value;

    private static string ResolveCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return Normalize(informational);
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is not null ? Normalize(assemblyVersion.ToString()) : "0.0.0";
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();

        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        return trimmed.Length == 0 ? "0.0.0" : trimmed;
    }
}
