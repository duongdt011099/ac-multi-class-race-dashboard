using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace MulticlassRace.Services;

/// <summary>
/// Keeps a second copy of the app from fighting the first one for the port. The dashboard starts
/// when you sign in, so every shortcut a user clicks afterwards starts another copy, and without
/// this the newcomer dies on "address already in use" with nothing to show for it.
///
/// The claim is an open handle to a named mutex, not ownership of it. Ownership would tie the mutex
/// to the thread that took it and throw AbandonedMutexException when that thread died, whereas a
/// bare handle is released by the kernel when the process exits, which is exactly the lifetime we
/// want to detect. Nothing is left behind by a crash, so a stale claim cannot block a real start.
///
/// A copy that loses the race has nothing useful to do, so it opens the dashboard in the browser and
/// exits. That makes every shortcut mean the same thing: show me the dashboard, starting it first if
/// it is not up yet.
/// </summary>
internal sealed class DashboardInstance : IDisposable
{
    private const string MutexName = @"Local\EnduranceRace.Dashboard";
    private const string DefaultUrl = "http://127.0.0.1:5000";

    // Long enough to cover a first copy that is still binding its port, short enough that a second
    // click never feels like it is waiting on something.
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Mutex _handle;
    private bool _disposed;

    private DashboardInstance(Mutex handle) => _handle = handle;

    /// <summary>
    /// The address this copy will serve on. Kestrel's own configuration wins because that is what the
    /// app binds, and a command line or environment override lands there first. The urls list is only
    /// a fallback for setups that bind through it instead.
    /// </summary>
    public static string ResolveUrl(IConfiguration configuration)
    {
        foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            if (TryNormalize(endpoint["Url"], out var url))
            {
                return url;
            }
        }

        var urls = configuration["urls"];

        if (!string.IsNullOrWhiteSpace(urls))
        {
            foreach (var candidate in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryNormalize(candidate, out var url))
                {
                    return url;
                }
            }
        }

        return DefaultUrl;
    }

    /// <summary>
    /// Claims the app for this process, or hands over to the copy that already holds it.
    /// Returns null once the dashboard has been opened in the browser and this copy should stop.
    /// </summary>
    public static async Task<IDisposable?> TryClaimOrHandOverAsync(string url)
    {
        var handle = new Mutex(initiallyOwned: false, MutexName, out var createdNew);

        if (createdNew)
        {
            return new DashboardInstance(handle);
        }

        try
        {
            // The first copy may still be binding its port, in which case the browser would be sent
            // to a page that is not listening yet. Wait briefly rather than open a broken page.
            await WaitForListenerAsync(url);
            OpenDashboard(url);
        }
        catch
        {
        }
        finally
        {
            handle.Dispose();
        }

        return null;
    }

    private static bool TryNormalize(string? candidate, out string url)
    {
        url = DefaultUrl;

        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // The dashboard is served from the root, so drop any path, query or fragment the caller
        // happened to include and leave the address in the same form Kestrel reports.
        url = parsed.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static async Task WaitForListenerAsync(string url)
    {
        var parsed = new Uri(url);
        var stopwatch = Stopwatch.StartNew();

        while (!await CanConnectAsync(parsed.Host, parsed.Port))
        {
            if (stopwatch.Elapsed >= HandoverTimeout)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }
    }

    private static async Task<bool> CanConnectAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeout);

            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hands the dashboard to the browser. Used when this copy lost the startup race, and by a copy
    /// that a person asked for by clicking a shortcut, which passes --open-dashboard.
    /// </summary>
    public static void OpenDashboard(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
        }
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
            _handle.Dispose();
        }
        catch
        {
        }
    }
}
