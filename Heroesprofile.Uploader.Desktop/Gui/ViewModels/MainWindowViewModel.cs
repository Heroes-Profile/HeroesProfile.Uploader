using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Migration;
using Heroesprofile.Uploader.Desktop.Platform;
using Heroesprofile.Uploader.Desktop.Updates;
using NLog;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Desktop.Gui.ViewModels
{
    /// <summary>
    /// Drives the main window, which mirrors the WPF app's MainWindow: replay list, status and per-status
    /// counts, the option checkboxes, Settings/Show log/Check for update, and the update banner. Owns
    /// config.json and the Manager. Manager fires from background threads, so every handler here
    /// marshals back to the UI thread before touching bound state.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static IPlatform Platform => Platforms.Current;

        public AppConfig Config { get; }
        public Manager Manager { get; private set; }

        /// <summary>WPF's title: "Heroesprofile.com Uploader v2.9" (patch and prerelease only when there is one).</summary>
        public string WindowTitle { get; } = $"Heroesprofile.com Uploader {VersionString(ReleaseVersion.Current())}";

        /// <summary>Theme 2's footer shows the version (Theme 1 has it in the title only, as in WPF).</summary>
        public string VersionText { get; } = VersionString(ReleaseVersion.Current());

        /// <summary>Main-window layout: <see cref="AppConfig.Theme1Design"/> or <see cref="AppConfig.Theme2Design"/>.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTheme2), nameof(MinWindowWidth))]
        private string design;

        public bool IsTheme2 => Design == AppConfig.Theme2Design;

        /// <summary>Theme 2 was designed for a narrower window (PR #53's 360px minimum).</summary>
        public double MinWindowWidth => IsTheme2 ? 360 : 480;

        /// <summary>Theme 2's stats grid - the same counts as <see cref="StatusCounts"/>, in PR #53's order and labels.</summary>
        public ObservableCollection<StatusCountViewModel> StatChips { get; } =
            new ObservableCollection<StatusCountViewModel>(StatusCountViewModel.ChipOrder.Select(s => new StatusCountViewModel(s, ReplayRowViewModel.ShortLabel(s))));

        /// <summary>Theme 2: "1,234 replays" under the list.</summary>
        [ObservableProperty]
        private string listCaption = "0 replays";

        /// <summary>Theme 2's header status colour: uploading (in progress) or with failed uploads.</summary>
        [ObservableProperty]
        private bool isUploading;

        public string FailedUploadsText => FailedCount == 1 ? "1 upload failed" : $"{FailedCount:N0} uploads failed";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FailedUploadsText))]
        private int failedCount;

        public string PauseButtonTooltip => IsPaused ? "Resume uploading" : "Pause uploading";

        [ObservableProperty]
        private ObservableCollection<ReplayRowViewModel> rows = new ObservableCollection<ReplayRowViewModel>();

        public ObservableCollection<StatusCountViewModel> StatusCounts { get; } =
            new ObservableCollection<StatusCountViewModel>(StatusCountViewModel.Lines.Select(l => new StatusCountViewModel(l.Status, l.Label)));

        /// <summary>The bold line above the counts: Manager's own "Idle"/"Uploading...", or "Paused".</summary>
        [ObservableProperty]
        private string statusText = Heroesprofile.Uploader.Common.Manager.IdleStatus;

        /// <summary>
        /// Set when no replay folder could be found at startup - the view shows it the way the WPF app's
        /// WarnIfReplayFolderMissing message box does, pointing the user at Settings.
        /// </summary>
        public string ReplayFolderError { get; private set; }

        [ObservableProperty]
        private bool isPaused;

        [ObservableProperty]
        private string twitchStatus = "";

        [ObservableProperty]
        private string updateStatusText = "";

        private string _updateReleaseUrl;

        /// <summary>An update is downloaded and ready - shows the WPF app's orange banner with "Restart now."</summary>
        [ObservableProperty]
        private bool showRestartBanner;

        private readonly AppUpdater _updater = new AppUpdater();
        private bool _restarting;

        [ObservableProperty]
        private bool postMatchPage;

        [ObservableProperty]
        private bool preMatchPage;

        [ObservableProperty]
        private bool twitchExtension;

        [ObservableProperty]
        private bool minimizeToTray;

        [ObservableProperty]
        private bool startOnLogin;

        /// <summary>Not a config setting: mirrors whether the app-menu entry exists (also set by the `install` CLI command).</summary>
        [ObservableProperty]
        private bool showInAppMenu;

        public bool SupportsStartOnLogin => Platform.SupportsStartOnLogin;

        /// <summary>"Show in app menu" is Linux-only: Windows and macOS installers add the app to their menus themselves.</summary>
        public bool SupportsAppMenuEntry => OperatingSystem.IsLinux();

        // The WPF wording, adjusted where the OS calls it something else.
        public string StartOnLoginLabel => OperatingSystem.IsWindows() ? "Start with windows" : "Start on login";
        public string MinimizeToTrayLabel => OperatingSystem.IsMacOS() ? "Minimize to menu bar" : "Minimize to tray";

        /// <summary>Title of the replay folder picker in Settings.</summary>
        public static string BrowseTitle => Platforms.Current.ReplayPathIsWinePrefix
            ? "Select the Wine/Proton prefix (or the Heroes of the Storm \"Accounts\" folder)"
            : "Select the Heroes of the Storm \"Accounts\" folder";

        private ReplayListBridge _bridge;

        public MainWindowViewModel()
        {
            Config = LoadConfigSafely();
            design = Config.Design;

            // Assign backing fields directly (not the generated properties) so this initial sync
            // doesn't itself trigger the OnXChanged handlers below, which persist to config.json.
            postMatchPage = Config.PostMatchPage;
            preMatchPage = Config.PreMatchPage;
            twitchExtension = Config.TwitchExtension;
            minimizeToTray = Config.MinimizeToTray;
            startOnLogin = Config.StartOnLogin;
            showInAppMenu = OperatingSystem.IsLinux() && DesktopIntegration.IsAppMenuEntryInstalled;

            if (!ReplayFolderSetup.TryApply(Platform, Config.ReplayPath, out var error)) {
                ReplayFolderError = error;
                _log.Warn($"Replay folder not found: {error}");
                return;
            }

            // Don't start uploading next to the WPF app - the window asks first (see ContinueNextToLegacyApp).
            WaitingForLegacyApp = LegacyApp.IsRunning();
            if (WaitingForLegacyApp) {
                _log.Warn(LegacyApp.RunningWarning);
                return;
            }
            StartManager();
        }

        /// <summary>True while the WPF uploader is running and the user hasn't chosen to run alongside it.</summary>
        public bool WaitingForLegacyApp { get; private set; }

        /// <summary>The user chose "Run anyway" with the WPF app still running.</summary>
        public void ContinueNextToLegacyApp()
        {
            WaitingForLegacyApp = false;
            if (ReplayFolderError == null) {
                StartManager();
            }
        }

        private static string VersionString(ReleaseVersion version)
        {
            var text = $"v{version.Major}.{version.Minor}" + (version.Patch == 0 ? "" : $".{version.Patch}");
            return version.PreRelease.Length == 0 ? text : $"{text}-{string.Join(".", version.PreRelease)}";
        }

        private static AppConfig LoadConfigSafely()
        {
            try {
                return AppConfig.LoadOrImport();
            }
            catch (ConfigError ex) {
                // Corrupt config.json - log it and start from defaults rather than refusing to launch;
                // the user can fix things up again from Settings (which will overwrite the bad file).
                _log.Error(ex.Message);
                return new AppConfig();
            }
        }

        private void StartManager()
        {
            if (Manager != null) {
                return;
            }

            Directory.CreateDirectory(AppConfig.DataDir);
            Manager = new Manager(new ReplayStorage(Path.Combine(AppConfig.DataDir, "replays_v8.xml"))) {
                PreMatchPage = Config.PreMatchPage,
                PostMatchPage = Config.PostMatchPage,
                DeleteAfterUpload = Config.DeleteAfterUpload,
            };
            WebhookNotifier.WebhookUrl = Config.WebhookUrl;
            Manager.Twitch.Key = Config.TwitchUploaderKey;
            Manager.Twitch.Enabled = Config.TwitchExtension;
            Manager.Twitch.StatusChanged += (_, e) => Dispatcher.UIThread.Post(() => TwitchStatus = e.Data);

            _bridge = new ReplayListBridge(Manager);
            Rows = _bridge.Rows;
            Rows.CollectionChanged += (_, __) => UpdateListCaption();
            UpdateListCaption();

            Manager.PropertyChanged += (_, e) => Dispatcher.UIThread.Post(() => OnManagerPropertyChanged(e.PropertyName));

            _log.Info($"Starting on {Platform.Name}: replayPath={Config.ReplayPath}, accounts={ReplayLocation.Current}, " +
                $"preMatchPage={Config.PreMatchPage}, postMatchPage={Config.PostMatchPage}, " +
                $"twitchExtension={Config.TwitchExtension}, webhook={(string.IsNullOrWhiteSpace(Config.WebhookUrl) ? "off" : "on")}");

            // Common.Uploader, spelled out - see RunCommand for why the plain name resolves wrong here.
            Manager.Start(SettledMonitor.ForPlatform(Platform), new LiveMonitor(), new Analyzer(), new Common.Uploader(), new LiveProcessor(Manager.PreMatchPage, Manager.Twitch));

            RefreshStatus();
        }

        private void OnManagerPropertyChanged(string propertyName)
        {
            if (propertyName == nameof(Manager.Paused)) {
                IsPaused = Manager.Paused;
            }
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var totals = Manager?.Aggregates;
            foreach (var line in StatusCounts.Concat(StatChips)) {
                line.Count = totals != null && totals.TryGetValue(line.Status, out var n) ? n : 0;
            }
            StatusText = Manager == null ? Heroesprofile.Uploader.Common.Manager.IdleStatus
                : IsPaused ? "Paused"
                : Manager.Status;
            // Fully qualified: the "Manager" property on this class shadows the "Manager" type name.
            IsUploading = !IsPaused && Manager?.Status == Heroesprofile.Uploader.Common.Manager.UploadingStatus;
            FailedCount = StatusCounts.Where(l => l.Status == UploadStatus.UploadError).Sum(l => l.Count);
            HasFailedUploads = FailedCount > 0;
        }

        private void UpdateListCaption()
        {
            ListCaption = Rows.Count == 1 ? "1 replay" : $"{Rows.Count:N0} replays";
        }

        partial void OnIsPausedChanged(bool value)
        {
            OnPropertyChanged(nameof(PauseButtonTooltip));
            RefreshStatus();
        }

        /// <summary>Shows "Retry failed uploads" - only while there's something to retry, so the panel otherwise looks as in WPF.</summary>
        [ObservableProperty]
        private bool hasFailedUploads;

        /// <summary>Tries every failed upload again now, instead of waiting for the next start of the app.</summary>
        [RelayCommand]
        private void RetryFailed()
        {
            Manager?.RetryFailed();
        }

        /// <summary>
        /// Not offered anywhere in the GUI any more: the Theme 2 header button and the tray menu's
        /// "Pause uploading" item are commented out, not deleted.
        /// Pausing replay uploads makes it easier for upload abusers to pause between games to remove losses from upload queue.
        /// </summary>
        [RelayCommand]
        private void TogglePause()
        {
            if (Manager != null) {
                Manager.Paused = !Manager.Paused;
            }
        }

        partial void OnPostMatchPageChanged(bool value)
        {
            Config.PostMatchPage = value;
            if (Manager != null) {
                Manager.PostMatchPage = value;
            }
            SaveConfig();
        }

        partial void OnPreMatchPageChanged(bool value)
        {
            Config.PreMatchPage = value;
            if (Manager != null) {
                Manager.PreMatchPage = value;
            }
            SaveConfig();
        }

        partial void OnTwitchExtensionChanged(bool value)
        {
            Config.TwitchExtension = value;
            Manager?.SetTwitchEnabled(value);
            TwitchStatus = value ? "Twitch extension on. Waiting for a game." : "";
            SaveConfig();
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            Config.MinimizeToTray = value;
            SaveConfig();
        }

        partial void OnStartOnLoginChanged(bool value)
        {
            Config.StartOnLogin = value;
            try {
                Platform.SetStartOnLogin(value);
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not update the autostart entry");
            }
            SaveConfig();
        }

        partial void OnShowInAppMenuChanged(bool value)
        {
            if (!OperatingSystem.IsLinux()) {
                return;
            }
            try {
                if (value) {
                    var whyNot = DesktopIntegration.WhyNotInstallable();
                    if (whyNot != null) {
                        _log.Warn(whyNot);
                    } else {
                        DesktopIntegration.InstallAppMenuEntry();
                    }
                } else {
                    DesktopIntegration.RemoveAppMenuEntry();
                }
                // The autostart entry points at the ~/.local/bin copy when there is one, so rewrite
                // it to follow that copy appearing or going away. If this *is* that copy, nothing
                // would be left to start at login, so turn Start on login off instead.
                if (StartOnLogin && !value && DesktopIntegration.IsRunningInstalledCopy) {
                    _log.Warn("Removed from the app menu while running from it - turning off Start on login too.");
                    StartOnLogin = false;
                } else if (StartOnLogin) {
                    DesktopIntegration.SetStartOnLogin(true);
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not update the app menu entry");
            }

            if (DesktopIntegration.IsAppMenuEntryInstalled != value) {
                // Didn't take; put the checkbox back without re-running this handler.
                showInAppMenu = !value;
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ShowInAppMenu)));
            }
        }

        public void SaveConfig()
        {
            try {
                Config.Save();
            }
            catch (Exception ex) {
                _log.Error(ex, "Could not save settings");
            }
        }

        // Settings applies each change as it's made, like the WPF SettingsWindow's two-way bindings.

        /// <summary>Points the app at a new replay path, starting the Manager if there wasn't a usable folder before.</summary>
        public void ApplyReplayPath(string replayPath)
        {
            Config.ReplayPath = string.IsNullOrWhiteSpace(replayPath) ? null : replayPath.Trim();
            // ReplayLocation.Changed (subscribed inside Manager.Start) reloads the folder for us if a
            // Manager is already running; if there wasn't a folder to start one with, start it now.
            if (ReplayFolderSetup.TryApply(Platform, Config.ReplayPath, out _)) {
                ReplayFolderError = null;
                if (!WaitingForLegacyApp) {
                    StartManager();
                }
            }
        }

        /// <summary>Settings' Design choice - switches the main window's layout straight away.</summary>
        public void ApplyDesign(string value)
        {
            Config.Design = value;
            Design = Config.Design;
        }

        public void ApplyTheme(string theme)
        {
            Config.Theme = theme;
            if (Application.Current != null) {
                Application.Current.RequestedThemeVariant = App.ThemeVariantFor(Config.Theme);
            }
        }

        public void ApplyTwitchKey(string key)
        {
            Config.TwitchUploaderKey = key?.Trim() ?? "";
            if (Manager != null) {
                Manager.Twitch.Key = Config.TwitchUploaderKey;
            }
        }

        public void ApplyWebhookUrl(string url)
        {
            Config.WebhookUrl = url?.Trim() ?? "";
            WebhookNotifier.WebhookUrl = Config.WebhookUrl;
        }

        /// <summary>WPF's "Show log" opens the logs folder, not the file.</summary>
        [RelayCommand]
        private void ShowLog()
        {
            try {
                var dir = Path.GetDirectoryName(Logging.LogFilePath);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not open the log folder");
            }
        }

        /// <summary>Opens heroesprofile.com - the WPF app's logo click.</summary>
        [RelayCommand]
        private void OpenWebsite() => OpenUrl("https://www.heroesprofile.com/");

        private static void OpenUrl(string url)
        {
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Could not open {url}");
            }
        }

        /// <summary>Startup/hourly auto-check, wired up by App.axaml.cs - the WPF app's own cadence.</summary>
        public Task RunAutoUpdateCheckAsync() => Config.AutoUpdate ? CheckForUpdateCoreAsync(manual: false) : Task.CompletedTask;

        [RelayCommand]
        private Task CheckForUpdateAsync() => CheckForUpdateCoreAsync(manual: true);

        /// <summary>
        /// The same check either way - the manual button just ignores AutoUpdate and reports what it
        /// found, where the automatic one stays quiet unless there's an update to install.
        /// </summary>
        private async Task CheckForUpdateCoreAsync(bool manual)
        {
            if (manual) {
                UpdateStatusText = "Checking…";
                _updateReleaseUrl = null;
            }

            var result = await _updater.CheckAndDownloadAsync(Config);
            switch (result.Outcome) {
                case AppUpdater.Outcome.ReadyToRestart:
                    ShowRestartBanner = true;
                    if (manual) {
                        UpdateStatusText = "";
                    }
                    break;

                case AppUpdater.Outcome.NoUpdate:
                    if (manual) {
                        UpdateStatusText = "You're up to date.";
                    }
                    break;

                case AppUpdater.Outcome.NotInstalled:
                    // A copy Velopack can't update (the Linux tarball, a dev build): point at the release
                    // instead - on the automatic check too, or tarball users would never hear of updates.
                    await ReportNewerReleaseAsync(manual);
                    break;

                default: // Failed - already logged.
                    if (manual) {
                        UpdateStatusText = "Couldn't check for updates - see log.";
                    }
                    break;
            }
        }

        /// <summary>
        /// Looks for a newer GitHub release and shows the "Update available" link if there is one. Only
        /// the manual check (<paramref name="manual"/>) also says "up to date" or reports a failure.
        /// </summary>
        private async Task ReportNewerReleaseAsync(bool manual)
        {
            try {
                var release = await new ReleaseChecker().FindNewerAsync(Config.UpdateRepository, Config.AllowPreReleases);
                if (release == null) {
                    if (manual) {
                        UpdateStatusText = "You're up to date.";
                    }
                } else {
                    if (_updateReleaseUrl != release.Url) {
                        _log.Info($"Update available: v{release.Version} ({release.Url}) - this copy can't install it itself.");
                    }
                    _updateReleaseUrl = release.Url;
                    UpdateStatusText = $"Update available: v{release.Version} — click to open the release page.";
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Update check failed");
                if (manual) {
                    UpdateStatusText = "Couldn't check for updates - see log.";
                }
            }
        }

        [RelayCommand]
        private void OpenUpdate()
        {
            if (!string.IsNullOrEmpty(_updateReleaseUrl)) {
                OpenUrl(_updateReleaseUrl);
            }
        }

        /// <summary>The banner's "Restart now." link - called from MainWindow's code-behind, which
        /// knows whether the window is currently hidden (tray) and passes that through as --minimized.</summary>
        public void RestartNow(bool minimized)
        {
            if (_restarting) {
                return;
            }
            _restarting = true;
            try {
                // Finish what's in flight and save the upload history before Velopack ends this process.
                Manager?.Stop();
                SaveConfig();
                if (!_updater.ApplyAndRestart(minimized)) {
                    _log.Warn("Restart now: no downloaded update to install.");
                    _restarting = false;
                }
            }
            catch (Exception ex) {
                _log.Error(ex, "Restart now failed");
                _restarting = false;
            }
        }
    }
}
