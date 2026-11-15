namespace MulticlassRace.Services.AssettoCorsa;

/// <summary>Whether Steam is running in the background; Assetto Corsa needs it to start.</summary>
public interface ISteamStatusService
{
    bool IsRunning();
}
