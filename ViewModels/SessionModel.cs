using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public class SessionModel
{
    public Guid SessionId { get; set; }

    public SessionType SessionType { get; set; }

    public DateTime SessionDate { get; set; }

    public List<DriverStandingModel> DriverStandings { get; set; } = new();
}