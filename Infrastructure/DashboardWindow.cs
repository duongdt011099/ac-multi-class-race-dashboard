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

        if (icon is not null)
        {
            Icon = icon;
        }

        Controls.Add(_webView);
    }

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

        Activate();

        if (!_initializing && !_initialized)
        {
            _initializing = true;
            _ = InitializeBrowserAsync();
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
    }

    private void SetAccessCookie(CoreWebView2 core)
    {
        var cookie = core.CookieManager.CreateCookie(AppAccessToken.CookieName, _token.Value, LoopbackHost, "/");
        cookie.IsHttpOnly = true;
        cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict;
        core.CookieManager.AddOrUpdateCookie(cookie);
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
