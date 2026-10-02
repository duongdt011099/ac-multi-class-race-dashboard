namespace MulticlassRace.Services;

/// <summary>
/// Where Content Manager keeps things. Content Manager is a separate program the user may or may not
/// have, and it always keeps its data in a fixed per-user folder, so there is nothing for a user to
/// tell us: if the folder is there we use it, and if it is not there the features that need it are
/// hidden rather than broken.
///
/// A configured value still wins over the derived default, because a dashboard installed before this
/// existed has a saved path in the database that may point at an unusual location.
/// </summary>
public static class ContentManagerPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AcTools Content Manager");

    /// <summary>Content Manager's shared library of grid presets. Note the plural.</summary>
    public static string PresetsRoot { get; } = Path.Combine(DataRoot, "Presets", "Race Grids");

    /// <summary>Where Content Manager writes the JSON result of every session it runs.</summary>
    public static string SessionsRoot { get; } = Path.Combine(DataRoot, "Progress", "Sessions");

    public static bool IsInstalled => Directory.Exists(DataRoot);

    public static string ResolvePreset(string? configuredPath) => Resolve(configuredPath, PresetsRoot);

    public static string ResolveResults(string? configuredPath) => Resolve(configuredPath, SessionsRoot);

    /// <summary>
    /// Whether grid presets can be exported. Exporting creates the folder it writes to, so asking
    /// first is what stops a machine without Content Manager from growing an empty AcTools tree.
    /// </summary>
    public static bool CanExportPresets(string? configuredPath) => Directory.Exists(ResolvePreset(configuredPath));

    /// <summary>
    /// Whether there is anywhere to look for Content Manager session results. The dropdown that
    /// offers these files is hidden entirely when there is not, rather than showing an empty list.
    /// </summary>
    public static bool HasSessionResults(string? configuredPath) => Directory.Exists(ResolveResults(configuredPath));

    private static string Resolve(string? configuredPath, string fallback)
    {
        var trimmed = configuredPath?.Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
