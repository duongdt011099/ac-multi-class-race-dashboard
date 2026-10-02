using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MulticlassRace.Services;

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
    private readonly ManualResetEventSlim _started = new(false);

    // Written by the tray thread before _started is set, read by Dispose afterwards.
    private ApplicationContext? _context;
    private bool _startFailed;

    private DashboardTray(ILogger logger, IHostApplicationLifetime lifetime, string dashboardUrl)
    {
        _logger = logger;
        _lifetime = lifetime;
        _dashboardUrl = dashboardUrl;
    }

    public static DashboardTray? Start(ILogger logger, IHostApplicationLifetime lifetime, string dashboardUrl)
    {
        var tray = new DashboardTray(logger, lifetime, dashboardUrl);

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
            image = LoadIcon();

            menu = BuildMenu();

            icon = new NotifyIcon
            {
                Icon = image,
                Text = "Endurance Race dashboard",
                ContextMenuStrip = menu
            };

            icon.DoubleClick += (_, _) => OpenUrl(_dashboardUrl);
            icon.Visible = true;

            // A bare context runs with no main form until ExitThread is called, which is what
            // Dispose does when the app is shutting down.
            var context = new ApplicationContext();
            _context = context;
            _started.Set();

            _logger.LogInformation("Tray icon ready at {Url}.", _dashboardUrl);

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
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenUrl(_dashboardUrl)));
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

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
        }
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
