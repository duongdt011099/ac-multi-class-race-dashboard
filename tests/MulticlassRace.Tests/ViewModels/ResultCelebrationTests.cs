using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.ViewModels;

public class ResultCelebrationTests
{
    private static SessionModel Session(SessionType type, int humanClassPosition) => new()
    {
        SessionType = type,
        DriverStandings =
        {
            new DriverStandingModel { DriverName = "A", TeamName = "T1", TeamClassName = "GT3", ClassPosition = humanClassPosition == 1 ? 2 : 1 },
            new DriverStandingModel { DriverName = "Me", TeamName = "T2", TeamClassName = "GT3", ClassPosition = humanClassPosition, IsHuman = true }
        }
    };

    [Theory]
    [InlineData(SessionType.Practice, 1, CelebrationKind.Pole)]
    [InlineData(SessionType.Qualifying, 1, CelebrationKind.Pole)]
    [InlineData(SessionType.Race, 1, CelebrationKind.Podium)]
    [InlineData(SessionType.Race, 3, CelebrationKind.Podium)]
    public void Evaluate_CelebratesPoleAndPodium(SessionType type, int position, CelebrationKind expected)
    {
        Assert.Equal(expected, ResultCelebration.Evaluate(Session(type, position))?.Kind);
    }

    [Theory]
    [InlineData(SessionType.Qualifying, 2)]
    [InlineData(SessionType.Race, 4)]
    public void Evaluate_ReturnsNullOffThePodium(SessionType type, int position)
    {
        Assert.Null(ResultCelebration.Evaluate(Session(type, position)));
    }

    [Fact]
    public void Evaluate_ReturnsNullWithoutHumanDriver()
    {
        var session = Session(SessionType.Race, 1);
        session.DriverStandings.ForEach(d => d.IsHuman = false);

        Assert.Null(ResultCelebration.Evaluate(session));
    }
}
