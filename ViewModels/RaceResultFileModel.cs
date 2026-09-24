namespace MulticlassRace.ViewModels;

public class RaceResultFileModel
{
    public required string FileName { get; set; }

    public DateTime SessionDate { get; set; }

    public required string Track { get; set; }
}