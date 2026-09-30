using MulticlassRace.Models;

namespace MulticlassRace.Services;

/// <summary>
/// Remembers the last session the dashboard handed off to Assetto Corsa so an
/// auto-detected result file knows which race it belongs to.
/// </summary>
public sealed record SessionLaunch(
    Guid RaceId,
    SessionType SessionType,
    string RaceName,
    string SeasonName,
    string ChampionshipName,
    string TrackName,
    DateTimeOffset LaunchedAt);

public class SessionLaunchTracker
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly object _gate = new();
    private SessionLaunch? _current;

    public event Action? Changed;

    public SessionLaunch? Current
    {
        get
        {
            lock (_gate)
            {
                return _current is not null && DateTimeOffset.UtcNow - _current.LaunchedAt < Lifetime
                    ? _current
                    : null;
            }
        }
    }

    public void Record(SessionLaunch launch)
    {
        lock (_gate)
        {
            _current = launch;
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
        }

        Changed?.Invoke();
    }
}
