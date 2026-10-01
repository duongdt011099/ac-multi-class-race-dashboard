namespace MulticlassRace.ViewModels;

/// <summary>
/// A track as the dropdowns show it: the AC folder id that everything stored or
/// written back to AC has to keep using, plus the name from ui_track.json that
/// people actually recognise.
/// </summary>
public sealed record TrackOptionModel(string TrackId, string DisplayName);
