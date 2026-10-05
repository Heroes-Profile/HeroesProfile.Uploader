using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Heroesprofile.Uploader.Desktop.Gui.ViewModels;
using Heroesprofile.Uploader.Desktop.Gui.Views;
using NLog;
using System;

namespace Heroesprofile.Uploader.Desktop.Gui
{
    public partial class App : Application
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>Set by <see cref="Gui.Run"/> before the framework initialization event fires.</summary>
        public static bool StartMinimized { get; set; }

        /// <summary>Set by <see cref="Gui.Run"/>; another launch asks through it for this window to be shown.</summary>
        internal static SingleInstance Instance { get; set; }

        private MainWindowViewModel _viewModel;
        private MainWindow _window;
        private TrayIcon _trayIcon;
        private DispatcherTimer _updateTimer;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Dispatcher.UIThread.UnhandledException += (_, e) => _log.Error(e.Exception, "Unhandled UI exception");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                _viewModel = new MainWindowViewModel();
                RequestedThemeVariant = ThemeVariantFor(_viewModel.Config.Theme);

                _window = new MainWindow { DataContext = _viewModel };
                desktop.MainWindow = _window;

                // TrayIcon/NativeMenuItem, declared under <TrayIcon.Icons> in App.axaml, aren't part
                // of a regular control tree - x:Name doesn't generate a field for them the way it does
                // for a Window's children, so the tray icon is fetched via the attached-property
                // getter instead.
                _trayIcon = TrayIcon.GetIcons(this)[0];

                // App.axaml's tray.png is the simplified logo (Linux panels scale it down). Windows picks
                // the exact 16/20/24 px size for its DPI from the .ico, so it stays sharp; the macOS menu
                // bar wants a black "template" image it tints to match a light or dark menu bar.
                if (OperatingSystem.IsWindows()) {
                    _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://heroesprofile-uploader/Gui/Assets/app-icon.ico")));
                } else if (OperatingSystem.IsMacOS()) {
                    _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://heroesprofile-uploader/Gui/Assets/icons/tray-mac-template.png")));
                    MacOSProperties.SetIsTemplateIcon(_trayIcon, true);
                }

                // Tray icon shows only while the window is hidden - mirrors the Windows app's own
                // NotifyIcon.Visible toggling in MainWindow.xaml.cs/App.xaml.cs.
                _window.PropertyChanged += (_, e) => {
                    if (e.Property == Visual.IsVisibleProperty) {
                        _trayIcon.IsVisible = !_window.IsVisible;
                    }
                };
                // Tray "Pause uploading" item removed (see MainWindowViewModel.TogglePause for why).
                //_viewModel.PropertyChanged += (_, e) => {
                //    if (e.PropertyName == nameof(MainWindowViewModel.IsPaused)) {
                //        SyncTrayPauseLabel();
                //    }
                //};

                desktop.Exit += (_, __) => _viewModel.Manager?.Stop();

                // macOS: clicking the Dock icon while the window is hidden (minimized to the menu bar)
                // brings it back, as users expect of a Mac app.
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable) {
                    activatable.Activated += (_, e) => {
                        if (e.Kind == ActivationKind.Reopen) {
                            _window.RestoreFromTray();
                        }
                    };
                }

                if (Instance != null) {
                    Instance.ActivationRequested += () => Dispatcher.UIThread.Post(() => {
                        _log.Info("Another launch asked for this window - showing it.");
                        _window.RestoreFromTray();
                    });
                }

                if (StartMinimized && _viewModel.Config.MinimizeToTray) {
                    // Never shown at all - straight to the tray, like the Windows app's --autorun.
                    _window.WindowState = WindowState.Minimized;
                    _window.ShowInTaskbar = false;
                }

                // Check for updates on startup and then hourly - same cadence as the Windows app's own
                // DispatcherTimer (App.xaml.cs), just without the Squirrel dependency.
                _ = _viewModel.RunAutoUpdateCheckAsync();
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
                _updateTimer.Tick += async (_, __) => await _viewModel.RunAutoUpdateCheckAsync();
                _updateTimer.Start();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void TrayOpen_Click(object sender, System.EventArgs e)
        {
            _window.RestoreFromTray();
        }

        // Clicking the icon itself reopens the window, like the WPF app's NotifyIcon.
        private void TrayIcon_Clicked(object sender, System.EventArgs e)
        {
            _window.RestoreFromTray();
        }

        // Pausing replay uploads makes it easier for upload abusers to pause between games to remove losses from upload queue.
        //private void TrayPause_Click(object sender, System.EventArgs e)
        //{
        //    _viewModel.TogglePauseCommand.Execute(null);
        //}

        private void TrayOpenLog_Click(object sender, System.EventArgs e)
        {
            _viewModel.ShowLogCommand.Execute(null);
        }

        private void TrayQuit_Click(object sender, System.EventArgs e)
        {
            _window.QuitForReal();
        }

        // Goes with the removed tray item. If it comes back: this finds it by position (NativeMenuItem
        // gets no x:Name field), so Items[1] must be the pause item again - order in App.axaml was
        // Open(0), Pause(1), Show log(2), separator(3), Quit(4).
        //private void SyncTrayPauseLabel()
        //{
        //    if (_trayIcon.Menu.Items[1] is NativeMenuItem pauseItem) {
        //        pauseItem.Header = _viewModel.IsPaused ? "Resume uploading" : "Pause uploading";
        //    }
        //}

        public static ThemeVariant ThemeVariantFor(string theme)
        {
            return theme switch {
                AppConfig.LightTheme => ThemeVariant.Light,
                AppConfig.SystemTheme => ThemeVariant.Default,
                _ => ThemeVariant.Dark,
            };
        }
    }
}
