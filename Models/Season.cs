namespace MulticlassRace.Models;

public class Season
{
    public Guid SeasonId { get; set; }

    public required string SeasonName { get; set; }

    public required Championship Championship { get; set; }

    /// <summary>
    /// The class system every race of this season uses. Null for seasons created before classes
    /// belonged to the season whose races mix several class settings; those races keep their own.
    /// </summary>
    public Guid? ClassSettingId { get; set; }

    public ClassSetting? ClassSetting { get; set; }

    public const int DefaultNumberOfRounds = 8;

    public const int MinRounds = 1;

    public const int MaxRounds = 50;

    /// <summary>
    /// How many races (rounds) the season has; once that many are finished the season is over. Null for seasons from
    /// before rounds existed: they have to be configured before races can be added, and never finish on their own.
    /// </summary>
    public int? NumberOfRounds { get; set; }

    /// <summary>A finished season is read-only: its races can no longer be added, changed or deleted.</summary>
    public bool IsFinished { get; set; }

    public DateTime? FinishedAt { get; set; }

    public ICollection<Race> Races { get; set; } = new List<Race>();
}