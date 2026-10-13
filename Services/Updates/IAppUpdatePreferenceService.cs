namespace MulticlassRace.Services.Updates;

public interface IAppUpdatePreferenceService
{
    Task<string> GetLastSeenVersionAsync();
    Task<DateTimeOffset?> GetLastCheckedAtAsync();
    Task MarkSeenAsync(string version);
    Task MarkCheckedAsync();
}
