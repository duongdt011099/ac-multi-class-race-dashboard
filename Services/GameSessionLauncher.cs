using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

/// <summary>
/// Writes the race.ini for the session and starts Assetto Corsa.
///
/// This runs in the same process and Windows session as the dashboard, which is why it works at
/// all: the game needs an interactive desktop, so this is why the dashboard runs as a normal app
/// that starts when you sign in, rather than as a Windows service.
/// </summary>
public sealed class GameSessionLauncher : IGameSessionLauncher
{
    private readonly ILogger<GameSessionLauncher> _logger;

    public GameSessionLauncher(ILogger<GameSessionLauncher> logger)
    {
        _logger = logger;
    }

    public Task LaunchAsync(string gamePath, string executablePath, string raceIni)
    {
        var gameDirectory = Path.GetFullPath(gamePath);
        var executable = Path.GetFullPath(executablePath);
        var allowedExecutables = new[]
        {
            Path.GetFullPath(Path.Combine(gameDirectory, "acs.exe")),
            Path.GetFullPath(Path.Combine(gameDirectory, "AssettoCorsa.exe"))
        };

        if (allowedExecutables.Contains(executable, StringComparer.OrdinalIgnoreCase) is false)
        {
            throw new InvalidOperationException("The requested game executable is outside the configured Assetto Corsa folder.");
        }

        if (File.Exists(executable) is false)
        {
            throw new InvalidOperationException($"Assetto Corsa executable not found: {executable}");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (string.IsNullOrWhiteSpace(documents))
        {
            throw new InvalidOperationException("Could not locate the Documents folder.");
        }

        var configDirectory = Path.Combine(documents, "Assetto Corsa", "cfg");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "race.ini"), raceIni);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = gameDirectory,
            UseShellExecute = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not start Assetto Corsa.");
        }

        _logger.LogInformation("Started Assetto Corsa (pid {ProcessId}) for {RaceIni}.", process.Id, Path.Combine(configDirectory, "race.ini"));

        return Task.CompletedTask;
    }
}
