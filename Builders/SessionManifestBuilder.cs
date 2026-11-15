using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Services.Races;

namespace MulticlassRace.Builders;

/// <summary>
/// Builds the session file the Lua app reads instead of detecting classes from car tags. It lists the
/// cars of race.ini by grid index (CAR_n) with the class each belongs to in the race's class setting.
/// Contract with assetto-corsa-app/Multi_Class/src/classes/session_file.lua.
/// </summary>
public static class SessionManifestBuilder
{
    public const string FileName = "mcr_session.json";

    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="human">Grid index 0; null when the race has no human driver.</param>
    /// <param name="opponents">Grid indexes 1..n, in the order they are written to race.ini.</param>
    public static string Build(Race race, SessionType sessionType, Driver? human, IReadOnlyList<Driver> opponents)
    {
        var classes = (race.ClassSetting?.Items ?? Array.Empty<ClassSettingItem>())
            .OrderBy(i => i.Order)
            .Select((i, index) => new { name = i.Name.Trim(), order = index, color = i.Color })
            .ToList();

        var cars = new List<object>();

        if (human is not null)
        {
            cars.Add(CarEntry(0, race, human));
        }

        for (var i = 0; i < opponents.Count; i++)
        {
            cars.Add(CarEntry(i + 1, race, opponents[i]));
        }

        return JsonSerializer.Serialize(new
        {
            version = Version,
            raceId = race.RaceId,
            sessionType = sessionType.ToString().ToLowerInvariant(),
            createdUtc = DateTimeOffset.UtcNow,
            classes,
            cars
        }, JsonOptions);
    }

    private static object CarEntry(int index, Race race, Driver driver) => new
    {
        index,
        carId = driver.Car?.Trim() ?? string.Empty,
        driverName = driver.DriverName?.Trim() ?? string.Empty,
        team = driver.Team?.TeamName ?? string.Empty,
        @class = driver.Team is null ? string.Empty : RaceClassOrdering.ClassNameOf(race, driver.Team).Trim()
    };
}
