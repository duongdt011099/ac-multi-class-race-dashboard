namespace MulticlassRace.Configuration;

public class UpdateCheckOptions
{
    public const string SectionName = "UpdateCheck";

    public bool Enabled { get; set; } = true;

    public string Owner { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public int CheckIntervalHours { get; set; } = 6;

    public string InstallerFileName { get; set; } = "EnduranceRaceDashboardSetup.exe";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository);

    public int ResolvedIntervalHours => CheckIntervalHours > 0 ? CheckIntervalHours : 6;
}
