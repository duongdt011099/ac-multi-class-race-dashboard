using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

/// <summary>Display text and CSS helpers shared by the race screens.</summary>
public static class RaceFormat
{
    public const string UnknownClassColor = "#52525b";

    public static string StatusLabel(RaceStatus status) => status switch
    {
        RaceStatus.NotStarted => "Not Started",
        RaceStatus.InProgress => "In Progress",
        _ => "Finished"
    };

    public static string StatusClass(RaceStatus status) => status switch
    {
        RaceStatus.NotStarted => "wec-status--notstarted",
        RaceStatus.InProgress => "wec-status--inprogress",
        _ => "wec-status--finished"
    };

    public static string SessionTypeLabel(SessionType type) => type switch
    {
        SessionType.Practice => "Practice",
        SessionType.Qualifying => "Qualifying",
        _ => "Race"
    };

    public static string FormatLap(int bestLapTimeMs)
    {
        if (bestLapTimeMs <= 0)
        {
            return "—";
        }

        var time = TimeSpan.FromMilliseconds(bestLapTimeMs);
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    public static string FormatDuration(int totalSeconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));

        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}h {time.Minutes:00}m";
        }

        return $"{time.Minutes}m {time.Seconds:00}s";
    }

    public static string TrackImageUrl(string track, string? layout, string imageType)
    {
        var query = $"track={Uri.EscapeDataString(track)}&type={imageType}";

        if (!string.IsNullOrWhiteSpace(layout))
        {
            query += $"&layout={Uri.EscapeDataString(layout)}";
        }

        return $"/track-image?{query}";
    }
}
