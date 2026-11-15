using System.Diagnostics;

namespace MulticlassRace.Services.AssettoCorsa;

public sealed class SteamStatusService : ISteamStatusService
{
    public bool IsRunning()
    {
        var processes = Process.GetProcessesByName("steam");

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
