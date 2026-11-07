using MulticlassRace.Models;

namespace MulticlassRace.Services.Championships;

public static class SeasonRules
{
    public const string FinishedMessage = "This season is finished, so its races can no longer be changed.";

    /// <summary>Throws when the season is over: a finished season is read-only.</summary>
    public static void EnsureOpen(Season season)
    {
        if (season.IsFinished)
        {
            throw new InvalidOperationException(FinishedMessage);
        }
    }
}
