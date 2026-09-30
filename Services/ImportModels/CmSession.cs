namespace MulticlassRace.Services.ImportModels;

internal sealed class CmSession
{
    public int Event { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Type { get; set; }

    public int LapsCount { get; set; }

    public int Duration { get; set; }

    public List<CmLap> Laps { get; set; } = new();

    public List<int> LapsTotal { get; set; } = new();

    public List<CmBestLap> BestLaps { get; set; } = new();

    public List<int>? RaceResult { get; set; }
}
