using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MulticlassRace.Web;

namespace MulticlassRace.Infrastructure;

/// <summary>
/// The only way into the dashboard. It embeds the web app in a WebView2 control and hands that control
/// the secret cookie the server insists on, so a regular browser that finds the port gets a 404.
///
/// Closing the window only hides it: the web app keeps running in the tray, and the Blazor circuit and
/// page state survive until the window is shown again. Exit from the tray is what really closes it.
/// The browser engine is not started until the window is first shown, so signing in stays cheap.
/// </summary>
internal sealed class DashboardWindow : Form
{
    private const string LoopbackHost = "127.0.0.1";

    // Width of the strip around the page that resizes the window. The page covers the rest of the form,
    // so only this strip reaches the form's own hit testing.
    private const int ResizeBorder = 6;

    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14;
    private const int HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
    private const int WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000;
    private const int WmNcLButtonDown = 0x00A1, HtCaption = 2;

    private readonly Uri _baseUri;
    private readonly AppAccessToken _token;
    private readonly ILogger _logger;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    private bool _initializing;
    private bool _initialized;
    private bool _closingForShutdown;

    public DashboardWindow(Uri baseUri, AppAccessToken token, Icon? icon, ILogger logger)
    {
        _baseUri = baseUri;
        _token = token;
        _logger = logger;

        Text = "Endurance Race Dashboard";
        Size = new Size(1360, 860);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        // No native title bar: the page draws its own window buttons. A borderless form would cover the
        // taskbar when maximized, so the maximized bounds are limited to the working area.
        FormBorderStyle = FormBorderStyle.None;
        MaximizedBounds = Screen.FromPoint(Cursor.Position).WorkingArea;

        // The strip around the page, used to resize the window while it is not maximized.
        BackColor = Color.FromArgb(0x1f, 0x1f, 0x26);
        UpdateResizeBorder();

        if (icon is not null)
        {
            Icon = icon;
        }

        Controls.Add(_webView);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // 20 is DWMWA_USE_IMMERSIVE_DARK_MODE (19 on early Windows 10 builds); failure just leaves the light bar.
        var dark = 1;

        if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
        }
    }

    // A borderless form has no minimize/maximize boxes, which also turns off minimizing from the taskbar,
    // Aero snap and double-clicking the drag area to maximize. These styles bring those back without drawing anything.
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= WsMinimizeBox | WsMaximizeBox;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            var lParam = (long)m.LParam;
            var screenPoint = new Point(unchecked((short)lParam), unchecked((short)(lParam >> 16)));
            var hit = ResizeHitTest(PointToClient(screenPoint));

            if (hit != 0)
            {
                m.Result = hit;
            }
        }
    }

    private int ResizeHitTest(Point point)
    {
        var left = point.X < ResizeBorder;
        var right = point.X >= ClientSize.Width - ResizeBorder;
        var top = point.Y < ResizeBorder;
        var bottom = point.Y >= ClientSize.Height - ResizeBorder;

        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => HtTopLeft,
            (true, _, _, true) => HtTopRight,
            (_, true, true, _) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtTop,
            (_, true, _, _) => HtBottom,
            (_, _, true, _) => HtLeft,
            (_, _, _, true) => HtRight,
            _ => 0,
        };
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateResizeBorder();
        PostWindowState();
    }

    // The resize strip only shows while the window is not maximized.
    private void UpdateResizeBorder()
    {
        Padding = WindowState == FormWindowState.Normal ? new Padding(ResizeBorder) : Padding.Empty;
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }

        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        WindowState = FormWindowState.Maximized;
    }

    // The page asks for this while the mouse is held down on an empty part of its top band. Pretending the
    // press landed on a title bar hands the move to Windows, which also snaps and restores a maximized window.
    private void StartDrag()
    {
        if (WindowState == FormWindowState.Minimized || (MouseButtons & MouseButtons.Left) == 0)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    // Tells the page whether the window is maximized, so its maximize button shows the right icon.
    private void PostWindowState()
    {
        if (_initialized && WindowState != FormWindowState.Minimized)
        {
            _webView.CoreWebView2?.PostWebMessageAsString(WindowState == FormWindowState.Maximized ? "maximized" : "normal");
        }
    }

    /// <summary>Creates the native window without showing it, so other threads can post work to it.</summary>
    public void PrepareHandle() => CreateHandle();

    /// <summary>Safe to call from any thread.</summary>
    public void ShowDashboard()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(ShowDashboard);
            return;
        }

        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        BringToForeground();

        if (!_initializing && !_initialized)
        {
            _initializing = true;
            _ = InitializeBrowserAsync();
        }
    }

    // Windows only lets the process the user is working in take the foreground. This one is usually
    // in the background (started by the update installer after it closed, or woken by a shortcut
    // click), so a plain Activate just flashes the taskbar button and the window stays behind.
    // Briefly joining the input of the foreground window's thread lifts that restriction, and the
    // top-most toggle keeps the window on top even if Windows still refuses to move the focus.
    private void BringToForeground()
    {
        var foreground = GetForegroundWindow();

        if (foreground == Handle)
        {
            return;
        }

        var thisThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != thisThread &&
            AttachThreadInput(thisThread, foregroundThread, true);

        try
        {
            TopMost = true;
            TopMost = false;
            BringWindowToTop(Handle);
            SetForegroundWindow(Handle);
            Activate();
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    public void CloseForShutdown()
    {
        _closingForShutdown = true;
        Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingForShutdown && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnduranceRaceDashboard",
                "WebView2");

            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            LockDown(core);
            SetAccessCookie(core);

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.WebMessageReceived += OnWebMessageReceived;

            _initialized = true;
            core.Navigate(_baseUri.ToString());
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            _logger.LogCritical(ex, "The WebView2 runtime is not installed.");
            ReportBrowserFailure(
                "The dashboard needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer." +
                $"{Environment.NewLine}{Environment.NewLine}Install it from https://developer.microsoft.com/microsoft-edge/webview2 and start the dashboard again.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "The dashboard window could not start its browser.");
            ReportBrowserFailure($"The dashboard window could not start.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
        finally
        {
            _initializing = false;
        }
    }

    private static void LockDown(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsNonClientRegionSupportEnabled = true;
    }

    private void SetAccessCookie(CoreWebView2 core)
    {
        var cookie = core.CookieManager.CreateCookie(AppAccessToken.CookieName, _token.Value, LoopbackHost, "/");
        cookie.IsHttpOnly = true;
        cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict;
        core.CookieManager.AddOrUpdateCookie(cookie);
    }

    // The page's own window buttons. Close only hides the window, exactly like the old title-bar X did.
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        switch (e.TryGetWebMessageAsString())
        {
            case "close":
                Close();
                break;
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "maximize":
                ToggleMaximize();
                break;
            case "state":
                PostWindowState();
                break;
            case "drag":
                // Posted so the page's message handler returns before Windows runs its move loop.
                BeginInvoke(StartDrag);
                break;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsDashboardAddress(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!IsDashboardAddress(e.Uri))
        {
            OpenExternally(e.Uri);
        }
    }

    private bool IsDashboardAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Host, LoopbackHost, StringComparison.Ordinal) &&
        uri.Port == _baseUri.Port;

    // Links that leave the dashboard belong to the user's own browser, never to this window. Only web
    // links go out: anything else would let a page launch arbitrary programs through the shell.
    private void OpenExternally(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not open {Url} in the default browser.", uri);
        }
    }

    private void ReportBrowserFailure(string message)
    {
        MessageBox.Show(this, message, "Endurance Race", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
