using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Heroesprofile.Uploader.Desktop.Gui.ViewModels;
using Heroesprofile.Uploader.Desktop.Migration;
using System;

namespace Heroesprofile.Uploader.Desktop.Gui.Views
{
    /// <summary>
    /// The main window. Business logic lives in <see cref="MainWindowViewModel"/> - this only handles
    /// what needs a Window: remembering its placement, the Settings dialog, the missing-folder message,
    /// and the WPF app's tray behaviour (minimizing hides it to the tray when "Minimize to tray" is on;
    /// closing it quits).
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

        // The design whose size the window currently has - each design remembers its own (AppConfig).
        private string _shownDesign;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, __) => RestorePlacement();
            PropertyChanged += OnWindowPropertyChanged;
            Opened += OnOpened;
            Closing += (_, __) => SavePlacement();
        }

        /// <summary>Puts the window back where the user left it, unless that's no longer on any screen.</summary>
        private void RestorePlacement()
        {
            if (ViewModel == null) {
                return;
            }
            var config = ViewModel.Config;
            _shownDesign = ViewModel.Design;
            ApplySize(config.WindowSizeFor(_shownDesign));
            ViewModel.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(MainWindowViewModel.Design)) {
                    SwitchSize();
                }
            };

            var position = new PixelPoint(config.WindowLeft, config.WindowTop);
            if (Screens.ScreenFromPoint(position) != null) {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = position;
            } else {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void SavePlacement()
        {
            if (ViewModel == null || WindowState != WindowState.Normal || !IsVisible) {
                return;
            }
            var config = ViewModel.Config;
            config.WindowLeft = Position.X;
            config.WindowTop = Position.Y;
            config.RememberWindowSize(_shownDesign, Width, Height);
            ViewModel.SaveConfig();
        }

        private void ApplySize((double Width, double Height) size)
        {
            // Each design has its own minimum width (Theme 2 is laid out narrower).
            MinWidth = ViewModel.MinWindowWidth;
            Width = Math.Max(size.Width, MinWidth);
            Height = Math.Max(size.Height, MinHeight);
        }

        /// <summary>Settings' Design changed: keep the old design's size, take the new one's.</summary>
        private void SwitchSize()
        {
            var config = ViewModel.Config;
            if (WindowState == WindowState.Normal && IsVisible) {
                config.RememberWindowSize(_shownDesign, Width, Height);
            }
            _shownDesign = ViewModel.Design;
            if (WindowState == WindowState.Normal) {
                ApplySize(config.WindowSizeFor(_shownDesign));
            }
        }

        private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && ViewModel?.Config.MinimizeToTray == true) {
                Hide();
            }
        }

        private async void OnOpened(object sender, EventArgs e)
        {
            // The old WPF uploader still installed: offer to uninstall it (this app already has its own
            // copies of the settings and history), so it stops starting with Windows and can't run next
            // to this one. Asked again next start if kept.
            if (OperatingSystem.IsWindows() && LegacyApp.IsInstalled()) {
                var answer = await MessageDialog.ShowAsync(this, "Old uploader installed", LegacyApp.RemovePrompt, "Uninstall", "Keep for now");
                if (answer == "Uninstall") {
                    try {
                        await System.Threading.Tasks.Task.Run(LegacyApp.Remove);
                    }
                    catch (Exception ex) {
                        _log.Warn(ex, "Could not remove the old uploader");
                        await MessageDialog.ShowAsync(this, "Old uploader installed",
                            $"Couldn't uninstall the old uploader: {ex.Message}\n\nYou can uninstall it from Settings → Apps instead. " +
                            "Your settings and upload history in this app are kept either way.");
                    }
                    // It's gone (and stopped), so there's nothing to wait for any more.
                    if (ViewModel?.WaitingForLegacyApp == true && !LegacyApp.IsRunning()) {
                        ViewModel.ContinueNextToLegacyApp();
                    }
                }
            } else if (OperatingSystem.IsWindows()) {
                await System.Threading.Tasks.Task.Run(LegacyApp.CleanUpLeftovers);
            }

            if (ViewModel?.WaitingForLegacyApp == true) {
                var answer = await MessageDialog.ShowAsync(this, "Old uploader running", LegacyApp.RunningWarning, "Quit", "Run anyway");
                if (answer != "Run anyway") {
                    Close();
                    return;
                }
                ViewModel.ContinueNextToLegacyApp();
            }

            // WPF's WarnIfReplayFolderMissing: without a replay folder nothing gets uploaded, so say so
            // once, up front, and point at the setting.
            if (ViewModel?.ReplayFolderError is string error) {
                var choice = await MessageDialog.ShowAsync(this, "Replay folder not found",
                    // Linux leads with the prefix, like Settings: it's the easy pick, and the replays are found from it.
                    $"{error}\n\n" + (OperatingSystem.IsLinux()
                        ? "Open Settings and select your Wine/Proton prefix (the folder containing drive_c), or the Heroes of the Storm \"Accounts\" folder itself."
                        : "Open Settings and select the Heroes of the Storm \"Accounts\" folder."),
                    "Open Settings", "Later");
                if (choice == "Open Settings") {
                    await OpenSettingsAsync();
                }
            }
        }

        /// <summary>The logo (Theme 1) or brand mark (Theme 2): opens heroesprofile.com.</summary>
        public void OpenWebsite() => ViewModel?.OpenWebsiteCommand.Execute(null);

        public async System.Threading.Tasks.Task OpenSettingsAsync()
        {
            var dialog = new SettingsWindow { DataContext = new SettingsWindowViewModel(ViewModel) };
            await dialog.ShowDialog(this);
        }

        /// <summary>The update banner's "Restart now." link.</summary>
        public void RestartNow()
        {
            // Minimized to the tray right now (hidden, not just iconified) - relaunch the same way so the
            // new process doesn't suddenly pop a window the user had tucked away.
            ViewModel.RestartNow(minimized: !IsVisible);
        }

        /// <summary>Un-hides the window - the tray icon's "Open" item, or clicking the tray icon itself.</summary>
        public void RestoreFromTray()
        {
            // Started with --minimized, the window was kept off the taskbar until now.
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>The tray icon's "Quit" item.</summary>
        public void QuitForReal()
        {
            Close();
        }
    }
}
