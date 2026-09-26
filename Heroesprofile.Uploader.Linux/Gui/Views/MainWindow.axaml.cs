using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Heroesprofile.Uploader.Linux.Gui.ViewModels;
using System.Linq;

namespace Heroesprofile.Uploader.Linux.Gui.Views
{
    /// <summary>
    /// The main window. Business logic lives in <see cref="MainWindowViewModel"/> - this only handles
    /// things that genuinely need a Window: the folder picker, the Settings dialog's owner, and
    /// "minimize/close to tray" (Window lifecycle events aren't something a plain view model can hook).
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _reallyQuitting;

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
            PropertyChanged += OnWindowPropertyChanged;
        }

        private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && ViewModel?.Config.MinimizeToTray == true) {
                Hide();
            }
        }

        private async void BrowsePrefix_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Select the Wine/Proton prefix (or the Heroes of the Storm \"Accounts\" folder)",
                AllowMultiple = false,
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string path) {
                await ViewModel.UseNewPrefixAsync(path);
            }
        }

        private async void RestartNow_Click(object sender, RoutedEventArgs e)
        {
            // Minimized-to-tray right now (hidden, not just iconified) - relaunch the same way so the
            // new process doesn't suddenly pop a window the user had tucked away.
            await ViewModel.RestartNowAsync(minimized: !IsVisible);
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsVm = new SettingsWindowViewModel(ViewModel.Config);
            var dialog = new SettingsWindow { DataContext = settingsVm };
            await dialog.ShowDialog(this);
            if (dialog.Result) {
                ViewModel.ApplySettings(settingsVm);
            }
        }

        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            if (_reallyQuitting || ViewModel?.Config.MinimizeToTray != true) {
                return;
            }
            // "Minimize to tray" - closing the window hides it instead of exiting, same as minimizing.
            e.Cancel = true;
            Hide();
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

        /// <summary>The tray icon's "Quit" item - bypasses the hide-on-close behaviour above.</summary>
        public void QuitForReal()
        {
            _reallyQuitting = true;
            Close();
        }
    }
}
