using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MulticlassRace.Web;

namespace MulticlassRace.Infrastructure;

/// <summary>
/// The dashboard runs as a windowless app that starts when you sign in, so there is no window to
/// click and no console to close. This is the whole user interface: it opens the dashboard, opens
/// the log folder when something has gone wrong, and shuts the app down again.
///
/// Everything WinForms lives on one dedicated STA thread. That is not a style choice: a NotifyIcon
/// owns a hidden native window created on the thread that constructs it, and the shell only draws
/// it - every click is delivered by a message loop on that same thread. Build it on the main thread
/// and the icon appears in the tray but the menu never opens, because the main thread is busy
/// running the web app and has no message loop at all.
/// </summary>
internal sealed class DashboardTray : IDisposable
{
    private const string IconResourceName = "EnduranceRace.favicon.ico";

    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string _dashboardUrl;
    private readonly AppAccessToken _token;
    private readonly bool _showOnStart;
    private readonly ManualResetEventSlim _started = new(false);

    // Written by the tray thread before _started is set, read by Dispose afterwards.
    private ApplicationContext? _context;
    private DashboardWindow? _window;
    private bool _startFailed;

    private DashboardTray(ILogger logger, IHostApplicationLifetime lifetime, string dashboardUrl, AppAccessToken token, bool showOnStart)
    {
        _logger = logger;
        _lifetime = lifetime;
        _dashboardUrl = dashboardUrl;
        _token = token;
        _showOnStart = showOnStart;
    }

    /// <summary>Brings the dashboard window to the front. Safe to call from any thread.</summary>
    public void ShowDashboard() => _window?.ShowDashboard();

    public static DashboardTray? Start(ILogger logger, IHostApplicationLifetime lifetime, string dashboardUrl, AppAccessToken token, bool showOnStart)
    {
        var tray = new DashboardTray(logger, lifetime, dashboardUrl, token, showOnStart);

        try
        {
            var thread = new Thread(tray.Run)
            {
                IsBackground = true,
                Name = "EnduranceRace.Tray"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Do not claim there is a tray icon until there actually is one.
            tray._started.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start the tray icon thread.");
            return null;
        }

        if (tray._startFailed)
        {
            return null;
        }

        return tray;
    }

    private void Run()
    {
        NotifyIcon? icon = null;
        ContextMenuStrip? menu = null;
        Icon? image = null;

        try
        {
            // Must run before the first window of the process is created to take effect.
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            image = LoadIcon();

            _window = new DashboardWindow(new Uri(_dashboardUrl), _token, image, _logger);
            _window.PrepareHandle();

            menu = BuildMenu();

            icon = new NotifyIcon
            {
                Icon = image,
                Text = "Endurance Race dashboard",
                ContextMenuStrip = menu
            };

            icon.DoubleClick += (_, _) => _window.ShowDashboard();
            icon.Visible = true;

            // A bare context runs with no main form until ExitThread is called, which is what
            // Dispose does when the app is shutting down.
            var context = new ApplicationContext();
            _context = context;
            _started.Set();

            _logger.LogInformation("Tray icon ready at {Url}.", _dashboardUrl);

            if (_showOnStart)
            {
                _window.ShowDashboard();
            }

            Application.Run(context);
        }
        catch (Exception ex)
        {
            _startFailed = true;
            _logger.LogWarning(ex, "The tray icon could not be shown. The dashboard is running, but cannot be closed from the tray.");
            _started.Set();
        }
        finally
        {
            // This thread owns these objects, so it is also the one that disposes them.
            try
            {
                if (icon is not null)
                {
                    icon.Visible = false;
                    icon.ContextMenuStrip = null;
                    icon.Dispose();
                }

                _window?.CloseForShutdown();
                menu?.Dispose();
                image?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The tray icon did not clean up tidily.");
            }
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var statusItem = new ToolStripMenuItem(_dashboardUrl)
        {
            Enabled = false
        };
        statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => _window?.ShowDashboard()));
        menu.Items.Add(new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(AppPaths.LogFilePath)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        return menu;
    }

    private void Exit()
    {
        try
        {
            _logger.LogInformation("Exit requested from the tray icon; shutting down.");
            _lifetime.StopApplication();

            // The open dashboard window holds a live Blazor connection, which can keep the graceful
            // shutdown waiting. If the process is still here after a few seconds, end it.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                _logger.LogWarning("Shutdown did not finish in time; forcing exit.");
                Environment.Exit(0);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not shut down cleanly.");
        }
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName);

            if (stream is not null)
            {
                return new Icon(stream, 32, 32);
            }
        }
        catch
        {
            // Fall through to the executable's own icon.
        }

        try
        {
            if (Environment.ProcessPath is { } path)
            {
                return Icon.ExtractAssociatedIcon(path);
            }
        }
        catch
        {
        }

        return null;
    }

    private static void OpenFolder(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

            if (Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            // ExitThread posts WM_QUIT, so this is safe from any thread. The tray thread then falls
            // out of Application.Run and disposes the icon on its way out.
            _context?.ExitThread();
        }
        catch
        {
        }
    }
}
