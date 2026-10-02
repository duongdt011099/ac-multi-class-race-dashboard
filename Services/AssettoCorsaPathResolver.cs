using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Services;

/// <summary>Where the game folder came from, in the order we are willing to trust it.</summary>
public enum AssettoCorsaPathSource
{
    /// <summary>No usable folder was found. The app cannot do anything without one.</summary>
    None,

    /// <summary>Recorded by the installer in install.json.</summary>
    Installer,

    /// <summary>Left over from a configuration saved before the installer supplied the path.</summary>
    Saved,

    /// <summary>Found by looking in the places Assetto Corsa is normally installed.</summary>
    AutoDetected
}

public sealed record AssettoCorsaGameLocation(string? Path, AssettoCorsaPathSource Source)
{
    public bool IsConfigured => string.IsNullOrWhiteSpace(Path) is false;
}

/// <summary>
/// Resolves the Assetto Corsa folder once and caches it.
///
/// The installer asks for the folder, so a normal install is answered before the app ever runs. Two
/// fallbacks exist because that answer is not always available: an existing configuration is
/// honoured, and otherwise the usual install locations are checked. That is what lets the app run
/// from a source checkout during development, and what rescues an install whose recorded folder has
/// since been moved.
///
/// A folder only counts once it contains the game executable. Without it nothing works - no liveries,
/// no track images, no sessions - so a stale path is treated as no path at all rather than failing
/// later at the point of use.
/// </summary>
public sealed class AssettoCorsaPathResolver
{
    private static readonly string[] ExecutableNames = ["acs.exe", "AssettoCorsa.exe"];

    private static readonly string[] GameFolderNames = ["Assetto Corsa", "assettocorsa"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssettoCorsaPathResolver> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AssettoCorsaGameLocation? _cached;

    public AssettoCorsaPathResolver(IServiceScopeFactory scopeFactory, ILogger<AssettoCorsaPathResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<AssettoCorsaGameLocation> GetAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync();

        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            _cached = await ResolveAsync();

            if (_cached.IsConfigured)
            {
                _logger.LogInformation("Assetto Corsa folder resolved from {Source}: {Path}", _cached.Source, _cached.Path);
            }
            else
            {
                _logger.LogWarning("No Assetto Corsa folder found. Reinstall the dashboard and point it at the game.");
            }

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AssettoCorsaGameLocation> ResolveAsync()
    {
        var fromInstaller = InstallPaths.GamePath;

        if (IsUsable(fromInstaller))
        {
            return new AssettoCorsaGameLocation(fromInstaller, AssettoCorsaPathSource.Installer);
        }

        var fromDatabase = await ReadSavedAsync();

        if (IsUsable(fromDatabase))
        {
            return new AssettoCorsaGameLocation(fromDatabase, AssettoCorsaPathSource.Saved);
        }

        var detected = Detect();

        if (IsUsable(detected))
        {
            return new AssettoCorsaGameLocation(detected, AssettoCorsaPathSource.AutoDetected);
        }

        return new AssettoCorsaGameLocation(null, AssettoCorsaPathSource.None);
    }

    private async Task<string?> ReadSavedAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAssettoCorsaGameConfigRepository>();
            var config = await repository.GetAsync();

            return config?.GamePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the saved Assetto Corsa folder.");
            return null;
        }
    }

    /// <summary>
    /// The places Assetto Corsa ends up when nobody is asked. Steam is checked first because it owns
    /// the library location in the registry, and the game is rarely installed under Program Files.
    /// </summary>
    private static string? Detect()
    {
        foreach (var candidate in Candidates())
        {
            if (IsUsable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        foreach (var steamPath in SteamPaths())
        {
            // Both spellings are in the wild: retail Steam installs use "Assetto Corsa", while plenty
            // of existing installs - including a lot of sim racing setups - use "assettocorsa".
            foreach (var folderName in GameFolderNames)
            {
                yield return Path.Combine(steamPath, "steamapps", "common", folderName);
            }
        }

        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                continue;
            }

            foreach (var folderName in GameFolderNames)
            {
                yield return Path.Combine(programFiles, "Steam", "steamapps", "common", folderName);
            }
        }

        foreach (var folderName in GameFolderNames)
        {
            yield return Path.Combine("C:\\", folderName);
        }
    }

    /// <summary>
    /// Steam is a 32-bit program, so on a 64-bit machine it records its path in the 32-bit view of the
    /// registry. Both views are read rather than assuming, because getting this wrong silently costs
    /// auto-detection.
    /// </summary>
    private static IEnumerable<string> SteamPaths()
    {
        foreach (var path in new[]
        {
            ReadSteamPath(RegistryHive.CurrentUser, RegistryView.Default),
            ReadSteamPath(RegistryHive.LocalMachine, RegistryView.Registry32),
            ReadSteamPath(RegistryHive.LocalMachine, RegistryView.Registry64)
        })
        {
            if (string.IsNullOrWhiteSpace(path) is false)
            {
                yield return path.Trim();
            }
        }
    }

    private static string? ReadSteamPath(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(@"Software\Valve\Steam");

            return key?.GetValue("SteamPath") as string;
        }
        catch
        {
            // No permission, or no Steam on this machine. Auto-detection is best effort by nature.
            return null;
        }
    }

    private static bool IsUsable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(path) is false)
            {
                return false;
            }

            return ExecutableNames.Any(name => File.Exists(Path.Combine(path, name)));
        }
        catch
        {
            return false;
        }
    }
}
