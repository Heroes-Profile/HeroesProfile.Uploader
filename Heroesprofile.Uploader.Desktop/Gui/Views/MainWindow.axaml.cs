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
            Width = Math.Max(config.WindowWidth, MinWidth);
            Height = Math.Max(config.WindowHeight, MinHeight);

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
            config.WindowWidth = Width;
            config.WindowHeight = Height;
            ViewModel.SaveConfig();
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
                    $"{error}\n\nOpen Settings and select the Heroes of the Storm \"Accounts\" folder" +
                    (OperatingSystem.IsLinux() ? " (or the Wine/Proton prefix it's in)" : "") + ".",
                    "Open Settings", "Later");
                if (choice == "Open Settings") {
                    await OpenSettingsAsync();
                }
            }
        }

        private void Logo_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            ViewModel.OpenWebsiteCommand.Execute(null);
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync();

        private async System.Threading.Tasks.Task OpenSettingsAsync()
        {
            var dialog = new SettingsWindow { DataContext = new SettingsWindowViewModel(ViewModel) };
            await dialog.ShowDialog(this);
        }

        private void RestartNow_Click(object sender, RoutedEventArgs e)
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
