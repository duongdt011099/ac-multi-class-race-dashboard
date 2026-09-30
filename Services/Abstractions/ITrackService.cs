namespace MulticlassRace.Services.Abstractions;

public interface ITrackService
{
    Task<IReadOnlyList<string>> GetTrackNamesAsync();

    Task<string?> ResolveTrackNameAsync(string? trackName);

    Task<IReadOnlyList<string>> GetTrackLayoutsAsync(string trackName);

    Task<int?> GetBestLapTimeAsync(string trackName, string? layout);

    Task<int?> GetPitCountAsync(string trackName, string? layout);
}
