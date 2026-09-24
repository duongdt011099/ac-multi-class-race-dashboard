namespace MulticlassRace.Models;

public class Season
{
    public Guid SeasonId { get; set; }

    public required string SeasonName { get; set; }

    public required Championship Championship { get; set; }
    
    public ICollection<Race> Races { get; set; } = new List<Race>();
}