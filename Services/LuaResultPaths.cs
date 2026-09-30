using System.Globalization;
using MulticlassRace.Models;

namespace MulticlassRace.Services;

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

    public static string BuildFileName(DateTime sessionDate)
    {
        return sessionDate.ToString(FileNameFormat, CultureInfo.InvariantCulture) + FileExtension;
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
