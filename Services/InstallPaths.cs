using System.Text.Json;

namespace MulticlassRace.Services;

/// <summary>
/// What the installer told the app. The setup asks for the Assetto Corsa folder, insists on a real
/// one, and writes the answer to install.json next to the executable. The game location is
/// therefore answered once at install time instead of being asked of the user on a settings page.
///
/// This is deliberately not part of the configuration system: install.json holds a single value that
/// no environment variable or command line switch should be able to shadow, and reading it here keeps
/// it out of the way of the Kestrel and urls overrides.
/// </summary>
internal static class InstallPaths
{
    public const string FileName = "install.json";

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// The Assetto Corsa folder recorded by the installer, or null when the app was not started from
    /// an installed copy. Re-read on demand rather than cached, so a reinstall that rewrites the file
    /// is picked up without needing a restart to take effect everywhere.
    /// </summary>
    public static string? GamePath
    {
        get
        {
            try
            {
                if (File.Exists(FilePath) is false)
                {
                    return null;
                }

                var json = File.ReadAllText(FilePath);
                var info = JsonSerializer.Deserialize<InstallInfo>(json, SerializerOptions);

                return string.IsNullOrWhiteSpace(info?.GamePath) ? null : info.GamePath.Trim();
            }
            catch
            {
                // A missing, empty or hand-mangled install.json must not stop the app from starting;
                // the resolver falls back to the saved value and then to auto-detection.
                return null;
            }
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class InstallInfo
    {
        public string? GamePath { get; set; }
    }
}
