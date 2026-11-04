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
    
    public ICollection<Race> Races { get; set; } = new List<Race>();
}