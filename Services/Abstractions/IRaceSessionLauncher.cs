using MulticlassRace.Models;

namespace MulticlassRace.Services.Abstractions;

public interface IRaceSessionLauncher
{
    /// <summary>Writes the race.ini for the chosen session and starts Assetto Corsa with it.</summary>
    Task LaunchSessionAsync(Guid raceId, SessionType sessionType, string weather, double sunAngle, bool penalties = true);
}
