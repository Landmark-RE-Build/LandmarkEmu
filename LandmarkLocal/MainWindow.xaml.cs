using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LandmarkLocal;

public partial class MainWindow : Window
{
    // LaunchPad palette: ok / accent / muted / danger
    static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0xC4, 0x7F));
    static readonly Brush StartingBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xA4, 0x3A));
    static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(0x97, 0xA6, 0xAB));
    static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x68, 0x5C));

    readonly LocalPaths _paths;
    LocalSettings _settings;
    LocalOrchestrator? _host;
    CancellationTokenSource? _cts;
    bool _busy;
    HostPhase _phase = HostPhase.Stopped;

    public MainWindow()
    {
        InitializeComponent();
        _paths = LocalPaths.Resolve();
        _settings = LocalSettings.LoadOrCreate(_paths);
        LandmarkBox.Text = string.IsNullOrWhiteSpace(_settings.LandmarkFolder)
            ? LocalSettings.TryFindSteamLandmark() ?? ""
            : _settings.LandmarkFolder;
        AdminBox.Text = _settings.AdminAccountName;
        Append("LandmarkLocal folder: " + _paths.Root);
        if (LocalSettings.TryFindSteamLandmark() is string found
            && string.IsNullOrWhiteSpace(_settings.LandmarkFolder))
            Append("Found Landmark at: " + found);
        UpdateChecklist();
        UpdateRemembered();
        _ = RefreshStatusAsync();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select your Landmark folder (Landmark64.exe)" };
        if (dlg.ShowDialog(this) == true)
            LandmarkBox.Text = dlg.FolderName;
    }

    void Form_Changed(object sender, RoutedEventArgs e)
    {
        UpdateChecklist();
        UpdateRemembered();
        SetButtons();
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        PersistForm();
        if (!CanStart(out string? startBlocker))
        {
            MessageBox.Show(this, startBlocker!, "LandmarkLocal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        SetPhase(HostPhase.Starting, "Starting…");
        SetButtons();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(OnStartupProgress);
        try
        {
            _host?.Dispose();
            _host = new LocalOrchestrator(_paths, _settings);
            await _host.StartAsync(progress, _cts.Token);
            SetPhase(HostPhase.Ready, "Ready");
            Append(_host.LastMessage);
        }
        catch (Exception ex)
        {
            SetPhase(HostPhase.Failed, "Failed");
            Append("Failed: " + ex.Message);
        }
        finally
        {
            _busy = false;
            SetButtons();
            UpdateChecklist();
        }
    }

    async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        _busy = true;
        SetButtons();
        _cts?.Cancel();
        var progress = new Progress<string>(Append);
        try
        {
            _host ??= new LocalOrchestrator(_paths, _settings);
            await _host.StopAsync(progress);
            SetPhase(HostPhase.Stopped, "Stopped");
        }
        catch (Exception ex)
        {
            Append("Stop: " + ex.Message);
        }
        finally
        {
            _busy = false;
            SetButtons();
            UpdateChecklist();
        }
    }

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        PersistForm();
        string? password = PasswordBox.Password.Length > 0 ? PasswordBox.Password : null;
        if (!_settings.HasRememberedLogin() && string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Enter a password (at least 6 characters) the first time you Play.",
                "LandmarkLocal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        SetButtons();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(Append);
        try
        {
            LocalPlayResult result = await LocalPlay.PlayAsync(_paths, _settings, password, progress, _cts.Token);
            _settings = LocalSettings.LoadOrCreate(_paths);
            PasswordBox.Clear();
            UpdateRemembered();
            Append(result.Message);
        }
        catch (Exception ex)
        {
            Append("Play: " + ex.Message);
            MessageBox.Show(this, ex.Message, "LandmarkLocal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            SetButtons();
            UpdateChecklist();
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    async Task RefreshStatusAsync()
    {
        try
        {
            _host ??= new LocalOrchestrator(_paths, _settings);
            ReadinessSnapshot snap = await _host.StatusAsync();
            SetPhase(snap.Phase, snap.Phase.ToString());
            // Avoid flooding the activity log on every Refresh with the same wait line.
            if (!string.IsNullOrWhiteSpace(snap.Message)
                && (snap.Phase == HostPhase.Ready || snap.Phase == HostPhase.Failed || snap.Phase == HostPhase.Stopped))
                Append(snap.Message);
            else if (snap.Phase == HostPhase.Starting)
                StatusLabel.Text = "Starting… " + snap.Message;
        }
        catch (Exception ex)
        {
            Append("Status: " + ex.Message);
        }
        finally
        {
            UpdateChecklist();
            SetButtons();
        }
    }

    void PersistForm()
    {
        string prevAccount = _settings.AdminAccountName.Trim();
        _settings.LandmarkFolder = LandmarkBox.Text.Trim().Trim('"');
        _settings.AdminAccountName = AdminBox.Text.Trim();
        if (!string.Equals(prevAccount, _settings.AdminAccountName, StringComparison.OrdinalIgnoreCase))
            _settings.ForgetLogin();
        _settings.EnsureCredentials();
        _settings.Save(_paths);
        _host?.UpdateSettings(_settings);
    }

    void SetPhase(HostPhase phase, string label)
    {
        _phase = phase;
        StatusLabel.Text = label;
        StatusLabel.Foreground = phase switch
        {
            HostPhase.Ready => ReadyBrush,
            HostPhase.Starting => StartingBrush,
            HostPhase.Failed => FailedBrush,
            _ => StoppedBrush,
        };
        StatusBanner.BorderBrush = StatusLabel.Foreground;
    }

    void UpdateChecklist()
    {
        bool folderOk = LocalSettings.LooksLikeLandmarkFolder(LandmarkBox.Text.Trim().Trim('"'));
        bool accountOk = AccountRules.IsValidAccountName(AdminBox.Text.Trim());
        bool passwordOk = _settings.HasRememberedLogin(AdminBox.Text.Trim())
                          || PasswordBox.Password.Length >= 6;
        bool ready = _phase == HostPhase.Ready;

        ChecklistBox.Text =
            Mark(folderOk) + " Landmark folder\n"
            + Mark(accountOk) + " Account name (this is also your admin)\n"
            + Mark(passwordOk) + " Password (before Start; remembered after Play)\n"
            + Mark(ready) + " Servers ready — then press Play";
    }

    void UpdateRemembered()
    {
        bool remembered = _settings.HasRememberedLogin(AdminBox.Text.Trim())
                          && PasswordBox.Password.Length == 0;
        RememberedLabel.Visibility = remembered ? Visibility.Visible : Visibility.Collapsed;
    }

    static string Mark(bool ok) => ok ? "✓" : "○";

    bool PasswordReady()
        => _settings.HasRememberedLogin(AdminBox.Text.Trim()) || PasswordBox.Password.Length >= 6;

    bool CanStart(out string? blocker)
    {
        if (!LocalSettings.LooksLikeLandmarkFolder(LandmarkBox.Text.Trim().Trim('"')))
        {
            blocker = "Set your Landmark folder (the one with Landmark64.exe) first.";
            return false;
        }
        if (!AccountRules.IsValidAccountName(AdminBox.Text.Trim()))
        {
            blocker = "Account name: 3 to 32 letters, digits or _.";
            return false;
        }
        if (!PasswordReady())
        {
            blocker = "Enter a password (at least 6 characters) before Start. It is used when you press Play.";
            return false;
        }
        blocker = null;
        return true;
    }

    void SetButtons()
    {
        // Start only when servers are down (or last start failed); Stop only when something is up / coming up.
        bool serversUpOrStarting = _phase is HostPhase.Starting or HostPhase.Ready;
        StartButton.IsEnabled = !_busy && !serversUpOrStarting && CanStart(out _);
        StopButton.IsEnabled = !_busy && serversUpOrStarting;
        RefreshButton.IsEnabled = !_busy;
        PlayButton.IsEnabled = !_busy && _phase == HostPhase.Ready;
    }

    void OnStartupProgress(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnStartupProgress(line));
            return;
        }
        if (line.StartsWith(LocalOrchestrator.StatusPrefix, StringComparison.Ordinal))
        {
            string status = line[LocalOrchestrator.StatusPrefix.Length..];
            if (_phase is HostPhase.Starting or HostPhase.Stopped)
                SetPhase(HostPhase.Starting, status);
            return;
        }
        Append(line);
    }

    void Append(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Append(line));
            return;
        }
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        // Do not stop servers when the window closes — CLI/GUI are control surfaces only.
        _host?.Dispose();
        base.OnClosed(e);
    }
}
