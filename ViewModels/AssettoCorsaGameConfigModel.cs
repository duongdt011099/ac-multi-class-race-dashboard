namespace MulticlassRace.ViewModels;

public class AssettoCorsaGameConfigModel
{
    public string GamePath { get; set; } = string.Empty;

    public string PresetPath { get; set; } = string.Empty;

    public string RaceResultsPath { get; set; } = string.Empty;

    public string LuaResultPath { get; set; } = string.Empty;

    /// <summary>Car id of the safety car. Empty means the first car found in the game folder.</summary>
    public string SafetyCar { get; set; } = string.Empty;
}