using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// The shell: a frameless window with the launcher page (wwwroot/index.html) in WebView2, as LaunchPad shows its
    /// page in an embedded browser. The page talks to <see cref="Bridge"/> with JSON messages.
    /// </summary>
    public partial class MainWindow : Window
    {
        private Bridge? bridge;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                try
                {
                    await StartAsync();
                }
                catch (Exception e)
                {
                    // e.g. no WebView2 runtime, or its profile busy: say so instead of vanishing.
                    MessageBox.Show($"LaunchPad could not start its window: {e.Message}", "LaunchPad", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown(1);
                }
            };
            // Closed while the game runs: hide and keep watching it, so LaunchPad can come back when it exits.
            Closing += (_, e) =>
            {
                if (bridge?.GameRunning == true)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        /// <summary>Back in front of everything (the game has just closed, so Windows lets it take the focus).</summary>
        public void BringBack()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Topmost = true;
            Activate();
            Topmost = false;
        }

        private async Task StartAsync()
        {
            // WebView2 keeps its profile with the launcher's settings (%AppData%), not in the game folder; one per game
            // folder, since WebView2 refuses a second process on a profile already open with other options.
            string profile = Path.Combine(Settings.Folder, "WebView2", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Game.Folder.ToLowerInvariant())))[..12]);
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await Web.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = Web.CoreWebView2;

            // The page is inside the exe (embedded wwwroot), served under a private host name; nothing else is reachable.
            core.AddWebResourceRequestedFilter("https://launchpad.local/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
                Stream? content = path.Contains("..") ? null
                    : typeof(MainWindow).Assembly.GetManifestResourceStream("wwwroot/" + (path.Length == 0 ? "index.html" : path));
                string type = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8", ".css" => "text/css", ".js" => "text/javascript",
                    ".png" => "image/png", ".jpg" => "image/jpeg", ".svg" => "image/svg+xml", _ => "application/octet-stream"
                };
                e.Response = content == null
                    ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "")
                    : core.Environment.CreateWebResourceResponse(content, 200, "OK", $"Content-Type: {type}\r\nCache-Control: no-store");
            };
            core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            // Links (news, the account website) open in the player's own browser, not inside the launcher.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                Bridge.OpenInBrowser(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://launchpad.local/", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    Bridge.OpenInBrowser(e.Uri);
                }
            };

            bridge = new Bridge(this, core);
            core.Navigate("https://launchpad.local/index.html");
        }

        /// <summary>
        /// The page's title bar: drag the window (LaunchPad's StartDrag). The mouse is captured by the browser control,
        /// so Windows is told the press was on a caption instead (WM_NCLBUTTONDOWN, HTCAPTION).
        /// </summary>
        public void BeginDrag()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
