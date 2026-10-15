namespace MulticlassRace.Infrastructure;

/// <summary>
/// Keeps a second copy of the app from running next to the first one. The dashboard starts when you
/// sign in, so every shortcut a user clicks afterwards starts another copy.
///
/// The claim is an open handle to a named mutex, not ownership of it. Ownership would tie the mutex
/// to the thread that took it and throw AbandonedMutexException when that thread died, whereas a
/// bare handle is released by the kernel when the process exits, which is exactly the lifetime we
/// want to detect. Nothing is left behind by a crash, so a stale claim cannot block a real start.
///
/// A copy that loses the race signals a named event and exits. The running copy answers by bringing
/// its window to the front, so every shortcut means the same thing: show me the dashboard, starting
/// it first if it is not up yet. The event is created by whichever copy gets there first and stays
/// signalled until the owner listens, so a signal sent during startup is not lost.
/// </summary>
internal sealed class DashboardInstance : IDisposable
{
    private const string MutexName = @"Local\EnduranceRace.Dashboard";
    private const string ShowEventName = @"Local\EnduranceRace.Dashboard.Show";

    private readonly Mutex _handle;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private DashboardInstance(Mutex handle, EventWaitHandle showEvent)
    {
        _handle = handle;
        _showEvent = showEvent;
    }

    /// <summary>
    /// Claims the app for this process, or asks the copy that already holds it to show its window.
    /// Returns null when another copy is running and this one should stop.
    /// </summary>
    public static DashboardInstance? TryClaimOrHandOver()
    {
        var handle = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        var showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventName);

        if (createdNew)
        {
            return new DashboardInstance(handle, showEvent);
        }

        try
        {
            showEvent.Set();
        }
        catch
        {
        }
        finally
        {
            showEvent.Dispose();
            handle.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Runs <paramref name="onShowRequested"/> each time another copy asks for the window. A request
    /// made before this was called is delivered straight away.
    /// </summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShowRequested(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _registration?.Unregister(null);
            _showEvent.Dispose();
            _handle.Dispose();
        }
        catch
        {
        }
    }
}
