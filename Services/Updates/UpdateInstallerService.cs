using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using MulticlassRace.Configuration;
using MulticlassRace.Infrastructure;

namespace MulticlassRace.Services.Updates;

public enum UpdateInstallPhase
{
    Idle,
    Downloading,
    Ready,
    Failed
}

/// <summary>The machine-facing side of an update, split out so the rules around it can be tested.</summary>
public interface IUpdateInstallerEnvironment
{
    bool IsGameRunning();

    /// <summary>An older version installed a Windows service that a per-user silent upgrade cannot remove.</summary>
    bool HasLegacyService();

    void Launch(string installerPath, string arguments);
}

public sealed class WindowsUpdateInstallerEnvironment : IUpdateInstallerEnvironment
{
    private static readonly string[] GameProcessNames = ["acs", "AssettoCorsa"];

    public bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            var found = processes.Length > 0;

            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasLegacyService()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EnduranceRace");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Launch(string installerPath, string arguments)
    {
        Process.Start(new ProcessStartInfo(installerPath, arguments) { UseShellExecute = true })?.Dispose();
    }
}

/// <summary>
/// Downloads the installer of a newer release, proves it is the file GitHub published, and hands it to
/// the OS. The installer does the real work (app files, the Lua app in the game folder, install.json),
/// so updating is just running it silently over the existing install and letting it start the app again.
/// </summary>
public sealed class UpdateInstallerService : IHostedService, IDisposable
{
    public const string HttpClientName = "UpdateDownload";

    private const string GitHubHost = "github.com";
    private const int ProgressStepPercent = 2;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<UpdateCheckOptions> _options;
    private readonly UpdateStateService _stateService;
    private readonly AppVersionProvider _versionProvider;
    private readonly IUpdateInstallerEnvironment _environment;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<UpdateInstallerService> _logger;
    private readonly string _workRoot;
    private readonly string _installDirectory;
    private readonly string? _gamePath;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();

    private UpdateInstallPhase _phase;
    private string? _tag;
    private string? _installerPath;
    private string? _error;
    private long _received;
    private long _total;

    public UpdateInstallerService(
        IHttpClientFactory httpClientFactory,
        IOptions<UpdateCheckOptions> options,
        UpdateStateService stateService,
        AppVersionProvider versionProvider,
        IUpdateInstallerEnvironment environment,
        IHostApplicationLifetime lifetime,
        ILogger<UpdateInstallerService> logger,
        string? workRoot = null,
        string? installDirectory = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _stateService = stateService;
        _versionProvider = versionProvider;
        _environment = environment;
        _lifetime = lifetime;
        _logger = logger;
        _workRoot = workRoot ?? Path.Combine(Path.GetTempPath(), "EnduranceRaceDashboardUpdate");
        _installDirectory = installDirectory ?? AppContext.BaseDirectory;
        _gamePath = installDirectory is null ? InstallPaths.GamePath : null;
    }

    public event Action? Changed;

    public UpdateInstallPhase Phase
    {
        get { lock (_gate) { return _phase; } }
    }

    /// <summary>The release tag the current phase is about.</summary>
    public string? Tag
    {
        get { lock (_gate) { return _tag; } }
    }

    public string? Error
    {
        get { lock (_gate) { return _error; } }
    }

