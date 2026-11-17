using System.Diagnostics;
using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.QuickRace;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;

namespace MulticlassRace.BackgroundServices;

/// <summary>
/// Watches Assetto Corsa and the Lua app's result folder. When acs.exe stops running, the result files the
/// Lua app finished writing are imported straight away: quick race results into the quick race history,
/// everything else into the race the session was started for. Nobody is asked; the outcome is reported
/// as a message. When a session was started from the dashboard but no result turns up, that is reported too.
/// </summary>
public class LuaResultImportWorker : BackgroundService
{
    private static readonly string[] GameProcessNames = ["acs", "AssettoCorsa"];

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Ignore files younger than this so a partially flushed write is never read.</summary>
    private static readonly TimeSpan FileSettleTime = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait before looking once more for a result that was not there at first.</summary>
    private static readonly TimeSpan MissingResultRetryDelay = TimeSpan.FromSeconds(6);

    internal const string MissingResultMessage = "Could not find the session result. Please import it manually.";

    // Quick Race has no manual import, so its message does not offer one.
    internal const string MissingQuickRaceResultMessage = "Could not find the session result.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LuaResultImportState _state;
    private readonly SessionLaunchTracker _launchTracker;
    private readonly LuaResultSweeper _sweeper;
    private readonly ResultScanBaseline _baseline;
    private readonly ILogger<LuaResultImportWorker> _logger;

    /// <summary>The launch whose result was last looked for, so one launch is only ever reported once.</summary>
    private DateTimeOffset _checkedLaunchAt = DateTimeOffset.MinValue;

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

        // The baseline must exist before any session can finish. Drawn lazily on the first scan it would
        // land after a result written while the game was running (dashboard-launched games skip the
        // startup scan), and that result would be discarded as history.
        await EnsureBaselineAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var gameIsRunning = IsGameRunning();

                // Scan once at startup so results produced while the dashboard was closed are still imported.
                if (!didInitialScan)
                {
                    didInitialScan = true;

                    if (!gameIsRunning)
                    {
                        // Expired results are cleared first, here rather than trusting the cleanup
                        // service to have run already: hosted services start in no guaranteed order.
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

                        await ScanWithIndicatorAsync(stoppingToken, reportMissingResult: false);
                    }
                }
                else if (gameWasRunning && !gameIsRunning)
                {
                    await ScanWithIndicatorAsync(stoppingToken, FileSettleTime, reportMissingResult: true);
                }

                gameWasRunning = gameIsRunning;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EnsureBaselineAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configRepository = scope.ServiceProvider.GetRequiredService<IAssettoCorsaGameConfigRepository>();
            var config = await configRepository.GetAsync();

            await _baseline.GetCutoffUtcAsync(LuaResultPaths.ResolveRoot(config?.LuaResultPath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not establish the Lua result baseline at startup.");
        }
    }

