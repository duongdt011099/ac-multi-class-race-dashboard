using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Mapping;

public static class DriverMappings
{
    /// <summary>Everything a driver table shows, including class tags and the car and livery image links.</summary>
    public static DriverModel ToModel(this Driver driver, string? gamePath)
    {
        return new DriverModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            LiveryImageUrl = BuildImageUrl(gamePath, driver.Car, driver.Skin, "livery"),
            CarImageUrl = BuildImageUrl(gamePath, driver.Car, driver.Skin, "car"),
            Nationality = driver.Nationality,
            Age = driver.Age,
            DriverAgression = driver.DriverAgression,
            DriverStrength = driver.DriverStrength,
            TeamName = driver.Team?.TeamName,
            TeamId = driver.Team?.TeamId,
            TeamLogo = driver.Team?.TeamLogo,
            ClassTags = driver.Team is null
                ? new List<ClassTagModel>()
                : driver.Team.ClassSettingItems.ToClassTags(),
            IsActive = driver.IsActive,
            IsHuman = driver.IsHuman
        };
    }

    /// <summary>The lighter shape used inside a team: no image links and no class tags.</summary>
    public static DriverModel ToSummaryModel(this Driver driver)
    {
        return new DriverModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            Nationality = driver.Nationality,
            TeamId = driver.Team?.TeamId,
            TeamLogo = driver.Team?.TeamLogo,
            IsActive = driver.IsActive
        };
    }

    public static DriverFormModel ToFormModel(this Driver driver)
    {
        return new DriverFormModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            Nationality = driver.Nationality,
            Age = driver.Age,
            DriverAgression = driver.DriverAgression,
            DriverStrength = driver.DriverStrength,
            TeamId = driver.Team?.TeamId,
            TeamName = driver.Team?.TeamName,
            IsActive = driver.IsActive,
            IsHuman = driver.IsHuman
        };
    }

    /// <summary>Null when the game folder is unknown or the car or skin is blank, since there is then nothing to show.</summary>
    public static string? BuildImageUrl(string? gamePath, string car, string skin, string type)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(car) || string.IsNullOrWhiteSpace(skin))
        {
            return null;
        }

        return $"/car-image?car={Uri.EscapeDataString(car)}&skin={Uri.EscapeDataString(skin)}&type={type}";
    }
}
