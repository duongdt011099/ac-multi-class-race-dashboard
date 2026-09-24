using System.Text.Json;

namespace MulticlassRace.Services;

public sealed class PresetBuilder
{
    private readonly List<string?> _carIds = new();
    private readonly List<string?> _skinIds = new();
    private readonly List<string?> _names = new();
    private readonly List<string> _aiLevels = new();
    private readonly List<string> _aiAggressions = new();
    private int _startingPosition = 1;

    public int Count => _carIds.Count;

    public PresetBuilder AddCar(string? carId, string? skinId, string? name, int? aiLevel, int? aiAggression)
    {
        _carIds.Add(carId);
        _skinIds.Add(string.IsNullOrWhiteSpace(skinId) ? null : skinId);
        _names.Add(string.IsNullOrWhiteSpace(name) ? null : name);
        _aiLevels.Add(aiLevel?.ToString() ?? "-1");
        _aiAggressions.Add(aiAggression?.ToString() ?? "-1");
        return this;
    }

    public PresetBuilder SetStartingPosition(int position)
    {
        _startingPosition = position;
        return this;
    }

    public string BuildJson()
    {
        var document = new
        {
            ModeId = "custom",
            FilterValue = "",
            CarIds = _carIds,
            AiLevels = _aiLevels,
            AiAggressions = _aiAggressions,
            Names = _names,
            SkinIds = _skinIds,
            ShuffleCandidates = false,
            VarietyLimitation = 0,
            OpponentsNumber = _carIds.Count,
            StartingPosition = _startingPosition,
            AiLevel = 100.0,
            AiLevelMin = 100.0,
            AiLevelArrangeRandom = 0.1,
            AiLevelArrangeReverse = false,
            AiLevelArrangePowerRatio = false,
            AiAggression = 0.0,
            AiAggressionMin = 0.0,
            AiAggressionArrangeRandom = 0.1,
            AiAggressionArrangeReverse = false
        };

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }
}