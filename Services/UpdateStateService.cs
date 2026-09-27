namespace MulticlassRace.Services;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeInBytes);

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? TagName,
    string? ReleaseUrl,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    ReleaseAsset? Installer,
    bool IsUpdateAvailable,
    DateTimeOffset CheckedAtUtc,
    string? ErrorMessage)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static UpdateCheckResult UpToDate(
        string currentVersion,
        string latestVersion,
        DateTimeOffset checkedAtUtc)
    {
        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            latestVersion,
            null,
            null,
            null,
            null,
            false,
            checkedAtUtc,
            null);
    }

    public static UpdateCheckResult Failed(
        string currentVersion,
        DateTimeOffset checkedAtUtc,
        string error,
        UpdateCheckResult? previous)
    {
        return new UpdateCheckResult(
            currentVersion,
            previous?.LatestVersion,
            previous?.TagName,
            null,
            null,
            null,
            null,
            false,
            checkedAtUtc,
            error);
    }
}

public class UpdateStateService
{
    private readonly object _gate = new();
    private UpdateCheckResult? _current;

    public event Action? AvailabilityChanged;

    public UpdateCheckResult? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Set(UpdateCheckResult? result)
    {
        lock (_gate)
        {
            _current = result;
        }

        AvailabilityChanged?.Invoke();
    }

    public void Clear()
    {
        Set(null);
    }
}
