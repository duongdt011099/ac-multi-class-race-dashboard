using MulticlassRace.Models;

namespace MulticlassRace.Services.Races.ImportModels;

internal sealed class ImportEntry
{
    public required (Driver Driver, Team Team) Match { get; set; }

    public int BestLapMs { get; set; }

    public int LapCount { get; set; }

    /// <summary>Index in the session's finishing order; <see cref="int.MaxValue"/> when the file has none.</summary>
    public int FinishOrder { get; set; } = int.MaxValue;
}
