using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Services;

/// <summary>
/// Watches Assetto Corsa and the Lua app's result folder. When acs.exe stops running, any result
/// files the Lua app finished writing are queued for import confirmation.
/// </summary>
public class LuaResultImportWorker : BackgroundService
{
    private static readonly string[] GameProcessNames = ["acs", "AssettoCorsa"];

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Ignore files younger than this so a partially flushed write is never read.</summary>
    private static readonly TimeSpan FileSettleTime = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LuaResultImportState _state;
    private readonly SessionLaunchTracker _launchTracker;
    private readonly ILogger<LuaResultImportWorker> _logger;

    public LuaResultImportWorker(
        IServiceScopeFactory scopeFactory,
        LuaResultImportState state,
        SessionLaunchTracker launchTracker,
        ILogger<LuaResultImportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _launchTracker = launchTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gameWasRunning = IsGameRunning();
        var didInitialScan = false;

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var gameIsRunning = IsGameRunning();

                // Scan once at startup so results produced while the dashboard was closed
                // are still offered.
                if (!didInitialScan)
                {
                    didInitialScan = true;

                    if (!gameIsRunning)
                    {
                        await ScanAsync(stoppingToken);
                    }
                }
                else if (gameWasRunning && !gameIsRunning)
                {
                    // Give the filesystem a moment to flush the file the Lua app just wrote.
                    await Task.Delay(FileSettleTime, stoppingToken);
                    await ScanAsync(stoppingToken);
                }

                gameWasRunning = gameIsRunning;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);

                try
                {
                    if (processes.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    // Enumerating processes opens handles that are only released by Dispose.
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A process can exit between the query and the enumeration; treat as not running.
            }
        }

        return false;
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configRepository = scope.ServiceProvider.GetRequiredService<IAssettoCorsaGameConfigRepository>();
            var importedRepository = scope.ServiceProvider.GetRequiredService<IImportedLuaResultRepository>();

            var config = await configRepository.GetAsync();
            var root = LuaResultPaths.ResolveRoot(config?.LuaResultPath);

            if (!Directory.Exists(root))
            {
                return;
            }

            var now = DateTime.UtcNow;

            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var sessionType = LuaResultPaths.GetSessionType(Path.GetFileName(folder));

                if (sessionType is null)
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*" + LuaResultPaths.FileExtension))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(file);

                    if (!TryParseSessionDate(fileName, out var sessionDate))
                    {
                        continue;
                    }

                    if (_state.IsKnown(file))
                    {
                        continue;
                    }

                    var info = new FileInfo(file);

                    if (!info.Exists || now - info.LastWriteTimeUtc < FileSettleTime)
                    {
                        continue;
                    }

                    var alreadySeen = await importedRepository.FindByFullPathAsync(file);

                    if (alreadySeen is not null)
                    {
                        // Already imported or previously declined: remember it, never prompt again.
                        _state.MarkHandled(file);
                        continue;
                    }

                    var track = await ReadTrackAsync(file);
                    var (raceId, raceLabel) = ResolveTarget(sessionType.Value, track);

                    _state.Enqueue(new PendingLuaResult(
                        file,
                        fileName,
                        sessionType.Value,
                        sessionDate,
                        track,
                        raceId,
                        raceLabel));

                    _logger.LogInformation(
                        "Detected Lua {SessionType} result {FileName} for race {RaceId}.",
                        sessionType.Value,
                        fileName,
                        raceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan the Lua result folder.");
        }
    }

    private (Guid? RaceId, string? Label) ResolveTarget(SessionType sessionType, string? track)
    {
        var launch = _launchTracker.Current;

        if (launch is null)
        {
            return (null, null);
        }

        // The recorded launch is only ever a hint, so it is discarded whenever the result
        // file contradicts it. A session-type mismatch is always a different session, and
        // practice/qualifying files carry no track, so that check must not be conditional
        // on the track being known. A known, different track means importing into the wrong
        // race, so fall back to the picker rather than guessing.
        if (launch.SessionType != sessionType)
        {
            return (null, null);
        }

        if (!string.IsNullOrWhiteSpace(track)
            && !string.IsNullOrWhiteSpace(launch.TrackName)
            && !string.Equals(track, launch.TrackName, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var label = string.IsNullOrWhiteSpace(launch.TrackName) || string.IsNullOrWhiteSpace(track)
            ? launch.RaceName
            : $"{launch.RaceName} ({launch.TrackName})";

        return (launch.RaceId, label);
    }

    private static async Task<string?> ReadTrackAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.TryGetProperty("track", out var track)
                ? track.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseSessionDate(string fileName, out DateTime date)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        return DateTime.TryParseExact(
            name,
            LuaResultPaths.FileNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
