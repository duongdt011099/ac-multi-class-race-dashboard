using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;

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
    private readonly LuaResultSweeper _sweeper;
    private readonly ResultScanBaseline _baseline;
    private readonly ILogger<LuaResultImportWorker> _logger;

    public LuaResultImportWorker(
        IServiceScopeFactory scopeFactory,
        LuaResultImportState state,
        SessionLaunchTracker launchTracker,
        LuaResultSweeper sweeper,
        ResultScanBaseline baseline,
        ILogger<LuaResultImportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _launchTracker = launchTracker;
        _sweeper = sweeper;
        _baseline = baseline;
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
                        // Expired results are cleared first, here rather than trusting the cleanup
                        // service to have run already: hosted services start in no guaranteed order,
                        // and if the scan ran first it would queue every historical file for the user
                        // to dismiss one at a time.
                        try
                        {
                            await _sweeper.SweepAsync(stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not sweep expired Lua results before scanning.");
                        }

                        await ScanWithIndicatorAsync(stoppingToken);
                    }
                }
                else if (gameWasRunning && !gameIsRunning)
                {
                    await ScanWithIndicatorAsync(stoppingToken, FileSettleTime);
                }

                gameWasRunning = gameIsRunning;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ScanWithIndicatorAsync(
        CancellationToken cancellationToken,
        TimeSpan? settleTime = null)
    {
        _state.SetScanning(true);
        try
        {
            if (settleTime is { } delay)
            {
                // Give the filesystem a moment to flush the file the Lua app just wrote.
                await Task.Delay(delay, cancellationToken);
            }

            await ScanAsync(cancellationToken);
        }
        finally
        {
            _state.SetScanning(false);
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
            var trackService = scope.ServiceProvider.GetRequiredService<ITrackService>();

            var config = await configRepository.GetAsync();
            var root = LuaResultPaths.ResolveRoot(config?.LuaResultPath);

            if (!Directory.Exists(root))
            {
                return;
            }

            var cutoff = await _baseline.GetCutoffUtcAsync(root);
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

                    if (cutoff is { } limit && info.LastWriteTimeUtc <= limit)
                    {
                        // Written before the dashboard was ever in use, so it is history rather than
                        // a session someone is waiting on. Remembered so no later scan reconsiders it.
                        _state.MarkHandled(file);
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
                    var launch = _launchTracker.Current;
                    var canonicalTrack = await trackService.ResolveTrackNameAsync(track);
                    var canonicalLaunchTrack = launch is null
                        ? null
                        : await trackService.ResolveTrackNameAsync(launch.TrackName);
                    var (raceId, raceLabel) = ResolveTarget(
                        sessionType.Value,
                        track,
                        canonicalTrack,
                        launch,
                        canonicalLaunchTrack);

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

    private static (Guid? RaceId, string? Label) ResolveTarget(
        SessionType sessionType,
        string? track,
        string? canonicalTrack,
        SessionLaunch? launch,
        string? canonicalLaunchTrack)
    {
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

        var resultTrackForMatch = string.IsNullOrWhiteSpace(canonicalTrack) ? track : canonicalTrack;
        var launchTrackForMatch = string.IsNullOrWhiteSpace(canonicalLaunchTrack)
            ? launch.TrackName
            : canonicalLaunchTrack;

        if (!string.IsNullOrWhiteSpace(resultTrackForMatch)
            && !string.IsNullOrWhiteSpace(launchTrackForMatch)
            && !string.Equals(resultTrackForMatch, launchTrackForMatch, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var label = string.IsNullOrWhiteSpace(track)
            ? launch.RaceName
            : $"{launch.RaceName} ({track.Trim()})";

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

internal static bool TryParseSessionDate(string fileName, out DateTime date)
    {
        return DateTime.TryParseExact(
            Path.GetFileNameWithoutExtension(fileName),
            LuaResultPaths.FileNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
