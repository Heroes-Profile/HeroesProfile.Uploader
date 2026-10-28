using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
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

        // The UI scale the window's current size was set for, so a scale change can resize it in proportion.
        private double _shownScale;

        // Where the window last was and how big while normal (not minimized or maximized), kept as it
        // moves and resizes. By the time it closes it may be hidden in the tray or minimized, and Windows
        // parks a minimized window at -32000,-32000, so its bounds then aren't the ones to save.
        private PixelPoint? _normalPosition;
        private Size? _normalSize;
        private bool _placementCapturePending;

        public MainWindow()
        {
            InitializeComponent();
            SetUpTitleBar();
            DataContextChanged += (_, __) => RestorePlacement();
            PropertyChanged += OnWindowPropertyChanged;
            PositionChanged += (_, __) => QueuePlacementCapture();
            Resized += (_, __) => QueuePlacementCapture();
            Opened += OnOpened;
            Closing += (_, __) => SavePlacement();
        }

        /// <summary>
        /// Windows: extend into the title bar, so the size buttons sit next to minimize/maximize/close
        /// (Avalonia then draws the title bar itself; its fullscreen button is hidden in Styles.axaml).
        /// Elsewhere the system's title bar stays, with the buttons on a strip below it.
        /// </summary>
        private void SetUpTitleBar()
        {
            if (!OperatingSystem.IsWindows()) {
                return;
            }
            ExtendClientAreaToDecorationsHint = true;
            // Room for the caption buttons on the right, and the title bar's height once it's known.
            ScaleButtons.Margin = new Thickness(0, 0, CaptionButtonsWidth + 8, 0);
            void MatchTitleBarHeight() => TitleBar.Height = Math.Max(WindowDecorationMargin.Top, TitleBar.MinHeight);
            PropertyChanged += (_, e) => {
                if (e.Property == WindowDecorationMarginProperty) {
                    MatchTitleBarHeight();
                }
            };
            Opened += (_, __) => MatchTitleBarHeight();
        }

        // Avalonia's drawn minimize/maximize/close: three 46px buttons.
        private const double CaptionButtonsWidth = 138;

        /// <summary>Puts the window back where the user left it, unless that's no longer on any screen.</summary>
        private void RestorePlacement()
        {
            if (ViewModel == null) {
                return;
            }
            var config = ViewModel.Config;
            _shownDesign = ViewModel.Design;
            _shownScale = ViewModel.UiScale;
            ApplySize(config.WindowSizeFor(_shownDesign));
            ViewModel.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(MainWindowViewModel.Design)) {
                    SwitchSize();
                } else if (e.PropertyName == nameof(MainWindowViewModel.UiScale)) {
                    Rescale();
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

        /// <summary>
        /// Windows moves and resizes a window it's minimizing or maximizing before it reports the new
        /// WindowState, so the bounds are read once that has settled (and once per burst while dragging).
        /// </summary>
        private void QueuePlacementCapture()
        {
            if (_placementCapturePending) {
                return;
            }
            _placementCapturePending = true;
            Dispatcher.UIThread.Post(() => {
                _placementCapturePending = false;
                CapturePlacement();
            }, DispatcherPriority.Background);
        }

        private void CapturePlacement()
        {
            if (WindowState == WindowState.Normal && IsVisible) {
                _normalPosition = Position;
                _normalSize = new Size(Width, Height);
            }
        }

        /// <summary>
        /// Saves where the window last was while normal - also when it's closed from the tray, closed
        /// minimized or maximized, or the app is restarting for an update.
        /// </summary>
        private void SavePlacement()
        {
            CapturePlacement();
            if (ViewModel == null || _normalPosition is not PixelPoint position) {
                return;
            }
            var config = ViewModel.Config;
            config.WindowLeft = position.X;
            config.WindowTop = position.Y;
            if (_normalSize is Size size) {
                config.RememberWindowSize(_shownDesign, size.Width, size.Height);
            }
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
            CapturePlacement();
            if (_normalSize is Size size) {
                config.RememberWindowSize(_shownDesign, size.Width, size.Height);
            }
            // The size captured from here on is the new design's; until then it keeps what it had.
            _normalSize = null;
            _shownDesign = ViewModel.Design;
            if (WindowState == WindowState.Normal) {
                ApplySize(config.WindowSizeFor(_shownDesign));
            }
        }

        /// <summary>The 1x/1.5x/2x changed: scale the window with its contents, so the layout keeps its proportions.</summary>
        private void Rescale()
        {
            var ratio = ViewModel.UiScale / _shownScale;
            _shownScale = ViewModel.UiScale;
            if (WindowState != WindowState.Normal) {
                MinWidth = ViewModel.MinWindowWidth;
                return;
            }
            // Kept on the screen it's on: no bigger than its working area, and moved up/left if it grew past its edge.
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            var width = Width * ratio;
            var height = Height * ratio;
            if (screen != null) {
                var max = screen.WorkingArea.Size.ToSize(screen.Scaling);
                width = Math.Min(width, max.Width);
                height = Math.Min(height, max.Height);
            }
            ApplySize((width, height));
            if (screen != null) {
                var area = screen.WorkingArea;
                var size = PixelSize.FromSize(new Size(Width, Height), screen.Scaling);
                Position = new PixelPoint(
                    Math.Max(area.X, Math.Min(Position.X, area.Right - size.Width)),
                    Math.Max(area.Y, Math.Min(Position.Y, area.Bottom - size.Height)));
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
            // Opened fires on every Show(), and restoring from the tray shows the window again - these
            // are questions for the first time it opens, not every time it comes back from the tray.
            Opened -= OnOpened;

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

        /// <summary>
        /// Whether <see cref="OnOpened"/> has something to ask as soon as the window opens: the old uploader
        /// installed or still running, or no replay folder. Uploading waits on those, so a start to the tray
        /// shows the window instead.
        /// </summary>
        public bool NeedsAttentionAtStartup =>
            ViewModel?.WaitingForLegacyApp == true || ViewModel?.ReplayFolderError != null || (OperatingSystem.IsWindows() && LegacyApp.IsInstalled());

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
            // Velopack ends the process without closing the window, so Closing never saves its placement.
            SavePlacement();
            // Minimized to the tray right now (hidden, not just iconified) - relaunch the same way so the
            // new process doesn't suddenly pop a window the user had tucked away.
            ViewModel.RestartNow(minimized: !IsVisible);
        }

        /// <summary>Un-hides the window - the tray icon's "Open" item, or clicking the tray icon itself.</summary>
        public void RestoreFromTray()
        {
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
