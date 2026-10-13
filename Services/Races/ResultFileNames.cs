using System.Globalization;
using MulticlassRace.Services.Results;

namespace MulticlassRace.Services.Races;

/// <summary>Reads what the yyMMdd-HHmmss naming of result files says.</summary>
internal static class ResultFileNames
{
    public static bool TryParseSessionDate(string fileName, out DateTime date)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        return DateTime.TryParseExact(
            name,
            LuaResultPaths.FileNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim();
    }
}
