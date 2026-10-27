using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.BackgroundServices;

namespace MulticlassRace.Services.Results;

/// <summary>
/// Deletes expired Lua session results.
///
/// The Lua app writes one JSON file per session into practice, qualifying and race folders next to
/// itself and never removes them, so a user who races regularly accumulates thousands. Anything past
/// the retention window is history the dashboard no longer offers for import, so it is deleted
/// permanently from disk.
///
/// Only the three session folders are touched, and only files sitting directly in them. Content
/// Manager results use the same yyMMdd-HHmmss.json naming but live directly in their own sessions
/// root, so this cannot reach them even when the configured path is pointed somewhere surprising.
/// </summary>
public sealed class LuaResultSweeper(
    IServiceScopeFactory scopeFactory,
    LuaResultImportState state,
    ILogger<LuaResultSweeper> logger)
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    /// <summary>Results older than this are no longer worth offering for import.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    // Older builds moved expired results here instead of deleting them.
    private const string LegacyQuarantineFolderName = ".deleted";

    private static readonly string[] SessionFolderNames =
        [LuaResultPaths.PracticeFolder, LuaResultPaths.QualifyingFolder, LuaResultPaths.RaceFolder];

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var root = await ResolveRootAsync();

        if (root is null || Directory.Exists(root) is false)
        {
            return 0;
        }

        TryDeleteLegacyQuarantine(root);

        var cutoff = DateTime.UtcNow - Retention;
        var deleted = 0;
        var failed = 0;

        foreach (var folderName in SessionFolderNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folder = Path.Combine(root, folderName);

            if (Directory.Exists(folder) is false)
            {
                continue;
            }

            foreach (var file in SafeEnumerate(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsExpired(file, cutoff) is false)
                {
                    continue;
                }

                // A result already queued for, or answered in, the import prompt is skipped: the
                // sweep and the prompt must not disagree about which files still exist.
                if (state.IsKnown(file))
                {
                    continue;
                }

                if (TryDelete(file) is false)
                {
                    failed++;
                    continue;
                }

                deleted++;
            }
        }

        if (deleted > 0 || failed > 0)
        {
            logger.LogInformation(
                "Swept Lua results older than {Retention} from {Root}: {Moved} deleted, {Failed} failed.",
                Retention,
                root,
                deleted,
                failed);
        }

        return deleted;
    }

    private async Task<string?> ResolveRootAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var configRepository = scope.ServiceProvider.GetRequiredService<IAssettoCorsaGameConfigRepository>();
            var config = await configRepository.GetAsync();

            return LuaResultPaths.ResolveRoot(config?.LuaResultPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve the Lua result folder to sweep.");
            return null;
        }
    }

    /// <summary>
    /// Expired only when both the session date in the file name and the last write are past the
    /// cutoff. A session that started yesterday and is still being written today is not expired, and
    /// requiring the write time as well keeps a file that was merely copied around recently.
    /// </summary>
    private static bool IsExpired(string file, DateTime cutoff)
    {
        if (LuaResultImportWorker.TryParseSessionDate(file, out var sessionDate) is false)
        {
            return false;
        }

        if (sessionDate.ToUniversalTime() > cutoff)
        {
            return false;
        }

        var lastWrite = File.GetLastWriteTimeUtc(file);

        return lastWrite <= cutoff;
    }

    private void TryDeleteLegacyQuarantine(string root)
    {
        var folder = Path.Combine(root, LegacyQuarantineFolderName);

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
                logger.LogInformation("Removed the legacy {Folder} folder.", folder);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove the legacy {Folder} folder.", folder);
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch
        {
            // One unreadable or locked file must not abandon the rest of the sweep.
            return false;
        }
    }

    private static string[] SafeEnumerate(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "*" + LuaResultPaths.FileExtension);
        }
        catch
        {
            return [];
        }
    }
}
