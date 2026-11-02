using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Heroesprofile.Uploader.Desktop.Gui.ViewModels;
using System.Linq;

namespace Heroesprofile.Uploader.Desktop.Gui.Views
{
    /// <summary>
    /// Replay folder, theme, Twitch key and webhook - see <see cref="SettingsWindowViewModel"/>. Like the
    /// WPF SettingsWindow there are no Save/Cancel buttons: changes apply as they're made and are saved
    /// on close, which an invalid webhook url blocks (unless the app is quitting).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private SettingsWindowViewModel ViewModel => (SettingsWindowViewModel)DataContext;

        public SettingsWindow()
        {
            InitializeComponent();
            // Tunnel, so the window sees Ctrl+Z before a focused text box takes it as Undo.
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            Closing += OnClosing;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // WPF's hidden switch for the beta updates option
            if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) {
                ViewModel.RevealPreReleases();
            }
        }

        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            // Only the user closing this window itself may be held up by an invalid webhook url. The tray's
            // Quit closes the main window, which asks this one first (OwnerWindowClosing), and logoff/shutdown
            // closes everything (ApplicationShutdown/OSShutdown) - a cancel here would cancel those too, so
            // Quit would do nothing and Windows would show the app as preventing shutdown.
            if (!ViewModel.TryClose(canStayOpen: e.CloseReason == WindowCloseReason.WindowClosing)) {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Turning rank reading on asks first: it captures the game window and, during the beta, uploads part of
        /// it. The box is bound one way, so ticking it only ticks the box - the setting (and with it the capture)
        /// is turned on once the user agrees. Bound both ways, the tick turned rank reading on before the
        /// question even showed, so a game loading meanwhile was captured and could be sent without consent.
        /// </summary>
        private async void ReadRanks_Click(object sender, RoutedEventArgs e)
        {
            var box = (CheckBox)sender;
            if (box.IsChecked != true) {
                ViewModel.ReadRanks = false;
                return;
            }
            var answer = await MessageDialog.ShowAsync(this, "Read ranks from the loading screen",
                SettingsWindowViewModel.RankReadingConsent, "Turn on", "Cancel");
            if (answer == "Turn on") {
                ViewModel.ReadRanks = true;
            } else {
                // The setting never changed, so the binding has nothing to push: untick the box the way the
                // tick was made (ToggleButton.Toggle uses SetCurrentValue too).
                box.SetCurrentValue(CheckBox.IsCheckedProperty, false);
            }
        }

        private async void BrowseReplayPath_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = MainWindowViewModel.BrowseTitle,
                AllowMultiple = false,
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string path) {
                ViewModel.SetReplayPath(path);
            }
        }
    }
}