    private async Task ScanWithIndicatorAsync(
        CancellationToken cancellationToken,
        TimeSpan? settleTime = null,
        bool reportMissingResult = false)
    {
        _state.SetScanning(true);
        try
        {
            if (settleTime is { } delay)
            {
                // Give the filesystem a moment to flush the file the Lua app just wrote.
                await Task.Delay(delay, cancellationToken);
            }

            var handled = await ScanAsync(cancellationToken);

            if (!reportMissingResult)
            {
                return;
            }

            var launch = _launchTracker.Current;

            if (launch is null || launch.LaunchedAt <= _checkedLaunchAt)
            {
                return;
            }

            if (handled == 0)
            {
                // The game can be a moment slower than its process: look once more before giving up.
                await Task.Delay(MissingResultRetryDelay, cancellationToken);
                handled = await ScanAsync(cancellationToken);
            }

            _checkedLaunchAt = launch.LaunchedAt;

            if (handled == 0)
            {
                _logger.LogWarning("The game was closed but no result was found for the launched session.");
                _state.Report(new LuaResultImportOutcome(
                    false,
                    launch.IsQuickRace ? MissingQuickRaceResultMessage : MissingResultMessage,
                    true));
            }
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

    /// <summary>Imports every new result file and returns how many files were dealt with (imported or failed).</summary>
    private async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var handled = 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configRepository = scope.ServiceProvider.GetRequiredService<IAssettoCorsaGameConfigRepository>();
            var importedRepository = scope.ServiceProvider.GetRequiredService<IImportedLuaResultRepository>();

            var config = await configRepository.GetAsync();
            var root = LuaResultPaths.ResolveRoot(config?.LuaResultPath);

            if (!Directory.Exists(root))
            {
                return 0;
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

                    if (!LuaResultPaths.TryParseFileName(fileName, out var sessionDate, out var isQuickRace))
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
                        // Already imported (or failed once): never touched again.
                        _state.MarkHandled(file);
                        continue;
                    }

                    if (!_state.MarkHandled(file))
                    {
                        continue;
                    }

                    handled++;

                    await ImportAsync(scope.ServiceProvider, file, fileName, sessionType.Value, sessionDate, isQuickRace);
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

        return handled;
    }

    private async Task ImportAsync(
        IServiceProvider services,
        string file,
        string fileName,
        SessionType sessionType,
        DateTime sessionDate,
        bool isQuickRace)
    {
        var importService = services.GetRequiredService<ILuaResultImportService>();
        var track = await ReadTrackAsync(file);
        var pending = new PendingLuaResult(file, fileName, sessionType, sessionDate, track, null, null);
        var launch = _launchTracker.Current;

        // An older Lua app does not prefix its file names: the launch tells which race it was.
        var quickRace = isQuickRace || (launch is { IsQuickRace: true } && launch.SessionType == sessionType);

        try
        {
            if (quickRace)
            {
                var quickRaceService = services.GetRequiredService<IQuickRaceService>();

                if (sessionType == SessionType.Qualifying)
                {
                    var qualifying = await quickRaceService.ImportQualifyingFromPathAsync(file, fileName);

                    _logger.LogInformation("Imported quick race qualifying result {FileName}.", fileName);
                    _state.Report(new LuaResultImportOutcome(
                        true,
                        $"Qualifying done ({qualifying.Rows.Count} cars). The grid follows the qualifying result.",
                        false));

                    return;
                }

                if (sessionType != SessionType.Race)
                {
                    throw new InvalidOperationException("Quick race only keeps qualifying and race results.");
                }
                var result = await quickRaceService.ImportResultFromPathAsync(file, fileName);

                _logger.LogInformation("Imported quick race result {FileName}.", fileName);
                _state.Report(new LuaResultImportOutcome(
                    true,
                    $"Quick race result imported ({result.Rows.Count} drivers).",
                    false));

                return;
            }

            var raceId = await ResolveRaceAsync(services, pending, launch);

            if (raceId is null)
            {
                // Remembered as seen, so the same file is not reported again on every start. It can still be
                // imported by hand: the manual import lists result files, not this bookkeeping.
                await importService.RecordDeclinedAsync(pending);

                _logger.LogWarning("Lua {SessionType} result {FileName} matches no race.", sessionType, fileName);
                _state.Report(new LuaResultImportOutcome(
                    false,
                    $"A {LuaResultPaths.GetSessionTypeLabel(sessionType).ToLowerInvariant()} result was saved, but the race it belongs to could not be found. Please import it manually.",
                    true));

                return;
            }

            var outcome = await importService.ImportAsync(pending, raceId.Value);

            _logger.LogInformation(
                "Imported Lua {SessionType} result {FileName} into race {RaceId}.",
                sessionType,
                fileName,
                raceId);
            _state.Report(outcome with
            {
                Message = $"{LuaResultPaths.GetSessionTypeLabel(sessionType)} result: {outcome.Message}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not import Lua result {FileName}.", fileName);

            try
            {
                await importService.RecordDeclinedAsync(pending);
            }
            catch (Exception recordEx)
            {
                _logger.LogWarning(recordEx, "Could not record the failed import of {FileName}.", fileName);
            }

            _state.Report(new LuaResultImportOutcome(false, $"Could not import the result: {ex.Message}", true));
        }
    }

    /// <summary>
    /// The race a championship result belongs to: the one the session was started for, or, when the game was
    /// started some other way, the only race that is waiting for a result on this track.
    /// </summary>
    private async Task<Guid?> ResolveRaceAsync(IServiceProvider services, PendingLuaResult pending, SessionLaunch? launch)
    {
        var trackService = services.GetRequiredService<ITrackService>();
        var canonicalTrack = await trackService.ResolveTrackNameAsync(pending.Track);
        var canonicalLaunchTrack = launch is null
            ? null
            : await trackService.ResolveTrackNameAsync(launch.TrackName);

        var fromLaunch = ResolveTarget(pending.SessionType, pending.Track, canonicalTrack, launch, canonicalLaunchTrack);

        if (fromLaunch is not null)
        {
            return fromLaunch;
        }

        var candidates = (await services.GetRequiredService<IRaceService>()
            .GetCandidateRacesAsync(pending.Track, pending.SessionType)).ToList();

        return candidates.Count == 1 ? candidates[0].RaceId : null;
    }

    private static Guid? ResolveTarget(
        SessionType sessionType,
        string? track,
        string? canonicalTrack,
        SessionLaunch? launch,
        string? canonicalLaunchTrack)
    {
        if (launch is null || launch.IsQuickRace)
        {
            return null;
        }

        // The recorded launch is only ever a hint, so it is discarded whenever the result
        // file contradicts it. A session-type mismatch is always a different session, and
        // practice/qualifying files carry no track, so that check must not be conditional
        // on the track being known. A known, different track means importing into the wrong
        // race.
        if (launch.SessionType != sessionType)
        {
            return null;
        }

        var resultTrackForMatch = string.IsNullOrWhiteSpace(canonicalTrack) ? track : canonicalTrack;
        var launchTrackForMatch = string.IsNullOrWhiteSpace(canonicalLaunchTrack)
            ? launch.TrackName
            : canonicalLaunchTrack;

        if (!string.IsNullOrWhiteSpace(resultTrackForMatch)
            && !string.IsNullOrWhiteSpace(launchTrackForMatch)
            && !string.Equals(resultTrackForMatch, launchTrackForMatch, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return launch.RaceId;
    }

    private static async Task<string?> ReadTrackAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var document = await JsonDocument.ParseAsync(stream);

            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("track", out var track)
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
        return LuaResultPaths.TryParseFileName(fileName, out date, out _);
    }
}
