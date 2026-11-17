using MulticlassRace.Models;

namespace MulticlassRace.Services.Races;

public sealed class RaceSessionChangeNotifier
{
    public event Action<Guid, SessionType>? Imported;

    /// <summary>Raised with the id of the stored result when a quick race result has been imported.</summary>
    public event Action<Guid>? QuickRaceImported;

    /// <summary>Raised when a quick race qualifying result has reordered the grid.</summary>
    public event Action<MulticlassRace.ViewModels.QuickRaceQualifyingModel>? QuickQualifyingImported;

    public void NotifyQuickQualifyingImported(MulticlassRace.ViewModels.QuickRaceQualifyingModel result)
    {
        var handlers = QuickQualifyingImported;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<MulticlassRace.ViewModels.QuickRaceQualifyingModel> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(result);
            }
            catch
            {
                // A disconnected UI subscriber must not turn a successful import into a failure.
            }
        }
    }

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

    public void NotifyQuickRaceImported(Guid resultId)
    {
        var handlers = QuickRaceImported;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<Guid> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(resultId);
            }
            catch
            {
                // A disconnected UI subscriber must not turn a successful import into a failure.
            }
        }
    }
}
