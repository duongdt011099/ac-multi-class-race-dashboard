namespace MulticlassRace.ViewModels;

/// <summary>
/// Layout option: the folder id (stored/validated/written) + UI display name.
/// </summary>
public sealed record TrackLayoutOptionModel(string LayoutId, string DisplayName);
