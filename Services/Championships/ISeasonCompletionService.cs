using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

/// <summary>Awards trophies for finished races and closes a season once all of its rounds are done.</summary>
public interface ISeasonCompletionService
{
    /// <summary>
    /// Call after a race got its result. Records (or withdraws) the human driver's race win and finishes the season
    /// when every round is done. Returns the final table when this call finished the season, otherwise null.
    /// </summary>
    Task<SeasonCompletion?> OnRaceFinishedAsync(Guid raceId);

    /// <summary>Finishes the season if every round is already finished. Returns the final table when it did.</summary>
    Task<SeasonCompletion?> TryFinishSeasonAsync(Guid seasonId);
}
