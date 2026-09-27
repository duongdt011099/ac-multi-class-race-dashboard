namespace MulticlassRace.Services.Abstractions;

public interface IAppUpdatePreferenceService
{
    Task<string> GetLastSeenVersionAsync();
    Task<DateTimeOffset?> GetLastCheckedAtAsync();
    Task MarkSeenAsync(string version);
    Task MarkCheckedAsync();
}
