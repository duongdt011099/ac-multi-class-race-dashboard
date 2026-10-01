using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

public sealed class GameSessionLauncher : IGameSessionLauncher
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    public async Task LaunchAsync(string gamePath, string executablePath, string raceIni)
    {
        var sessionId = GetInteractiveSessionId();
        var request = new GameLaunchRequest(gamePath, executablePath, raceIni);

        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName(sessionId),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                "The interactive game launcher is not running for the active Windows user. Sign in again or restart the launcher agent.",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "Could not connect to the interactive game launcher. Make sure the launcher agent is running in the active Windows session.",
                ex);
        }

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

        await writer.WriteLineAsync(JsonSerializer.Serialize(request));

        var responseLine = await reader.ReadLineAsync().WaitAsync(ResponseTimeout);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new InvalidOperationException("The interactive game launcher returned an empty response.");
        }

        var response = JsonSerializer.Deserialize<GameLaunchResponse>(responseLine);
        if (response is null || !response.Success)
        {
            throw new InvalidOperationException(response?.Error ?? "The interactive game launcher could not start Assetto Corsa.");
        }
    }

    internal static string PipeName(int sessionId) => $"EnduranceRace.GameLauncher.{sessionId}";

    private static int GetInteractiveSessionId()
    {
        if (Environment.UserInteractive)
        {
            return Process.GetCurrentProcess().SessionId;
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue)
        {
            throw new InvalidOperationException("No interactive Windows user session is currently active.");
        }

        return checked((int)sessionId);
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}

internal sealed record GameLaunchRequest(string GamePath, string ExecutablePath, string RaceIni);

internal sealed record GameLaunchResponse(bool Success, string? Error = null);
