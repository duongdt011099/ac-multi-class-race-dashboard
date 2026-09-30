namespace MulticlassRace.Services.ImportModels;

internal sealed class SessionResultEntry
{
    public string Driver { get; set; } = string.Empty;

    public string Car { get; set; } = string.Empty;

    public string Skin { get; set; } = string.Empty;

    public string? BestLapTimeMs { get; set; }
}
