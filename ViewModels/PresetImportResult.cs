namespace MulticlassRace.ViewModels;

public class PresetImportResult
{
    public required string PresetName { get; init; }

    public int Added { get; init; }

    public int Skipped { get; init; }
}