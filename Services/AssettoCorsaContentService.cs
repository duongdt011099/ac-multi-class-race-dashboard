using System.Text.Json;
using System.Text.RegularExpressions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class AssettoCorsaContentService : IAssettoCorsaContentService
{
    private static readonly Regex CarNameFallbackPattern = new(
        "(?m)^[\\t ]*\"name\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AssettoCorsaPathResolver _pathResolver;
    private readonly ILogger<AssettoCorsaContentService> _logger;

    public AssettoCorsaContentService(
        AssettoCorsaPathResolver pathResolver,
        ILogger<AssettoCorsaContentService> logger)
    {
        _pathResolver = pathResolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetCarsAsync()
    {
        var carsRoot = await GetCarsRootAsync();

        return carsRoot is null
            ? Array.Empty<string>()
            : ListDirectories(carsRoot);
    }

    public async Task<IReadOnlyList<CarOptionModel>> GetCarOptionsAsync()
    {
        var carsRoot = await GetCarsRootAsync();

        if (carsRoot is null)
        {
            return Array.Empty<CarOptionModel>();
        }

        var cars = new List<CarOptionModel>();

        foreach (var carId in ListDirectories(carsRoot))
        {
            var displayName = await ReadCarDisplayNameAsync(carsRoot, carId) ?? carId;
            cars.Add(new CarOptionModel(carId, displayName));
        }

        return cars
            .OrderBy(car => car.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(car => car.CarId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> GetCarDisplayNameAsync(string car)
    {
        var carsRoot = await GetCarsRootAsync();

        if (carsRoot is null)
        {
            return null;
        }

        return await ReadCarDisplayNameAsync(carsRoot, car);
    }

    private async Task<string?> ReadCarDisplayNameAsync(string carsRoot, string car)
    {
        if (IsSafeFolderName(car) is false)
        {
            return null;
        }

        var carDirectory = Path.GetFullPath(Path.Combine(carsRoot, car.Trim()));

        if (!IsInside(carsRoot, carDirectory))
        {
            return null;
        }

        var uiCarPath = Path.Combine(carDirectory, "ui", "ui_car.json");

        if (!File.Exists(uiCarPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(uiCarPath);

            try
            {
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String)
                {
                    return NormalizeDisplayName(name.GetString());
                }
            }
            catch (JsonException)
            {
                // Some shipped AC metadata has a raw newline in a description string. The file is
                // invalid JSON, but its top-level name may still be intact and usable for display.
            }

            var match = CarNameFallbackPattern.Match(json);

            if (!match.Success)
            {
                return null;
            }

            try
            {
                return NormalizeDisplayName(JsonSerializer.Deserialize<string>($"\"{match.Groups[1].Value}\""));
            }
            catch (JsonException)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read the car display name from '{Path}'.", uiCarPath);
            return null;
        }
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public async Task<IReadOnlyList<string>> GetSkinsAsync(string car)
    {
        var carsRoot = await GetCarsRootAsync();

        if (carsRoot is null || !IsSafeFolderName(car))
        {
            return Array.Empty<string>();
        }

        var skinsRoot = Path.GetFullPath(Path.Combine(carsRoot, car.Trim(), "skins"));

        if (!IsInside(carsRoot, skinsRoot))
        {
            return Array.Empty<string>();
        }

        return ListDirectories(skinsRoot);
    }

    public string? BuildSkinPreviewUrl(string? car, string? skin)
    {
        if (!IsSafeFolderName(car) || !IsSafeFolderName(skin))
        {
            return null;
        }

        return $"/car-image?car={Uri.EscapeDataString(car!.Trim())}&skin={Uri.EscapeDataString(skin!.Trim())}&type=car";
    }

    private async Task<string?> GetCarsRootAsync()
    {
        var gamePath = (await _pathResolver.GetAsync()).Path;

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return null;
        }

        var carsRoot = Path.GetFullPath(Path.Combine(gamePath, "content", "cars"));

        return Directory.Exists(carsRoot) ? carsRoot : null;
    }

    private IReadOnlyList<string> ListDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read car content folder '{Path}'.", path);
            return Array.Empty<string>();
        }
    }

    private static bool IsSafeFolderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed != "."
            && trimmed != ".."
            && !trimmed.Contains('\\')
            && !trimmed.Contains('/');
    }

    private static bool IsInside(string root, string candidate)
    {
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
