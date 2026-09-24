using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public class RaceResultImportResult
{
    public int Imported { get; set; }

    public int Skipped { get; set; }

    public DateTime SessionDate { get; set; }

    public SessionType SessionType { get; set; }
}