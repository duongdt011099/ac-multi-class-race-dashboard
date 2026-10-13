namespace MulticlassRace.Services.AssettoCorsa;

public interface IGameSessionLauncher
{
    Task LaunchAsync(string gamePath, string executablePath, string raceIni);
}
