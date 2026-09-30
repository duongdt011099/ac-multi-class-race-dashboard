using System.Globalization;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class LuaResultImportService : ILuaResultImportService
{
    private readonly IRaceService _raceService;
    private readonly IImportedLuaResultRepository _importedRepository;
    private readonly RaceSessionChangeNotifier _sessionChangeNotifier;
    private readonly ILogger<LuaResultImportService> Logger;

    public LuaResultImportService(
        IRaceService raceService,
        IImportedLuaResultRepository importedRepository,
        RaceSessionChangeNotifier sessionChangeNotifier,
        ILogger<LuaResultImportService> logger)
    {
        _raceService = raceService;
        _importedRepository = importedRepository;
        _sessionChangeNotifier = sessionChangeNotifier;
        Logger = logger;
    }

    public async Task<LuaResultImportOutcome> ImportAsync(PendingLuaResult result, Guid raceId)
    {
        try
        {
            RaceResultImportResult importResult;

            if (result.SessionType == SessionType.Race)
            {
                importResult = await _raceService.ImportRaceResultFromPathAsync(
                    raceId,
                    result.FullPath,
                    result.FileName);
            }
            else
            {
                await using var stream = File.OpenRead(result.FullPath);
                importResult = await _raceService.ImportSessionResultAsync(
                    raceId,
                    result.SessionType,
                    stream,
                    result.FileName);
            }

            _sessionChangeNotifier.NotifyImported(raceId, result.SessionType);
            await UpsertAsync(result, imported: true, importCount: 1, error: null);

            return new LuaResultImportOutcome(
                true,
                $"Imported {importResult.Imported} result(s), skipped {importResult.Skipped}.",
                importResult.Skipped > 0);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to import Lua result {FileName} into race {RaceId}.",
                result.FileName,
                raceId);

            // Recorded so the same broken file is not offered again on every restart.
            // It stays available through the manual import controls. The bookkeeping must
            // never mask the real import error, so a failure here is logged, not rethrown.
            try
            {
                await UpsertAsync(result, imported: false, importCount: 0, error: ex.Message);
            }
            catch (Exception recordEx)
            {
                Logger.LogWarning(
                    recordEx,
                    "Could not record the import attempt for {FileName}; it may be offered again.",
                    result.FileName);
            }

            return new LuaResultImportOutcome(false, ex.Message, true);
        }
    }

    public Task RecordDeclinedAsync(PendingLuaResult result)
    {
        return UpsertAsync(result, imported: false, importCount: 0, error: null);
    }

    public async Task RecordManualImportAsync(string fullPath, string fileName)
    {
        // Only Lua-produced files are tracked. Their immediate parent is always one of the
        // three session folders, whereas a Content Manager result sits directly in the AC
        // results folder, so this excludes CM files without needing the configured root.
        var sessionType = LuaResultPaths.GetSessionType(
            Path.GetFileName(Path.GetDirectoryName(fullPath)));

        if (sessionType is null)
        {
            return;
        }

        var sessionDate = DateTime.TryParseExact(
            Path.GetFileNameWithoutExtension(fileName),
            LuaResultPaths.FileNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : DateTime.MinValue;

        try
        {
            await UpsertAsync(
                new PendingLuaResult(
                    fullPath,
                    fileName,
                    sessionType.Value,
                    sessionDate,
                    null,
                    null,
                    null),
                imported: true,
                importCount: 1,
                error: null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Imported {FileName} manually but could not record it.", fileName);
        }
    }

    private async Task UpsertAsync(
        PendingLuaResult result,
        bool imported,
        int importCount,
        string? error)
    {
        var existing = await _importedRepository.FindByFullPathAsync(result.FullPath);
        var isNew = existing is null;

        existing ??= new ImportedLuaResult
        {
            ImportedLuaResultId = Guid.NewGuid(),
        };

        // Every field is assigned before the single write so a new row is never persisted
        // in a half-built state.
        existing.FileName = result.FileName;
        existing.FullPath = result.FullPath;
        existing.SessionType = result.SessionType;
        existing.SessionDate = result.SessionDate;
        existing.Imported = imported;
        existing.ImportCount += importCount;
        existing.ImportedAtUtc = imported ? DateTime.UtcNow : null;
        existing.LastError = error;

        if (isNew)
        {
            await _importedRepository.AddAsync(existing);
        }
        else
        {
            await _importedRepository.UpdateAsync(existing);
        }
    }
}
