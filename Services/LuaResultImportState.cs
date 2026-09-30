using MulticlassRace.Models;

namespace MulticlassRace.Services;

public sealed record PendingLuaResult(
    string FullPath,
    string FileName,
    SessionType SessionType,
    DateTime SessionDate,
    string? Track,
    Guid? TargetRaceId,
    string? TargetRaceLabel);

/// <summary>
/// Holds results detected by <see cref="LuaResultImportWorker"/> until the user answers the
/// confirmation prompt. One result is presented at a time.
/// </summary>
public class LuaResultImportState
{
    private readonly object _gate = new();
    private readonly Queue<PendingLuaResult> _queue = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isScanning;

    public event Action? Changed;

    public PendingLuaResult? Current { get; private set; }

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

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    public bool IsKnown(string fullPath)
    {
        lock (_gate)
        {
            return _knownPaths.Contains(fullPath);
        }
    }

    public void Enqueue(PendingLuaResult result)
    {
        bool notify = false;

        lock (_gate)
        {
            if (_knownPaths.Contains(result.FullPath))
            {
                return;
            }

            _knownPaths.Add(result.FullPath);
            _queue.Enqueue(result);

            if (Current is null)
            {
                Current = _queue.Dequeue();
                notify = true;
            }
        }

        if (notify)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Records a path the user has already handled (imported or declined) so later scans skip
    /// it without ever showing a prompt.
    /// </summary>
    public void MarkHandled(string fullPath)
    {
        lock (_gate)
        {
            _knownPaths.Add(fullPath);
        }
    }

    public void Advance()
    {
        lock (_gate)
        {
            Current = _queue.Count > 0 ? _queue.Dequeue() : null;
        }

        Changed?.Invoke();
    }
}
