using MulticlassRace.Models;

namespace MulticlassRace.Services.ImportModels;

internal sealed class ImportEntry
{
    public required (Driver Driver, Team Team) Match { get; set; }

    public int BestLapMs { get; set; }

    public int LapCount { get; set; }
}
