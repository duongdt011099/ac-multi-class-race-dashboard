using MulticlassRace.Models;

namespace MulticlassRace.Services.Results;

/// <summary>A result file found by the worker, with what its file name and content say about it.</summary>
public sealed record PendingLuaResult(
    string FullPath,
    string FileName,
    SessionType SessionType,
    DateTime SessionDate,
    string? Track,
    Guid? TargetRaceId,
    string? TargetRaceLabel);

/// <summary>
/// Shared state of the result import: whether a scan is running (the "checking for results" indicator),
/// which files were already looked at, and the outcome messages the UI turns into toasts.
/// Results are imported by <see cref="BackgroundServices.LuaResultImportWorker"/> without asking.
/// </summary>
public class LuaResultImportState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isScanning;

    /// <summary>Raised when the scanning flag changes.</summary>
    public event Action? Changed;

    /// <summary>Raised with the outcome of an automatic import, or when no result could be found.</summary>
    public event Action<LuaResultImportOutcome>? OutcomeReported;

    public bool IsScanning
    {
        get
        {
            lock (_gate)
            {
                return _isScanning;
            }
        }
    }

    public void SetScanning(bool isScanning)
    {
        bool notify;
        lock (_gate)
        {
            notify = _isScanning != isScanning;
            _isScanning = isScanning;
        }

        if (notify)
        {
            Changed?.Invoke();
        }
    }

    public bool IsKnown(string fullPath)
    {
        lock (_gate)
        {
            return _knownPaths.Contains(fullPath);
        }
    }

    /// <summary>
    /// Remembers a path that has been dealt with (imported, failed or written as history) so later scans
    /// skip it. Returns false when it was already known.
    /// </summary>
    public bool MarkHandled(string fullPath)
    {
        lock (_gate)
        {
            return _knownPaths.Add(fullPath);
        }
    }

    public void Report(LuaResultImportOutcome outcome)
    {
        var handlers = OutcomeReported;

        if (handlers is null)
        {
            return;
        }

        foreach (Action<LuaResultImportOutcome> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(outcome);
            }
            catch
            {
                // A circuit that went away must not stop the import from reporting to the others.
            }
        }
    }
}
