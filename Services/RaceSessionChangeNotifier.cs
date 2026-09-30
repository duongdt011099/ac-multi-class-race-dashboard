using MulticlassRace.Models;

namespace MulticlassRace.Services;

public sealed class RaceSessionChangeNotifier
{
    public event Action<Guid, SessionType>? Imported;

    public void NotifyImported(Guid raceId, SessionType sessionType)
    {
        var handlers = Imported;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<Guid, SessionType> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(raceId, sessionType);
            }
            catch
            {
                // A disconnected UI subscriber must not turn a successful import into a failure.
            }
        }
    }
}
