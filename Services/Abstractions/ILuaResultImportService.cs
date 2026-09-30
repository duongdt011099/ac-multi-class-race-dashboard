namespace MulticlassRace.Services.Abstractions;

public sealed record LuaResultImportOutcome(bool Success, string Message, bool IsError);

public interface ILuaResultImportService
{
    Task<LuaResultImportOutcome> ImportAsync(PendingLuaResult result, Guid raceId);

    Task RecordDeclinedAsync(PendingLuaResult result);

    /// <summary>
    /// Marks a result file as handled after it was imported through the manual Race controls,
    /// so the auto-import worker does not offer the very same file again on the next scan.
    /// Failures are swallowed because this is bookkeeping, not the import itself.
    /// </summary>
    Task RecordManualImportAsync(string fullPath, string fileName);
}
