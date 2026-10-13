namespace MulticlassRace.ViewModels;

/// <summary>Layout, estimate and validation rules behind the race form.</summary>
public static class RaceFormRules
{
    public const int MaxPracticeMinutes = 90;

    public const int MinQualifyingMinutes = 5;

    public const int MaxQualifyingMinutes = 90;

    /// <summary>A blank layout id is the track's default configuration.</summary>
    public static bool HasDefaultLayout(IReadOnlyCollection<TrackLayoutOptionModel> layouts)
        => layouts.Any(layout => string.IsNullOrWhiteSpace(layout.LayoutId));

    /// <summary>A layout must be picked only when the track has layouts but no default one.</summary>
    public static bool RequiresLayout(IReadOnlyCollection<TrackLayoutOptionModel> layouts)
        => layouts.Count > 0 && !HasDefaultLayout(layouts);

    public static bool ShowTrackPreview(RaceFormModel form, IReadOnlyCollection<TrackLayoutOptionModel> layouts)
    {
        return !string.IsNullOrWhiteSpace(form.TrackName) &&
               (!string.IsNullOrWhiteSpace(form.TrackLayout) || layouts.Count == 0 || HasDefaultLayout(layouts));
    }

    public static int ClampPracticeMinutes(int? minutes) => Math.Clamp(minutes ?? 20, 0, MaxPracticeMinutes);

    public static int ClampQualifyingMinutes(int? minutes)
        => Math.Clamp(minutes ?? 20, MinQualifyingMinutes, MaxQualifyingMinutes);

    /// <summary>Estimated race length in seconds, or null when laps or a best lap time are unknown.</summary>
    public static int? EstimateDurationSeconds(int? laps, int? bestLapTimeMs)
    {
        return laps is > 0 && bestLapTimeMs is > 0
            ? (int)Math.Round((double)laps.Value * bestLapTimeMs.Value / 1000.0)
            : null;
    }

    /// <summary>The first reason the race cannot be saved, or null when it can.</summary>
    public static string? Validate(RaceFormModel form, IReadOnlyCollection<TrackLayoutOptionModel> layouts)
    {
        if (string.IsNullOrWhiteSpace(form.RaceName))
        {
            return "Race name is required.";
        }

        if (string.IsNullOrWhiteSpace(form.TrackName))
        {
            return "Select a track.";
        }

        if (RequiresLayout(layouts) && string.IsNullOrWhiteSpace(form.TrackLayout))
        {
            return "Select a track layout.";
        }

        if (form.PointSettingId == Guid.Empty)
        {
            return "Select a point setting.";
        }

        if (form.ClassSettingId == Guid.Empty)
        {
            return "Select a class setting.";
        }

        return null;
    }
}
