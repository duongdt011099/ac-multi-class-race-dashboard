namespace MulticlassRace.Services.AssettoCorsa;

public interface IGameSessionLauncher
{
    /// <param name="sessionManifest">Content of the session file the Lua app reads; written next to race.ini.</param>
    Task LaunchAsync(string gamePath, string executablePath, string raceIni, string? sessionManifest = null);
}
