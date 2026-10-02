using System.Globalization;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Services;

/// <summary>
/// Moves expired Lua session results out of the way.
///
/// The Lua app writes one JSON file per session into practice, qualifying and race folders next to
/// itself and never removes them, so a user who races regularly accumulates thousands. Anything past
/// the retention window is history the dashboard no longer offers for import, so it is moved rather
/// than deleted: a wrong age test costs nothing but an empty folder the user can move files back out of.
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

    private const string QuarantineFolderName = ".deleted";

    private static readonly string[] SessionFolderNames =
        [LuaResultPaths.PracticeFolder, LuaResultPaths.QualifyingFolder, LuaResultPaths.RaceFolder];

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var root = await ResolveRootAsync();

        if (root is null || Directory.Exists(root) is false)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow - Retention;
        var moved = 0;
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

                if (TryQuarantine(file, cutoff) is false)
                {
                    failed++;
                    continue;
                }

                moved++;
            }
        }

        if (moved > 0 || failed > 0)
        {
            logger.LogInformation(
                "Swept Lua results older than {Retention} from {Root}: {Moved} moved, {Failed} failed.",
                Retention,
                root,
                moved,
                failed);
        }

        return moved;
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

    private static bool TryQuarantine(string file, DateTime cutoff)
    {
        // Dated by the cutoff the file expired against rather than by now, so a sweep that runs
        // after the app has been shut down for a week files everything under the right day.
        var destination = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(file)) ?? string.Empty,
            QuarantineFolderName,
            cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Path.GetFileName(file));

        try
        {
            var folder = Path.GetDirectoryName(destination);

            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
            }

            File.Move(file, destination);
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

/// <summary>
/// Runs <see cref="LuaResultSweeper"/> at startup and hourly thereafter. Startup covers results left
/// behind while the dashboard was not running, which is when they accumulate unnoticed.
/// </summary>
internal sealed class LuaResultCleanupService(
    LuaResultSweeper sweeper,
    ILogger<LuaResultCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LuaResultSweeper.PollInterval);

        do
        {
            try
            {
                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The Lua result sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
