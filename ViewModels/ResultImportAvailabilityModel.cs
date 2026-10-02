namespace MulticlassRace.ViewModels;

/// <summary>
/// Which of the Content Manager backed features can do anything on this machine. Result folders and
/// the preset library belong to Content Manager, so a user without it gets these controls hidden
/// rather than a control that quietly writes into a folder it invented.
/// </summary>
public class ResultImportAvailabilityModel
{
    /// <summary>Whether there is a folder anywhere to import a race result from.</summary>
    public bool CanImportResults { get; set; }

    /// <summary>Whether grid presets have a real Content Manager folder to be written to.</summary>
    public bool CanExportPresets { get; set; }
}