    public int ProgressPercent
    {
        get
        {
            lock (_gate)
            {
                return _total <= 0 ? 0 : (int)Math.Min(100, _received * 100 / _total);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stateService.AvailabilityChanged += OnAvailabilityChanged;

        // The first check can finish before this service starts.
        OnAvailabilityChanged();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stateService.AvailabilityChanged -= OnAvailabilityChanged;
        _shutdown.Cancel();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stateService.AvailabilityChanged -= OnAvailabilityChanged;
        _shutdown.Dispose();
        _downloadLock.Dispose();
    }

    private void OnAvailabilityChanged()
    {
        var current = _stateService.Current;

        if (!_options.Value.AutoDownload || current is not { IsUpdateAvailable: true, Installer: not null })
        {
            return;
        }

        _ = Task.Run(() => DownloadAsync(current, _shutdown.Token));
    }

    /// <summary>
    /// Fetches and verifies the installer for <paramref name="update"/>. Failures are reported through
    /// <see cref="Phase"/> and <see cref="Error"/> rather than thrown, so the manual download link stays usable.
    /// </summary>
    public async Task DownloadAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        if (update is not { IsUpdateAvailable: true, Installer: { } asset, TagName: { } tag })
        {
            return;
        }

        if (!await _downloadLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (string.Equals(_tag, tag, StringComparison.OrdinalIgnoreCase)
                    && _phase is UpdateInstallPhase.Ready or UpdateInstallPhase.Downloading)
                {
                    return;
                }

                _phase = UpdateInstallPhase.Downloading;
                _tag = tag;
                _installerPath = null;
                _error = null;
                _received = 0;
                _total = asset.SizeInBytes;
            }

            Raise();

            var problem = ValidateAsset(asset, _options.Value);
            if (problem is not null)
            {
                Fail(problem);
                return;
            }

            try
            {
                var path = await DownloadVerifiedAsync(asset, tag, cancellationToken);

                lock (_gate)
                {
                    _phase = UpdateInstallPhase.Ready;
                    _installerPath = path;
                }

                Raise();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_gate)
                {
                    _phase = UpdateInstallPhase.Idle;
                }

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Downloading update {Tag} failed.", tag);
                Fail(ex is InvalidDataException ? ex.Message : "The update could not be downloaded.");
                return;
            }

            if (_options.Value.AutoInstall)
            {
                TryInstall(out _);
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    /// Starts the downloaded installer and shuts the app down so it can replace the files.
    /// Returns false with a reason when it would not be safe or possible.
    /// </summary>
    public bool TryInstall(out string? error)
    {
        string path;

        lock (_gate)
        {
            if (_phase != UpdateInstallPhase.Ready || _installerPath is null || !File.Exists(_installerPath))
            {
                error = "The update has not been downloaded yet.";
                return false;
            }

            path = _installerPath;
        }

        if (_environment.IsGameRunning())
        {
            error = "Close Assetto Corsa first, then restart to update.";
            return false;
        }

        if (_environment.HasLegacyService())
        {
            error = "An older Windows service is still installed. Use View Release and run the setup, choosing \"Install for all users\".";
            return false;
        }

        try
        {
            _environment.Launch(path, BuildInstallerArguments(_installDirectory, _gamePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start the update installer.");
            error = "The installer could not be started.";
            return false;
        }

        _logger.LogInformation("Update installer started; shutting down for the update.");
        error = null;
        _lifetime.StopApplication();

        return true;
    }

    internal static string? ValidateAsset(ReleaseAsset asset, UpdateCheckOptions options)
    {
        if (string.IsNullOrWhiteSpace(asset.Sha256))
        {
            return "This release does not publish a checksum, so it cannot be verified. Use the manual download.";
        }

        if (!asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(asset.Name), asset.Name, StringComparison.Ordinal))
        {
            return "The release installer has an unexpected file name.";
        }

        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                $"/{options.Owner.Trim()}/{options.Repository.Trim()}/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            return "The installer is not hosted in this project's GitHub releases.";
        }

        return null;
    }

    internal static string BuildInstallerArguments(string installDirectory, string? gamePath)
    {
        // /DIR is needed because the installer deliberately forgets the previous folder
        // (UsePreviousAppDir=no), which would otherwise move a custom install to the default one.
        var args = new List<string>
        {
            "/SILENT",
            "/SUPPRESSMSGBOXES",
            "/NOCANCEL",
            "/CLOSEAPPLICATIONS",
            "/RELAUNCH=1",
            $"/DIR={Quote(installDirectory)}"
        };

        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            args.Add($"/ACFOLDER={Quote(gamePath)}");
        }

        return string.Join(' ', args);
    }

    private static string Quote(string path)
    {
        // A trailing backslash would escape the closing quote.
        return $"\"{path.Replace("\"", string.Empty).TrimEnd('\\', '/')}\"";
    }

    private async Task<string> DownloadVerifiedAsync(ReleaseAsset asset, string tag, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_workRoot, SanitizeSegment(tag));
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, asset.Name);
        var partialPath = finalPath + ".partial";

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnduranceRaceDashboard", _versionProvider.CurrentVersion));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri is { Scheme: not "https" })
            {
                throw new InvalidDataException("The download was redirected to an insecure address.");
            }

            var total = response.Content.Headers.ContentLength ?? asset.SizeInBytes;
            lock (_gate)
            {
                _total = total;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var lastReported = -ProgressStepPercent;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    lock (_gate)
                    {
                        _received += read;
                    }

                    var percent = ProgressPercent;
                    if (percent - lastReported >= ProgressStepPercent)
                    {
                        lastReported = percent;
                        Raise();
                    }
                }
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());

            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded installer does not match its published checksum, so it was discarded.");
            }

            File.Move(partialPath, finalPath, overwrite: true);

            return finalPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private void Fail(string message)
    {
        lock (_gate)
        {
            _phase = UpdateInstallPhase.Failed;
            _error = message;
        }

        Raise();
    }

    private void Raise()
    {
        Changed?.Invoke();
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A leftover partial file is replaced by the next attempt.
        }
    }
}
