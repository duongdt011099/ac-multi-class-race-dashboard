using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public class AssettoCorsaContentService : IAssettoCorsaContentService
{
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
