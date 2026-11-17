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

    /// <summary>
    /// The session file of a quick race. The classes are the grid's own columns, so there is no race or
    /// class setting behind it.
    /// </summary>
    /// <param name="classes">The columns that have a car, in grid order.</param>
    /// <param name="cars">Grid index 0 is the human; the rest follow in start order.</param>
    public static string BuildQuickRace(
        IReadOnlyList<(string Name, string Color)> classes,
        IReadOnlyList<ViewModels.QuickRaceLaunchCar> cars,
        SessionType sessionType = SessionType.Race)
    {
        return JsonSerializer.Serialize(new
        {
            version = Version,
            quickRace = true,
            sessionType = sessionType.ToString().ToLowerInvariant(),
            createdUtc = DateTimeOffset.UtcNow,
            classes = classes.Select((c, index) => new { name = c.Name.Trim(), order = index, color = c.Color }).ToList(),
            cars = cars.Select(c => new
            {
                index = c.Index,
                carId = c.Car.Trim(),
                driverName = c.DriverName.Trim(),
                team = c.TeamName ?? string.Empty,
                @class = c.ClassName.Trim()
            }).ToList()
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
