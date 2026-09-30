namespace MulticlassRace.Models;

public class ImportedLuaResult
{
    public Guid ImportedLuaResultId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public SessionType SessionType { get; set; }

    public DateTime SessionDate { get; set; }

    public bool Imported { get; set; }

    public DateTime? ImportedAtUtc { get; set; }

    public string SessionTypeLabel => SessionType switch
    {
        SessionType.Practice => "Practice",
        SessionType.Qualifying => "Qualifying",
        _ => "Race"
    };

    public int ImportCount { get; set; }

    public string? LastError { get; set; }
}