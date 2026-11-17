using System.Globalization;
using MulticlassRace.Models;

namespace MulticlassRace.Services.Results;

public static class LuaResultPaths
{
    public const string PracticeFolder = "practice";

    public const string QualifyingFolder = "qualifying";

    public const string RaceFolder = "race";

    public const string FileExtension = ".json";

    public const string FileNameFormat = "yyMMdd-HHmmss";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Assetto Corsa",
        "mcr-results");

    public static string ResolveRoot(string? configuredPath)
    {
        var trimmed = configuredPath?.Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? DefaultRoot : trimmed;
    }

    /// <summary>
    /// The Lua app starts the name of a quick race result with this, e.g. quick-race-261117-203015.json,
    /// so it is never mistaken for a championship race result.
    /// </summary>
    public const string QuickRacePrefix = "quick-race-";

    public static string BuildFileName(DateTime sessionDate)
    {
        return sessionDate.ToString(FileNameFormat, CultureInfo.InvariantCulture) + FileExtension;
    }

    public static bool IsQuickRaceFile(string? fileName)
    {
        return Path.GetFileName(fileName ?? string.Empty).StartsWith(QuickRacePrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the session date out of a Lua result file name, with or without the quick race prefix.
    /// </summary>
    public static bool TryParseFileName(string? fileName, out DateTime date, out bool isQuickRace)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        isQuickRace = name.StartsWith(QuickRacePrefix, StringComparison.OrdinalIgnoreCase);

        if (isQuickRace)
        {
            name = name[QuickRacePrefix.Length..];
        }

        return DateTime.TryParseExact(
            name,
            FileNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    public static SessionType? GetSessionType(string? folderName)
    {
        return (folderName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            PracticeFolder => SessionType.Practice,
            QualifyingFolder => SessionType.Qualifying,
            RaceFolder => SessionType.Race,
            _ => null
        };
    }

    public static string GetSessionTypeLabel(SessionType sessionType)
    {
        return sessionType switch
        {
            SessionType.Practice => "Practice",
            SessionType.Qualifying => "Qualifying",
            _ => "Race"
        };
    }
}
