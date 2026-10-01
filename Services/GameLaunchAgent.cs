using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MulticlassRace.Services;

public static class GameLaunchAgent
{
    public static async Task RunAsync()
    {
        var pipeName = GameSessionLauncher.PipeName(Process.GetCurrentProcess().SessionId);

        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync();
                await HandleRequestAsync(pipe);
            }
            catch (IOException)
            {
                await Task.Delay(250);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(1000);
            }
        }
    }

    private static async Task HandleRequestAsync(NamedPipeServerStream pipe)
    {
        using var reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true
        };

        GameLaunchResponse response;
        try
        {
            var requestLine = await reader.ReadLineAsync();
            var request = string.IsNullOrWhiteSpace(requestLine)
                ? null
                : JsonSerializer.Deserialize<GameLaunchRequest>(requestLine);

            response = request is null
                ? new GameLaunchResponse(false, "The launch request was empty or invalid.")
                : StartGame(request);
        }
        catch (Exception ex)
        {
            response = new GameLaunchResponse(false, ex.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }

    private static GameLaunchResponse StartGame(GameLaunchRequest request)
    {
        var gamePath = Path.GetFullPath(request.GamePath);
        var executablePath = Path.GetFullPath(request.ExecutablePath);
        var allowedExecutables = new[]
        {
            Path.GetFullPath(Path.Combine(gamePath, "acs.exe")),
            Path.GetFullPath(Path.Combine(gamePath, "AssettoCorsa.exe"))
        };

        if (!allowedExecutables.Contains(executablePath, StringComparer.OrdinalIgnoreCase))
        {
            return new GameLaunchResponse(false, "The requested game executable is outside the configured Assetto Corsa folder.");
        }

        if (!File.Exists(executablePath))
        {
            return new GameLaunchResponse(false, $"Assetto Corsa executable not found: {executablePath}");
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            return new GameLaunchResponse(false, "Could not locate the signed-in user's Documents folder.");
        }

        var configDirectory = Path.Combine(documents, "Assetto Corsa", "cfg");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "race.ini"), request.RaceIni);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = gamePath,
            UseShellExecute = true
        });

        return process is null
            ? new GameLaunchResponse(false, "Windows did not start Assetto Corsa.")
            : new GameLaunchResponse(true);
    }
}
