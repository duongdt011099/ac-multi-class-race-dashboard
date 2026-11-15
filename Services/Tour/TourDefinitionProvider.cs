using System.Text.Json;

namespace MulticlassRace.Services.Tour;

public interface ITourDefinitionProvider
{
    /// <summary>Reads the tour script, or returns null when it is missing or unusable.</summary>
    Task<TourDefinition?> LoadAsync();
}

/// <summary>
/// Loads the tour from a JSON file next to the executable. The file is read on every call, so editing
/// it takes effect the next time the tour starts without rebuilding anything.
/// </summary>
public sealed class TourDefinitionProvider : ITourDefinitionProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _path;
    private readonly ILogger<TourDefinitionProvider> _logger;

    public TourDefinitionProvider(ILogger<TourDefinitionProvider> logger)
        : this(Path.Combine(AppContext.BaseDirectory, "Tour", "tour-steps.json"), logger)
    {
    }

    internal TourDefinitionProvider(string path, ILogger<TourDefinitionProvider> logger)
    {
        _path = path;
        _logger = logger;
    }

    public async Task<TourDefinition?> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _logger.LogWarning("Tour script {Path} was not found; the guided tour is unavailable.", _path);
                return null;
            }

            await using var stream = File.OpenRead(_path);
            var definition = await JsonSerializer.DeserializeAsync<TourDefinition>(stream, Options);

            return Validate(definition);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A hand-edited file may be broken; the tour is a nicety, so say why in the log and move on.
            _logger.LogWarning(ex, "Tour script {Path} could not be read; the guided tour is unavailable.", _path);
            return null;
        }
    }

    private TourDefinition? Validate(TourDefinition? definition)
    {
        if (definition is null)
        {
            return null;
        }

        // Drop steps that cannot be shown rather than refusing the whole tour.
        definition.Steps = definition.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Message))
            .ToList();

        if (definition.Steps.Count == 0)
        {
            _logger.LogWarning("Tour script {Path} has no usable steps; the guided tour is unavailable.", _path);
            return null;
        }

        return definition;
    }
}
