using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

/// <summary>Tells the open UI that a season's last round was just finished, whichever way the result arrived.</summary>
public sealed class SeasonCompletionNotifier
{
    public event Action<SeasonCompletion>? Completed;

    public void NotifyCompleted(SeasonCompletion completion)
    {
        var handlers = Completed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<SeasonCompletion> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(completion);
            }
            catch
            {
                // A disconnected UI subscriber must not turn a successful import into a failure.
            }
        }
    }
}
