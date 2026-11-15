namespace MulticlassRace.Configuration;

public class UpdateCheckOptions
{
    public const string SectionName = "UpdateCheck";

    public bool Enabled { get; set; } = true;

    public string Owner { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public int CheckIntervalHours { get; set; } = 6;

    public string InstallerFileName { get; set; } = "EnduranceRaceDashboardSetup.exe";

    /// <summary>Download a newer installer in the background as soon as one is found.</summary>
    public bool AutoDownload { get; set; } = true;

    /// <summary>
    /// Run the downloaded installer without asking. Off by default: the app imports results while the
    /// game runs, so restarting it should be the user's call. Never fires while the game is running.
    /// </summary>
    public bool AutoInstall { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository);

    public int ResolvedIntervalHours => CheckIntervalHours > 0 ? CheckIntervalHours : 6;
}
