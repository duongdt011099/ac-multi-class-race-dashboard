namespace MulticlassRace.ViewModels;

public class RaceResultFileModel
{
    public required string FileName { get; set; }

    public required string FullPath { get; set; }

    public DateTime SessionDate { get; set; }

    public required string Track { get; set; }

    public bool IsLuaResult { get; set; }
}