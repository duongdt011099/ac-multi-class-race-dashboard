namespace MulticlassRace.Models;

public class AssettoCorsaGameConfig
{
    public Guid AssettoCorsaGameConfigId { get; set; }

    public string GamePath { get; set; } = string.Empty;

    public string PresetPath { get; set; } = string.Empty;

    public string RaceResultsPath { get; set; } = string.Empty;
}