namespace MulticlassRace.ViewModels;

/// <summary>
/// Layout option: the folder id (stored/validated/written) + UI display name. An empty id means the
/// track's root/default configuration, which Assetto Corsa represents with a blank CONFIG_TRACK.
/// </summary>
public sealed record TrackLayoutOptionModel(string LayoutId, string DisplayName);
