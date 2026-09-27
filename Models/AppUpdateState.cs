namespace MulticlassRace.Models;

public class AppUpdateState
{
    public Guid AppUpdateStateId { get; set; }
    public string LastSeenVersion { get; set; } = string.Empty;
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
}
