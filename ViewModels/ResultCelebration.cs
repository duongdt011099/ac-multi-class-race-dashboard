using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public enum CelebrationKind
{
    Pole,
    Podium
}

/// <summary>What to celebrate after a result import: the human driver's class position and the top three of that class.</summary>
public sealed record ResultCelebration(
    CelebrationKind Kind,
    SessionType SessionType,
    DriverStandingModel Driver,
    IReadOnlyList<DriverStandingModel> Podium)
{
    public string Message => Kind == CelebrationKind.Pole
        ? "Congrats, you are Pole Position!"
        : "Congrats, you are on the podium!";

    /// <summary>
    /// Practice and qualifying celebrate the top spot in the human driver's class; a race celebrates any top-three finish.
    /// Returns null when the human driver is not in the session or finished off the podium.
    /// </summary>
    public static ResultCelebration? Evaluate(SessionModel? session)
    {
        var human = session?.DriverStandings.FirstOrDefault(d => d.IsHuman);

        if (session is null || human is null || human.ClassPosition < 1)
        {
            return null;
        }

        var kind = session.SessionType == SessionType.Race ? CelebrationKind.Podium : CelebrationKind.Pole;
        var limit = kind == CelebrationKind.Pole ? 1 : 3;

        if (human.ClassPosition > limit)
        {
            return null;
        }

        var podium = session.DriverStandings
            .Where(d => string.Equals(d.TeamClassName, human.TeamClassName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.ClassPosition)
            .Take(3)
            .ToList();

        return new ResultCelebration(kind, session.SessionType, human, podium);
    }
}
