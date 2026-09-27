namespace MulticlassRace.Services;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeInBytes);

public sealed record UpdateAvailability(
    string LatestVersion,
    string TagName,
    string ReleaseUrl,
    string? ReleaseNotes,
    DateTimeOffset PublishedAt,
    ReleaseAsset? Installer);

public class UpdateStateService
{
    private readonly object _gate = new();
    private UpdateAvailability? _current;

    public event Action? AvailabilityChanged;

    public UpdateAvailability? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Set(UpdateAvailability? availability)
    {
        lock (_gate)
        {
            _current = availability;
        }

        AvailabilityChanged?.Invoke();
    }

    public void Clear()
    {
        Set(null);
    }
}
