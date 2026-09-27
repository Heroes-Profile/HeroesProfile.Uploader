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
    /// on close, which an invalid webhook url blocks.
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
            if (!ViewModel.TryClose()) {
                e.Cancel = true;
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
