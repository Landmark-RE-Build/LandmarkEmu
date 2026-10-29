using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LandmarkEmulator.ClientLaunch;
using LandmarkEmulator.PatchPacks;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LandmarkLocal;

public partial class MainWindow : Window
{
    // LaunchPad palette: ok / accent / muted / danger
    static readonly SolidColorBrush ReadyBrush = new(Color.FromRgb(0x7C, 0xC4, 0x7F));
    static readonly SolidColorBrush StartingBrush = new(Color.FromRgb(0xE3, 0xA4, 0x3A));
    static readonly SolidColorBrush StoppedBrush = new(Color.FromRgb(0x97, 0xA6, 0xAB));
    static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(0xD9, 0x68, 0x5C));

    readonly LocalPaths _paths;
    readonly DispatcherTimer _clientPoll;
    LocalSettings _settings;
    LocalOrchestrator? _host;
    CancellationTokenSource? _cts;
    bool _busy;
    bool _loadingForm;
    HostPhase _phase = HostPhase.Stopped;
    bool _forceClose;
    bool _closeInProgress;
    bool _clientRunning;
    /// <summary>Settings opened because Landmark folder was missing/invalid — close when a valid folder is chosen.</summary>
    bool _promptingForGameFolder;
    Forms.NotifyIcon? _tray;
    WindowState _lastNonMinimized = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        _paths = LocalPaths.Resolve();
        _settings = LocalSettings.LoadOrCreate(_paths);
        LandmarkBox.Text = string.IsNullOrWhiteSpace(_settings.LandmarkFolder)
            ? LocalSettings.TryFindSteamLandmark() ?? ""
            : _settings.LandmarkFolder;
        AdminBox.Text = _settings.AdminAccountName;
        LoadClientFormFromSettings();
        ShowSettingsTab("game");
        Append("LandmarkLocal folder: " + _paths.Root);
        if (LocalSettings.TryFindSteamLandmark() is string found
            && string.IsNullOrWhiteSpace(_settings.LandmarkFolder))
            Append("Found Landmark at: " + found);

        if (LocalSettings.LooksLikeLandmarkFolder(CurrentLandmarkFolder()))
        {
            // Steam auto-fill or saved path: persist so next launch is already set.
            if (string.IsNullOrWhiteSpace(_settings.LandmarkFolder))
                PersistForm();
        }
        else
            PromptForGameFolder();

        RefreshLandmarkFolderHint();
        UpdateChecklist();
        UpdateRemembered();
        EnsureTray();
        _clientPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _clientPoll.Tick += (_, _) => RefreshClientRunning();
        _clientPoll.Start();
        RefreshClientRunning();
        _ = RefreshStatusAsync();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select your Landmark folder (Landmark64.exe)" };
        if (dlg.ShowDialog(this) != true)
            return;

        LandmarkBox.Text = dlg.FolderName;
        if (LocalSettings.LooksLikeLandmarkFolder(dlg.FolderName))
        {
            AcceptGameFolderAndLeaveSettingsIfPrompting();
            return;
        }

        WarnWrongGameFolder();
        RefreshLandmarkFolderHint();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings("game");

    void SettingsClose_Click(object sender, RoutedEventArgs e) => CloseSettings();

    void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab })
            ShowSettingsTab(tab);
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsOverlay.Visibility == Visibility.Visible)
        {
            CloseSettings();
            e.Handled = true;
        }
    }

    void OpenSettings(string tab)
    {
        ShowSettingsTab(tab);
        SettingsOverlay.Visibility = Visibility.Visible;
        RefreshLandmarkFolderHint();
    }

    void CloseSettings()
    {
        PersistForm();
        _promptingForGameFolder = false;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        RefreshLandmarkFolderHint();
        UpdateChecklist();
        SetButtons();
    }

    /// <summary>Start / Stop / Play should show the activity log, not sit under Settings.</summary>
    void DismissSettingsForAction()
    {
        if (SettingsOverlay.Visibility == Visibility.Visible)
            CloseSettings();
    }

    void PromptForGameFolder()
    {
        _promptingForGameFolder = true;
        OpenSettings("game");
        Append("Choose your Landmark folder (the one with Landmark64.exe).");
    }

    void LandmarkFolder_Changed(object sender, TextChangedEventArgs e)
    {
        Form_Changed(sender, e);
        RefreshLandmarkFolderHint();
        if (_promptingForGameFolder
            && SettingsOverlay.Visibility == Visibility.Visible
            && LocalSettings.LooksLikeLandmarkFolder(CurrentLandmarkFolder()))
            AcceptGameFolderAndLeaveSettingsIfPrompting();
    }

    void AcceptGameFolderAndLeaveSettingsIfPrompting()
    {
        if (!_promptingForGameFolder)
        {
            PersistForm();
            RefreshLandmarkFolderHint();
            UpdateChecklist();
            SetButtons();
            return;
        }

        Append("Landmark folder set: " + CurrentLandmarkFolder());
        CloseSettings();
    }

    void WarnWrongGameFolder()
    {
        System.Windows.MessageBox.Show(this,
            "That folder doesn't look like Landmark.\n\n"
            + "Pick the install that contains Landmark64.exe and a Resources folder "
            + "(usually under Steam → steamapps → common → Landmark).",
            "LandmarkLocal — Wrong folder",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    string CurrentLandmarkFolder() => LandmarkBox.Text.Trim().Trim('"');

    void RefreshLandmarkFolderHint()
    {
        string folder = CurrentLandmarkFolder();
        if (LocalSettings.LooksLikeLandmarkFolder(folder))
        {
            LandmarkFolderHint.Text = "Looks good — Landmark64.exe found.";
            LandmarkFolderHint.Foreground = ReadyBrush;
            return;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            LandmarkFolderHint.Text = _promptingForGameFolder
                ? "Required — browse to your Landmark install (Landmark64.exe)."
                : "Not set — browse to the folder with Landmark64.exe.";
            LandmarkFolderHint.Foreground = (SolidColorBrush)FindResource("MutedBrush");
            return;
        }

        LandmarkFolderHint.Text =
            "This isn't a Landmark folder — need Landmark64.exe and a Resources folder inside it.";
        LandmarkFolderHint.Foreground = FailedBrush;
    }

    void ShowSettingsTab(string tab)
    {
        TabGame.Visibility = tab == "game" ? Visibility.Visible : Visibility.Collapsed;
        TabClient.Visibility = tab == "client" ? Visibility.Visible : Visibility.Collapsed;
        TabAbout.Visibility = tab == "about" ? Visibility.Visible : Visibility.Collapsed;
        PaintNav(NavGameButton, tab == "game");
        PaintNav(NavClientButton, tab == "client");
        PaintNav(NavAboutButton, tab == "about");
    }

    void PaintNav(Button button, bool active)
    {
        button.Background = active
            ? (SolidColorBrush)FindResource("Panel2Brush")
            : Brushes.Transparent;
        button.BorderBrush = active
            ? (SolidColorBrush)FindResource("AccentBrush")
            : Brushes.Transparent;
        button.Foreground = active
            ? (SolidColorBrush)FindResource("TextBrush")
            : (SolidColorBrush)FindResource("MutedBrush");
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
        DismissSettingsForAction();
        await StartServersAsync();
    }

    async Task<bool> StartServersAsync()
    {
        PersistForm();
        if (!CanStartServers(out string? startBlocker))
        {
            if (!LocalSettings.LooksLikeLandmarkFolder(CurrentLandmarkFolder()))
                PromptForGameFolder();
            System.Windows.MessageBox.Show(this, startBlocker!, "LandmarkLocal",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        _busy = true;
        SetPhase(HostPhase.Starting, "Starting…");
        ShowProgressIndeterminate();
        SetButtons();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(OnStartupProgress);
        try
        {
            _host?.Dispose();
            _host = new LocalOrchestrator(_paths, _settings);
            await _host.StartAsync(progress, _cts.Token);
            SetPhase(HostPhase.Ready, "Ready");
            HideProgress();
            Append(_host.LastMessage);
            UpdateTrayText();
            return true;
        }
        catch (Exception ex)
        {
            string detail = ex.Message.Trim();
            string headline = FirstLine(detail);
            if (headline.Length > 80)
                headline = headline[..77] + "…";
            SetPhase(HostPhase.Failed, "Failed — " + headline);
            HideProgress();
            Append("Failed: " + detail);
            System.Windows.MessageBox.Show(this, detail, "LandmarkLocal — Start failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _busy = false;
            SetButtons();
            UpdateChecklist();
        }
    }

    static string FirstLine(string text)
    {
        int n = text.IndexOfAny(['\r', '\n']);
        return n < 0 ? text : text[..n];
    }

    async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        DismissSettingsForAction();
        await StopServersAsync();
    }

    async Task StopServersAsync()
    {
        _busy = true;
        SetPhase(HostPhase.Stopping, "Stopping…");
        ShowProgressIndeterminate();
        SetButtons();
        _cts?.Cancel();
        var progress = new Progress<string>(OnStartupProgress);
        try
        {
            _host ??= new LocalOrchestrator(_paths, _settings);
            await _host.StopAsync(progress);
            SetPhase(HostPhase.Stopped, "Stopped");
            HideProgress();
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            Append("Stop: " + ex.Message);
            SetPhase(HostPhase.Failed, "Stop failed");
            HideProgress();
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
        DismissSettingsForAction();
        PersistForm();
        string? password = PasswordBox.Password.Length > 0 ? PasswordBox.Password : null;
        if (!_settings.HasRememberedLogin() && string.IsNullOrEmpty(password))
        {
            System.Windows.MessageBox.Show(this,
                "Enter a password (at least 6 characters) the first time you Play.",
                "LandmarkLocal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_phase != HostPhase.Ready)
        {
            if (!await StartServersAsync())
                return;
            if (_phase != HostPhase.Ready)
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
            LoadClientFormFromSettings();
            PasswordBox.Clear();
            UpdateRemembered();
            Append(result.Message);
            if (!string.IsNullOrEmpty(result.PatchAlert))
            {
                Append(result.PatchAlert);
                System.Windows.MessageBox.Show(this, result.PatchAlert, "Experimental client patch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshClientRunning();
        }
        catch (Exception ex)
        {
            Append("Play: " + ex.Message);
            System.Windows.MessageBox.Show(this, ex.Message, "LandmarkLocal",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
            else if (snap.Phase is HostPhase.Starting or HostPhase.Stopping)
                StatusLabel.Text = snap.Phase + "… " + snap.Message;
        }
        catch (Exception ex)
        {
            Append("Status: " + ex.Message);
        }
        finally
        {
            UpdateChecklist();
            SetButtons();
            UpdateTrayText();
        }
    }

    void LoadClientFormFromSettings()
    {
        _loadingForm = true;
        try
        {
            SelectLocale(_settings.Locale);
            FebruaryWeakBox.IsChecked = _settings.AllowFebruaryWeak;
            SelectAllocatorExpand(_settings.AllocatorSlotExpand);
            SelectContourExpand(ContourThreadPatch.ClampToAllocator(
                _settings.ContourThreadExpand, _settings.AllocatorSlotExpand));
            UpdateContourExpandEnabled();
            SelectDirectThreadCount(_settings.ClientConfigForPlay());
        }
        finally
        {
            _loadingForm = false;
        }
    }

    void PersistForm()
    {
        string prevAccount = _settings.AdminAccountName.Trim();
        _settings.LandmarkFolder = CurrentLandmarkFolder();
        _settings.AdminAccountName = AdminBox.Text.Trim();
        _settings.Locale = ReadLocale();
        _settings.AllowFebruaryWeak = FebruaryWeakBox.IsChecked == true;
        PersistDirectThreadCount();
        _settings.AllocatorSlotExpand = ReadAllocatorExpand();
        _settings.ContourThreadExpand = ContourThreadPatch.ClampToAllocator(
            ReadContourExpand(), _settings.AllocatorSlotExpand);
        if (!string.Equals(prevAccount, _settings.AdminAccountName, StringComparison.OrdinalIgnoreCase))
            _settings.ForgetLogin();
        _settings.EnsureCredentials();
        _settings.Save(_paths);
        _host?.UpdateSettings(_settings);
    }

    void Dtc_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingForm)
            return;
        DtcError.Visibility = Visibility.Collapsed;
        if (DtcCustom.IsChecked == true)
            DtcValueBox.Focus();
        Form_Changed(sender, e);
    }

    void DtcValue_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingForm)
            return;
        if (DtcCustom.IsChecked != true)
            DtcCustom.IsChecked = true;
        DtcError.Visibility = Visibility.Collapsed;
        Form_Changed(sender, e);
    }

    void SelectDirectThreadCount(ClientConfigOptions options)
    {
        SettingChoice choice = options.Effective(ClientConfigFile.ThreadsKey);
        DtcValueBox.Text = string.IsNullOrWhiteSpace(choice.Value)
            ? ClientConfigFile.Threads.Recommended
            : choice.Value.Trim();
        switch (choice.Mode)
        {
            case ConfigChoice.Custom:
                DtcCustom.IsChecked = true;
                break;
            case ConfigChoice.Leave:
                DtcLeave.IsChecked = true;
                break;
            case ConfigChoice.Recommended:
                DtcRecommended.IsChecked = true;
                break;
            default:
                DtcRecommended.IsChecked = true;
                break;
        }
    }

    void PersistDirectThreadCount()
    {
        ClientConfigOptions options = _settings.ClientConfigForPlay();
        ConfigChoice mode = ConfigChoice.Recommended;
        if (DtcLeave.IsChecked == true)
            mode = ConfigChoice.Leave;
        else if (DtcCustom.IsChecked == true)
            mode = ConfigChoice.Custom;

        string value = DtcValueBox.Text.Trim();
        if (mode == ConfigChoice.Custom && ClientConfigFile.Threads.Validate(value) is string bad)
        {
            DtcError.Text = bad + " PLAY will use the recommended 2 until this is fixed.";
            DtcError.Visibility = Visibility.Visible;
        }
        else if (string.IsNullOrWhiteSpace(value))
            value = ClientConfigFile.Threads.Recommended;

        options.Choices[ClientConfigFile.ThreadsKey] = new SettingChoice
        {
            Mode = mode,
            Value = value,
        };
    }

    void AllocatorExpand_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateContourExpandEnabled();
        if (ReadAllocatorExpand() == AllocatorSlotExpand.Off)
            SelectContourExpand(ContourThreadExpand.Off);
        Form_Changed(sender, e);
    }

    void UpdateContourExpandEnabled()
    {
        bool on = ReadAllocatorExpand() is AllocatorSlotExpand.Auto or AllocatorSlotExpand.On;
        ContourExpandBox.IsEnabled = on;
        ContourNeedsAllocatorHint.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (!on)
            SelectContourExpand(ContourThreadExpand.Off);
    }

    void SelectAllocatorExpand(AllocatorSlotExpand mode)
    {
        string tag = mode.ToString();
        foreach (var item in AllocatorExpandBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                AllocatorExpandBox.SelectedItem = item;
                return;
            }
        }
        AllocatorExpandBox.SelectedIndex = 0;
    }

    void SelectContourExpand(ContourThreadExpand mode)
    {
        string tag = mode.ToString();
        foreach (var item in ContourExpandBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                ContourExpandBox.SelectedItem = item;
                return;
            }
        }
        ContourExpandBox.SelectedIndex = 0;
    }

    void SelectLocale(string locale)
    {
        string want = locale ?? "";
        foreach (var item in LocaleBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string ?? "", want, StringComparison.OrdinalIgnoreCase))
            {
                LocaleBox.SelectedItem = item;
                return;
            }
        }
        LocaleBox.SelectedIndex = 0;
    }

    string ReadLocale()
    {
        if (LocaleBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "";
    }

    AllocatorSlotExpand ReadAllocatorExpand()
    {
        if (AllocatorExpandBox.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse(tag, true, out AllocatorSlotExpand mode)
            && Enum.IsDefined(mode))
            return mode;
        return AllocatorSlotExpand.Off;
    }

    ContourThreadExpand ReadContourExpand()
    {
        if (ContourExpandBox.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse(tag, true, out ContourThreadExpand mode)
            && Enum.IsDefined(mode))
            return mode;
        return ContourThreadExpand.Off;
    }

    void SetPhase(HostPhase phase, string label)
    {
        _phase = phase;
        StatusLabel.Text = label;
        StatusLabel.Foreground = phase switch
        {
            HostPhase.Ready => ReadyBrush,
            HostPhase.Starting or HostPhase.Stopping => StartingBrush,
            HostPhase.Failed => FailedBrush,
            _ => StoppedBrush,
        };
        StatusBanner.BorderBrush = StatusLabel.Foreground;
    }

    void UpdateChecklist()
    {
        bool folderOk = LocalSettings.LooksLikeLandmarkFolder(CurrentLandmarkFolder());
        bool accountOk = AccountRules.IsValidAccountName(AdminBox.Text.Trim());
        bool passwordOk = _settings.HasRememberedLogin(AdminBox.Text.Trim())
                          || PasswordBox.Password.Length >= 6;
        bool ready = _phase == HostPhase.Ready;

        ChecklistBox.Text =
            Mark(folderOk) + " Landmark folder (Settings ⚙)\n"
            + Mark(accountOk) + " Account name (this is also your admin)\n"
            + Mark(passwordOk) + " Password for Play (remembered after first Play)\n"
            + Mark(ready) + " Servers ready — or press PLAY to start them";
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

    bool CanStartServers(out string? blocker)
    {
        if (!LocalSettings.LooksLikeLandmarkFolder(CurrentLandmarkFolder()))
        {
            blocker = "Set your Landmark folder in Settings ⚙ (the one with Landmark64.exe) first.";
            return false;
        }
        if (!AccountRules.IsValidAccountName(AdminBox.Text.Trim()))
        {
            blocker = "Account name: 3 to 32 letters, digits or _.";
            return false;
        }
        blocker = null;
        return true;
    }

    void RefreshClientRunning()
    {
        string folder = CurrentLandmarkFolder();
        bool running = folder.Length > 0
                       && LocalSettings.LooksLikeLandmarkFolder(folder)
                       && GameFolder.IsRunning(folder);
        if (running == _clientRunning)
            return;
        _clientRunning = running;
        SetButtons();
    }

    void SetButtons()
    {
        bool serversUpOrMoving = _phase is HostPhase.Starting or HostPhase.Ready or HostPhase.Stopping;
        bool canForm = CanStartServers(out _);
        StartButton.IsEnabled = !_busy && !serversUpOrMoving && canForm;
        StopButton.IsEnabled = !_busy && _phase is HostPhase.Starting or HostPhase.Ready;
        RefreshButton.IsEnabled = !_busy;

        if (_clientRunning)
        {
            PlayButton.Content = "RUNNING";
            PlayButton.IsEnabled = false;
            PlayButton.ToolTip = "Landmark is already running. Close the game first.";
            return;
        }

        PlayButton.Content = "PLAY";
        PlayButton.ToolTip = "Starts the servers if needed, then launches Landmark";
        // PLAY: start+launch when stopped/failed, or launch when ready — needs password when not remembered.
        bool playFormOk = canForm && PasswordReady();
        PlayButton.IsEnabled = !_busy && playFormOk
            && _phase is HostPhase.Stopped or HostPhase.Failed or HostPhase.Ready;
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
            if (_phase is HostPhase.Starting or HostPhase.Stopped or HostPhase.Stopping)
            {
                HostPhase paint = _phase == HostPhase.Stopping ? HostPhase.Stopping : HostPhase.Starting;
                SetPhase(paint, status);
            }
            ApplyProgressFromLine(status);
            return;
        }
        Append(line);
        ApplyProgressFromLine(line);
    }

    void ApplyProgressFromLine(string line)
    {
        if (StartupProgress.TryParse(line, out double percent, out bool determinate) && determinate)
        {
            SetupProgress.Visibility = Visibility.Visible;
            SetupProgress.IsIndeterminate = false;
            SetupProgress.Value = percent;
        }
        else if (_phase is HostPhase.Starting or HostPhase.Stopping)
        {
            // Keep bar visible during waits without a % — indeterminate.
            if (SetupProgress.Visibility != Visibility.Visible || !SetupProgress.IsIndeterminate)
                ShowProgressIndeterminate();
        }
    }

    void ShowProgressIndeterminate()
    {
        SetupProgress.Visibility = Visibility.Visible;
        SetupProgress.IsIndeterminate = true;
    }

    void HideProgress()
    {
        SetupProgress.IsIndeterminate = false;
        SetupProgress.Value = 0;
        SetupProgress.Visibility = Visibility.Collapsed;
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

    void EnsureTray()
    {
        if (_tray != null)
            return;
        _tray = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "LandmarkLocal",
            Visible = false,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open LandmarkLocal", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Stop servers", null, async (_, _) =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                ShowFromTray();
                if (!_busy && _phase is HostPhase.Ready or HostPhase.Starting)
                    await StopServersAsync();
            });
        });
        menu.Items.Add("Quit", null, async (_, _) =>
        {
            await Dispatcher.InvokeAsync(async () => await QuitFromTrayAsync());
        });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    void UpdateTrayText()
    {
        if (_tray == null)
            return;
        _tray.Text = _phase switch
        {
            HostPhase.Ready => "LandmarkLocal — servers running",
            HostPhase.Starting => "LandmarkLocal — starting…",
            HostPhase.Stopping => "LandmarkLocal — stopping…",
            _ => "LandmarkLocal",
        };
    }

    void ShowInTray()
    {
        EnsureTray();
        UpdateTrayText();
        _tray!.Visible = true;
        _tray.ShowBalloonTip(3000, "LandmarkLocal",
            "Servers are still running. Double-click the tray icon to open again.",
            Forms.ToolTipIcon.Info);
        Hide();
    }

    void ShowFromTray()
    {
        Show();
        WindowState = _lastNonMinimized == WindowState.Minimized ? WindowState.Normal : _lastNonMinimized;
        Activate();
        if (_tray != null)
            _tray.Visible = false;
    }

    async Task QuitFromTrayAsync()
    {
        if (_busy)
            return;
        if (_phase is HostPhase.Ready or HostPhase.Starting)
            await StopServersAsync();
        _forceClose = true;
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Close();
    }

    void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized
            && _phase is HostPhase.Ready or HostPhase.Starting)
        {
            ShowInTray();
            return;
        }
        if (WindowState != WindowState.Minimized)
            _lastNonMinimized = WindowState;
    }

    async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            return;
        }

        if (_phase is HostPhase.Stopped or HostPhase.Failed)
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            return;
        }

        if (_phase is HostPhase.Stopping || _closeInProgress)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        MessageBoxResult choice = System.Windows.MessageBox.Show(this,
            "Servers are still running.\n\n" +
            "Yes — Stop servers and quit\n" +
            "No — Leave servers running (stay in the tray)\n" +
            "Cancel — Keep this window open",
            "LandmarkLocal",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
            return;

        if (choice == MessageBoxResult.No)
        {
            ShowInTray();
            return;
        }

        // Yes — stop then quit
        _closeInProgress = true;
        try
        {
            if (!_busy)
                await StopServersAsync();
            _forceClose = true;
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            Close();
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clientPoll.Stop();
        _cts?.Cancel();
        // Do not stop servers here — Closing already handled Stop-and-quit vs leave-running.
        _host?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        base.OnClosed(e);
    }
}
