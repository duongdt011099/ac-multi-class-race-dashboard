namespace MulticlassRace.Services.Abstractions;

public interface IGameSessionLauncher
{
    Task LaunchAsync(string gamePath, string executablePath, string raceIni);
}
